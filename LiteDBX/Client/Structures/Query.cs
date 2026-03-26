using System;
using System.Collections.Generic;
using LiteDbX.Spatial;

namespace LiteDbX;

/// <summary>
/// Class is a result from optimized QueryBuild. Indicate how engine must run query - there is no more decisions to engine
/// made, must only execute as query was defined
/// </summary>
public partial class Query
{
    /// <summary>
    /// Indicate when a query must execute in ascending order
    /// </summary>
    public const int Ascending = 1;

    /// <summary>
    /// Indicate when a query must execute in descending order
    /// </summary>
    public const int Descending = -1;

    /// <summary>
    /// Returns all documents
    /// </summary>
    public static Query All()
    {
        return new Query();
    }

    /// <summary>
    /// Returns all documents
    /// </summary>
    public static Query All(int order = Ascending)
    {
        return new Query { OrderBy = "_id", Order = order };
    }

    /// <summary>
    /// Returns all documents
    /// </summary>
    public static Query All(string field, int order = Ascending)
    {
        return new Query { OrderBy = field, Order = order };
    }

    /// <summary>
    /// Returns all documents that value are equals to value (=)
    /// </summary>
    public static BsonExpression EQ(string field, BsonValue value)
    {
        if (field.IsNullOrWhiteSpace())
        {
            throw new ArgumentNullException(nameof(field));
        }

        return BsonExpression.Create($"{field} = {value ?? BsonValue.Null}");
    }

    /// <summary>
    /// Returns all documents that value are less than value (&lt;)
    /// </summary>
    public static BsonExpression LT(string field, BsonValue value)
    {
        if (field.IsNullOrWhiteSpace())
        {
            throw new ArgumentNullException(nameof(field));
        }

        return BsonExpression.Create($"{field} < {value ?? BsonValue.Null}");
    }

    /// <summary>
    /// Returns all documents that value are less than or equals value (&lt;=)
    /// </summary>
    public static BsonExpression LTE(string field, BsonValue value)
    {
        if (field.IsNullOrWhiteSpace())
        {
            throw new ArgumentNullException(nameof(field));
        }

        return BsonExpression.Create($"{field} <= {value ?? BsonValue.Null}");
    }

    /// <summary>
    /// Returns all document that value are greater than value (&gt;)
    /// </summary>
    public static BsonExpression GT(string field, BsonValue value)
    {
        if (field.IsNullOrWhiteSpace())
        {
            throw new ArgumentNullException(nameof(field));
        }

        return BsonExpression.Create($"{field} > {value ?? BsonValue.Null}");
    }

    /// <summary>
    /// Returns all documents that value are greater than or equals value (&gt;=)
    /// </summary>
    public static BsonExpression GTE(string field, BsonValue value)
    {
        if (field.IsNullOrWhiteSpace())
        {
            throw new ArgumentNullException(nameof(field));
        }

        return BsonExpression.Create($"{field} >= {value ?? BsonValue.Null}");
    }

    /// <summary>
    /// Returns all document that values are between "start" and "end" values (BETWEEN)
    /// </summary>
    public static BsonExpression Between(string field, BsonValue start, BsonValue end)
    {
        if (field.IsNullOrWhiteSpace())
        {
            throw new ArgumentNullException(nameof(field));
        }

        return BsonExpression.Create($"{field} BETWEEN {start ?? BsonValue.Null} AND {end ?? BsonValue.Null}");
    }

    /// <summary>
    /// Returns all documents that starts with value (LIKE)
    /// </summary>
    public static BsonExpression StartsWith(string field, string value)
    {
        if (field.IsNullOrWhiteSpace())
        {
            throw new ArgumentNullException(nameof(field));
        }

        if (value.IsNullOrWhiteSpace())
        {
            throw new ArgumentNullException(nameof(value));
        }

        return BsonExpression.Create($"{field} LIKE {new BsonValue(value + "%")}");
    }

    /// <summary>
    /// Returns all documents that contains value (CONTAINS) - string Contains
    /// </summary>
    public static BsonExpression Contains(string field, string value)
    {
        if (field.IsNullOrWhiteSpace())
        {
            throw new ArgumentNullException(nameof(field));
        }

        if (value.IsNullOrEmpty())
        {
            throw new ArgumentNullException(nameof(value));
        }

        return BsonExpression.Create($"{field} LIKE {new BsonValue("%" + value + "%")}");
    }

    /// <summary>
    /// Returns all documents that are not equals to value (not equals)
    /// </summary>
    public static BsonExpression Not(string field, BsonValue value)
    {
        if (field.IsNullOrWhiteSpace())
        {
            throw new ArgumentNullException(nameof(field));
        }

        return BsonExpression.Create($"{field} != {value ?? BsonValue.Null}");
    }

