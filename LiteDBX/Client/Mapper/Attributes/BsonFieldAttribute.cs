using System;

namespace LiteDbX;

/// <summary>
/// Set a name to this property in BsonDocument
/// </summary>
public class BsonFieldAttribute : Attribute
{
    public BsonFieldAttribute(string name)
    {
        Name = name;
    }

    public BsonFieldAttribute() { }

    public string Name { get; set; }
}