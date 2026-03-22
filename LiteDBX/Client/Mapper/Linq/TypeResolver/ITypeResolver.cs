using System.Reflection;

namespace LiteDbX;

internal interface ITypeResolver
{
    string ResolveMethod(MethodInfo method);

    string ResolveMember(MemberInfo member);

    string ResolveCtor(ConstructorInfo ctor);
}