    /// <summary>
    /// Returns all documents that has value in values list (IN)
    /// </summary>
    public static BsonExpression In(string field, BsonArray value)
    {
        if (field.IsNullOrWhiteSpace())
        {
            throw new ArgumentNullException(nameof(field));
        }

        if (value == null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        return BsonExpression.Create($"{field} IN {value}");
    }

    /// <summary>
    /// Returns all documents that has value in values list (IN)
    /// </summary>
    public static BsonExpression In(string field, params BsonValue[] values)
    {
        return In(field, new BsonArray(values));
    }

    /// <summary>
    /// Returns all documents that has value in values list (IN)
    /// </summary>
    public static BsonExpression In(string field, IEnumerable<BsonValue> values)
    {
        return In(field, new BsonArray(values));
    }

    /// <summary>
    /// Returns all documents whose point at <paramref name="field"/> lies within <paramref name="radiusMeters"/> of <paramref name="center"/>.
    /// </summary>
    public static BsonExpression Near(string field, GeoPoint center, double radiusMeters)
    {
        return Near(field, center, radiusMeters, LiteDbX.Spatial.Spatial.Options.Distance);
    }

    /// <summary>
    /// Returns all documents whose point at <paramref name="field"/> lies within <paramref name="radiusMeters"/> of <paramref name="center"/> using the specified distance formula.
    /// </summary>
    public static BsonExpression Near(string field, GeoPoint center, double radiusMeters, DistanceFormula formula)
    {
        ValidateSpatialField(field);

        if (center == null)
        {
            throw new ArgumentNullException(nameof(center));
        }

        if (radiusMeters < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(radiusMeters));
        }

        EnsureSpatialMapperRegistration();

        return BsonExpression.Create($"SPATIAL_NEAR({field}, @0, @1, @2)", SerializeSpatial(center), radiusMeters, formula.ToString());
    }

    /// <summary>
    /// Returns all documents whose spatial value at <paramref name="field"/> intersects the supplied bounding box.
    /// </summary>
    public static BsonExpression WithinBoundingBox(string field, double minLat, double minLon, double maxLat, double maxLon)
    {
        ValidateSpatialField(field);
        return BsonExpression.Create($"SPATIAL_WITHIN_BOX({field}, @0, @1, @2, @3)", minLat, minLon, maxLat, maxLon);
    }

    /// <summary>
    /// Returns all documents whose spatial value at <paramref name="field"/> lies within the supplied polygon.
    /// </summary>
    public static BsonExpression Within(string field, GeoPolygon polygon)
    {
        ValidateSpatialField(field);

        if (polygon == null)
        {
            throw new ArgumentNullException(nameof(polygon));
        }

        EnsureSpatialMapperRegistration();

        return BsonExpression.Create($"SPATIAL_WITHIN({field}, @0)", SerializeSpatial(polygon));
    }

    /// <summary>
    /// Returns all documents whose spatial value at <paramref name="field"/> intersects <paramref name="shape"/>.
    /// </summary>
    public static BsonExpression Intersects(string field, GeoShape shape)
    {
        ValidateSpatialField(field);

        if (shape == null)
        {
            throw new ArgumentNullException(nameof(shape));
        }

        EnsureSpatialMapperRegistration();

        return BsonExpression.Create($"SPATIAL_INTERSECTS({field}, @0)", SerializeSpatial(shape));
    }

    /// <summary>
    /// Returns all documents whose spatial value at <paramref name="field"/> contains <paramref name="point"/>.
    /// </summary>
    public static BsonExpression ContainsPoint(string field, GeoPoint point)
    {
        ValidateSpatialField(field);

        if (point == null)
        {
            throw new ArgumentNullException(nameof(point));
        }

        EnsureSpatialMapperRegistration();

        return BsonExpression.Create($"SPATIAL_CONTAINS_POINT({field}, @0)", SerializeSpatial(point));
    }

    /// <summary>
    /// Get all operands to works with array or enumerable values
    /// </summary>
    public static QueryAny Any()
    {
        return new QueryAny();
    }

    private static void ValidateSpatialField(string field)
    {
        if (field.IsNullOrWhiteSpace())
        {
            throw new ArgumentNullException(nameof(field));
        }
    }

    private static void EnsureSpatialMapperRegistration()
    {
        LiteDbX.Spatial.Spatial.Register();
    }

    private static BsonValue SerializeSpatial(GeoShape shape)
    {
        return BsonMapper.Global.Serialize(shape.GetType(), shape);
    }

    /// <summary>
    /// Returns document that exists in BOTH queries results. If both queries has indexes, left query has index preference
    /// (other side will be run in full scan)
    /// </summary>
    public static BsonExpression And(BsonExpression left, BsonExpression right)
    {
        if (left == null)
        {
            throw new ArgumentNullException(nameof(left));
        }

        if (right == null)
        {
            throw new ArgumentNullException(nameof(right));
        }

        return $"({left.Source} AND {right.Source})";
    }

    /// <summary>
    /// Returns document that exists in ALL queries results.
    /// </summary>
    public static BsonExpression And(params BsonExpression[] queries)
    {
        if (queries == null || queries.Length < 2)
        {
            throw new ArgumentException("At least two Query should be passed");
        }

        var left = queries[0];

        for (var i = 1; i < queries.Length; i++)
        {
            left = And(left, queries[i]);
        }

        return left;
    }

    /// <summary>
    /// Returns documents that exists in ANY queries results (Union).
    /// </summary>
    public static BsonExpression Or(BsonExpression left, BsonExpression right)
    {
        if (left == null)
        {
            throw new ArgumentNullException(nameof(left));
        }

        if (right == null)
        {
            throw new ArgumentNullException(nameof(right));
        }

        return $"({left.Source} OR {right.Source})";
    }

    /// <summary>
    /// Returns document that exists in ANY queries results (Union).
    /// </summary>
    public static BsonExpression Or(params BsonExpression[] queries)
    {
        if (queries == null || queries.Length < 2)
        {
            throw new ArgumentException("At least two Query should be passed");
        }

        var left = queries[0];

        for (var i = 1; i < queries.Length; i++)
        {
            left = Or(left, queries[i]);
        }

        return left;
    }
}