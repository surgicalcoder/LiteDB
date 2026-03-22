using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static LiteDbX.Constants;

namespace LiteDbX.Engine;

/// <summary>
/// Manages state and page access for a single transaction.
///
/// Phase 2 redesign:
/// <list type="bullet">
///   <item><c>ThreadID</c> property removed — transaction ownership is no longer thread-affine.</item>
///   <item><see cref="CreateSnapshotAsync"/> added as the Phase 2 async path for snapshot creation.</item>
///   <item><see cref="CreateSnapshot"/> retained as Phase 3 bridge for legacy sync callers (QueryExecutor).</item>
/// </list>
/// </summary>
internal class TransactionService : IDisposable
{
    private readonly DiskService _disk;

    // instances from Engine
    private readonly HeaderPage _header;
    private readonly LockService _locker;
    private readonly TransactionMonitor _monitor;
    private readonly DiskReader _reader;

    // transaction controls
    private readonly Dictionary<string, Snapshot> _snapshots = new(StringComparer.OrdinalIgnoreCase);

    // transaction info
    private readonly WalIndexService _walIndex;

    public TransactionService(HeaderPage header, LockService locker, DiskService disk, WalIndexService walIndex, int maxTransactionSize, TransactionMonitor monitor, bool queryOnly)
    {
        // retain instances
        _header = header;
        _locker = locker;
        _disk = disk;
        _walIndex = walIndex;
        _monitor = monitor;

        QueryOnly = queryOnly;
        MaxTransactionSize = maxTransactionSize;

        // create new transactionID
        TransactionID = walIndex.NextTransactionID();
        StartTime = DateTime.UtcNow;
        _reader = _disk.GetReader();
    }

    // expose (as read only)
    // ThreadID removed in Phase 2 — transaction ownership is no longer thread-affine.

    public uint TransactionID { get; }

    public TransactionState State { get; private set; } = TransactionState.Active;

    public LockMode Mode { get; private set; } = LockMode.Read;

    public TransactionPages Pages { get; } = new();

    public DateTime StartTime { get; }

    public IEnumerable<Snapshot> Snapshots => _snapshots.Values;
    public bool QueryOnly { get; }

    // get/set
    public int MaxTransactionSize { get; set; }

    /// <summary>
    /// Get/Set how many open cursor this transaction are running
    /// </summary>
    public List<CursorInfo> OpenCursors { get; } = new();

    /// <summary>
    /// Get/Set if this transaction was opened by BeginTrans() method (not by AutoTransaction/Cursor)
    /// </summary>
    public bool ExplicitTransaction { get; set; } = false;

    /// <summary>
    /// Create (or get from transaction-cache) snapshot and return.
    /// Phase 3 bridge: uses synchronous lock acquisition via <see cref="LockService.EnterLockSync"/>.
    /// Use <see cref="CreateSnapshotAsync"/> for Phase 2+ async callers.
    /// </summary>
    public Snapshot CreateSnapshot(LockMode mode, string collection, bool addIfNotExists)
    {
        ENSURE(State == TransactionState.Active, "transaction must be active to create new snapshot");

        Snapshot create()
        {
            // lockAlreadyAcquired=false: constructor will call EnterLockSync (Phase 3 bridge)
            return new Snapshot(mode, collection, _header, TransactionID, Pages, _locker, _walIndex, _reader, _disk, addIfNotExists, lockAlreadyAcquired: false);
        }

        if (_snapshots.TryGetValue(collection, out var snapshot))
        {
            if ((mode == LockMode.Write && snapshot.Mode == LockMode.Read) || (addIfNotExists && snapshot.CollectionPage == null))
            {
                snapshot.Dispose();
                _snapshots.Remove(collection);
                _snapshots[collection] = snapshot = create();
            }
        }
        else
        {
            _snapshots[collection] = snapshot = create();
        }

        if (mode == LockMode.Write)
        {
            Mode = LockMode.Write;
        }

        return snapshot;
    }

    /// <summary>
    /// Async version of <see cref="CreateSnapshot"/>. Acquires the collection write lock asynchronously.
    /// This is the Phase 2 primary path; use this from all callers within <c>AutoTransactionAsync</c>.
    /// </summary>
    public async ValueTask<Snapshot> CreateSnapshotAsync(LockMode mode, string collection, bool addIfNotExists, CancellationToken ct = default)
    {
        ENSURE(State == TransactionState.Active, "transaction must be active to create new snapshot");

        async ValueTask<Snapshot> createAsync()
        {
            return await Snapshot.CreateAsync(mode, collection, _header, TransactionID, Pages, _locker, _walIndex, _reader, _disk, addIfNotExists, ct).ConfigureAwait(false);
        }

        if (_snapshots.TryGetValue(collection, out var snapshot))
        {
            if ((mode == LockMode.Write && snapshot.Mode == LockMode.Read) || (addIfNotExists && snapshot.CollectionPage == null))
            {
                snapshot.Dispose();
                _snapshots.Remove(collection);
                _snapshots[collection] = snapshot = await createAsync().ConfigureAwait(false);
            }
        }
        else
        {
            _snapshots[collection] = snapshot = await createAsync().ConfigureAwait(false);
        }

        if (mode == LockMode.Write)
        {
            Mode = LockMode.Write;
        }

        return snapshot;
    }

