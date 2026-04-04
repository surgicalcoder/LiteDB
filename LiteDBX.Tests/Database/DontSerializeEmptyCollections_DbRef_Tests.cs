using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace LiteDbX.Tests.Database;

public class DontSerializeEmptyCollections_DbRef_Tests
{
    [Fact]
    public async Task Empty_DbRef_Lists_Are_Omitted_While_Non_Empty_Lists_Are_Preserved()
    {
        var mapper = new BsonMapper
        {
            DontSerializeEmptyCollections = true,
            SerializeNullValues = true
        };

        mapper.Entity<DbRefOrder>()
            .DbRef(x => x.ProductsEmpty, "products")
            .DbRef(x => x.ProductsNullOnly, "products")
            .DbRef(x => x.ProductsSingle, "products");

        await using var db = await LiteDatabase.Open(new MemoryStream(), mapper, new MemoryStream());
        var products = db.GetCollection<DbRefProduct>("products");
        var orders = db.GetCollection<DbRefOrder>("orders");
        var rawOrders = db.GetCollection("orders");

        var product = new DbRefProduct { Id = 7, Name = "Monitor" };
        await products.Insert(product);

        var order = new DbRefOrder
        {
            ProductsEmpty = new List<DbRefProduct>(),
            ProductsNullOnly = new List<DbRefProduct> { null },
            ProductsSingle = new List<DbRefProduct> { product }
        };

        await orders.Insert(order);

        var raw = await rawOrders.FindById(order.Id);

        Assert.NotNull(raw);
        Assert.False(raw.ContainsKey(nameof(DbRefOrder.ProductsEmpty)));
        Assert.False(raw.ContainsKey(nameof(DbRefOrder.ProductsNullOnly)));
        Assert.True(raw.ContainsKey(nameof(DbRefOrder.ProductsSingle)));
        Assert.Single(raw[nameof(DbRefOrder.ProductsSingle)].AsArray);
    }

    private class DbRefOrder
    {
        public ObjectId Id { get; set; }
        public List<DbRefProduct> ProductsEmpty { get; set; }
        public List<DbRefProduct> ProductsNullOnly { get; set; }
        public List<DbRefProduct> ProductsSingle { get; set; }
    }

    private class DbRefProduct
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }
}

