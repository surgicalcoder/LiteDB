using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace LiteDbX.Spatial;

public enum DistanceFormula
{
	Haversine,
	Vincenty
}

public enum AngleUnit
{
	Degrees,
	Radians
}

public sealed class SpatialOptions
{
	private int _defaultIndexPrecisionBits = 52;
	private double _toleranceDegrees = 1e-9;

	public DistanceFormula Distance { get; set; } = DistanceFormula.Haversine;

	public bool SortNearByDistance { get; set; } = true;

	public int MaxCoveringCells { get; set; } = 32;

	public AngleUnit AngleUnit { get; set; } = AngleUnit.Degrees;

	public int DefaultIndexPrecisionBits
	{
		get => _defaultIndexPrecisionBits;
		set => _defaultIndexPrecisionBits = value;
	}

	public int IndexPrecisionBits
	{
		get => _defaultIndexPrecisionBits;
		set => _defaultIndexPrecisionBits = value;
	}

	public double NumericToleranceDegrees
	{
		get => _toleranceDegrees;
		set => _toleranceDegrees = value;
	}

	public double ToleranceDegrees
	{
		get => _toleranceDegrees;
		set => _toleranceDegrees = value;
	}

	public double BoundingBoxPaddingMeters { get; set; }

	public double DistanceToleranceMeters { get; set; } = 0.001d;
}

public abstract class GeoShape
{
	internal abstract GeoBoundingBox GetBoundingBox();
}

public sealed class GeoPoint : GeoShape
{
	public GeoPoint(double lat, double lon)
	{
		GeoValidation.EnsureValidCoordinate(lat, lon);

		Lat = GeoMath.ClampLatitude(lat);
		Lon = GeoMath.NormalizeLongitude(lon);
	}

	public double Lat { get; }

	public double Lon { get; }

	internal override GeoBoundingBox GetBoundingBox() => new(Lat, Lon, Lat, Lon);

	public GeoPoint Normalize() => new(Lat, Lon);

	public override string ToString() => $"({Lat:F6}, {Lon:F6})";
}

public sealed class GeoLineString : GeoShape
{
	public GeoLineString(IReadOnlyList<GeoPoint> points)
	{
		if (points == null)
		{
			throw new ArgumentNullException(nameof(points));
		}

		if (points.Count < 2)
		{
			throw new ArgumentException("LineString requires at least two points", nameof(points));
		}

		Points = new ReadOnlyCollection<GeoPoint>(points.Select(p => p ?? throw new ArgumentNullException(nameof(points), "LineString points cannot contain null"))
														.Select(p => p.Normalize())
														.ToList());
	}

	public IReadOnlyList<GeoPoint> Points { get; }

	internal override GeoBoundingBox GetBoundingBox() => GeoBoundingBox.FromPoints(Points);
}

public sealed class GeoPolygon : GeoShape
{
	public GeoPolygon(IReadOnlyList<GeoPoint> outer, IReadOnlyList<IReadOnlyList<GeoPoint>> holes = null)
	{
		if (outer == null)
		{
			throw new ArgumentNullException(nameof(outer));
		}

		if (outer.Count < 4)
		{
			throw new ArgumentException("Polygon outer ring must contain at least four points including closure", nameof(outer));
		}

		var normalizedOuter = NormalizeRing(outer, nameof(outer));
		Outer = new ReadOnlyCollection<GeoPoint>(normalizedOuter);

		if (holes == null)
		{
			Holes = Array.Empty<IReadOnlyList<GeoPoint>>();
		}
		else
		{
			var normalizedHoles = holes.Select((hole, index) =>
			{
				var points = NormalizeRing(hole, $"holes[{index}]");
				return (IReadOnlyList<GeoPoint>)new ReadOnlyCollection<GeoPoint>(points);
			}).ToList();

			Holes = new ReadOnlyCollection<IReadOnlyList<GeoPoint>>(normalizedHoles);
		}

		foreach (var hole in Holes)
		{
			if (!Geometry.IsRingInside(Outer, hole))
			{
				throw new ArgumentException("Polygon hole must lie within the outer ring", nameof(holes));
			}

			foreach (var other in Holes)
			{
				if (!ReferenceEquals(hole, other) && Geometry.RingsOverlap(hole, other))
				{
					throw new ArgumentException("Polygon holes must not overlap", nameof(holes));
				}
			}
		}
	}

