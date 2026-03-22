using System.Threading;
using System.Threading.Tasks;

namespace LiteDbX.Engine;

public partial class LiteEngine
{
    /// <summary>
    /// Implement a full rebuild database. Engine will be closed and re-created in another instance.
    /// A backup copy will be created with -backup extension. All data will be read and re-created in
    /// another database. After rebuild, the engine is re-opened.
    ///
    /// Phase 2: signature updated to match <see cref="ILiteEngine.Rebuild(RebuildOptions, CancellationToken)"/>.
    /// Phase 3 bridge: Close, RebuildService.Rebuild, and Open are all synchronous disk operations;
    /// returns a completed <see cref="ValueTask{T}"/>. Phase 3 (Disk and Streams) will make this async.
    /// </summary>
    public ValueTask<long> Rebuild(RebuildOptions options, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_settings.Filename))
        {
            return new ValueTask<long>(0L); // works only with OS file
        }

        Close();

        var rebuilder = new RebuildService(_settings);

        // Phase 3 bridge: rebuild is still synchronous disk I/O.
        var diff = rebuilder.Rebuild(options);

        Open();
        _state.Disposed = false;

        return new ValueTask<long>(diff);
    }

    /// <summary>
    /// Convenience overload: rebuild using current collation and password settings.
    /// Not part of <see cref="ILiteEngine"/> — additional helper on <see cref="LiteEngine"/>.
    ///
    /// Phase 3 bridge: reads collation directly from <see cref="HeaderPage.Pragmas"/> (synchronous)
    /// to avoid sync-over-async. Phase 3 will unify this with the main async overload.
    /// </summary>
    public ValueTask<long> Rebuild(CancellationToken cancellationToken = default)
    {
        var collation = new Collation(_header.Pragmas.Get(Pragmas.COLLATION).AsString);
        var password = _settings.Password;

        return Rebuild(new RebuildOptions { Password = password, Collation = collation }, cancellationToken);
    }

    /// <summary>
    /// Fill current database with data inside file reader — runs inside a transaction.
    /// Called exclusively by <see cref="RebuildService.Rebuild"/>.
    ///
    /// Phase 3 bridge: all transaction and disk operations here are synchronous.
    /// Documents are inserted per-collection in a dedicated transaction; indexes are created
    /// afterwards via separate auto-transactions (one per index).  This avoids nesting a
    /// second transaction acquisition inside the outer doc-insert transaction, which was
    /// possible in the old thread-local model but is not safe with the async model.
    ///
    /// The per-collection transaction approach is functionally equivalent and slightly more
    /// memory-efficient for large data sets.
    /// </summary>
    internal void RebuildContent(IFileReader reader)
    {
        foreach (var collection in reader.GetCollections())
        {
            // Phase 3 bridge: synchronous transaction entry.
            var transaction = _monitor.GetOrCreateTransactionSync(false, out _);

            try
            {
                var snapshot = transaction.CreateSnapshot(LockMode.Write, collection, true);
                var indexer = new IndexService(snapshot, _header.Pragmas.Collation, _disk.MAX_ITEMS_COUNT);
                var data = new DataService(snapshot, _disk.MAX_ITEMS_COUNT);

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

            // Index creation: each EnsureIndex creates its own auto-transaction.
            // Phase 3 bridge: GetAwaiter().GetResult() is safe here because this code
            // runs on a dedicated thread outside any async continuation.
            foreach (var index in reader.GetIndexes(collection))
            {
                EnsureIndex(
                    collection,
                    index.Name,
                    BsonExpression.Create(index.Expression),
                    index.Unique).GetAwaiter().GetResult();
            }
        }
    }
}

