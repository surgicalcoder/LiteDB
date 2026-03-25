using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using LiteDbX.Engine;

namespace LiteDbX;

/// <summary>
/// Shared-mode engine wrapper that serialises concurrent access within a single process
/// via an async-safe <see cref="SemaphoreSlim"/>.
///
/// Phase 6 redesign decision:
///
///   The previous implementation used a named OS <see cref="Mutex"/> with a blocking
///   <c>WaitOne()</c> call, which violates the async-only architecture rule that no
///   blocking waits may exist in operational paths.
///
///   The <see cref="Mutex"/> has been replaced with a <c>SemaphoreSlim(1,1)</c>
///   whose <see cref="SemaphoreSlim.WaitAsync()"/> is used in all operational paths,
///   eliminating thread-blocking from the async runtime path.
///
///   Cross-process exclusive file coordination (the original named-mutex purpose)
///   is <b>explicitly deferred</b>. No hidden sync fallback is left in place.
///   A future phase may introduce async-safe cross-process coordination (e.g. a
///   polling file-lock strategy using async Task.Delay retries).
///
///   <see cref="BeginTransaction"/> remains unsupported: the shared-mode redesign now
///   supports reentrant nested single-call operations within the same async flow, but
///   explicit transaction scope across arbitrary user code still requires a deeper lifecycle
///   redesign.
/// </summary>
public class SharedEngine : ILiteEngine
{
    private sealed class SharedSession
    {
        public int RefCount { get; set; }
    }

    private sealed class LeaseContext
    {
        public LeaseContext(SharedEngine owner, SharedSession session, LeaseContext previous)
        {
            Owner = owner;
            Session = session;
            Previous = previous;
        }

        public SharedEngine Owner { get; }
        public SharedSession Session { get; }
        public LeaseContext Previous { get; }
    }

    private sealed class Lease : IDisposable, IAsyncDisposable
    {
        private readonly SharedEngine _owner;
        private readonly LeaseContext _context;
        private readonly bool _ownsAmbientContext;
        private readonly SharedSession _session;
        private bool _disposed;

        public Lease(
            SharedEngine owner,
            SharedSession session,
            LeaseContext context,
            bool ownsAmbientContext,
            LiteEngine engine)
        {
            _owner = owner;
            _session = session;
            _context = context;
            _ownsAmbientContext = ownsAmbientContext;
            Engine = engine;
        }