	public IReadOnlyList<GeoPoint> Outer { get; }

	public IReadOnlyList<IReadOnlyList<GeoPoint>> Holes { get; }

	internal override GeoBoundingBox GetBoundingBox()
	{
		var allPoints = new List<GeoPoint>(Outer.Count + Holes.Sum(h => h.Count));
		allPoints.AddRange(Outer);

		foreach (var hole in Holes)
		{
			allPoints.AddRange(hole);
		}

		return GeoBoundingBox.FromPoints(allPoints);
	}

	private static List<GeoPoint> NormalizeRing(IReadOnlyList<GeoPoint> ring, string argumentName)
	{
		if (ring == null)
		{
			throw new ArgumentNullException(argumentName);
		}

		if (ring.Count < 4)
		{
			throw new ArgumentException("Polygon rings must contain at least four points including closure", argumentName);
		}

		var normalized = ring.Select(p => p ?? throw new ArgumentNullException(argumentName, "Polygon ring point cannot be null"))
							 .Select(p => p.Normalize())
							 .ToList();

		if (!Geometry.IsRingClosed(normalized))
		{
			throw new ArgumentException("Polygon rings must be closed (first point equals last point)", argumentName);
		}

		if (Geometry.HasSelfIntersection(normalized))
		{
			throw new ArgumentException("Polygon rings must not self-intersect", argumentName);
		}

		return normalized;
	}
}

public static class GeoJson
{
	public static string Serialize(GeoShape shape)
	{
		if (shape == null)
		{
			throw new ArgumentNullException(nameof(shape));
		}

		return JsonSerializer.Serialize(ToBson(shape), indent: false);
	}

	public static T Deserialize<T>(string json) where T : GeoShape
	{
		var shape = Deserialize(json);

		if (shape is T typed)
		{
			return typed;
		}

		throw new LiteException(0, $"GeoJSON payload describes a '{shape?.GetType().Name}', not '{typeof(T).Name}'.");
	}

	public static GeoShape Deserialize(string json)
	{
		if (json == null)
		{
			throw new ArgumentNullException(nameof(json));
		}

		var bson = JsonSerializer.Deserialize(json);

		if (!bson.IsDocument)
		{
			throw new LiteException(0, "GeoJSON payload must be a JSON object");
		}

		var document = bson.AsDocument;

		if (document.ContainsKey("crs"))
		{
			throw new LiteException(0, "Only the default WGS84 CRS is supported.");
		}

		return FromBson(document);
	}

	internal static BsonValue ToBson(GeoShape shape)
	{
		switch (shape)
		{
			case null:
				return BsonValue.Null;

			case GeoPoint point:
				return new BsonDocument
				{
					["type"] = "Point",
					["coordinates"] = new BsonArray { point.Lon, point.Lat }
				};

			case GeoLineString line:
				return new BsonDocument
				{
					["type"] = "LineString",
					["coordinates"] = new BsonArray(line.Points.Select(p => new BsonArray { p.Lon, p.Lat }))
				};

			case GeoPolygon polygon:
				return new BsonDocument
				{
					["type"] = "Polygon",
					["coordinates"] = BuildPolygonCoordinates(polygon)
				};

			default:
				throw new LiteException(0, $"Unsupported GeoShape type '{shape.GetType().Name}'.");
		}
	}

