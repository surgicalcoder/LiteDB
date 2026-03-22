using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace LiteDbX.Engine;

public partial class LiteEngine
{
    /// <summary>
    /// Run a query over a collection and stream matching documents as an <see cref="IAsyncEnumerable{T}"/>.
    ///
    /// Phase 2: updated to match <see cref="ILiteEngine.Query"/> contract.
    /// Phase 4 bridge: the underlying <see cref="QueryExecutor"/> still uses a synchronous pipeline
    /// and <see cref="TransactionMonitor.GetOrCreateTransactionSync"/> for transaction entry.
    ///
    /// Cursor lifetime guarantee:
    /// The inner <see cref="BsonDataReader"/> holds the transaction gate slot for the duration of
    /// enumeration. When the caller breaks early, cancels, or fully consumes the sequence, the
    /// <c>finally</c> block awaits <see cref="IBsonDataReader.DisposeAsync"/> which releases the
    /// cursor registration and the transaction gate slot.
    /// </summary>
    public IAsyncEnumerable<BsonDocument> Query(
        string collection,
        Query query,
        CancellationToken cancellationToken = default)
    {
        // Eager argument / state validation — occurs before the async iterator starts,
        // so callers see ArgumentNullException immediately rather than on first MoveNextAsync.
        if (string.IsNullOrWhiteSpace(collection))
            throw new ArgumentNullException(nameof(collection));
        if (query == null)
            throw new ArgumentNullException(nameof(query));

        _state.Validate();

        IEnumerable<BsonDocument> source = null;

        // Resolve system collections ($) before entering the async iterator.
        if (collection.StartsWith("$"))
        {
            SqlParser.ParseCollection(new Tokenizer(collection), out var name, out var options);
            var sys = GetSystemCollection(name);
            source = sys.Input(options);
            collection = sys.Name;
        }

        return QueryCore(collection, query, source, cancellationToken);
    }

    /// <summary>
    /// Inner async iterator for <see cref="Query"/>.
    /// Separated from the public method so that argument validation runs eagerly.
    /// </summary>
    private async IAsyncEnumerable<BsonDocument> QueryCore(
        string collection,
        Query query,
        IEnumerable<BsonDocument> source,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var exec = new QueryExecutor(
            this,
            _state,
            _monitor,
            _sortDisk,
            _disk,
            _header.Pragmas,
            collection,
            query,
            source);

        // Phase 4 bridge: ExecuteQuery uses GetOrCreateTransactionSync internally.
        // The try/finally ensures cleanup even when the caller breaks early or cancels.
        var reader = exec.ExecuteQuery();
        try
        {
            while (await reader.Read(cancellationToken).ConfigureAwait(false))
            {
                yield return reader.Current.AsDocument;
            }
        }
        finally
        {
            await reader.DisposeAsync().ConfigureAwait(false);
        }
    }
}

