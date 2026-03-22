using System;
using System.Threading;
using System.Threading.Tasks;

namespace LiteDbX.Engine;

/// <summary>
/// Represents an explicit async transaction scope.
/// Replaces the former ambient per-thread BeginTrans/Commit/Rollback model.
///
/// Phase 1 contract — implementation details (lock association, snapshot isolation, etc.)
/// are deferred to Phase 2 (Transactions and Locking).
///
/// Usage pattern:
/// <code>
/// await using var tx = await engine.BeginTransaction();
/// await collection.Insert(doc);
/// await tx.Commit();
/// </code>
///
/// If <see cref="DisposeAsync"/> is called before <see cref="Commit"/>, the transaction is rolled back.
/// </summary>
public interface ILiteTransaction : IAsyncDisposable
{
    /// <summary>
    /// Commit the current transaction, persisting all changes made within its scope.
    /// </summary>
    ValueTask Commit(CancellationToken cancellationToken = default);

    /// <summary>
    /// Rollback the current transaction, discarding all changes made within its scope.
    /// </summary>
    ValueTask Rollback(CancellationToken cancellationToken = default);
}