	internal static GeoShape FromBson(BsonDocument document)
	{
		if (document == null)
		{
			throw new ArgumentNullException(nameof(document));
		}

		if (!document.TryGetValue("type", out var typeValue) || !typeValue.IsString)
		{
			throw new LiteException(0, "GeoJSON requires a string 'type' property");
		}

		return typeValue.AsString switch
		{
			"Point" => ParsePoint(document),
			"LineString" => ParseLineString(document),
			"Polygon" => ParsePolygon(document),
			_ => throw new LiteException(0, $"Unsupported GeoJSON geometry type '{typeValue.AsString}'")
		};
	}

	private static BsonArray BuildPolygonCoordinates(GeoPolygon polygon)
	{
		var rings = new List<BsonArray>
		{
			new(polygon.Outer.Select(p => new BsonArray { p.Lon, p.Lat }))
		};

		foreach (var hole in polygon.Holes)
		{
			rings.Add(new BsonArray(hole.Select(p => new BsonArray { p.Lon, p.Lat })));
		}

		return new BsonArray(rings);
	}

	private static GeoPoint ParsePoint(BsonDocument document)
	{
		var (lon, lat) = ReadCoordinateArray(document);
		return new GeoPoint(lat, lon);
	}

	private static GeoLineString ParseLineString(BsonDocument document)
	{
		var array = EnsureArray(document, "coordinates");
		return new GeoLineString(array.Select(ToPoint).ToList());
	}

	private static GeoPolygon ParsePolygon(BsonDocument document)
	{
		var array = EnsureArray(document, "coordinates");

		if (array.Count == 0)
		{
			throw new LiteException(0, "Polygon must contain at least one ring");
		}

		var rings = array.Select(ToRing).ToList();
		var outer = rings[0];
		var holes = rings.Skip(1).Select(x => (IReadOnlyList<GeoPoint>)x).ToList();
		return new GeoPolygon(outer, holes);
	}

	private static (double lon, double lat) ReadCoordinateArray(BsonDocument document)
	{
		var array = EnsureArray(document, "coordinates");

		if (array.Count < 2)
		{
			throw new LiteException(0, "Coordinate array must contain longitude and latitude");
		}

		return (array[0].AsDouble, array[1].AsDouble);
	}

	private static List<GeoPoint> ToRing(BsonValue value)
	{
		if (!value.IsArray)
		{
			throw new LiteException(0, "Ring must be an array of positions");
		}

		return value.AsArray.Select(ToPoint).ToList();
	}

	private static GeoPoint ToPoint(BsonValue value)
	{
		if (!value.IsArray)
		{
			throw new LiteException(0, "Point must be expressed as an array");
		}

		var coords = value.AsArray;

		if (coords.Count < 2)
		{
			throw new LiteException(0, "Point array must contain longitude and latitude");
		}

		return new GeoPoint(coords[1].AsDouble, coords[0].AsDouble);
	}

	private static BsonArray EnsureArray(BsonDocument document, string key)
	{
		if (!document.TryGetValue(key, out var value) || !value.IsArray)
		{
			throw new LiteException(0, $"GeoJSON requires '{key}' to be an array");
		}

		return value.AsArray;
	}
}

internal static class GeoValidation
{
	private const double LatitudeMin = -90d;
	private const double LatitudeMax = 90d;
	private const double LongitudeMin = -180d;
	private const double LongitudeMax = 180d;

	public static void EnsureValidCoordinate(double lat, double lon)
	{
		if (lat < LatitudeMin || lat > LatitudeMax)
		{
			throw new ArgumentOutOfRangeException(nameof(lat), $"Latitude must be between {LatitudeMin} and {LatitudeMax}");
		}

		if (lon < LongitudeMin || lon > LongitudeMax)
		{
			throw new ArgumentOutOfRangeException(nameof(lon), $"Longitude must be between {LongitudeMin} and {LongitudeMax}");
		}
	}
}

