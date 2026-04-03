using System.Threading.Tasks;
using Xunit;

namespace LiteDbX.Tests.Issues;

public class Issue2534_Tests
{
    [Fact]
    public async Task Test()
    {
        using var file = new TempFile();

        await using LiteDatabase database = await LiteDatabase.Open(new ConnectionString
        {
            Filename = file.Filename,
            Connection = ConnectionType.Shared
        });

        var accounts = database.GetCollection("Issue2534");

        if (await accounts.Count() < 3)
        {
            await accounts.Insert(new BsonDocument());
            await accounts.Insert(new BsonDocument());
            await accounts.Insert(new BsonDocument());
        }

        await foreach (var document in accounts.FindAll())
        {
            await accounts.Update(document);
        }
    }
}