    /// <summary>Public implementation of Dispose pattern.</summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    // Finalizer removed in Phase 2: GC-based cleanup that called _monitor.ReleaseTransaction()
    // was inherently thread-affine and unsafe in an async context.
    // Users must use explicit disposal (await using LiteTransaction / explicit Release) to ensure
    // the transaction gate is correctly released. Leaked transactions will not be auto-cleaned by GC.

    /// <summary>
    /// If current transaction contains too much pages, now is safe to remove clean pages from memory and flush to wal disk
    /// dirty pages
    /// </summary>
    public void Safepoint()
    {
        if (State != TransactionState.Active)
        {
            throw new LiteException(0, "This transaction are invalid state");
        }

        if (_monitor.CheckSafepoint(this))
        {
            LOG($"safepoint flushing transaction pages: {Pages.TransactionSize}", "TRANSACTION");

            // if any snapshot are writable, persist pages
            if (Mode == LockMode.Write)
            {
                PersistDirtyPages(false);
            }

            // clear local pages in all snapshots (read/write snapshosts)
            foreach (var snapshot in _snapshots.Values)
            {
                snapshot.Clear();
            }

            // there is no local pages in cache and all dirty pages are in log file
            Pages.TransactionSize = 0;
        }
    }

    /// <summary>
    /// Persist all dirty in-memory pages (in all snapshots) and clear local pages list (even clean pages)
    /// </summary>
    private int PersistDirtyPages(bool commit)
    {
        var dirty = 0;

        // inner method to get all dirty pages
        IEnumerable<PageBuffer> source()
        {
            // get all dirty pages from all write snapshots
            // can include (or not) collection pages
            // update DirtyPagesLog inside transPage for all dirty pages was write on disk
            var pages = _snapshots.Values
                                  .Where(x => x.Mode == LockMode.Write)
                                  .SelectMany(x => x.GetWritablePages(true, commit));

            // mark last dirty page as confirmed only if there is no header change in commit
            var markLastAsConfirmed = commit && !Pages.HeaderChanged;

            // neet use "IsLast" method to get when loop are last item
            foreach (var page in pages.IsLast())
            {
                // update page transactionID
                page.Item.TransactionID = TransactionID;

                // if last page, mask as confirm (only if a real commit and no header changes)
                if (page.IsLast)
                {
                    page.Item.IsConfirmed = markLastAsConfirmed;
                }

                // if current page is last deleted page, point this page to last free
                if (Pages.LastDeletedPageID == page.Item.PageID && commit)
                {
                    ENSURE(Pages.HeaderChanged, "must header be in lock");
                    ENSURE(page.Item.PageType == PageType.Empty, "must be marked as deleted page");

                    // join existing free list pages into new list of deleted pages
                    page.Item.NextPageID = _header.FreeEmptyPageList;

                    // and now, set header free list page to this new list
                    _header.FreeEmptyPageList = Pages.FirstDeletedPageID;
                }

                var buffer = page.Item.UpdateBuffer();

                // buffer position will be set at end of file (it´s always log file)
                yield return buffer;

                dirty++;

                Pages.DirtyPages[page.Item.PageID] = new PagePosition(page.Item.PageID, buffer.Position);
            }

            // in commit with header page change, last page will be header
            if (commit && Pages.HeaderChanged)
            {
                // update this confirm page with current transactionID
                _header.TransactionID = TransactionID;

                // this header page will be marked as confirmed page in log file
                _header.IsConfirmed = true;

                // invoke all header callbacks (new/drop collections)
                Pages.OnCommit(_header);

                // clone header page
                var buffer = _header.UpdateBuffer();
                var clone = _disk.NewPage();

                // mem copy from current header to new header clone
                Buffer.BlockCopy(buffer.Array, buffer.Offset, clone.Array, clone.Offset, clone.Count);

                // persist header in log file
                yield return clone;
            }
        }

        ;

        // write all dirty pages, in sequence on log-file and store references into log pages on transPages
        // (works only for Write snapshots)
        var count = _disk.WriteLogDisk(source());

        // now, discard all clean pages (because those pages are writable and must be readable)
        // from write snapshots
        _disk.DiscardCleanPages(_snapshots.Values
                                          .Where(x => x.Mode == LockMode.Write)
                                          .SelectMany(x => x.GetWritablePages(false, commit))
                                          .Select(x => x.Buffer));

        return count;
    }

    /// <summary>
    /// Write pages into disk and confirm transaction in wal-index. Returns true if any dirty page was updated
    /// After commit, all snapshot are closed
    /// </summary>
    public void Commit()
    {
        ENSURE(State == TransactionState.Active, "transaction must be active to commit (current state: {0})", State);

        LOG($"commit transaction ({Pages.TransactionSize} pages)", "TRANSACTION");

        if (Mode == LockMode.Write || Pages.HeaderChanged)
        {
            lock (_header)
            {
                // persist all dirty page as commit mode (mark last page as IsConfirm)
                var count = PersistDirtyPages(true);

                // update wal-index (if any page was added into log disk)
                if (count > 0)
                {
                    _walIndex.ConfirmTransaction(TransactionID, Pages.DirtyPages.Values);
                }
            }
        }

        // dispose all snapshots
        foreach (var snapshot in _snapshots.Values)
        {
            snapshot.Dispose();
        }

        State = TransactionState.Committed;
    }

