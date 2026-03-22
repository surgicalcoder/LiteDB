using Xunit;

namespace LiteDbX.Tests.Issues;

public class Issue2534_Tests
{
    [Fact]
    public void Test()
    {
        using LiteDatabase database = new(new ConnectionString
        {
            Filename = "Demo.db",
            Connection = ConnectionType.Shared
        });

        var accounts = database.GetCollection("Issue2534");

        if (accounts.Count() < 3)
        {
            accounts.Insert(new BsonDocument());
            accounts.Insert(new BsonDocument());
            accounts.Insert(new BsonDocument());
        }

        foreach (var document in accounts.FindAll())
        {
            accounts.Update(document);
        }
    }
}