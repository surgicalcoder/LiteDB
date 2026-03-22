using System;
using System.Threading;
using System.Threading.Tasks;

namespace LiteDbX.Engine;

/// <summary>
/// Concrete implementation of <see cref="ILiteTransaction"/>.
///
/// Uses <see cref="AsyncLocal{T}"/> to track the currently active transaction for the logical
/// async execution context, replacing the former <c>ThreadLocal&lt;TransactionService&gt;</c>
/// model that was incompatible with <c>await</c>-based continuations where thread continuations
/// may resume on a different managed thread.
///
/// Only one explicit transaction is active per async execution context at a time.
///
/// Lifecycle:
/// <list type="bullet">
///   <item>Created by <see cref="LiteEngine.BeginTransaction"/>.</item>
///   <item>Constructor registers this instance as the ambient context for the current logical flow.</item>
///   <item>Calling <see cref="DisposeAsync"/> without a prior <see cref="Commit"/> triggers an implicit rollback.</item>
/// </list>
///
/// Phase 2 — implements the <see cref="ILiteTransaction"/> contract defined in Phase 1.
/// Phase 2 — transaction–operation association uses <see cref="AsyncLocal{T}"/> ambient context.
/// Phase 2 — internal commit/rollback paths are still synchronous; Phase 3 will make disk I/O async.
/// </summary>
internal sealed class LiteTransaction : ILiteTransaction
{
    private static readonly AsyncLocal<LiteTransaction> _currentAmbient = new AsyncLocal<LiteTransaction>();

    private readonly TransactionMonitor _monitor;
    private readonly TransactionService _service;
    private bool _disposed;

    internal LiteTransaction(TransactionService service, TransactionMonitor monitor)
    {
        _service = service;
        _monitor = monitor;
        _currentAmbient.Value = this;
    }

    // ── Ambient context ───────────────────────────────────────────────────────

    /// <summary>
    /// The active explicit transaction for this async execution context,
    /// or <c>null</c> if no explicit transaction has been started.
    /// </summary>
    public static LiteTransaction CurrentAmbient => _currentAmbient.Value;

    /// <summary>Returns <c>true</c> if an explicit transaction is active in this async context.</summary>
    public static bool HasActive => _currentAmbient.Value != null;

    // ── Internal access ───────────────────────────────────────────────────────

    internal TransactionService Service => _service;

    // ── ILiteTransaction ──────────────────────────────────────────────────────

    /// <inheritdoc/>
    public ValueTask Commit(CancellationToken cancellationToken = default)
    {
        if (_service.State == TransactionState.Active)
        {
            // Phase 2 bridge: commit is still synchronous internally.
            // Phase 3 (Disk and Streams) will make the WAL write truly async.
            _service.Commit();
        }

        return default;
    }

    /// <inheritdoc/>
    public ValueTask Rollback(CancellationToken cancellationToken = default)
    {
        if (_service.State == TransactionState.Active)
        {
            _service.Rollback();
        }

        return default;
    }

    // ── IAsyncDisposable ──────────────────────────────────────────────────────

    /// <summary>
    /// Dispose the transaction. If <see cref="Commit"/> was not called, performs an implicit rollback.
    /// Clears the ambient context for the current async execution flow.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        if (_disposed) return default;
        _disposed = true;

        // Implicit rollback if the caller did not commit.
        if (_service.State == TransactionState.Active)
        {
            _service.Rollback();
        }

        _monitor.ReleaseTransaction(_service);

        // Clear the ambient context so nested operations no longer see this transaction.
        _currentAmbient.Value = null;

        return default;
    }
}