internal readonly struct LongitudeRange
{
	private readonly double _start;
	private readonly double _end;
	private readonly bool _wraps;

	public LongitudeRange(double start, double end)
	{
		_start = GeoMath.NormalizeLongitude(start);
		_end = GeoMath.NormalizeLongitude(end);
		_wraps = _start > _end;
	}

	public bool Contains(double lon)
	{
		lon = GeoMath.NormalizeLongitude(lon);

		if (!_wraps)
		{
			return lon >= _start && lon <= _end;
		}

		return lon >= _start || lon <= _end;
	}

	public bool Intersects(LongitudeRange other)
	{
		if (!_wraps && !other._wraps)
		{
			return !(_start > other._end || other._start > _end);
		}

		for (var i = 0; i < 2; i++)
		{
			var aStart = i == 0 ? _start : -180d;
			var aEnd = i == 0 ? (_wraps ? 180d : _end) : _end;

			if (aStart > aEnd)
			{
				continue;
			}

			for (var j = 0; j < 2; j++)
			{
				var bStart = j == 0 ? other._start : -180d;
				var bEnd = j == 0 ? (other._wraps ? 180d : other._end) : other._end;

				if (bStart > bEnd)
				{
					continue;
				}

				if (!(aStart > bEnd || bStart > aEnd))
				{
					return true;
				}
			}
		}

		return false;
	}
}

internal readonly struct GeoBoundingBox
{
	public GeoBoundingBox(double minLat, double minLon, double maxLat, double maxLon)
	{
		MinLat = GeoMath.ClampLatitude(Math.Min(minLat, maxLat));
		MaxLat = GeoMath.ClampLatitude(Math.Max(minLat, maxLat));

		var rawSpan = Math.Abs(maxLon - minLon);
		var spansFull = double.IsInfinity(rawSpan) || rawSpan >= 360d - GeoMath.EpsilonDegrees;

		if (spansFull)
		{
			SpansAllLongitudes = true;
			var offset = Math.Max(GeoMath.EpsilonDegrees / 2d, 1e-12);
			MinLon = -180d + offset;
			MaxLon = 180d - offset;
		}
		else
		{
			SpansAllLongitudes = false;
			MinLon = GeoMath.NormalizeLongitude(minLon);
			MaxLon = GeoMath.NormalizeLongitude(maxLon);
		}
	}

	public double MinLat { get; }
	public double MinLon { get; }
	public double MaxLat { get; }
	public double MaxLon { get; }
	public bool SpansAllLongitudes { get; }

	public static GeoBoundingBox FromPoints(IEnumerable<GeoPoint> points)
	{
		if (points == null)
		{
			throw new ArgumentNullException(nameof(points));
		}

		var list = points.ToList();

		if (list.Count == 0)
		{
			throw new ArgumentException("Bounding box requires at least one point", nameof(points));
		}

		return new GeoBoundingBox(list.Min(p => p.Lat), list.Min(p => p.Lon), list.Max(p => p.Lat), list.Max(p => p.Lon));
	}

	public double[] ToArray() => new[] { MinLat, MinLon, MaxLat, MaxLon };

	public bool Contains(GeoPoint point)
	{
		if (point == null)
		{
			return false;
		}

		return point.Lat >= MinLat && point.Lat <= MaxLat && IsLongitudeWithin(point.Lon);
	}

	public bool Intersects(GeoBoundingBox other)
	{
		if (other.MinLat > MaxLat || other.MaxLat < MinLat)
		{
			return false;
		}

		if (SpansAllLongitudes || other.SpansAllLongitudes)
		{
			return true;
		}

		return LongitudesOverlap(other);
	}

	public GeoBoundingBox Expand(double meters)
	{
		if (meters <= 0d)
		{
			return this;
		}

		var angularDistance = meters / GeoMath.EarthRadiusMeters;
		var deltaDegrees = angularDistance * (180d / Math.PI);
		var minLat = GeoMath.ClampLatitude(MinLat - deltaDegrees);
		var maxLat = GeoMath.ClampLatitude(MaxLat + deltaDegrees);

		if (SpansAllLongitudes)
		{
			return new GeoBoundingBox(minLat, -180d, maxLat, 180d);
		}

		return new GeoBoundingBox(minLat, GeoMath.NormalizeLongitude(MinLon - deltaDegrees), maxLat, GeoMath.NormalizeLongitude(MaxLon + deltaDegrees));
	}

	private bool LongitudesOverlap(GeoBoundingBox other)
	{
		var lonRange = new LongitudeRange(MinLon, MaxLon);
		var otherRange = new LongitudeRange(other.MinLon, other.MaxLon);
		return lonRange.Intersects(otherRange);
	}

	private bool IsLongitudeWithin(double lon)
	{
		if (SpansAllLongitudes)
		{
			return true;
		}

		return new LongitudeRange(MinLon, MaxLon).Contains(lon);
	}
}

