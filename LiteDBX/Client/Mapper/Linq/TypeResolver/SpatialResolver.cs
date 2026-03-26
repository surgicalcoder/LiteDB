using System;
using System.Reflection;
using LiteDbX.Spatial;
using SpatialApi = LiteDbX.Spatial.Spatial;

namespace LiteDbX;

internal class SpatialResolver : ITypeResolver
{
    public string ResolveMethod(MethodInfo method)
    {
        if (method == null)
        {
            return null;
        }

        if (method.DeclaringType == typeof(SpatialExpressions))
        {
            return ResolveSpatialExpressions(method);
        }

        if (method.DeclaringType == typeof(SpatialApi))
        {
            return ResolveSpatialMethods(method);
        }

        return null;
    }

    public string ResolveMember(MemberInfo member) => null;

    public string ResolveCtor(ConstructorInfo ctor) => null;

    private static string ResolveSpatialExpressions(MethodInfo method)
    {
        return method.Name switch
        {
            nameof(SpatialExpressions.Near) => ResolveNearPattern(method),
            nameof(SpatialExpressions.Within) => "SPATIAL_WITHIN(@0, @1)",
            nameof(SpatialExpressions.Intersects) => "SPATIAL_INTERSECTS(@0, @1)",
            nameof(SpatialExpressions.Contains) => "SPATIAL_CONTAINS(@0, @1)",
            nameof(SpatialExpressions.WithinBoundingBox) => "SPATIAL_WITHIN_BOX(@0, @1, @2, @3, @4)",
            _ => null
        };
    }

    private static string ResolveSpatialMethods(MethodInfo method)
    {
        var parameters = method.GetParameters();

        if (method.Name == nameof(SpatialApi.Near) && parameters.Length == 3 && parameters[0].ParameterType == typeof(GeoPoint))
        {
            return ResolveNearPattern(method);
        }

        if (method.Name == nameof(SpatialApi.Within) && parameters.Length == 2 && parameters[0].ParameterType == typeof(GeoShape))
        {
            return "SPATIAL_WITHIN(@0, @1)";
        }

        if (method.Name == nameof(SpatialApi.Intersects) && parameters.Length == 2 && parameters[0].ParameterType == typeof(GeoShape))
        {
            return "SPATIAL_INTERSECTS(@0, @1)";
        }

        if (method.Name == nameof(SpatialApi.Contains) && parameters.Length == 2 && parameters[0].ParameterType == typeof(GeoShape))
        {
            return "SPATIAL_CONTAINS_POINT(@0, @1)";
        }

        return null;
    }

    private static string ResolveNearPattern(MethodInfo method)
    {
        var parameters = method.GetParameters();

        if (parameters.Length == 3)
        {
            return $"SPATIAL_NEAR(@0, @1, @2, '{SpatialApi.Options.Distance}')";
        }

        if (parameters.Length == 4)
        {
            return "SPATIAL_NEAR(@0, @1, @2, @3)";
        }

        throw new NotSupportedException("Unsupported overload for spatial Near expression");
    }
}

