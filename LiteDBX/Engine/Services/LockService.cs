using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using static LiteDbX.Constants;

namespace LiteDbX.Engine;

/// <summary>
/// Async-safe lock service for collection-based and database-level coordination.
///
/// Phase 2 redesign:
/// <list type="bullet">
///   <item><see cref="ReaderWriterLockSlim"/> replaced with <see cref="AsyncReaderWriterLock"/> for the transaction gate.</item>
///   <item>Per-collection <c>Monitor.TryEnter</c> replaced with <see cref="SemaphoreSlim"/>(1,1) per collection.</item>
///   <item>All lock-entry methods are async; exit methods remain synchronous (<c>Release()</c> is always safe).</item>
///   <item><c>IsInTransaction</c> removed — callers use <see cref="LiteTransaction.HasActive"/> instead.</item>
/// </list>
///
/// Bridge methods (<c>EnterTransactionSync</c>, <c>EnterLockSync</c>) remain for internal paths that have not yet
/// been converted to fully async (QueryExecutor, WalIndexService checkpoint). These will be eliminated in Phase 3/4.
/// [ThreadSafe]
/// </summary>
internal class LockService : IDisposable
{
    // Per-collection write semaphores: only one writer per collection at a time.
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _collections =
        new ConcurrentDictionary<string, SemaphoreSlim>(StringComparer.OrdinalIgnoreCase);

    private readonly EnginePragmas _pragmas;

    // Async RW lock: concurrent transactions = readers, checkpoint/rebuild = writer.
    private readonly AsyncReaderWriterLock _transactionGate = new AsyncReaderWriterLock();

    internal LockService(EnginePragmas pragmas)
    {
        _pragmas = pragmas;
    }

    /// <summary>
    /// Exposes the configured lock timeout so cooperating services (e.g. WalIndexService)
    /// can use the same timeout value without holding a direct reference to EnginePragmas.
    /// </summary>
    public TimeSpan Timeout => _pragmas.Timeout;

    public void Dispose()
    {
        _transactionGate.Dispose();
        foreach (var sem in _collections.Values) sem.Dispose();
    }

    // ── Transaction gate ──────────────────────────────────────────────────────

    /// <summary>
    /// Returns the number of currently open transactions (approximate; for diagnostics).
    /// </summary>
    public int TransactionsCount { get; private set; }

    /// <summary>
    /// Asynchronously acquire a transaction-gate read slot.
    /// Multiple transactions can hold this concurrently; exclusive (checkpoint/rebuild) operations block.
    /// </summary>
    public async ValueTask EnterTransactionAsync(CancellationToken ct = default)
    {
        try
        {
            await _transactionGate.EnterReadAsync(_pragmas.Timeout, ct).ConfigureAwait(false);
            Interlocked.Increment(ref _transactionsCount);
        }
        catch (TimeoutException)
        {
            throw LiteException.LockTimeout("transaction", _pragmas.Timeout);
        }
    }

    private int _transactionsCount;

    /// <summary>
    /// Phase 3/4 bridge: synchronous transaction-gate entry for code paths not yet converted to async
    /// (e.g. <c>QueryExecutor</c>, <c>WalIndexService</c>).
    /// Uses <see cref="SemaphoreSlim"/>.<c>Wait(timeout)</c> which blocks the calling thread.
    /// </summary>
    internal void EnterTransactionSync()
    {
        try
        {
            _transactionGate.EnterReadAsync(_pragmas.Timeout).GetAwaiter().GetResult();
            Interlocked.Increment(ref _transactionsCount);
        }
        catch (TimeoutException)
        {
            throw LiteException.LockTimeout("transaction", _pragmas.Timeout);
        }
    }

    /// <summary>Release a transaction-gate read slot.</summary>
    public void ExitTransaction()
    {
        _transactionGate.ExitRead();
        Interlocked.Decrement(ref _transactionsCount);
    }

    // ── Collection write locks ────────────────────────────────────────────────

    /// <summary>
    /// Asynchronously acquire the exclusive write lock for a collection.
    /// Only one writer per collection at a time; concurrent readers are blocked by the collection semaphore.
    /// </summary>
    public async ValueTask EnterLockAsync(string collectionName, CancellationToken ct = default)
    {
        var sem = _collections.GetOrAdd(collectionName, _ => new SemaphoreSlim(1, 1));
        try
        {
            if (!await sem.WaitAsync(_pragmas.Timeout, ct).ConfigureAwait(false))
                throw LiteException.LockTimeout("write", collectionName, _pragmas.Timeout);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
    }

    /// <summary>
    /// Phase 3 bridge: synchronous collection write-lock entry for code paths not yet fully async
    /// (e.g. <see cref="Snapshot"/> constructor called from legacy sync paths).
    /// </summary>
    internal void EnterLockSync(string collectionName)
    {
        var sem = _collections.GetOrAdd(collectionName, _ => new SemaphoreSlim(1, 1));
        if (!sem.Wait(_pragmas.Timeout))
            throw LiteException.LockTimeout("write", collectionName, _pragmas.Timeout);
    }

    /// <summary>Release the exclusive write lock for a collection.</summary>
    public void ExitLock(string collectionName)
    {
        if (_collections.TryGetValue(collectionName, out var sem))
        {
            sem.Release();
        }
        else
        {
            throw LiteException.CollectionLockerNotFound(collectionName);
        }
    }

    // ── Exclusive lock (checkpoint / rebuild) ─────────────────────────────────

    /// <summary>
    /// Asynchronously enter exclusive mode — waits for all open transactions to complete.
    /// Used by checkpoint and rebuild operations.
    /// Returns <c>true</c> if the lock was acquired (must call <see cref="ExitExclusive"/> afterwards).
    /// Returns <c>false</c> if already in exclusive mode.
    /// </summary>
    public async ValueTask<bool> EnterExclusiveAsync(CancellationToken ct = default)
    {
        try
        {
            await _transactionGate.EnterWriteAsync(_pragmas.Timeout, ct).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {
            throw LiteException.LockTimeout("exclusive", _pragmas.Timeout);
        }
    }

    /// <summary>
    /// Phase 3 bridge: synchronous exclusive-lock entry (used by <c>WalIndexService.Checkpoint()</c>).
    /// </summary>
    internal bool EnterExclusive()
    {
        try
        {
            _transactionGate.EnterWriteAsync(_pragmas.Timeout).GetAwaiter().GetResult();
            return true;
        }
        catch (TimeoutException)
        {
            throw LiteException.LockTimeout("exclusive", _pragmas.Timeout);
        }
    }

    /// <summary>
    /// Try to enter exclusive mode immediately (no wait).
    /// Returns <c>false</c> if any readers or another writer currently holds the lock.
    /// </summary>
    public bool TryEnterExclusive(out bool mustExit)
    {
        if (_transactionGate.TryEnterWrite())
        {
            mustExit = true;
            return true;
        }

        mustExit = false;
        return false;
    }

    /// <summary>Release the exclusive lock.</summary>
    public void ExitExclusive()
    {
        _transactionGate.ExitWrite();
    }
}