internal static class GeoMath
{
	public const double EarthRadiusMeters = 6_371_000d;

	private const double DegToRad = Math.PI / 180d;
	private const double RadToDeg = 180d / Math.PI;
	private const double Wgs84EquatorialRadius = 6_378_137d;
	private const double Wgs84Flattening = 1d / 298.257223563d;
	private const double Wgs84PolarRadius = Wgs84EquatorialRadius * (1d - Wgs84Flattening);

	internal static double EpsilonDegrees => Spatial.Options.ToleranceDegrees;

	public static double ClampLatitude(double latitude) => Math.Max(-90d, Math.Min(90d, latitude));

	public static double NormalizeLongitude(double lon)
	{
		if (double.IsNaN(lon))
		{
			return lon;
		}

		var result = lon % 360d;

		if (result <= -180d)
		{
			result += 360d;
		}
		else if (result > 180d)
		{
			result -= 360d;
		}

		return result;
	}

	public static double ToRadians(double degrees) => degrees * DegToRad;

	public static double DistanceMeters(GeoPoint a, GeoPoint b, DistanceFormula formula = DistanceFormula.Haversine)
	{
		if (a == null) throw new ArgumentNullException(nameof(a));
		if (b == null) throw new ArgumentNullException(nameof(b));

		return formula switch
		{
			DistanceFormula.Haversine => Haversine(a, b),
			DistanceFormula.Vincenty => Vincenty(a, b),
			_ => Haversine(a, b)
		};
	}

	internal static GeoBoundingBox BoundingBoxForCircle(GeoPoint center, double radiusMeters)
	{
		if (center == null)
		{
			throw new ArgumentNullException(nameof(center));
		}

		if (radiusMeters < 0d)
		{
			throw new ArgumentOutOfRangeException(nameof(radiusMeters));
		}

		var angularDistance = radiusMeters / EarthRadiusMeters;
		var centerLatRadians = ToRadians(center.Lat);
		var minLatRadians = centerLatRadians - angularDistance;
		var maxLatRadians = centerLatRadians + angularDistance;
		var minLat = ClampLatitude(minLatRadians * RadToDeg);
		var maxLat = ClampLatitude(maxLatRadians * RadToDeg);

		double minLon;
		double maxLon;

		if (minLatRadians <= -Math.PI / 2d || maxLatRadians >= Math.PI / 2d)
		{
			minLon = -180d;
			maxLon = 180d;
		}
		else
		{
			var cosLat = Math.Cos(centerLatRadians);

			if (cosLat <= 0d)
			{
				minLon = -180d;
				maxLon = 180d;
			}
			else
			{
				var ratio = Math.Min(1d, Math.Max(-1d, Math.Sin(angularDistance) / cosLat));
				var deltaLon = Math.Asin(ratio) * RadToDeg;
				minLon = NormalizeLongitude(center.Lon - deltaLon);
				maxLon = NormalizeLongitude(center.Lon + deltaLon);
			}
		}

		return new GeoBoundingBox(minLat, minLon, maxLat, maxLon);
	}

