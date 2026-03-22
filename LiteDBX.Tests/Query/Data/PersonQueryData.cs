using System;
using System.Linq;

namespace LiteDbX.Tests.QueryTest;

public class PersonQueryData : IDisposable
{
    private readonly ILiteCollection<Person> _collection;
    private readonly ILiteDatabase _db;
    private readonly Person[] _local;

    public PersonQueryData()
    {
        _local = DataGen.Person().ToArray();

        _db = new LiteDatabase(":memory:");
        _collection = _db.GetCollection<Person>("person");
        _collection.Insert(_local);
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