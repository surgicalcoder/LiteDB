using System;
using System.Threading;
using System.Threading.Tasks;

namespace LiteDbX;

/// <summary>
/// An async cursor over a BsonValue result set.
///
/// Design choice (Phase 1): retained as an explicit cursor rather than replaced with
/// <c>IAsyncEnumerable&lt;BsonDocument&gt;</c> because:
/// <list type="bullet">
///   <item>It carries <see cref="Collection"/> context that is required by SQL-level <c>Execute</c> callers.</item>
///   <item>It supports indexed field access via <c>this[string field]</c>.</item>
///   <item>It maps cleanly to the cursor-based reader pattern familiar to database client consumers.</item>
/// </list>
///
/// Callers can obtain an <c>IAsyncEnumerable&lt;BsonValue&gt;</c> via the
/// <see cref="BsonDataReaderExtensions.ToAsyncEnumerable"/> extension method.
///
/// Phase 4 (Query Pipeline) is responsible for updating the concrete <c>BsonDataReader</c> implementation.
/// </summary>
public interface IBsonDataReader : IAsyncDisposable
{
    /// <summary>Access a field by name in the current document.</summary>
    BsonValue this[string field] { get; }

    /// <summary>Name of the collection this reader is iterating, if applicable.</summary>
    string Collection { get; }

    /// <summary>The current <see cref="BsonValue"/> after the last successful <see cref="Read"/> call.</summary>
    BsonValue Current { get; }

    /// <summary>Returns <c>true</c> if the result set contains at least one value.</summary>
    bool HasValues { get; }

    /// <summary>
    /// Advance the cursor to the next result.
    /// Returns <c>true</c> if a value is available in <see cref="Current"/>; <c>false</c> at end of stream.
    /// </summary>
    ValueTask<bool> Read(CancellationToken cancellationToken = default);
}