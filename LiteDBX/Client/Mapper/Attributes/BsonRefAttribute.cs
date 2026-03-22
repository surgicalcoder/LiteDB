using System;

namespace LiteDbX;

/// <summary>
/// Indicate that field are not persisted inside this document but it's a reference for another document (DbRef)
/// </summary>
public class BsonRefAttribute : Attribute
{
    public BsonRefAttribute(string collection)
    {
        Collection = collection;
    }

    public BsonRefAttribute()
    {
        Collection = null;
    }

    public string Collection { get; set; }
}