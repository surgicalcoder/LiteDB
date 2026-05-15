using System;
using System.Collections.Generic;
using System.IO;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace LiteDbX;

/// <summary>
/// Repository convenience wrapper around <see cref="ILiteDatabase"/>.
/// Implements <see cref="ILiteRepository"/> — all storage operations are async.
/// Open synchronously with <see cref="Open(string, BsonMapper, CancellationToken)"/> or
/// asynchronously with <see cref="OpenAsync(string, BsonMapper, CancellationToken)"/>.
/// </summary>
public class LiteRepository : ILiteRepository
{
    #region Properties

    /// <inheritdoc/>
    public ILiteDatabase Database { get; }

    #endregion

    #region Open

    /// <summary>Open a repository from a connection string synchronously.</summary>
    public static LiteRepository Open(
        string connectionString,
        BsonMapper mapper = null,
        CancellationToken cancellationToken = default)
        => new(LiteDatabase.Open(connectionString, mapper, cancellationToken));

    /// <summary>Open a repository from a <see cref="ConnectionString"/> synchronously.</summary>
    public static LiteRepository Open(
        ConnectionString connectionString,
        BsonMapper mapper = null,
        CancellationToken cancellationToken = default)
        => new(LiteDatabase.Open(connectionString, mapper, cancellationToken));

    /// <summary>Open a stream-backed repository synchronously.</summary>
    public static LiteRepository Open(
        Stream stream,
        BsonMapper mapper = null,
        Stream logStream = null,
        CancellationToken cancellationToken = default)
        => new(LiteDatabase.Open(stream, mapper, logStream, cancellationToken));

    /// <summary>Open a repository from a connection string asynchronously.</summary>
    public static async ValueTask<LiteRepository> OpenAsync(
        string connectionString,
        BsonMapper mapper = null,
        CancellationToken cancellationToken = default)
        => new(await LiteDatabase.OpenAsync(connectionString, mapper, cancellationToken).ConfigureAwait(false));

    /// <summary>Open a repository from a <see cref="ConnectionString"/> asynchronously.</summary>
    public static async ValueTask<LiteRepository> OpenAsync(
        ConnectionString connectionString,
        BsonMapper mapper = null,
        CancellationToken cancellationToken = default)
        => new(await LiteDatabase.OpenAsync(connectionString, mapper, cancellationToken).ConfigureAwait(false));

    /// <summary>Open a stream-backed repository asynchronously.</summary>
    public static async ValueTask<LiteRepository> OpenAsync(
        Stream stream,
        BsonMapper mapper = null,
        Stream logStream = null,
        CancellationToken cancellationToken = default)
        => new(await LiteDatabase.OpenAsync(stream, mapper, logStream, cancellationToken).ConfigureAwait(false));

    #endregion

    /// <summary>
    /// Wrap an existing <see cref="ILiteDatabase"/> instance.
    /// This overload does not open a database; it only layers repository helpers over an already
    /// initialized database instance.
    /// </summary>
    public LiteRepository(ILiteDatabase database)
    {
        Database = database ?? throw new ArgumentNullException(nameof(database));
    }

    #region Query (sync builder — no I/O)

    /// <inheritdoc/>
    public ILiteQueryable<T> Query<T>(string collectionName = null)
        => Database.GetCollection<T>(collectionName).Query();

    #endregion

    #region Insert

    /// <inheritdoc/>
    public ValueTask<BsonValue> Insert<T>(T entity, string collectionName = null, CancellationToken cancellationToken = default)
        => Database.GetCollection<T>(collectionName).Insert(entity, cancellationToken);

    /// <inheritdoc/>
    public ValueTask<int> Insert<T>(IEnumerable<T> entities, string collectionName = null, CancellationToken cancellationToken = default)
        => Database.GetCollection<T>(collectionName).Insert(entities, cancellationToken);

    #endregion

    #region Update

    /// <inheritdoc/>
    public ValueTask<bool> Update<T>(T entity, string collectionName = null, CancellationToken cancellationToken = default)
        => Database.GetCollection<T>(collectionName).Update(entity, cancellationToken);

    /// <inheritdoc/>
    public ValueTask<int> Update<T>(IEnumerable<T> entities, string collectionName = null, CancellationToken cancellationToken = default)
        => Database.GetCollection<T>(collectionName).Update(entities, cancellationToken);

    #endregion

    #region Upsert

    /// <inheritdoc/>
    public ValueTask<bool> Upsert<T>(T entity, string collectionName = null, CancellationToken cancellationToken = default)
        => Database.GetCollection<T>(collectionName).Upsert(entity, cancellationToken);

    /// <inheritdoc/>
    public ValueTask<int> Upsert<T>(IEnumerable<T> entities, string collectionName = null, CancellationToken cancellationToken = default)
        => Database.GetCollection<T>(collectionName).Upsert(entities, cancellationToken);

    #endregion

    #region Delete

    /// <inheritdoc/>
    public ValueTask<bool> Delete<T>(BsonValue id, string collectionName = null, CancellationToken cancellationToken = default)
        => Database.GetCollection<T>(collectionName).Delete(id, cancellationToken);

