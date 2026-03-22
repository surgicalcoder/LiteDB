using System.Collections.Generic;
using System.Linq;

namespace LiteDbX;

/// <summary>
/// Implement some Enumerable methods to IBsonDataReader
/// </summary>
public static class BsonDataReaderExtensions
{
    public static IEnumerable<BsonValue> ToEnumerable(this IBsonDataReader reader)
    {
        try
        {
            while (reader.Read())
            {
                yield return reader.Current;
            }
        }
        finally
        {
            reader.Dispose();
        }
    }

    public static BsonValue[] ToArray(this IBsonDataReader reader)
    {
        return ToEnumerable(reader).ToArray();
    }

    public static IList<BsonValue> ToList(this IBsonDataReader reader)
    {
        return ToEnumerable(reader).ToList();
    }

    public static BsonValue First(this IBsonDataReader reader)
    {
        return ToEnumerable(reader).First();
    }

    public static BsonValue FirstOrDefault(this IBsonDataReader reader)
    {
        return ToEnumerable(reader).FirstOrDefault();
    }

    public static BsonValue Single(this IBsonDataReader reader)
    {
        return ToEnumerable(reader).Single();
    }

    public static BsonValue SingleOrDefault(this IBsonDataReader reader)
    {
        return ToEnumerable(reader).SingleOrDefault();
    }
}