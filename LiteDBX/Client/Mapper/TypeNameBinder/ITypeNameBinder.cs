using System;

namespace LiteDbX;

public interface ITypeNameBinder
{
    string GetName(Type type);
    Type GetType(string name);
}