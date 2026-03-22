using System;
using FluentAssertions;
using Xunit;

namespace LiteDbX.Tests.Database;

public class IndexSortAndFilterTest : IDisposable
{
    private readonly ILiteCollection<Item> _collection;
    private readonly ILiteDatabase _database;
    private readonly TempFile _tempFile;

    public IndexSortAndFilterTest()
    {
        _tempFile = new TempFile();
        _database = new LiteDatabase(_tempFile.Filename);
        _collection = _database.GetCollection<Item>("items");

        _collection.Upsert(new Item { Id = "C", Value = "Value 1" });
        _collection.Upsert(new Item { Id = "A", Value = "Value 2" });
        _collection.Upsert(new Item { Id = "B", Value = "Value 1" });

        _collection.EnsureIndex("idx_value", x => x.Value);
    }

    public void Dispose()
    {
        _database.Dispose();
        _tempFile.Dispose();
    }

    [Fact]
    public void FilterAndSortAscending()
    {
        var result = _collection.Query()
                                .Where(x => x.Value == "Value 1")
                                .OrderBy(x => x.Id)
                                .ToList();

        result[0].Id.Should().Be("B");
        result[1].Id.Should().Be("C");
    }

    [Fact]
    public void FilterAndSortDescending()
    {
        var result = _collection.Query()
                                .Where(x => x.Value == "Value 1")
                                .OrderBy(x => x.Id, Query.Descending)
                                .ToList();

        result[0].Id.Should().Be("C");
        result[1].Id.Should().Be("B");
    }

    [Fact]
    public void FilterAndSortAscendingWithoutIndex()
    {
        _collection.DropIndex("idx_value");

        var result = _collection.Query()
                                .Where(x => x.Value == "Value 1")
                                .OrderBy(x => x.Id)
                                .ToList();

        result[0].Id.Should().Be("B");
        result[1].Id.Should().Be("C");
    }

    #region Model

    public class Item
    {
        public string Id { get; set; }
        public string Value { get; set; }
    }

    #endregion
}