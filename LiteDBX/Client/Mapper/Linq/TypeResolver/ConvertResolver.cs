using System.Reflection;

namespace LiteDbX;

internal class ConvertResolver : ITypeResolver
{
    public string ResolveMethod(MethodInfo method)
    {
        switch (method.Name)
        {
            case "ToInt32": return "INT32(@0)";
            case "ToInt64": return "INT64(@0)";
            case "ToDouble": return "DOUBLE(@0)";
            case "ToDecimal": return "DECIMAL(@0)";

            case "ToDateTime": return "DATE(@0)";
            case "FromBase64String": return "BINARY(@0)";
            case "ToBoolean": return "BOOL(@0)";
            case "ToString": return "STRING(@0)";
        }

        return null;
    }

    public string ResolveMember(MemberInfo member)
    {
        return null;
    }

    public string ResolveCtor(ConstructorInfo ctor)
    {
        return null;
    }
}