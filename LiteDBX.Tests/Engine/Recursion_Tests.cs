using FluentAssertions;
using System;
using System.Threading.Tasks;
using Xunit;

namespace LiteDbX.Tests.Engine;

public class Recursion_Tests
{
    [Fact]
    public async Task UpdateInFindAll()
    {
        await Test(async collection =>
        {
            var updated = 0;

            await foreach (var document in collection.FindAll())
            {
                await collection.Update(document);
                updated++;
            }

            updated.Should().Be(3);
            (await collection.Count()).Should().Be(3);
        });
    }

    [Fact]
    public async Task InsertDeleteInFindAll()
    {
        await Test(async collection =>
        {
            var iterations = 0;

            await foreach (var _ in collection.FindAll())
            {
                var id = await collection.Insert(new BsonDocument());
                await collection.Delete(id);
                iterations++;
            }

            iterations.Should().Be(3);
            (await collection.Count()).Should().Be(3);
        });
    }

    [Fact]
    public async Task QueryInFindAll()
    {
        await Test(async collection =>
        {
            var iterations = 0;

            await foreach (var _ in collection.FindAll())
            {
                (await collection.Query().Count()).Should().Be(3);
                iterations++;
            }

            iterations.Should().Be(3);
        });
    }

    [Fact]
    public async Task BreakInFindAll_ReleasesSharedLease()
    {
        await Test(async collection =>
        {
            var iterations = 0;

            await foreach (var _ in collection.FindAll())
            {
                iterations++;
                break;
            }

            iterations.Should().Be(1);

            await collection.Insert(new BsonDocument());

            (await collection.Count()).Should().Be(4);
        });
    }

    private static async Task Test(Func<ILiteCollection<BsonDocument>, Task> action)
    {
        using var file = new TempFile();

        await using var database = new LiteDatabase(new ConnectionString
        {
            Filename = file.Filename,
            Connection = ConnectionType.Shared
        });

        var accounts = database.GetCollection("Recursion");

        if (await accounts.Count() < 3)
        {
            await accounts.Insert(new BsonDocument());
            await accounts.Insert(new BsonDocument());
            await accounts.Insert(new BsonDocument());
        }

        await action(accounts);
    }
}