        public LiteEngine Engine { get; }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _owner.ReleaseLease(_session, _context, _ownsAmbientContext);
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return default;
        }
    }

    // Phase 6: SemaphoreSlim replaces the blocking OS Mutex for in-process serialisation.
    // Cross-process coordination is explicitly deferred (see class doc).
    private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
    private static readonly AsyncLocal<LeaseContext> _currentLease = new AsyncLocal<LeaseContext>();
    private readonly object _syncRoot = new object();
    private readonly EngineSettings _settings;
    private LiteEngine _engine;
    private SharedSession _activeSession;
    private bool _disposeRequested;
    private bool _disposed;

    public SharedEngine(EngineSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    ~SharedEngine() { Dispose(false); }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposing) return;

        LiteEngine engineToDispose = null;
        var disposeGate = false;

        lock (_syncRoot)
        {
            if (_disposed || _disposeRequested) return;

            _disposeRequested = true;

            if (_activeSession == null)
            {
                _disposed = true;
                engineToDispose = _engine;
                _engine = null;
                disposeGate = true;
            }
        }

        engineToDispose?.Dispose();

        if (disposeGate)
        {
            _gate.Dispose();
        }
    }

    /// <summary>
    /// Phase 6: <see cref="DisposeAsync"/> respects the active shared-session lease and only
    /// performs immediate engine disposal when no session is currently pinned.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        LiteEngine engineToDispose = null;
        var disposeGate = false;

        lock (_syncRoot)
        {
            if (_disposed || _disposeRequested) return;

            _disposeRequested = true;

            if (_activeSession == null)
            {
                _disposed = true;
                engineToDispose = _engine;
                _engine = null;
                disposeGate = true;
            }
        }

        if (engineToDispose != null)
        {
            await engineToDispose.DisposeAsync().ConfigureAwait(false);
        }

        if (disposeGate)
        {
            _gate.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    // ── Shared-session lease helpers ───────────────────────────────────────────

    /// <summary>
    /// Acquire a shared-mode lease.
    ///
    /// The outermost lease waits on the per-instance gate, opens the engine, and
    /// publishes an ambient async-flow context. Nested operations in the same
    /// logical async flow reuse the same open engine without waiting on the gate
    /// again, avoiding self-deadlock during streaming enumeration.
    /// </summary>
    private async ValueTask<Lease> AcquireLeaseAsync(CancellationToken cancellationToken)
    {
        var ambient = _currentLease.Value;

        if (ambient != null && ReferenceEquals(ambient.Owner, this))
        {
            lock (_syncRoot)
            {
                if (_disposed)
                {
                    throw new ObjectDisposedException(nameof(SharedEngine));
                }

                if (!ReferenceEquals(_activeSession, ambient.Session) || _engine == null)
                {
                    throw new InvalidOperationException("SharedEngine ambient lease is no longer active.");
                }

                ambient.Session.RefCount++;

                return new Lease(this, ambient.Session, ambient, ownsAmbientContext: false, _engine);
            }
        }

        lock (_syncRoot)
        {
            if (_disposed || _disposeRequested)
            {
                throw new ObjectDisposedException(nameof(SharedEngine));
            }
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            lock (_syncRoot)
            {
                if (_disposed || _disposeRequested)
                {
                    throw new ObjectDisposedException(nameof(SharedEngine));
                }

                _engine ??= new LiteEngine(_settings);

                var session = new SharedSession { RefCount = 1 };

                _activeSession = session;

                var context = new LeaseContext(this, session, ambient);

                _currentLease.Value = context;

                return new Lease(this, session, context, ownsAmbientContext: true, _engine);
            }
        }
        catch
        {
            _gate.Release();
            throw;
        }
    }

    private void ReleaseLease(SharedSession session, LeaseContext context, bool ownsAmbientContext)
    {
        LiteEngine engineToDispose = null;
        var releaseGate = false;
        var disposeGate = false;

        lock (_syncRoot)
        {
            if (ownsAmbientContext && ReferenceEquals(_currentLease.Value, context))
            {
                _currentLease.Value = context.Previous;
            }

            if (session.RefCount == 0)
            {
                return;
            }

            session.RefCount--;

            if (session.RefCount == 0)
            {
                if (ReferenceEquals(_activeSession, session))
                {
                    _activeSession = null;
                }

                engineToDispose = _engine;
                _engine = null;

                if (_disposeRequested)
                {
                    _disposed = true;
                    disposeGate = true;
                }

                releaseGate = true;
            }
        }

        engineToDispose?.Dispose();

        if (releaseGate)
        {
            _gate.Release();

            if (disposeGate)
            {
                _gate.Dispose();
            }
        }
    }

    private async ValueTask<T> ExecuteWithLeaseAsync<T>(
        Func<LiteEngine, ValueTask<T>> operation,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AcquireLeaseAsync(cancellationToken).ConfigureAwait(false);

        return await operation(lease.Engine).ConfigureAwait(false);
    }

    // ── Transactions ─────────────────────────────────────────────────────────

    /// <summary>
    /// Explicit multi-call transaction scope remains unsupported.
    ///
    /// The reentrant shared-session lease model supports nested single-call
    /// operations and streaming enumeration in the same async flow, but it does
    /// not expose an explicit transaction scope across arbitrary user code.
    /// </summary>
    public ValueTask<ILiteTransaction> BeginTransaction(CancellationToken cancellationToken = default)
        => throw new NotSupportedException(
            "Explicit transactions are not supported in SharedEngine. " +
            "Use nested single-call operations, or use a dedicated LiteEngine instance for transaction scope.");

    // ── Query ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Materialises query results under the shared-session lease, then releases the
    /// lease before yielding to the caller.
    ///
    /// This preserves seamless nested operations inside the consumer's
    /// <c>await foreach</c> body. An ambient <see cref="AsyncLocal{T}"/> context set
    /// inside the producer iterator is not guaranteed to be visible in the consumer
    /// body, so shared-mode queries must not rely on reentrant gate ownership across
    /// the iterator boundary.
    /// </summary>
    public IAsyncEnumerable<BsonDocument> Query(
        string collection,
        Query query,
        CancellationToken cancellationToken = default)
    {
        return QueryStream(collection, query, cancellationToken);
    }

    public IAsyncEnumerable<BsonDocument> Query(
        string collection,
        Query query,
        ILiteTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        if (transaction != null)
        {
            throw new NotSupportedException(
                "Explicit transaction-bound queries are not supported in SharedEngine. " +
                "Use a dedicated LiteEngine/LiteDatabase instance for transaction scope.");
        }

        return Query(collection, query, cancellationToken);
    }

    private async IAsyncEnumerable<BsonDocument> QueryStream(
        string collection,
        Query query,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        List<BsonDocument> documents;

        await using (var lease = await AcquireLeaseAsync(cancellationToken).ConfigureAwait(false))
        {
            documents = new List<BsonDocument>();

            await foreach (var doc in lease.Engine.Query(collection, query, cancellationToken).ConfigureAwait(false))
            {
                documents.Add(doc);
            }
        }

        foreach (var doc in documents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return doc;
        }
    }

    // ── Write operations ──────────────────────────────────────────────────────

    public ValueTask<int> Insert(string collection, IEnumerable<BsonDocument> docs, BsonAutoId autoId, CancellationToken cancellationToken = default)
        => ExecuteWithLeaseAsync(engine => engine.Insert(collection, docs, autoId, cancellationToken), cancellationToken);

    public ValueTask<int> Insert(string collection, IEnumerable<BsonDocument> docs, BsonAutoId autoId, ILiteTransaction transaction, CancellationToken cancellationToken = default)
    {
        if (transaction != null)
        {
            throw new NotSupportedException(
                "Explicit transaction-bound inserts are not supported in SharedEngine. " +
                "Use a dedicated LiteEngine/LiteDatabase instance for transaction scope.");
        }

        return Insert(collection, docs, autoId, cancellationToken);
    }

    public ValueTask<int> Update(string collection, IEnumerable<BsonDocument> docs, CancellationToken cancellationToken = default)
        => ExecuteWithLeaseAsync(engine => engine.Update(collection, docs, cancellationToken), cancellationToken);

    public ValueTask<int> UpdateMany(string collection, BsonExpression extend, BsonExpression predicate, CancellationToken cancellationToken = default)
        => ExecuteWithLeaseAsync(engine => engine.UpdateMany(collection, extend, predicate, cancellationToken), cancellationToken);

    public ValueTask<int> Upsert(string collection, IEnumerable<BsonDocument> docs, BsonAutoId autoId, CancellationToken cancellationToken = default)
        => ExecuteWithLeaseAsync(engine => engine.Upsert(collection, docs, autoId, cancellationToken), cancellationToken);

    public ValueTask<int> Delete(string collection, IEnumerable<BsonValue> ids, CancellationToken cancellationToken = default)
        => ExecuteWithLeaseAsync(engine => engine.Delete(collection, ids, cancellationToken), cancellationToken);

    public ValueTask<int> DeleteMany(string collection, BsonExpression predicate, CancellationToken cancellationToken = default)
        => ExecuteWithLeaseAsync(engine => engine.DeleteMany(collection, predicate, cancellationToken), cancellationToken);

    // ── Schema management ─────────────────────────────────────────────────────

    public ValueTask<bool> DropCollection(string name, CancellationToken cancellationToken = default)
        => ExecuteWithLeaseAsync(engine => engine.DropCollection(name, cancellationToken), cancellationToken);

    public ValueTask<bool> RenameCollection(string name, string newName, CancellationToken cancellationToken = default)
        => ExecuteWithLeaseAsync(engine => engine.RenameCollection(name, newName, cancellationToken), cancellationToken);

    public ValueTask<bool> EnsureIndex(string collection, string name, BsonExpression expression, bool unique, CancellationToken cancellationToken = default)
        => ExecuteWithLeaseAsync(engine => engine.EnsureIndex(collection, name, expression, unique, cancellationToken), cancellationToken);

    public ValueTask<bool> DropIndex(string collection, string name, CancellationToken cancellationToken = default)
        => ExecuteWithLeaseAsync(engine => engine.DropIndex(collection, name, cancellationToken), cancellationToken);

    // ── Maintenance ───────────────────────────────────────────────────────────

    public ValueTask<int> Checkpoint(CancellationToken cancellationToken = default)
        => ExecuteWithLeaseAsync(engine => engine.Checkpoint(cancellationToken), cancellationToken);

    public ValueTask<long> Rebuild(RebuildOptions options, CancellationToken cancellationToken = default)
        => ExecuteWithLeaseAsync(engine => engine.Rebuild(options, cancellationToken), cancellationToken);

    // ── Pragmas ───────────────────────────────────────────────────────────────

    public ValueTask<BsonValue> Pragma(string name, CancellationToken cancellationToken = default)
        => ExecuteWithLeaseAsync(engine => engine.Pragma(name, cancellationToken), cancellationToken);

    public ValueTask<bool> Pragma(string name, BsonValue value, CancellationToken cancellationToken = default)
        => ExecuteWithLeaseAsync(engine => engine.Pragma(name, value, cancellationToken), cancellationToken);
}