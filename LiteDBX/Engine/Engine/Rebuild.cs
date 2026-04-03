using System.Threading;
using System.Threading.Tasks;

namespace LiteDbX.Engine;

public partial class LiteEngine
{
    /// <summary>
    /// Rebuild the database fully asynchronously.
    ///
    /// Phase 6: uses <see cref="CloseAsync"/> and <see cref="RebuildService.RebuildAsync"/>
    /// so no thread is blocked during the rebuild I/O. Re-opening after the rebuild now
    /// uses the explicit async-native startup lifecycle via <c>LiteEngine.Open(...)</c> internals.
    /// </summary>
    public async ValueTask<long> Rebuild(RebuildOptions options, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_settings.Filename))
        {
            return 0L; // memory-only databases cannot be rebuilt via file replacement
        }

        await CloseAsync().ConfigureAwait(false);

        var rebuilder = new RebuildService(_settings);
        var diff = await rebuilder.RebuildAsync(options, cancellationToken).ConfigureAwait(false);

        await OpenInstance(cancellationToken).ConfigureAwait(false);
        _state.Disposed = false;

        return diff;
    }

    /// <summary>
    /// Convenience overload: rebuild using current collation and password settings.
    /// </summary>
    public ValueTask<long> Rebuild(CancellationToken cancellationToken = default)
    {
        var collation = new Collation(_header.Pragmas.Get(Pragmas.COLLATION).AsString);
        var password = _settings.Password;

        return Rebuild(new RebuildOptions { Password = password, Collation = collation }, cancellationToken);
    }

    // ── Async content rebuild (Phase 6 primary path) ──────────────────────────

    /// <summary>
    /// Copy all documents and indexes from <paramref name="reader"/> into the engine,
    /// using the async transaction entry point.
    ///
    /// Phase 6: replaces the Phase 3 bridge that used
    /// <c>GetOrCreateTransactionSync</c> and <c>EnsureIndex(...).GetAwaiter().GetResult()</c>.
    /// All transaction acquisition and index creation are now genuinely awaited.
    /// </summary>
    internal async ValueTask RebuildContentAsync(
        IFileReader reader,
        CancellationToken cancellationToken = default)
    {
        foreach (var collection in reader.GetCollections())
        {
            var (transaction, _) = await _monitor
                .GetOrCreateTransactionAsync(false, cancellationToken)
                .ConfigureAwait(false);

            try
            {
                var snapshot = transaction.CreateSnapshot(LockMode.Write, collection, true);
                var indexer  = new IndexService(snapshot, _header.Pragmas.Collation, _disk.MAX_ITEMS_COUNT);
                var data     = new DataService(snapshot, _disk.MAX_ITEMS_COUNT);

                foreach (var doc in reader.GetDocuments(collection))
                {
                    transaction.Safepoint();
                    InsertDocument(snapshot, doc, BsonAutoId.ObjectId, indexer, data);
                }

                transaction.Commit();
                _monitor.ReleaseTransaction(transaction);
            }
            catch
            {
                if (transaction.State == TransactionState.Active)
                {
                    transaction.Rollback();
                }

                _monitor.ReleaseTransaction(transaction);
                throw;
            }

            // Each index is created in its own auto-transaction (Phase 6: truly async).
            foreach (var index in reader.GetIndexes(collection))
            {
                await EnsureIndex(
                    collection,
                    index.Name,
                    BsonExpression.Create(index.Expression),
                    index.Unique,
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }

}

