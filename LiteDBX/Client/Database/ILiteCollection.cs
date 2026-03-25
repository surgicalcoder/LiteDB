using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using LiteDbX.Engine;

namespace LiteDbX;

/// <summary>
/// Async-only contract for a typed document collection.
///
/// Query composition methods (<see cref="Include{K}"/>, <see cref="Query"/>) remain synchronous
/// because they build expression trees without touching storage.
/// All operations that read from or write to storage are async.
/// </summary>
public interface ILiteCollection<T>
{
    // ── Collection metadata (sync — in-memory only) ───────────────────────────

    /// <summary>Collection name.</summary>
    string Name { get; }

    /// <summary>The auto-id strategy applied when documents without an explicit <c>_id</c> are inserted.</summary>
    BsonAutoId AutoId { get; }

    /// <summary>The entity mapper for this collection, or <c>null</c> for <see cref="BsonDocument"/> collections.</summary>
    EntityMapper EntityMapper { get; }

    // ── Include (sync builder — no I/O) ──────────────────────────────────────

    /// <summary>
    /// Register a DbRef path to be eagerly loaded in query results.
    /// Returns a new collection instance with the include registered.
    /// </summary>
    ILiteCollection<T> Include<K>(Expression<Func<T, K>> keySelector);

    /// <inheritdoc cref="Include{K}(Expression{Func{T,K}})"/>
    ILiteCollection<T> Include(BsonExpression keySelector);

    // ── Upsert ────────────────────────────────────────────────────────────────

    /// <summary>Insert or update a single document. Returns <c>true</c> if the document was inserted (not updated).</summary>
    ValueTask<bool> Upsert(T entity, CancellationToken cancellationToken = default);

    /// <summary>Insert or update a collection of documents. Returns the number of documents inserted.</summary>
    ValueTask<int> Upsert(IEnumerable<T> entities, CancellationToken cancellationToken = default);

    /// <summary>Insert or update a document with an explicit id.</summary>
    ValueTask<bool> Upsert(BsonValue id, T entity, CancellationToken cancellationToken = default);

    // ── Update ────────────────────────────────────────────────────────────────

    /// <summary>Update a document. Returns <c>false</c> if no document with the same <c>_id</c> was found.</summary>
    ValueTask<bool> Update(T entity, CancellationToken cancellationToken = default);

    /// <summary>Update a document identified by <paramref name="id"/>.</summary>
    ValueTask<bool> Update(BsonValue id, T entity, CancellationToken cancellationToken = default);

    /// <summary>Update a set of documents. Returns the number of documents updated.</summary>
    ValueTask<int> Update(IEnumerable<T> entities, CancellationToken cancellationToken = default);

    /// <summary>
    /// Apply a transform expression to all documents matching <paramref name="predicate"/>.
    /// Returns the number of documents updated.
    /// </summary>
    ValueTask<int> UpdateMany(BsonExpression transform, BsonExpression predicate, CancellationToken cancellationToken = default);

    /// <summary>
    /// Apply a LINQ-expressed transformation to all documents matching <paramref name="predicate"/>.
    /// Returns the number of documents updated.
    /// </summary>
    ValueTask<int> UpdateMany(Expression<Func<T, T>> extend, Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default);

    // ── Insert ────────────────────────────────────────────────────────────────

    /// <summary>Insert a new document. Returns the auto-generated or existing <c>_id</c>.</summary>
    ValueTask<BsonValue> Insert(T entity, CancellationToken cancellationToken = default);

    /// <summary>Insert a new document using the provided explicit transaction.</summary>
    ValueTask<BsonValue> Insert(T entity, ILiteTransaction transaction, CancellationToken cancellationToken = default);

    /// <summary>Insert a new document with an explicit <paramref name="id"/>.</summary>
    ValueTask Insert(BsonValue id, T entity, CancellationToken cancellationToken = default);

    /// <summary>Insert a new document with an explicit <paramref name="id"/> using the provided explicit transaction.</summary>
    ValueTask Insert(BsonValue id, T entity, ILiteTransaction transaction, CancellationToken cancellationToken = default);

    /// <summary>Insert a batch of documents. Returns the number inserted.</summary>
    ValueTask<int> Insert(IEnumerable<T> entities, CancellationToken cancellationToken = default);

    /// <summary>Insert a batch of documents using the provided explicit transaction. Returns the number inserted.</summary>
    ValueTask<int> Insert(IEnumerable<T> entities, ILiteTransaction transaction, CancellationToken cancellationToken = default);

