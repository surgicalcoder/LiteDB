using System;
using System.IO;
using System.Linq;

namespace LiteDbX.Tests.QueryTest;

public class PersonGroupByData : IDisposable
{
    private readonly ILiteCollection<Person> _collection;
    private readonly ILiteDatabase _db;
    private readonly Person[] _local;

    public PersonGroupByData()
    {
        _local = DataGen.Person(1, 1000).ToArray();
        _db = new LiteDatabase(new MemoryStream());
        _collection = _db.GetCollection<Person>();

        _collection.Insert(_local);
        _collection.EnsureIndex(x => x.Age);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    public (ILiteCollection<Person>, Person[]) GetData()
    {
        return (_collection, _local);
    }
}