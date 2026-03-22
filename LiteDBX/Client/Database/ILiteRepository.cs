using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace LiteDbX;

/// <summary>
/// Async-only repository convenience wrapper.
///
/// All operations that touch storage are async.
/// The query builder (<see cref="Query{T}"/>) is synchronous because it only builds a plan.
/// Lifecycle is managed by <see cref="IAsyncDisposable"/> since the repository owns
/// an <see cref="ILiteDatabase"/> reference.
/// </summary>
public interface ILiteRepository : IAsyncDisposable
{
    /// <summary>The underlying database instance.</summary>
    ILiteDatabase Database { get; }

    // ── Insert ────────────────────────────────────────────────────────────────

    /// <summary>Insert a document. Returns the auto-generated or existing <c>_id</c>.</summary>
    ValueTask<BsonValue> Insert<T>(T entity, string collectionName = null, CancellationToken cancellationToken = default);

    /// <summary>Insert a batch of documents. Returns the number inserted.</summary>
    ValueTask<int> Insert<T>(IEnumerable<T> entities, string collectionName = null, CancellationToken cancellationToken = default);

    // ── Update ────────────────────────────────────────────────────────────────

    /// <summary>Update a document. Returns <c>false</c> if not found.</summary>
    ValueTask<bool> Update<T>(T entity, string collectionName = null, CancellationToken cancellationToken = default);

    /// <summary>Update a batch of documents. Returns the number updated.</summary>
    ValueTask<int> Update<T>(IEnumerable<T> entities, string collectionName = null, CancellationToken cancellationToken = default);

    // ── Upsert ────────────────────────────────────────────────────────────────

    /// <summary>Insert or update a document. Returns <c>true</c> if inserted.</summary>
    ValueTask<bool> Upsert<T>(T entity, string collectionName = null, CancellationToken cancellationToken = default);

    /// <summary>Insert or update a batch of documents. Returns the number inserted.</summary>
    ValueTask<int> Upsert<T>(IEnumerable<T> entities, string collectionName = null, CancellationToken cancellationToken = default);

    // ── Delete ────────────────────────────────────────────────────────────────

    /// <summary>Delete a document by <c>_id</c>. Returns <c>true</c> if it was found and deleted.</summary>
    ValueTask<bool> Delete<T>(BsonValue id, string collectionName = null, CancellationToken cancellationToken = default);

    /// <summary>Delete documents matching a BsonExpression predicate. Returns the number deleted.</summary>
    ValueTask<int> DeleteMany<T>(BsonExpression predicate, string collectionName = null, CancellationToken cancellationToken = default);

    /// <summary>Delete documents matching a LINQ predicate. Returns the number deleted.</summary>
    ValueTask<int> DeleteMany<T>(Expression<Func<T, bool>> predicate, string collectionName = null, CancellationToken cancellationToken = default);

    // ── Query (sync builder — no I/O) ─────────────────────────────────────────

    /// <summary>
    /// Return a fluent query builder for <typeparamref name="T"/>.
    /// Execution is deferred until a terminal async operation is called.
    /// </summary>
    ILiteQueryable<T> Query<T>(string collectionName = null);

    // ── Index management ──────────────────────────────────────────────────────

    /// <summary>Create a named index if it does not already exist.</summary>
    ValueTask<bool> EnsureIndex<T>(string name, BsonExpression expression, bool unique = false, string collectionName = null, CancellationToken cancellationToken = default);

    /// <summary>Create an index on a BsonExpression if it does not already exist.</summary>
    ValueTask<bool> EnsureIndex<T>(BsonExpression expression, bool unique = false, string collectionName = null, CancellationToken cancellationToken = default);

    /// <summary>Create an index from a LINQ key selector if it does not already exist.</summary>
    ValueTask<bool> EnsureIndex<T, K>(Expression<Func<T, K>> keySelector, bool unique = false, string collectionName = null, CancellationToken cancellationToken = default);

    /// <summary>Create a named index from a LINQ key selector if it does not already exist.</summary>
    ValueTask<bool> EnsureIndex<T, K>(string name, Expression<Func<T, K>> keySelector, bool unique = false, string collectionName = null, CancellationToken cancellationToken = default);

    // ── Convenience queries ───────────────────────────────────────────────────

    /// <summary>Find a single document by <c>_id</c>. Throws if not found.</summary>
    ValueTask<T> SingleById<T>(BsonValue id, string collectionName = null, CancellationToken cancellationToken = default);

    /// <summary>Fetch all documents matching a BsonExpression predicate into a list.</summary>
    ValueTask<List<T>> Fetch<T>(BsonExpression predicate, string collectionName = null, CancellationToken cancellationToken = default);

    /// <summary>Fetch all documents matching a LINQ predicate into a list.</summary>
    ValueTask<List<T>> Fetch<T>(Expression<Func<T, bool>> predicate, string collectionName = null, CancellationToken cancellationToken = default);

    /// <summary>Return the first document matching a BsonExpression predicate. Throws if not found.</summary>
    ValueTask<T> First<T>(BsonExpression predicate, string collectionName = null, CancellationToken cancellationToken = default);

    /// <summary>Return the first document matching a LINQ predicate. Throws if not found.</summary>
    ValueTask<T> First<T>(Expression<Func<T, bool>> predicate, string collectionName = null, CancellationToken cancellationToken = default);

    /// <summary>Return the first document matching a BsonExpression predicate, or default if not found.</summary>
    ValueTask<T> FirstOrDefault<T>(BsonExpression predicate, string collectionName = null, CancellationToken cancellationToken = default);

    /// <summary>Return the first document matching a LINQ predicate, or default if not found.</summary>
    ValueTask<T> FirstOrDefault<T>(Expression<Func<T, bool>> predicate, string collectionName = null, CancellationToken cancellationToken = default);

    /// <summary>Return the single document matching a BsonExpression predicate. Throws if not exactly one.</summary>
    ValueTask<T> Single<T>(BsonExpression predicate, string collectionName = null, CancellationToken cancellationToken = default);

    /// <summary>Return the single document matching a LINQ predicate. Throws if not exactly one.</summary>
    ValueTask<T> Single<T>(Expression<Func<T, bool>> predicate, string collectionName = null, CancellationToken cancellationToken = default);

    /// <summary>Return the single document matching a BsonExpression predicate, or default if not found. Throws if more than one.</summary>
    ValueTask<T> SingleOrDefault<T>(BsonExpression predicate, string collectionName = null, CancellationToken cancellationToken = default);

    /// <summary>Return the single document matching a LINQ predicate, or default if not found. Throws if more than one.</summary>
    ValueTask<T> SingleOrDefault<T>(Expression<Func<T, bool>> predicate, string collectionName = null, CancellationToken cancellationToken = default);
}