    /// <summary>
    /// Bulk-insert documents in batches of <paramref name="batchSize"/>.
    /// Returns the total number of documents inserted.
    /// </summary>
    ValueTask<int> InsertBulk(IEnumerable<T> entities, int batchSize = 5000, CancellationToken cancellationToken = default);

    // ── Index management ──────────────────────────────────────────────────────

    /// <summary>Create a named index if it does not already exist. Returns <c>true</c> if created.</summary>
    ValueTask<bool> EnsureIndex(string name, BsonExpression expression, bool unique = false, CancellationToken cancellationToken = default);

    /// <summary>Create an index on a BsonExpression if it does not already exist.</summary>
    ValueTask<bool> EnsureIndex(BsonExpression expression, bool unique = false, CancellationToken cancellationToken = default);

    /// <summary>Create an index from a LINQ key selector if it does not already exist.</summary>
    ValueTask<bool> EnsureIndex<K>(Expression<Func<T, K>> keySelector, bool unique = false, CancellationToken cancellationToken = default);

    /// <summary>Create a named index from a LINQ key selector if it does not already exist.</summary>
    ValueTask<bool> EnsureIndex<K>(string name, Expression<Func<T, K>> keySelector, bool unique = false, CancellationToken cancellationToken = default);

    /// <summary>Drop an index. Returns <c>true</c> if the index existed and was removed.</summary>
    ValueTask<bool> DropIndex(string name, CancellationToken cancellationToken = default);

    // ── Query builder (sync — no I/O at this stage) ───────────────────────────

    /// <summary>
    /// Return a query builder for composing filters, ordering, projections, and limits.
    /// Execution is deferred until a terminal async operation is called.
    /// </summary>
    ILiteQueryable<T> Query();

    /// <summary>
    /// Return a query builder bound to the provided explicit transaction.
    /// Execution is deferred until a terminal async operation is called.
    /// </summary>
    ILiteQueryable<T> Query(ILiteTransaction transaction);

    // ── Find / Enumerate ──────────────────────────────────────────────────────

    /// <summary>Stream documents matching a BsonExpression predicate.</summary>
    IAsyncEnumerable<T> Find(BsonExpression predicate, int skip = 0, int limit = int.MaxValue, CancellationToken cancellationToken = default);

    /// <summary>Stream documents matching a structured <see cref="Query"/>.</summary>
    IAsyncEnumerable<T> Find(Query query, int skip = 0, int limit = int.MaxValue, CancellationToken cancellationToken = default);

    /// <summary>Stream documents matching a LINQ predicate.</summary>
    IAsyncEnumerable<T> Find(Expression<Func<T, bool>> predicate, int skip = 0, int limit = int.MaxValue, CancellationToken cancellationToken = default);

    /// <summary>Find a single document by its <c>_id</c>. Returns <c>null</c> if not found.</summary>
    ValueTask<T> FindById(BsonValue id, CancellationToken cancellationToken = default);

    /// <summary>Find the first document matching a BsonExpression. Returns <c>null</c> if not found.</summary>
    ValueTask<T> FindOne(BsonExpression predicate, CancellationToken cancellationToken = default);

    /// <summary>Find the first document matching a parameterised BsonExpression. Returns <c>null</c> if not found.</summary>
    ValueTask<T> FindOne(string predicate, BsonDocument parameters, CancellationToken cancellationToken = default);

    /// <summary>Find the first document matching a BsonExpression with positional args. Returns <c>null</c> if not found.</summary>
    ValueTask<T> FindOne(BsonExpression predicate, params BsonValue[] args);

    /// <summary>Find the first document matching a LINQ predicate. Returns <c>null</c> if not found.</summary>
    ValueTask<T> FindOne(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default);

    /// <summary>Find the first document matching a structured <see cref="Query"/>. Returns <c>null</c> if not found.</summary>
    ValueTask<T> FindOne(Query query, CancellationToken cancellationToken = default);

    /// <summary>Stream all documents in this collection, ordered by <c>_id</c>.</summary>
    IAsyncEnumerable<T> FindAll(CancellationToken cancellationToken = default);

    // ── Delete ────────────────────────────────────────────────────────────────

    /// <summary>Delete a document by its <c>_id</c>. Returns <c>true</c> if it was found and deleted.</summary>
    ValueTask<bool> Delete(BsonValue id, CancellationToken cancellationToken = default);

    /// <summary>Delete all documents in the collection. Returns the number of documents deleted.</summary>
    ValueTask<int> DeleteAll(CancellationToken cancellationToken = default);

    /// <summary>Delete all documents matching a BsonExpression predicate. Returns the number deleted.</summary>
    ValueTask<int> DeleteMany(BsonExpression predicate, CancellationToken cancellationToken = default);