    /// <summary>
    /// Rollback transaction operation - ignore all modified pages and return new pages into disk
    /// After rollback, all snapshot are closed
    /// </summary>
    public void Rollback()
    {
        ENSURE(State == TransactionState.Active, "transaction must be active to rollback (current state: {0})", State);

        LOG($"rollback transaction ({Pages.TransactionSize} pages with {Pages.NewPages.Count} returns)", "TRANSACTION");

        // if transaction contains new pages, must return to database in another transaction
        if (Pages.NewPages.Count > 0)
        {
            ReturnNewPages();
        }

        // dispose all snapshots
        foreach (var snapshot in _snapshots.Values)
        {
            // but first, if writable, discard changes
            if (snapshot.Mode == LockMode.Write)
            {
                // discard all dirty pages
                _disk.DiscardDirtyPages(snapshot.GetWritablePages(true, true).Select(x => x.Buffer));

                // discard all clean pages
                _disk.DiscardCleanPages(snapshot.GetWritablePages(false, true).Select(x => x.Buffer));
            }

            // now, release pages
            snapshot.Dispose();
        }

        State = TransactionState.Aborted;
    }

    /// <summary>
    /// Return added pages when occurs an rollback transaction (run this only in rollback). Create new transactionID and add
    /// into
    /// Log file all new pages as EmptyPage in a linked order - also, update SharedPage before store
    /// </summary>
    private void ReturnNewPages()
    {
        // create new transaction ID
        var transactionID = _walIndex.NextTransactionID();

        // now lock header to update LastTransactionID/FreePageList
        lock (_header)
        {
            // persist all empty pages into wal-file
            var pagePositions = new Dictionary<uint, PagePosition>();

            IEnumerable<PageBuffer> source()
            {
                // create list of empty pages with forward link pointer
                for (var i = 0; i < Pages.NewPages.Count; i++)
                {
                    var pageID = Pages.NewPages[i];
                    var next = i < Pages.NewPages.Count - 1 ? Pages.NewPages[i + 1] : _header.FreeEmptyPageList;

                    var buffer = _disk.NewPage();

                    var page = new BasePage(buffer, pageID, PageType.Empty)
                    {
                        NextPageID = next,
                        TransactionID = transactionID
                    };

                    yield return page.UpdateBuffer();

                    // update wal
                    pagePositions[pageID] = new PagePosition(pageID, buffer.Position);
                }

                // update header page with my new transaction ID
                _header.TransactionID = transactionID;
                _header.FreeEmptyPageList = Pages.NewPages[0];
                _header.IsConfirmed = true;

                // clone header buffer
                var buf = _header.UpdateBuffer();
                var clone = _disk.NewPage();

                Buffer.BlockCopy(buf.Array, buf.Offset, clone.Array, clone.Offset, clone.Count);

                yield return clone;
            }

            ;

            // create a header save point before any change
            var safepoint = _header.Savepoint();

            try
            {
                // write all pages (including new header)
                _disk.WriteLogDisk(source());
            }
            catch
            {
                // must revert all header content if any error occurs during header change
                _header.Restore(safepoint);

                throw;
            }

            // now confirm this transaction to wal
            _walIndex.ConfirmTransaction(transactionID, pagePositions.Values);
        }
    }

    // Protected implementation of Dispose pattern.
    protected virtual void Dispose(bool dispose)
    {
        if (State == TransactionState.Disposed)
        {
            return;
        }

        ENSURE(State != TransactionState.Disposed, "transaction must be active before call Done");

        // clean snapshots if there is no commit/rollback
        if (State == TransactionState.Active && _snapshots.Count > 0)
        {
            // release writable snapshots
            foreach (var snapshot in _snapshots.Values.Where(x => x.Mode == LockMode.Write))
            {
                _disk.DiscardDirtyPages(snapshot.GetWritablePages(true, true).Select(x => x.Buffer));
                _disk.DiscardCleanPages(snapshot.GetWritablePages(false, true).Select(x => x.Buffer));
            }

            // release buffers in read-only snapshots
            foreach (var snapshot in _snapshots.Values.Where(x => x.Mode == LockMode.Read))
            {
                foreach (var page in snapshot.LocalPages)
                    page.Buffer.Release();
                snapshot.CollectionPage?.Buffer.Release();
            }
        }

        _reader.Dispose();

        State = TransactionState.Disposed;

        // Phase 2: the finalizer no longer calls ReleaseTransaction.
        // ReleaseTransaction (which exits the transaction gate) must be called explicitly
        // via LiteTransaction.DisposeAsync or TransactionMonitor.ReleaseTransaction.
    }
}

