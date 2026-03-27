using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using LiteDbX.Spatial;
using LiteDbX.Engine;

namespace LiteDbX;

public partial class LiteCollection<T>
{
    /// <summary>
    /// Return a new LiteQueryable to build more complex queries.
    /// </summary>
    public ILiteQueryable<T> Query()
    {
        return Query(null);
    }

    /// <summary>
    /// Return a new LiteQueryable bound to the provided explicit transaction.
    /// </summary>
    public ILiteQueryable<T> Query(ILiteTransaction transaction)
    {
        return new LiteQueryable<T>(_engine, _mapper, Name, new Query(), transaction).Include(_includes);
    }

    /// <summary>
    /// Return a provider-backed LINQ query root.
    /// Composition is synchronous and lowers into the native query model in later phases.
    /// </summary>
    public IQueryable<T> AsQueryable()
    {
        return AsQueryable(null);
    }

    /// <summary>
    /// Return a provider-backed LINQ query root bound to the provided explicit transaction.
    /// </summary>
    public IQueryable<T> AsQueryable(ILiteTransaction transaction)
    {
        var root = new global::LiteDbX.LiteDbXQueryRoot(_engine, _mapper, Name, typeof(T), _includes, transaction);
        var provider = new global::LiteDbX.LiteDbXQueryProvider(root);
        var state = global::LiteDbX.LiteDbXQueryState.CreateRoot(root);

        return new global::LiteDbX.LiteDbXQueryable<T>(provider, state);
    }

    #region Find

    /// <summary>Stream documents matching a BsonExpression predicate.</summary>
    public IAsyncEnumerable<T> Find(
        BsonExpression predicate,
        int skip = 0,
        int limit = int.MaxValue,
        CancellationToken cancellationToken = default)
    {
        if (predicate == null) throw new ArgumentNullException(nameof(predicate));

        return Query()
            .Include(_includes)
            .Where(predicate)
            .Skip(skip)
            .Limit(limit)
            .ToEnumerable(cancellationToken);
    }

    /// <summary>Stream documents matching a structured <see cref="global::LiteDbX.Query"/>.</summary>
    public IAsyncEnumerable<T> Find(
        Query query,
        int skip = 0,
        int limit = int.MaxValue,
        CancellationToken cancellationToken = default)
    {
        if (query == null) throw new ArgumentNullException(nameof(query));

        if (skip != 0) query.Offset = skip;
        if (limit != int.MaxValue) query.Limit = limit;

        return new LiteQueryable<T>(_engine, _mapper, Name, query).ToEnumerable(cancellationToken);
    }

    /// <summary>Stream documents matching a LINQ predicate.</summary>
    public IAsyncEnumerable<T> Find(
        Expression<Func<T, bool>> predicate,
        int skip = 0,
        int limit = int.MaxValue,
        CancellationToken cancellationToken = default)
    {
        return Find(_mapper.GetExpression(predicate), skip, limit, cancellationToken);
    }

    #endregion

    #region FindById / FindOne / FindAll

    /// <summary>Find a single document by its <c>_id</c>. Returns <c>null</c> if not found.</summary>
    public ValueTask<T> FindById(BsonValue id, CancellationToken cancellationToken = default)
    {
        if (id == null || id.IsNull) throw new ArgumentNullException(nameof(id));

        return Query()
            .Where(BsonExpression.Create("_id = @0", id))
            .FirstOrDefault(cancellationToken);
    }

    /// <summary>Find the first document matching a BsonExpression. Returns <c>null</c> if not found.</summary>
    public ValueTask<T> FindOne(BsonExpression predicate, CancellationToken cancellationToken = default)
    {
        return Query().Where(predicate).FirstOrDefault(cancellationToken);
    }

    /// <summary>Find the first document matching a parameterised predicate. Returns <c>null</c> if not found.</summary>
    public ValueTask<T> FindOne(string predicate, BsonDocument parameters, CancellationToken cancellationToken = default)
    {
        return FindOne(BsonExpression.Create(predicate, parameters), cancellationToken);
    }

    /// <summary>Find the first document matching a predicate with positional args. Returns <c>null</c> if not found.</summary>
    public ValueTask<T> FindOne(BsonExpression predicate, params BsonValue[] args)
    {
        return FindOne(BsonExpression.Create(predicate, args));
    }

    /// <summary>Find the first document matching a LINQ predicate. Returns <c>null</c> if not found.</summary>
    public ValueTask<T> FindOne(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
    {
        return FindOne(_mapper.GetExpression(predicate), cancellationToken);
    }

    /// <summary>Find the first document matching a structured <see cref="global::LiteDbX.Query"/>. Returns <c>null</c> if not found.</summary>
    public ValueTask<T> FindOne(Query query, CancellationToken cancellationToken = default)
    {
        return new LiteQueryable<T>(_engine, _mapper, Name, query).FirstOrDefault(cancellationToken);
    }

    /// <summary>Stream all documents in this collection, ordered by <c>_id</c>.</summary>
    public IAsyncEnumerable<T> FindAll(CancellationToken cancellationToken = default)
    {
        return Query().Include(_includes).ToEnumerable(cancellationToken);
    }

    public IAsyncEnumerable<T> FindNear(
        Expression<Func<T, GeoPoint>> field,
        GeoPoint center,
        double radiusMeters,
        int skip = 0,
        int limit = int.MaxValue,
        CancellationToken cancellationToken = default)
    {
        return Find(Query.Near(GetFieldExpression(field), center, radiusMeters), skip, limit, cancellationToken);
    }

    public IAsyncEnumerable<T> FindWithinBoundingBox(
        Expression<Func<T, GeoPoint>> field,
        double minLat,
        double minLon,
        double maxLat,
        double maxLon,
        int skip = 0,
        int limit = int.MaxValue,
        CancellationToken cancellationToken = default)
    {
        return Find(Query.WithinBoundingBox(GetFieldExpression(field), minLat, minLon, maxLat, maxLon), skip, limit, cancellationToken);
    }

    public IAsyncEnumerable<T> FindWithin(
        Expression<Func<T, GeoShape>> field,
        GeoPolygon polygon,
        int skip = 0,
        int limit = int.MaxValue,
        CancellationToken cancellationToken = default)
    {
        return Find(Query.Within(GetFieldExpression(field), polygon), skip, limit, cancellationToken);
    }

    public IAsyncEnumerable<T> FindIntersects(
        Expression<Func<T, GeoShape>> field,
        GeoShape shape,
        int skip = 0,
        int limit = int.MaxValue,
        CancellationToken cancellationToken = default)
    {
        return Find(Query.Intersects(GetFieldExpression(field), shape), skip, limit, cancellationToken);
    }

    public IAsyncEnumerable<T> FindContainsPoint(
        Expression<Func<T, GeoShape>> field,
        GeoPoint point,
        int skip = 0,
        int limit = int.MaxValue,
        CancellationToken cancellationToken = default)
    {
        return Find(Query.ContainsPoint(GetFieldExpression(field), point), skip, limit, cancellationToken);
    }

    #endregion
}