    /// <summary>Delete all documents matching a parameterised predicate. Returns the number deleted.</summary>
    ValueTask<int> DeleteMany(string predicate, BsonDocument parameters, CancellationToken cancellationToken = default);

    /// <summary>Delete all documents matching a predicate with positional args. Returns the number deleted.</summary>
    ValueTask<int> DeleteMany(string predicate, params BsonValue[] args);

    /// <summary>Delete all documents matching a LINQ predicate. Returns the number deleted.</summary>
    ValueTask<int> DeleteMany(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default);

    // ── Count / Exists ────────────────────────────────────────────────────────

    /// <summary>Count all documents in the collection.</summary>
    ValueTask<int> Count(CancellationToken cancellationToken = default);

    /// <summary>Count documents matching a BsonExpression predicate.</summary>
    ValueTask<int> Count(BsonExpression predicate, CancellationToken cancellationToken = default);

    /// <summary>Count documents matching a parameterised predicate.</summary>
    ValueTask<int> Count(string predicate, BsonDocument parameters, CancellationToken cancellationToken = default);

    /// <summary>Count documents matching a predicate with positional args.</summary>
    ValueTask<int> Count(string predicate, params BsonValue[] args);

    /// <summary>Count documents matching a LINQ predicate.</summary>
    ValueTask<int> Count(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default);

    /// <summary>Count documents matching a structured <see cref="Query"/>.</summary>
    ValueTask<int> Count(Query query, CancellationToken cancellationToken = default);

    /// <summary>Count all documents in the collection as a <c>long</c>.</summary>
    ValueTask<long> LongCount(CancellationToken cancellationToken = default);

    /// <summary>Count documents matching a BsonExpression predicate as a <c>long</c>.</summary>
    ValueTask<long> LongCount(BsonExpression predicate, CancellationToken cancellationToken = default);

    /// <summary>Count documents matching a parameterised predicate as a <c>long</c>.</summary>
    ValueTask<long> LongCount(string predicate, BsonDocument parameters, CancellationToken cancellationToken = default);

    /// <summary>Count documents matching a predicate with positional args as a <c>long</c>.</summary>
    ValueTask<long> LongCount(string predicate, params BsonValue[] args);

    /// <summary>Count documents matching a LINQ predicate as a <c>long</c>.</summary>
    ValueTask<long> LongCount(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default);

    /// <summary>Count documents matching a structured <see cref="Query"/> as a <c>long</c>.</summary>
    ValueTask<long> LongCount(Query query, CancellationToken cancellationToken = default);

    /// <summary>Returns <c>true</c> if any document matches a BsonExpression predicate.</summary>
    ValueTask<bool> Exists(BsonExpression predicate, CancellationToken cancellationToken = default);

    /// <summary>Returns <c>true</c> if any document matches a parameterised predicate.</summary>
    ValueTask<bool> Exists(string predicate, BsonDocument parameters, CancellationToken cancellationToken = default);

    /// <summary>Returns <c>true</c> if any document matches a predicate with positional args.</summary>
    ValueTask<bool> Exists(string predicate, params BsonValue[] args);

    /// <summary>Returns <c>true</c> if any document matches a LINQ predicate.</summary>
    ValueTask<bool> Exists(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default);

    /// <summary>Returns <c>true</c> if any document matches a structured <see cref="Query"/>.</summary>
    ValueTask<bool> Exists(Query query, CancellationToken cancellationToken = default);

    // ── Min / Max ─────────────────────────────────────────────────────────────

    /// <summary>Return the minimum value of a key expression across all documents.</summary>
    ValueTask<BsonValue> Min(BsonExpression keySelector, CancellationToken cancellationToken = default);

    /// <summary>Return the minimum <c>_id</c> value in the collection.</summary>
    ValueTask<BsonValue> Min(CancellationToken cancellationToken = default);

    /// <summary>Return the minimum value of a LINQ key selector.</summary>
    ValueTask<K> Min<K>(Expression<Func<T, K>> keySelector, CancellationToken cancellationToken = default);

    /// <summary>Return the maximum value of a key expression across all documents.</summary>
    ValueTask<BsonValue> Max(BsonExpression keySelector, CancellationToken cancellationToken = default);

    /// <summary>Return the maximum <c>_id</c> value in the collection.</summary>
    ValueTask<BsonValue> Max(CancellationToken cancellationToken = default);

    /// <summary>Return the maximum value of a LINQ key selector.</summary>
    ValueTask<K> Max<K>(Expression<Func<T, K>> keySelector, CancellationToken cancellationToken = default);
}