	private static double Haversine(GeoPoint a, GeoPoint b, bool allowVincentyFallback = true)
	{
		if (allowVincentyFallback && Math.Abs(a.Lat) > 89d && Math.Abs(b.Lat) > 89d)
		{
			var lonDifference = Math.Abs(NormalizeLongitude(b.Lon - a.Lon));

			if (lonDifference > 135d)
			{
				return Vincenty(a, b);
			}

			return EarthRadiusMeters * ToRadians(Math.Abs(a.Lat - b.Lat));
		}

		var lat1 = ToRadians(a.Lat);
		var lat2 = ToRadians(b.Lat);
		var dLat = lat2 - lat1;
		var dLon = ToRadians(NormalizeLongitude(b.Lon - a.Lon));
		var hav = Math.Sin(dLat / 2d) * Math.Sin(dLat / 2d) +
				  Math.Cos(lat1) * Math.Cos(lat2) * Math.Sin(dLon / 2d) * Math.Sin(dLon / 2d);

		hav = Math.Min(1d, Math.Max(0d, hav));

		return EarthRadiusMeters * 2d * Math.Atan2(Math.Sqrt(hav), Math.Sqrt(Math.Max(0d, 1d - hav)));
	}

	private static double Vincenty(GeoPoint a, GeoPoint b)
	{
		var phi1 = ToRadians(a.Lat);
		var phi2 = ToRadians(b.Lat);
		var lambda = ToRadians(NormalizeLongitude(b.Lon - a.Lon));
		var f = Wgs84Flattening;
		var aRadius = Wgs84EquatorialRadius;
		var bRadius = Wgs84PolarRadius;
		var tanU1 = (1d - f) * Math.Tan(phi1);
		var cosU1 = 1d / Math.Sqrt(1d + tanU1 * tanU1);
		var sinU1 = tanU1 * cosU1;
		var tanU2 = (1d - f) * Math.Tan(phi2);
		var cosU2 = 1d / Math.Sqrt(1d + tanU2 * tanU2);
		var sinU2 = tanU2 * cosU2;
		var lambdaIter = lambda;
		double lambdaPrev;
		const int maxIterations = 100;
		var iteration = 0;
		double sinSigma;
		double cosSigma;
		double sigma;
		double cosSqAlpha;
		double cos2SigmaM = 0d;

		do
		{
			var sinLambda = Math.Sin(lambdaIter);
			var cosLambda = Math.Cos(lambdaIter);
			var term1 = cosU2 * sinLambda;
			var term2 = cosU1 * sinU2 - sinU1 * cosU2 * cosLambda;
			sinSigma = Math.Sqrt(term1 * term1 + term2 * term2);

			if (sinSigma == 0d)
			{
				return 0d;
			}

			cosSigma = sinU1 * sinU2 + cosU1 * cosU2 * cosLambda;
			sigma = Math.Atan2(sinSigma, cosSigma);
			var sinAlpha = cosU1 * cosU2 * sinLambda / sinSigma;
			cosSqAlpha = 1d - sinAlpha * sinAlpha;
			cos2SigmaM = cosSqAlpha != 0d ? cosSigma - 2d * sinU1 * sinU2 / cosSqAlpha : 0d;
			var c = f / 16d * cosSqAlpha * (4d + f * (4d - 3d * cosSqAlpha));
			lambdaPrev = lambdaIter;
			lambdaIter = lambda + (1d - c) * f * sinAlpha * (sigma + c * sinSigma * (cos2SigmaM + c * cosSigma * (-1d + 2d * cos2SigmaM * cos2SigmaM)));
			iteration++;
		}
		while (Math.Abs(lambdaIter - lambdaPrev) > 1e-12 && iteration < maxIterations);

		if (iteration == maxIterations)
		{
			return Haversine(a, b, allowVincentyFallback: false);
		}

		var uSq = cosSqAlpha * (aRadius * aRadius - bRadius * bRadius) / (bRadius * bRadius);
		var bigA = 1d + uSq / 16384d * (4096d + uSq * (-768d + uSq * (320d - 175d * uSq)));
		var bigB = uSq / 1024d * (256d + uSq * (-128d + uSq * (74d - 47d * uSq)));
		var deltaSigma = bigB * sinSigma * (cos2SigmaM + bigB / 4d * (cosSigma * (-1d + 2d * cos2SigmaM * cos2SigmaM) - bigB / 6d * cos2SigmaM * (-3d + 4d * sinSigma * sinSigma) * (-3d + 4d * cos2SigmaM * cos2SigmaM)));
		return bRadius * bigA * (sigma - deltaSigma);
	}
}

