using Xunit;

namespace LiteDbX.Tests.Mapper;

public class Records_Tests
{
    [Fact]
    public void Record_Simple_Mapper()
    {
        var mapper = new BsonMapper();

        var user = new User(1, "John");
        var doc = mapper.ToDocument(user);
        var user2 = mapper.ToObject<User>(doc);

        Assert.Equal(user.Id, user2.Id);
        Assert.Equal(user.Name, user2.Name);
    }

    public record User(int Id, string Name);
}