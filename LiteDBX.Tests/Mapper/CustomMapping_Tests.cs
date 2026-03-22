using FluentAssertions;
using Xunit;

namespace LiteDbX.Tests.Mapper;

public class CustomMappingCtor_Tests
{
    [Fact]
    public void Custom_Ctor_With_Custom_Id()
    {
        var mapper = new BsonMapper();

        mapper.Entity<UserWithCustomId>()
              .Id(u => u.Key, false);

        var doc = new BsonDocument { ["_id"] = 10, ["name"] = "John" };

        var user = mapper.ToObject<UserWithCustomId>(doc);

        user.Key.Should().Be(10); //     Expected user.Key to be 10, but found 0.
        user.Name.Should().Be("John");
    }

    [Fact]
    public void Custom_Id_In_Interface()
    {
        var mapper = new BsonMapper();

        var obj = new ConcreteClass { CustomId = "myid", Name = "myname" };
        var doc = mapper.Serialize(obj) as BsonDocument;
        doc["_id"].Should().NotBeNull();
        doc["_id"].Should().Be("myid");
        doc["CustomName"].Should().NotBe(BsonValue.Null);
        doc["CustomName"].Should().Be("myname");
        doc["Name"].Should().Be(BsonValue.Null);
        doc.Keys.ExpectCount(2);
    }

    public class UserWithCustomId
    {
        public UserWithCustomId(int key, string name)
        {
            Key = key;
            Name = name;
        }

        public int Key { get; }
        public string Name { get; }
    }

    public abstract class BaseClass
    {
        [BsonId]
        public string CustomId { get; set; }

        [BsonField("CustomName")]
        public string Name { get; set; }
    }

    public class ConcreteClass : BaseClass { }
}