    /// <inheritdoc/>
    public ValueTask<int> DeleteMany<T>(BsonExpression predicate, string collectionName = null, CancellationToken cancellationToken = default)
        => Database.GetCollection<T>(collectionName).DeleteMany(predicate, cancellationToken);

    /// <inheritdoc/>
    public ValueTask<int> DeleteMany<T>(Expression<Func<T, bool>> predicate, string collectionName = null, CancellationToken cancellationToken = default)
        => Database.GetCollection<T>(collectionName).DeleteMany(predicate, cancellationToken);

    #endregion

    #region EnsureIndex

    /// <inheritdoc/>
    public ValueTask<bool> EnsureIndex<T>(string name, BsonExpression expression, bool unique = false, string collectionName = null, CancellationToken cancellationToken = default)
        => Database.GetCollection<T>(collectionName).EnsureIndex(name, expression, unique, cancellationToken);

    /// <inheritdoc/>
    public ValueTask<bool> EnsureIndex<T>(BsonExpression expression, bool unique = false, string collectionName = null, CancellationToken cancellationToken = default)
        => Database.GetCollection<T>(collectionName).EnsureIndex(expression, unique, cancellationToken);

    /// <inheritdoc/>
    public ValueTask<bool> EnsureIndex<T, K>(Expression<Func<T, K>> keySelector, bool unique = false, string collectionName = null, CancellationToken cancellationToken = default)
        => Database.GetCollection<T>(collectionName).EnsureIndex(keySelector, unique, cancellationToken);

    /// <inheritdoc/>
    public ValueTask<bool> EnsureIndex<T, K>(string name, Expression<Func<T, K>> keySelector, bool unique = false, string collectionName = null, CancellationToken cancellationToken = default)
        => Database.GetCollection<T>(collectionName).EnsureIndex(name, keySelector, unique, cancellationToken);

    #endregion

    #region Convenience queries

    /// <inheritdoc/>
    public ValueTask<T> SingleById<T>(BsonValue id, string collectionName = null, CancellationToken cancellationToken = default)
    {
        var collection = (LiteCollection<T>)Database.GetCollection<T>(collectionName);
        var normalizedId = collection.NormalizeId(id);

        return collection.Query().Where("_id = @0", new[] { normalizedId }).Single(cancellationToken);
    }

    /// <inheritdoc/>
    public ValueTask<List<T>> Fetch<T>(BsonExpression predicate, string collectionName = null, CancellationToken cancellationToken = default)
        => Query<T>(collectionName).Where(predicate).ToList(cancellationToken);

    /// <inheritdoc/>
    public ValueTask<List<T>> Fetch<T>(Expression<Func<T, bool>> predicate, string collectionName = null, CancellationToken cancellationToken = default)
        => Query<T>(collectionName).Where(predicate).ToList(cancellationToken);

    /// <inheritdoc/>
    public ValueTask<T> First<T>(BsonExpression predicate, string collectionName = null, CancellationToken cancellationToken = default)
        => Query<T>(collectionName).Where(predicate).First(cancellationToken);

    /// <inheritdoc/>
    public ValueTask<T> First<T>(Expression<Func<T, bool>> predicate, string collectionName = null, CancellationToken cancellationToken = default)
        => Query<T>(collectionName).Where(predicate).First(cancellationToken);

    /// <inheritdoc/>
    public ValueTask<T> FirstOrDefault<T>(BsonExpression predicate, string collectionName = null, CancellationToken cancellationToken = default)
        => Query<T>(collectionName).Where(predicate).FirstOrDefault(cancellationToken);

    /// <inheritdoc/>
    public ValueTask<T> FirstOrDefault<T>(Expression<Func<T, bool>> predicate, string collectionName = null, CancellationToken cancellationToken = default)
        => Query<T>(collectionName).Where(predicate).FirstOrDefault(cancellationToken);

    /// <inheritdoc/>
    public ValueTask<T> Single<T>(BsonExpression predicate, string collectionName = null, CancellationToken cancellationToken = default)
        => Query<T>(collectionName).Where(predicate).Single(cancellationToken);

    /// <inheritdoc/>
    public ValueTask<T> Single<T>(Expression<Func<T, bool>> predicate, string collectionName = null, CancellationToken cancellationToken = default)
        => Query<T>(collectionName).Where(predicate).Single(cancellationToken);

    /// <inheritdoc/>
    public ValueTask<T> SingleOrDefault<T>(BsonExpression predicate, string collectionName = null, CancellationToken cancellationToken = default)
        => Query<T>(collectionName).Where(predicate).SingleOrDefault(cancellationToken);

    /// <inheritdoc/>
    public ValueTask<T> SingleOrDefault<T>(Expression<Func<T, bool>> predicate, string collectionName = null, CancellationToken cancellationToken = default)
        => Query<T>(collectionName).Where(predicate).SingleOrDefault(cancellationToken);

    #endregion

    #region Lifecycle

    /// <inheritdoc/>
    public ValueTask DisposeAsync() => Database.DisposeAsync();


    #endregion
}