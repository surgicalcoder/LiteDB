using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using LiteDbX.Engine;

namespace LiteDbX.Spatial;

public static class Spatial
{
    private const string MetadataCollection = "_spatial_meta";
    private const string PointIndexName = "_gh";
    private const string BoundingBoxFieldName = "_mbb";

    private static readonly object MapperLock = new();
    private static readonly Dictionary<BsonMapper, bool> RegisteredMappers = new();
    private static readonly ConcurrentDictionary<EngineCollectionKey, SpatialIndexMetadata> MetadataCache = new();

    static Spatial()
    {
        Register(BsonMapper.Global);
    }

    public static SpatialOptions Options { get; set; } = new();

    public static void Register(BsonMapper mapper = null)
    {
        EnsureMapperRegistration(mapper ?? BsonMapper.Global);
    }

    public static async ValueTask EnsurePointIndex<T>(
        ILiteCollection<T> collection,
        Expression<Func<T, GeoPoint>> selector,
        int precisionBits = 0,
        CancellationToken cancellationToken = default)
    {
        if (collection == null) throw new ArgumentNullException(nameof(collection));
        if (selector == null) throw new ArgumentNullException(nameof(selector));

        var lite = GetLiteCollection(collection);
        var mapper = GetMapper(lite);
        EnsureMapperRegistration(mapper);

        if (precisionBits <= 0)
        {
            precisionBits = Options.DefaultIndexPrecisionBits;
        }

        var getter = selector.Compile();

        SpatialMapping.EnsureComputedMember(lite, PointIndexName, typeof(long), entity =>
        {
            var point = getter(entity);
            return point == null ? null : SpatialIndexing.ComputeMorton(point.Normalize(), precisionBits);
        });

        SpatialMapping.EnsureBoundingBox(lite, entity => getter(entity)?.Normalize());

        await lite.EnsureIndex(PointIndexName, BsonExpression.Create("$._gh"), cancellationToken: cancellationToken).ConfigureAwait(false);
        await PersistPointIndexMetadata(lite, precisionBits, cancellationToken).ConfigureAwait(false);
    }

    public static ValueTask EnsureShapeIndex<T>(
        ILiteCollection<T> collection,
        Expression<Func<T, GeoShape>> selector,
        CancellationToken cancellationToken = default)
    {
        if (selector == null) throw new ArgumentNullException(nameof(selector));
        return EnsureShapeIndexInternal(collection, selector.Compile(), cancellationToken);
    }

    public static ValueTask EnsureShapeIndex<T, TShape>(
        ILiteCollection<T> collection,
        Expression<Func<T, TShape>> selector,
        CancellationToken cancellationToken = default)
        where TShape : GeoShape
    {
        if (selector == null) throw new ArgumentNullException(nameof(selector));
        return EnsureShapeIndexInternal(collection, entity => selector.Compile()(entity), cancellationToken);
    }

    public static bool Near(GeoPoint candidate, GeoPoint center, double radiusMeters)
    {
        if (center == null) throw new ArgumentNullException(nameof(center));
        if (radiusMeters < 0d) throw new ArgumentOutOfRangeException(nameof(radiusMeters));

        return SpatialExpressions.Near(candidate, center, radiusMeters, Options.Distance);
    }

    public static bool Within(GeoShape candidate, GeoPolygon area)
    {
        if (area == null) throw new ArgumentNullException(nameof(area));
        return SpatialExpressions.Within(candidate, area);
    }

    public static bool Intersects(GeoShape candidate, GeoShape query)
    {
        return candidate != null && query != null && SpatialExpressions.Intersects(candidate, query);
    }

    public static bool Contains(GeoShape candidate, GeoPoint point)
    {
        if (point == null) throw new ArgumentNullException(nameof(point));
        return SpatialExpressions.Contains(candidate, point);
    }

