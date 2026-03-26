using System;
using LiteDbX.Spatial;
using SpatialApi = LiteDbX.Spatial.Spatial;

namespace LiteDbX;

internal partial class BsonExpressionMethods
{
    public static BsonValue SPATIAL_INTERSECTS_MBB(BsonValue mbb, BsonValue minLat, BsonValue minLon, BsonValue maxLat, BsonValue maxLon)
    {
        if (mbb == null || mbb.IsNull || !mbb.IsArray || mbb.AsArray.Count != 4)
        {
            return false;
        }

        if (!minLat.IsNumber || !minLon.IsNumber || !maxLat.IsNumber || !maxLon.IsNumber)
        {
            return false;
        }

        var candidate = ToBoundingBox(mbb);
        var query = new GeoBoundingBox(minLat.AsDouble, minLon.AsDouble, maxLat.AsDouble, maxLon.AsDouble);
        return candidate.Intersects(query);
    }

    public static BsonValue SPATIAL_MBB_INTERSECTS(BsonValue mbb, BsonValue minLat, BsonValue minLon, BsonValue maxLat, BsonValue maxLon)
        => SPATIAL_INTERSECTS_MBB(mbb, minLat, minLon, maxLat, maxLon);

    public static BsonValue SPATIAL_NEAR(BsonValue candidate, BsonValue center, BsonValue radius)
        => SPATIAL_NEAR(candidate, center, radius, BsonValue.Null);

    public static BsonValue SPATIAL_NEAR(BsonValue candidate, BsonValue center, BsonValue radius, BsonValue formula)
    {
        if (!radius.IsNumber)
        {
            return false;
        }

        var candidatePoint = ToShape(candidate) as GeoPoint;
        var centerPoint = ToShape(center) as GeoPoint;

        if (candidatePoint == null || centerPoint == null)
        {
            return false;
        }

        return SpatialExpressions.Near(candidatePoint, centerPoint, radius.AsDouble, ParseFormula(formula));
    }

    public static BsonValue SPATIAL_WITHIN_BOX(BsonValue value, BsonValue minLat, BsonValue minLon, BsonValue maxLat, BsonValue maxLon)
    {
        var shape = ToShape(value);
        if (shape == null)
        {
            return false;
        }

        var box = new GeoBoundingBox(minLat.AsDouble, minLon.AsDouble, maxLat.AsDouble, maxLon.AsDouble);

        return shape switch
        {
            GeoPoint point => SpatialExpressions.WithinBoundingBox(point, box.MinLat, box.MinLon, box.MaxLat, box.MaxLon),
            GeoShape geoShape => geoShape.GetBoundingBox().Intersects(box),
            _ => false
        };
    }

    public static BsonValue SPATIAL_WITHIN(BsonValue candidate, BsonValue polygon)
    {
        var shape = ToShape(candidate);
        var area = ToShape(polygon) as GeoPolygon;
        return shape != null && area != null && SpatialExpressions.Within(shape, area);
    }

    public static BsonValue SPATIAL_INTERSECTS(BsonValue candidate, BsonValue other)
    {
        var left = ToShape(candidate);
        var right = ToShape(other);
        return left != null && right != null && SpatialExpressions.Intersects(left, right);
    }

    public static BsonValue SPATIAL_CONTAINS(BsonValue candidate, BsonValue point)
    {
        var shape = ToShape(candidate);
        var geoPoint = ToShape(point) as GeoPoint;
        return shape != null && geoPoint != null && SpatialExpressions.Contains(shape, geoPoint);
    }

    public static BsonValue SPATIAL_CONTAINS_POINT(BsonValue candidate, BsonValue point)
        => SPATIAL_CONTAINS(candidate, point);

    private static GeoBoundingBox ToBoundingBox(BsonValue value)
    {
        var array = value.AsArray;
        return new GeoBoundingBox(array[0].AsDouble, array[1].AsDouble, array[2].AsDouble, array[3].AsDouble);
    }

    private static GeoShape ToShape(BsonValue value)
    {
        if (value == null || value.IsNull)
        {
            return null;
        }

        if (value.IsDocument)
        {
            return GeoJson.FromBson(value.AsDocument);
        }

        if (value.IsArray && value.AsArray.Count >= 2)
        {
            return new GeoPoint(value.AsArray[1].AsDouble, value.AsArray[0].AsDouble);
        }

        if (value.RawValue is GeoShape shape)
        {
            return shape;
        }

        return null;
    }

    private static DistanceFormula ParseFormula(BsonValue formula)
    {
        if (formula == null || formula.IsNull)
        {
            return SpatialApi.Options.Distance;
        }

        if (formula.IsString && Enum.TryParse(formula.AsString, out DistanceFormula parsed))
        {
            return parsed;
        }

        if (formula.IsInt32)
        {
            return (DistanceFormula)formula.AsInt32;
        }

        return SpatialApi.Options.Distance;
    }
}