internal static class Geometry
{
	private const double Epsilon = 1e-9;

	public static bool ContainsPoint(GeoPolygon polygon, GeoPoint point)
	{
		if (polygon == null) throw new ArgumentNullException(nameof(polygon));
		if (point == null) throw new ArgumentNullException(nameof(point));

		if (!IsPointInRing(polygon.Outer, point))
		{
			return false;
		}

		foreach (var hole in polygon.Holes)
		{
			if (IsPointInRing(hole, point))
			{
				return false;
			}
		}

		return true;
	}

	public static bool Intersects(GeoLineString line, GeoPolygon polygon)
	{
		if (line == null) throw new ArgumentNullException(nameof(line));
		if (polygon == null) throw new ArgumentNullException(nameof(polygon));

		for (var i = 0; i < line.Points.Count - 1; i++)
		{
			var segmentStart = line.Points[i];
			var segmentEnd = line.Points[i + 1];

			if (ContainsPoint(polygon, segmentStart) || ContainsPoint(polygon, segmentEnd) || IntersectsRing(segmentStart, segmentEnd, polygon.Outer))
			{
				return true;
			}

			foreach (var hole in polygon.Holes)
			{
				if (IntersectsRing(segmentStart, segmentEnd, hole))
				{
					return true;
				}
			}
		}

		return false;
	}

	public static bool Intersects(GeoPolygon a, GeoPolygon b)
	{
		if (a == null) throw new ArgumentNullException(nameof(a));
		if (b == null) throw new ArgumentNullException(nameof(b));

		if (!a.GetBoundingBox().Intersects(b.GetBoundingBox()))
		{
			return false;
		}

		return a.Outer.Any(p => ContainsPoint(b, p)) || b.Outer.Any(p => ContainsPoint(a, p)) || RingsIntersect(a.Outer, b.Outer);
	}

	public static bool Intersects(GeoLineString a, GeoLineString b)
	{
		for (var i = 0; i < a.Points.Count - 1; i++)
		{
			for (var j = 0; j < b.Points.Count - 1; j++)
			{
				if (SegmentsIntersect(a.Points[i], a.Points[i + 1], b.Points[j], b.Points[j + 1]))
				{
					return true;
				}
			}
		}

		return false;
	}

	public static bool RingsOverlap(IReadOnlyList<GeoPoint> a, IReadOnlyList<GeoPoint> b) => RingsIntersect(a, b) || a.Any(p => IsPointInRing(b, p)) || b.Any(p => IsPointInRing(a, p));

	public static bool HasSelfIntersection(IReadOnlyList<GeoPoint> ring)
	{
		for (var i = 0; i < ring.Count - 1; i++)
		{
			for (var j = i + 1; j < ring.Count - 1; j++)
			{
				if (Math.Abs(i - j) <= 1 || (i == 0 && j == ring.Count - 2) || SharesEndpoint(ring[i], ring[i + 1], ring[j], ring[j + 1]))
				{
					continue;
				}

				if (SegmentsIntersect(ring[i], ring[i + 1], ring[j], ring[j + 1]))
				{
					return true;
				}
			}
		}

		return false;
	}

	public static bool IsRingClosed(IReadOnlyList<GeoPoint> ring)
	{
		if (ring.Count < 4)
		{
			return false;
		}

		var first = ring[0];
		var last = ring[ring.Count - 1];
		return Math.Abs(first.Lat - last.Lat) < Epsilon && Math.Abs(first.Lon - last.Lon) < Epsilon;
	}

