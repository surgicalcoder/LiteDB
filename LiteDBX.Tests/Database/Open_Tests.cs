using System;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using LiteDbX.Engine;
using Xunit;

namespace LiteDbX.Tests.Database;

public class Open_Tests
{
    [Fact]
    public async Task Database_Open_Can_Be_Called_Synchronously_From_A_Constructor()
    {
        await using var holder = new DatabaseHolder();
        var customers = holder.Database.GetCollection<Customer>("customers");

        await customers.Insert(new Customer { Id = 1, Name = "John" });

        (await customers.Count()).Should().Be(1);
    }

    [Fact]
    public async Task Repository_Open_Can_Be_Called_Synchronously()
    {
        await using var repo = LiteRepository.Open(":memory:");

        await repo.Insert(new Customer { Id = 1, Name = "Jane" }, "customers");

        var customer = await repo.SingleById<Customer>(1, "customers");

        customer.Name.Should().Be("Jane");
    }

    [Fact]
    public async Task Engine_Open_Can_Be_Called_Synchronously()
    {
        await using var engine = LiteEngine.Open(new EngineSettings { DataStream = new MemoryStream() });
        await using var db = new LiteDatabase(engine, disposeOnClose: false);
        var customers = db.GetCollection<Customer>("customers");

        await customers.Insert(new Customer { Id = 1, Name = "Ana" });

        (await customers.Count()).Should().Be(1);
    }

    [Fact]
    public async Task OpenAsync_Remains_Available_Across_Public_Entry_Points()
    {
        await using var database = await LiteDatabase.OpenAsync(":memory:");
        await using var repository = await LiteRepository.OpenAsync(":memory:");
        await using var engine = await LiteEngine.OpenAsync(new EngineSettings { DataStream = new MemoryStream() });
        await using var engineDatabase = new LiteDatabase(engine, disposeOnClose: false);

        await database.GetCollection<Customer>("database_customers").Insert(new Customer { Id = 1, Name = "Db" });
        await repository.Insert(new Customer { Id = 2, Name = "Repo" }, "repo_customers");
        await engineDatabase.GetCollection<Customer>("engine_customers").Insert(new Customer { Id = 3, Name = "Engine" });

        (await database.GetCollection<Customer>("database_customers").Count()).Should().Be(1);
        (await repository.SingleById<Customer>(2, "repo_customers")).Name.Should().Be("Repo");
        (await engineDatabase.GetCollection<Customer>("engine_customers").Count()).Should().Be(1);
    }

    private sealed class DatabaseHolder : IAsyncDisposable
    {
        public DatabaseHolder()
        {
            Database = LiteDatabase.Open(":memory:");
        }

        public LiteDatabase Database { get; }

        public ValueTask DisposeAsync()
        {
            return Database.DisposeAsync();
        }
    }

    private sealed class Customer
    {
        public int Id { get; set; }

        public string Name { get; set; }
    }
}


