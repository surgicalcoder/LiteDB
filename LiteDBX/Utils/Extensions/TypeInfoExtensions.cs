using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace LiteDbX;

internal static class TypeInfoExtensions
{
    public static bool IsAnonymousType(this Type type)
    {
        var isAnonymousType =
            type.FullName.Contains("AnonymousType") &&
            type.GetTypeInfo().GetCustomAttributes(typeof(CompilerGeneratedAttribute), false).Any();

        return isAnonymousType;
    }

    public static bool IsEnumerable(this Type type)
    {
        return
            type != typeof(string) &&
            typeof(IEnumerable).IsAssignableFrom(type);
    }
}