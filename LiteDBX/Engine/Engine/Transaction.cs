using System;
using System.Threading;
using System.Threading.Tasks;
using static LiteDbX.Constants;

namespace LiteDbX.Engine;

public partial class LiteEngine
{
    // ── ILiteEngine — explicit transaction ────────────────────────────────────

    /// <summary>
    /// Begin an explicit async transaction scope.
    /// The returned <see cref="LiteTransaction"/> sets the ambient context via <see cref="AsyncLocal{T}"/>
    /// so that all engine operations within the same logical async flow automatically participate.
    /// Disposing without committing triggers an implicit rollback.
    /// </summary>
    public async ValueTask<ILiteTransaction> BeginTransaction(CancellationToken cancellationToken = default)
    {
        _state.Validate();

        if (LiteTransaction.HasActive)
        {
            throw new LiteException(0, "An explicit transaction is already active in this async context. " +
                "Nested explicit transactions are not supported. Use the existing transaction or complete it first.");
        }

        var service = await _monitor.CreateExplicitTransactionAsync(cancellationToken).ConfigureAwait(false);
        return new LiteTransaction(service, _monitor);
    }

    private TransactionService ResolveExplicitTransaction(ILiteTransaction transaction)
    {
        _state.Validate();

        if (transaction == null)
        {
            throw new ArgumentNullException(nameof(transaction));
        }

        if (transaction is not LiteTransaction liteTransaction)
        {
            throw new ArgumentException("Transaction must be created by LiteDbX.", nameof(transaction));
        }

        if (!ReferenceEquals(liteTransaction.Monitor, _monitor))
        {
            throw new ArgumentException("Transaction does not belong to this database instance.", nameof(transaction));
        }

        if (liteTransaction.Service.State != TransactionState.Active)
        {
            throw new LiteException(0, $"Transaction must be active (current state: {liteTransaction.Service.State})");
        }

        return liteTransaction.Service;
    }

    private ValueTask<T> ExplicitTransactionAsync<T>(
        ILiteTransaction transaction,
        Func<TransactionService, CancellationToken, ValueTask<T>> fn,
        CancellationToken ct = default)
    {
        var service = ResolveExplicitTransaction(transaction);
        return fn(service, ct);
    }

    // ── Internal async transaction helpers ───────────────────────────────────

    /// <summary>
    /// Execute <paramref name="fn"/> within an auto-transaction.
    /// If an explicit ambient transaction is active it is reused (no commit/release on exit).
    /// If a new auto-transaction is created it is committed and released after <paramref name="fn"/> returns.
    /// </summary>
    private async ValueTask<T> AutoTransactionAsync<T>(
        Func<TransactionService, CancellationToken, ValueTask<T>> fn,
        CancellationToken ct = default)
    {
        _state.Validate();

        var (transaction, isNew) = await _monitor.GetOrCreateTransactionAsync(false, ct).ConfigureAwait(false);

        try
        {
            var result = await fn(transaction, ct).ConfigureAwait(false);

            if (isNew)
            {
                await CommitAndReleaseTransactionAsync(transaction, ct).ConfigureAwait(false);
            }

            return result;
        }
        catch (Exception ex)
        {
            if (_state.Handle(ex))
            {
                await transaction.RollbackAsync(ct).ConfigureAwait(false);
                _monitor.ReleaseTransaction(transaction);
            }

            throw;
        }
    }

    private async ValueTask CommitAndReleaseTransactionAsync(TransactionService transaction, CancellationToken ct = default)
    {
        await transaction.CommitAsync(ct).ConfigureAwait(false);

        _monitor.ReleaseTransaction(transaction);

        // auto-checkpoint after commit if WAL exceeds the configured threshold
        if (_header.Pragmas.Checkpoint > 0 &&
            _disk.GetFileLength(FileOrigin.Log) > _header.Pragmas.Checkpoint * PAGE_SIZE)
        {
            await _walIndex.TryCheckpoint(ct).ConfigureAwait(false);
        }
    }
}