    public static async IAsyncEnumerable<T> Near<T>(
        ILiteCollection<T> collection,
        Func<T, GeoPoint> selector,
        GeoPoint center,
        double radiusMeters,
        int? limit = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (collection == null) throw new ArgumentNullException(nameof(collection));
        if (selector == null) throw new ArgumentNullException(nameof(selector));
        if (center == null) throw new ArgumentNullException(nameof(center));
        if (radiusMeters < 0d) throw new ArgumentOutOfRangeException(nameof(radiusMeters));

        var lite = GetLiteCollection(collection);
        var mapper = GetMapper(lite);
        EnsureMapperRegistration(mapper);

        var normalizedCenter = center.Normalize();
        var precisionBits = await GetPointIndexPrecision(lite, cancellationToken).ConfigureAwait(false);
        var boundingBox = GeoMath.BoundingBoxForCircle(normalizedCenter, radiusMeters);
        var queryBoundingBox = ExpandBoundingBoxForQuery(boundingBox);
        var ranges = SpatialIndexing.CoverBoundingBox(queryBoundingBox, precisionBits, Options.MaxCoveringCells);
        var rangePredicate = SpatialQueryBuilder.BuildRangePredicate(ranges);
        var boundingPredicate = SpatialQueryBuilder.BuildBoundingBoxPredicate(queryBoundingBox);
        var predicate = SpatialQueryBuilder.CombineSpatialPredicates(rangePredicate, boundingPredicate);
        var source = predicate != null ? lite.Find(predicate, cancellationToken: cancellationToken) : lite.FindAll(cancellationToken);
        var matches = new List<(T Item, double Distance)>();

        await foreach (var item in source.ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var point = selector(item);

            if (point == null || !queryBoundingBox.Contains(point))
            {
                continue;
            }

            var distance = GeoMath.DistanceMeters(normalizedCenter, point, Options.Distance);
            if (distance <= radiusMeters + GetDistanceToleranceMeters())
            {
                matches.Add((item, distance));
            }
        }

        if (Options.SortNearByDistance)
        {
            matches.Sort((x, y) => x.Distance.CompareTo(y.Distance));
        }

        IEnumerable<(T Item, double Distance)> final = matches;
        if (limit.HasValue)
        {
            final = final.Take(limit.Value);
        }

        foreach (var match in final)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return match.Item;
        }
    }

    public static async IAsyncEnumerable<T> WithinBoundingBox<T>(
        ILiteCollection<T> collection,
        Func<T, GeoPoint> selector,
        double minLat,
        double minLon,
        double maxLat,
        double maxLon,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (collection == null) throw new ArgumentNullException(nameof(collection));
        if (selector == null) throw new ArgumentNullException(nameof(selector));

        var lite = GetLiteCollection(collection);
        var mapper = GetMapper(lite);
        EnsureMapperRegistration(mapper);

        var boundingBox = new GeoBoundingBox(minLat, minLon, maxLat, maxLon);
        var queryBoundingBox = ExpandBoundingBoxForQuery(boundingBox);
        var precisionBits = await GetPointIndexPrecision(lite, cancellationToken).ConfigureAwait(false);
        var ranges = SpatialIndexing.CoverBoundingBox(queryBoundingBox, precisionBits, Options.MaxCoveringCells);
        var rangePredicate = SpatialQueryBuilder.BuildRangePredicate(ranges);
        var boundingPredicate = SpatialQueryBuilder.BuildBoundingBoxPredicate(queryBoundingBox);
        var predicate = SpatialQueryBuilder.CombineSpatialPredicates(rangePredicate, boundingPredicate);
        var source = predicate != null ? lite.Find(predicate, cancellationToken: cancellationToken) : lite.FindAll(cancellationToken);

        await foreach (var entity in source.ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var point = selector(entity);

            if (point != null && queryBoundingBox.Contains(point) && boundingBox.Contains(point))
            {
                yield return entity;
            }
        }
    }

    public static async IAsyncEnumerable<T> Within<T>(
        ILiteCollection<T> collection,
        Func<T, GeoShape> selector,
        GeoPolygon area,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (collection == null) throw new ArgumentNullException(nameof(collection));
        if (selector == null) throw new ArgumentNullException(nameof(selector));
        if (area == null) throw new ArgumentNullException(nameof(area));

        var lite = GetLiteCollection(collection);
        EnsureMapperRegistration(GetMapper(lite));

        var predicate = SpatialQueryBuilder.BuildBoundingBoxPredicate(ExpandBoundingBoxForQuery(area.GetBoundingBox()));
        var source = predicate != null ? lite.Find(predicate, cancellationToken: cancellationToken) : lite.FindAll(cancellationToken);

        await foreach (var entity in source.ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var shape = selector(entity);
            if (shape != null && Within(shape, area))
            {
                yield return entity;
            }
        }
    }

    public static async IAsyncEnumerable<T> Intersects<T>(
        ILiteCollection<T> collection,
        Func<T, GeoShape> selector,
        GeoShape query,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (collection == null) throw new ArgumentNullException(nameof(collection));
        if (selector == null) throw new ArgumentNullException(nameof(selector));
        if (query == null) throw new ArgumentNullException(nameof(query));

        var lite = GetLiteCollection(collection);
        EnsureMapperRegistration(GetMapper(lite));

        var predicate = SpatialQueryBuilder.BuildBoundingBoxPredicate(ExpandBoundingBoxForQuery(query.GetBoundingBox()));
        var source = predicate != null ? lite.Find(predicate, cancellationToken: cancellationToken) : lite.FindAll(cancellationToken);

        await foreach (var entity in source.ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var shape = selector(entity);
            if (shape != null && Intersects(shape, query))
            {
                yield return entity;
            }
        }
    }

    public static async IAsyncEnumerable<T> Contains<T>(
        ILiteCollection<T> collection,
        Func<T, GeoShape> selector,
        GeoPoint point,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (collection == null) throw new ArgumentNullException(nameof(collection));
        if (selector == null) throw new ArgumentNullException(nameof(selector));
        if (point == null) throw new ArgumentNullException(nameof(point));

        var lite = GetLiteCollection(collection);
        EnsureMapperRegistration(GetMapper(lite));

        var box = new GeoBoundingBox(point.Lat, point.Lon, point.Lat, point.Lon);
        var predicate = SpatialQueryBuilder.BuildBoundingBoxPredicate(ExpandBoundingBoxForQuery(box));
        var source = predicate != null ? lite.Find(predicate, cancellationToken: cancellationToken) : lite.FindAll(cancellationToken);

        await foreach (var entity in source.ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var shape = selector(entity);
            if (shape != null && Contains(shape, point))
            {
                yield return entity;
            }
        }
    }

    internal static double GetDistanceToleranceMeters()
    {
        var distanceTolerance = Math.Max(0d, Options.DistanceToleranceMeters);
        return distanceTolerance + GetAngularToleranceMeters();
    }

    private static ValueTask EnsureShapeIndexInternal<T>(ILiteCollection<T> collection, Func<T, GeoShape> getter, CancellationToken cancellationToken)
    {
        if (collection == null) throw new ArgumentNullException(nameof(collection));
        if (getter == null) throw new ArgumentNullException(nameof(getter));

        var lite = GetLiteCollection(collection);
        EnsureMapperRegistration(GetMapper(lite));
        SpatialMapping.EnsureBoundingBox(lite, getter);
        return default;
    }

    private static GeoBoundingBox ExpandBoundingBoxForQuery(GeoBoundingBox box)
    {
        var padding = Math.Max(0d, Options.BoundingBoxPaddingMeters) + GetAngularToleranceMeters();
        return padding > 0d ? box.Expand(padding) : box;
    }

    private static double GetAngularToleranceMeters()
    {
        var toleranceDegrees = Options.ToleranceDegrees;
        return toleranceDegrees <= 0d ? 0d : GeoMath.EarthRadiusMeters * toleranceDegrees * (Math.PI / 180d);
    }

    private static LiteCollection<T> GetLiteCollection<T>(ILiteCollection<T> collection)
    {
        if (collection is LiteCollection<T> liteCollection)
        {
            return liteCollection;
        }

        throw new NotSupportedException("Spatial helpers require LiteCollection<T> instances.");
    }

    private static ILiteEngine GetEngine<T>(LiteCollection<T> collection)
    {
        return typeof(LiteCollection<T>).GetField("_engine", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(collection) as ILiteEngine;
    }

    private static BsonMapper GetMapper<T>(LiteCollection<T> collection)
    {
        return typeof(LiteCollection<T>).GetField("_mapper", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(collection) as BsonMapper ?? BsonMapper.Global;
    }

    private static void EnsureMapperRegistration(BsonMapper mapper)
    {
        lock (MapperLock)
        {
            if (RegisteredMappers.ContainsKey(mapper))
            {
                return;
            }

            SpatialMapping.RegisterGeoTypes(mapper);
            RegisteredMappers[mapper] = true;
        }
    }

    private static async ValueTask PersistPointIndexMetadata<T>(LiteCollection<T> collection, int precisionBits, CancellationToken cancellationToken)
    {
        var engine = GetEngine(collection);
        if (engine == null)
        {
            return;
        }

        var document = new BsonDocument
        {
            ["_id"] = $"{collection.Name}.{PointIndexName}",
            ["collection"] = collection.Name,
            ["index"] = PointIndexName,
            ["precisionBits"] = precisionBits,
            ["updatedUtc"] = DateTime.UtcNow
        };

        await engine.Upsert(MetadataCollection, new[] { document }, BsonAutoId.ObjectId, cancellationToken).ConfigureAwait(false);
        MetadataCache[EngineCollectionKey.Create(engine, collection.Name)] = new SpatialIndexMetadata(precisionBits);
    }

    private static async ValueTask<int> GetPointIndexPrecision<T>(LiteCollection<T> collection, CancellationToken cancellationToken)
    {
        var engine = GetEngine(collection);
        if (engine == null)
        {
            return Options.DefaultIndexPrecisionBits;
        }

        var key = EngineCollectionKey.Create(engine, collection.Name);
        if (MetadataCache.TryGetValue(key, out var metadata))
        {
            return metadata.PrecisionBits;
        }

        var predicate = Query.And(Query.EQ("collection", collection.Name), Query.EQ("index", PointIndexName));

        try
        {
            await foreach (var doc in engine.Query(MetadataCollection, new Query { Where = { predicate } }, cancellationToken).ConfigureAwait(false))
            {
                if (doc.TryGetValue("precisionBits", out var precisionValue) && precisionValue.IsInt32)
                {
                    var precisionBits = precisionValue.AsInt32;
                    MetadataCache[key] = new SpatialIndexMetadata(precisionBits);
                    return precisionBits;
                }
            }
        }
        catch (LiteException)
        {
        }

        return Options.DefaultIndexPrecisionBits;
    }

    private readonly struct SpatialIndexMetadata
    {
        public SpatialIndexMetadata(int precisionBits)
        {
            PrecisionBits = precisionBits;
        }

        public int PrecisionBits { get; }
    }

    private readonly struct EngineCollectionKey : IEquatable<EngineCollectionKey>
    {
        private readonly ILiteEngine _engine;
        private readonly string _collection;

        private EngineCollectionKey(ILiteEngine engine, string collection)
        {
            _engine = engine;
            _collection = collection;
        }

        public static EngineCollectionKey Create(ILiteEngine engine, string collection) => new(engine, collection);

        public bool Equals(EngineCollectionKey other)
        {
            return ReferenceEquals(_engine, other._engine) && string.Equals(_collection, other._collection, StringComparison.OrdinalIgnoreCase);
        }

        public override bool Equals(object obj) => obj is EngineCollectionKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = RuntimeHelpers.GetHashCode(_engine);
                hash = (hash * 397) ^ (_collection == null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(_collection));
                return hash;
            }
        }
    }

    internal static class SpatialMapping
    {
        private static readonly Type EnumerableType = typeof(IEnumerable);

        public static void RegisterGeoTypes(BsonMapper mapper)
        {
            mapper.RegisterType(typeof(GeoShape),
                shape => shape == null ? BsonValue.Null : GeoJson.ToBson((GeoShape)shape),
                bson => DeserializeShape(bson));

            mapper.RegisterType<GeoPoint>(
                point => point == null ? BsonValue.Null : GeoJson.ToBson(point),
                bson => (GeoPoint)DeserializeTyped<GeoPoint>(bson));

            mapper.RegisterType<GeoLineString>(
                line => line == null ? BsonValue.Null : GeoJson.ToBson(line),
                bson => (GeoLineString)DeserializeTyped<GeoLineString>(bson));

            mapper.RegisterType<GeoPolygon>(
                polygon => polygon == null ? BsonValue.Null : GeoJson.ToBson(polygon),
                bson => (GeoPolygon)DeserializeTyped<GeoPolygon>(bson));
        }

        public static void EnsureBoundingBox<T>(LiteCollection<T> collection, Func<T, GeoShape> getter)
        {
            EnsureComputedMember(collection, BoundingBoxFieldName, typeof(double[]), entity =>
            {
                var shape = getter(entity);
                return shape?.GetBoundingBox().ToArray();
            });
        }

        public static void EnsureComputedMember<T>(LiteCollection<T> collection, string fieldName, Type dataType, Func<T, object> getter)
        {
            var entity = collection.EntityMapper ?? throw new InvalidOperationException("Entity mapper not available for collection.");
            entity.WaitForInitialization();

            var member = entity.Members.FirstOrDefault(x => string.Equals(x.FieldName, fieldName, StringComparison.OrdinalIgnoreCase));

            if (member == null)
            {
                var bindingFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                MemberInfo memberInfo = typeof(T).GetProperty(fieldName, bindingFlags);
                memberInfo ??= typeof(T).GetField(fieldName, bindingFlags);

                GenericSetter setter = null;
                if (memberInfo != null)
                {
                    setter = Reflection.CreateGenericSetter(typeof(T), memberInfo);
                }

                member = new MemberMapper
                {
                    FieldName = fieldName,
                    MemberName = memberInfo?.Name ?? fieldName,
                    DataType = dataType,
                    UnderlyingType = dataType,
                    IsEnumerable = EnumerableType.IsAssignableFrom(dataType) && dataType != typeof(string),
                    Getter = obj => getter((T)obj),
                    Setter = setter
                };

                entity.Members.Add(member);
            }
            else
            {
                member.Getter = obj => getter((T)obj);
                member.DataType = dataType;
                member.UnderlyingType = dataType;
                member.IsEnumerable = EnumerableType.IsAssignableFrom(dataType) && dataType != typeof(string);
            }
        }

        private static GeoShape DeserializeShape(BsonValue bson)
        {
            if (bson == null || bson.IsNull)
            {
                return null;
            }

            if (!bson.IsDocument)
            {
                throw new LiteException(0, "GeoJSON value must be a document.");
            }

            return GeoJson.FromBson(bson.AsDocument);
        }

        private static GeoShape DeserializeTyped<TShape>(BsonValue bson) where TShape : GeoShape
        {
            var shape = DeserializeShape(bson);
            if (shape == null)
            {
                return null;
            }

            if (shape is TShape typed)
            {
                return typed;
            }

            throw new LiteException(0, $"GeoJSON payload describes a '{shape.GetType().Name}', not '{typeof(TShape).Name}'.");
        }
    }

    internal static class SpatialIndexing
    {
        public static long ComputeMorton(GeoPoint point, int precisionBits)
        {
            if (point == null) throw new ArgumentNullException(nameof(point));
            if (precisionBits <= 0 || precisionBits > 60) throw new ArgumentOutOfRangeException(nameof(precisionBits), "Precision must be between 1 and 60 bits");

            var normalized = point.Normalize();
            var bitsPerCoordinate = Math.Max(1, precisionBits / 2);
            var scale = (1UL << bitsPerCoordinate) - 1UL;
            var latNormalized = Math.Min(1d, Math.Max(0d, (normalized.Lat + 90d) / 180d));
            var lonNormalized = Math.Min(1d, Math.Max(0d, (normalized.Lon + 180d) / 360d));
            var latBits = (ulong)Math.Round(latNormalized * scale);
            var lonBits = (ulong)Math.Round(lonNormalized * scale);
            return unchecked((long)Interleave(lonBits, latBits, bitsPerCoordinate));
        }

        public static IReadOnlyList<(long Start, long End)> CoverBoundingBox(GeoBoundingBox box, int precisionBits, int maxCells)
        {
            if (precisionBits <= 0)
            {
                return Array.Empty<(long Start, long End)>();
            }

            maxCells = Math.Max(1, maxCells);
            var segments = SplitBoundingBox(box);
            var ranges = new List<(long Start, long End)>();
            var latCells = Math.Max(1, (int)Math.Round(Math.Sqrt(maxCells)));
            var lonCells = Math.Max(1, maxCells / latCells);

            foreach (var segment in segments)
            {
                var latStep = (segment.MaxLat - segment.MinLat) / latCells;
                var lonStep = (segment.MaxLon - segment.MinLon) / lonCells;
                if (latStep == 0d) latStep = segment.MaxLat - segment.MinLat;
                if (lonStep == 0d) lonStep = segment.MaxLon - segment.MinLon;

                for (var latIndex = 0; latIndex < latCells; latIndex++)
                {
                    var cellMinLat = segment.MinLat + latStep * latIndex;
                    var cellMaxLat = latIndex == latCells - 1 ? segment.MaxLat : cellMinLat + latStep;

                    for (var lonIndex = 0; lonIndex < lonCells; lonIndex++)
                    {
                        var cellMinLon = segment.MinLon + lonStep * lonIndex;
                        var cellMaxLon = lonIndex == lonCells - 1 ? segment.MaxLon : cellMinLon + lonStep;
                        var start = ComputeMorton(new GeoPoint(cellMinLat, cellMinLon), precisionBits);
                        var end = ComputeMorton(new GeoPoint(cellMaxLat, cellMaxLon), precisionBits);

                        if (start > end)
                        {
                            (start, end) = (end, start);
                        }

                        ranges.Add((start, end));
                    }
                }
            }

            return MergeRanges(ranges);
        }

        private static ulong Interleave(ulong x, ulong y, int bits)
        {
            ulong result = 0;
            for (var i = 0; i < bits; i++)
            {
                result |= ((x >> i) & 1UL) << (2 * i);
                result |= ((y >> i) & 1UL) << (2 * i + 1);
            }

            return result;
        }

        private static IReadOnlyList<GeoBoundingBox> SplitBoundingBox(GeoBoundingBox box)
        {
            if (box.SpansAllLongitudes || box.MaxLon >= box.MinLon)
            {
                return new[] { box };
            }

            return new[]
            {
                new GeoBoundingBox(box.MinLat, box.MinLon, box.MaxLat, 180d),
                new GeoBoundingBox(box.MinLat, -180d, box.MaxLat, box.MaxLon)
            };
        }

        private static IReadOnlyList<(long Start, long End)> MergeRanges(List<(long Start, long End)> ranges)
        {
            if (ranges.Count == 0)
            {
                return ranges;
            }

            ranges.Sort((a, b) => a.Start.CompareTo(b.Start));
            var merged = new List<(long Start, long End)>(ranges.Count);
            var current = ranges[0];

            for (var i = 1; i < ranges.Count; i++)
            {
                var next = ranges[i];
                if (next.Start <= current.End + 1)
                {
                    current = (current.Start, Math.Max(current.End, next.End));
                }
                else
                {
                    merged.Add(current);
                    current = next;
                }
            }

            merged.Add(current);
            return merged;
        }
    }

    internal static class SpatialQueryBuilder
    {
        public static BsonExpression BuildRangePredicate(IReadOnlyList<(long Start, long End)> ranges)
        {
            if (ranges == null || ranges.Count == 0)
            {
                return null;
            }

            var expressions = new List<BsonExpression>(ranges.Count);
            foreach (var range in ranges)
            {
                expressions.Add(Query.Between("$._gh", range.Start, range.End));
            }

            return CombineOr(expressions);
        }

        public static BsonExpression BuildBoundingBoxPredicate(GeoBoundingBox box)
        {
            if (!box.SpansAllLongitudes && box.MaxLon < box.MinLon)
            {
                return null;
            }

            var parameters = new[]
            {
                new BsonValue(box.MaxLat),
                new BsonValue(box.MinLat),
                new BsonValue(box.MaxLon),
                new BsonValue(box.MinLon)
            };

            const string predicate = "($._mbb != null) AND $._mbb[0] <= @0 AND $._mbb[2] >= @1 AND $._mbb[1] <= @2 AND $._mbb[3] >= @3";
            return BsonExpression.Create(predicate, parameters);
        }

        public static BsonExpression CombineSpatialPredicates(BsonExpression rangeExpression, BsonExpression boundingExpression)
        {
            if (rangeExpression == null)
            {
                return boundingExpression;
            }

            if (boundingExpression == null)
            {
                return rangeExpression;
            }

            var parameters = new BsonDocument();
            if (boundingExpression.Parameters != null)
            {
                foreach (var parameter in boundingExpression.Parameters)
                {
                    parameters[parameter.Key] = parameter.Value;
                }
            }

            return BsonExpression.Create($"({rangeExpression.Source}) AND ({boundingExpression.Source})", parameters);
        }

        private static BsonExpression CombineOr(IReadOnlyList<BsonExpression> expressions)
        {
            if (expressions.Count == 0)
            {
                return null;
            }

            if (expressions.Count == 1)
            {
                return expressions[0];
            }

            var buffer = new BsonExpression[expressions.Count];
            for (var i = 0; i < expressions.Count; i++)
            {
                buffer[i] = expressions[i];
            }

            return Query.Or(buffer);
        }
    }
}