	public static bool IsRingInside(IReadOnlyList<GeoPoint> outer, IReadOnlyList<GeoPoint> inner) => inner.All(p => IsPointInRing(outer, p));

	public static bool LineContainsPoint(GeoLineString line, GeoPoint point)
	{
		if (line == null) throw new ArgumentNullException(nameof(line));
		if (point == null) throw new ArgumentNullException(nameof(point));

		for (var i = 0; i < line.Points.Count - 1; i++)
		{
			var start = line.Points[i];
			var end = line.Points[i + 1];

			if (OnSegment(start, end, point) && Math.Abs(Direction(start, end, point)) < Epsilon)
			{
				return true;
			}
		}

		return false;
	}

	private static bool IsPointInRing(IReadOnlyList<GeoPoint> ring, GeoPoint point)
	{
		var inside = false;

		for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
		{
			var pi = ring[i];
			var pj = ring[j];
			var intersects = ((pi.Lat > point.Lat) != (pj.Lat > point.Lat)) &&
							 (point.Lon < (pj.Lon - pi.Lon) * (point.Lat - pi.Lat) / (pj.Lat - pi.Lat + double.Epsilon) + pi.Lon);

			if (intersects)
			{
				inside = !inside;
			}
		}

		return inside;
	}

	private static bool IntersectsRing(GeoPoint a, GeoPoint b, IReadOnlyList<GeoPoint> ring)
	{
		for (var i = 0; i < ring.Count - 1; i++)
		{
			if (SegmentsIntersect(a, b, ring[i], ring[i + 1]))
			{
				return true;
			}
		}

		return false;
	}

	private static bool RingsIntersect(IReadOnlyList<GeoPoint> a, IReadOnlyList<GeoPoint> b)
	{
		for (var i = 0; i < a.Count - 1; i++)
		{
			for (var j = 0; j < b.Count - 1; j++)
			{
				if (SegmentsIntersect(a[i], a[i + 1], b[j], b[j + 1]))
				{
					return true;
				}
			}
		}

		return false;
	}

	internal static bool SegmentsIntersect(GeoPoint a1, GeoPoint a2, GeoPoint b1, GeoPoint b2)
	{
		var d1 = Direction(a1, a2, b1);
		var d2 = Direction(a1, a2, b2);
		var d3 = Direction(b1, b2, a1);
		var d4 = Direction(b1, b2, a2);

		if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) &&
			((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0)))
		{
			return true;
		}

		return (Math.Abs(d1) < Epsilon && OnSegment(a1, a2, b1)) ||
			   (Math.Abs(d2) < Epsilon && OnSegment(a1, a2, b2)) ||
			   (Math.Abs(d3) < Epsilon && OnSegment(b1, b2, a1)) ||
			   (Math.Abs(d4) < Epsilon && OnSegment(b1, b2, a2));
	}

	private static double Direction(GeoPoint a, GeoPoint b, GeoPoint c) => (b.Lon - a.Lon) * (c.Lat - a.Lat) - (b.Lat - a.Lat) * (c.Lon - a.Lon);

	private static bool SharesEndpoint(GeoPoint a1, GeoPoint a2, GeoPoint b1, GeoPoint b2) => IsSamePoint(a1, b1) || IsSamePoint(a1, b2) || IsSamePoint(a2, b1) || IsSamePoint(a2, b2);

	private static bool IsSamePoint(GeoPoint a, GeoPoint b) => Math.Abs(a.Lat - b.Lat) < Epsilon && Math.Abs(a.Lon - b.Lon) < Epsilon;

	private static bool OnSegment(GeoPoint a, GeoPoint b, GeoPoint c)
		=> c.Lat >= Math.Min(a.Lat, b.Lat) - Epsilon &&
		   c.Lat <= Math.Max(a.Lat, b.Lat) + Epsilon &&
		   c.Lon >= Math.Min(a.Lon, b.Lon) - Epsilon &&
		   c.Lon <= Math.Max(a.Lon, b.Lon) + Epsilon;
}

