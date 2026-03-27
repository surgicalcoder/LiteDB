using System;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace LiteDbX.Tests.Mapper;

public class InheritedMemberConventions_Tests
{
    [Fact]
    public void Inherited_Conventions_Are_Applied_To_Derived_Type_Serialization_And_Deserialization()
    {
        var mapper = CreateMapper();
        var objectId = ObjectId.NewObjectId();
        var entity = new Customer
        {
            Id = objectId.ToString(),
            Name = "John",
            PartitionKey = "north",
            IgnoredA = "secret-a",
            IgnoredB = "secret-b"
        };

        var doc = mapper.ToDocument(entity);

        doc["_id"].IsObjectId.Should().BeTrue();
        doc["_id"].AsObjectId.Should().Be(objectId);
        doc["Name"].Should().Be("John");
        doc["PartitionKey"].Should().Be("pk::north");
        doc.ContainsKey(nameof(EntityBase.IgnoredA)).Should().BeFalse();
        doc.ContainsKey(nameof(EntityBase.IgnoredB)).Should().BeFalse();

        var roundTrip = mapper.ToObject<Customer>(doc);

        roundTrip.Id.Should().Be(objectId.ToString());
        roundTrip.Name.Should().Be("John");
        roundTrip.PartitionKey.Should().Be("north");
        roundTrip.IgnoredA.Should().BeNull();
        roundTrip.IgnoredB.Should().BeNull();
    }

    [Fact]
    public void Inherited_Conventions_Work_With_Ctor_Hydration()
    {
        var mapper = CreateMapper();
        var objectId = ObjectId.NewObjectId();
        var doc = new BsonDocument
        {
            ["_id"] = objectId,
            ["PartitionKey"] = "pk::north",
            ["Name"] = "John"
        };

        var entity = mapper.ToObject<CustomerCtor>(doc);

        entity.Id.Should().Be(objectId.ToString());
        entity.PartitionKey.Should().Be("north");
        entity.Name.Should().Be("John");
    }

    [Fact]
    public void Inherited_Conventions_Affect_DbRef_Id_Storage()
    {
        var mapper = CreateMapper();
        var objectId = ObjectId.NewObjectId();
        var doc = mapper.ToDocument(new CustomerLink
        {
            Id = 1,
            Customer = new Customer { Id = objectId.ToString(), Name = "John", PartitionKey = "north" }
        });

        doc["Customer"].IsDocument.Should().BeTrue();
        doc["Customer"]["$id"].IsObjectId.Should().BeTrue();
        doc["Customer"]["$id"].AsObjectId.Should().Be(objectId);

        var hydrated = mapper.ToObject<CustomerLink>(doc);
        hydrated.Customer.Should().NotBeNull();
        hydrated.Customer.Id.Should().Be(objectId.ToString());
    }

    [Fact]
    public void Inherited_Conventions_Do_Not_Affect_Unrelated_Types()
    {
        var mapper = CreateMapper();
        var doc = mapper.ToDocument(new UnrelatedEntity { Id = "plain-id", Name = "Other" });

        doc["_id"].IsString.Should().BeTrue();
        doc["_id"].AsString.Should().Be("plain-id");
    }

    [Fact]
    public void Inherited_Convention_Conflict_On_Ignore_And_Id_Throws()
    {
        var mapper = new BsonMapper();
        mapper.Inheritance<EntityBase>().Ignore(x => x.Id);

        var act = () => mapper.Inheritance<EntityBase>().Id(x => x.Id, BsonType.ObjectId);

        act.Should().Throw<LiteException>();
    }

    [Fact]
    public void Late_Inheritance_Registration_After_Mapping_Throws()
    {
        var mapper = new BsonMapper();
        _ = mapper.ToDocument(new Customer { Id = ObjectId.NewObjectId().ToString(), Name = "John", PartitionKey = "north" });

        var act = () => mapper.Inheritance<EntityBase>().Id(x => x.Id, BsonType.ObjectId);

        act.Should().Throw<LiteException>();
    }

    [Fact]
    public async Task Insert_AutoId_Writes_Back_String_Id_And_Linq_Uses_Member_Aware_Parameters()
    {
        var mapper = CreateMapper();

        await using var db = new LiteDatabase(":memory:", mapper);
        var col = db.GetCollection<Customer>("customers");

        var entity = new Customer
        {
            Name = "John",
            PartitionKey = "north",
            IgnoredA = "secret-a",
            IgnoredB = "secret-b"
        };

        var bsonId = await col.Insert(entity);

        bsonId.IsObjectId.Should().BeTrue();
        entity.Id.Should().Be(bsonId.AsObjectId.ToString());

        var loaded = await col.FindOne(x => x.Id == entity.Id && x.PartitionKey == "north");

        loaded.Should().NotBeNull();
        loaded.Id.Should().Be(entity.Id);
        loaded.PartitionKey.Should().Be("north");
        loaded.Name.Should().Be("John");
    }

    [Fact]
    public async Task Invalid_ObjectId_String_Throws_On_Insert()
    {
        var mapper = CreateMapper();

        await using var db = new LiteDatabase(":memory:", mapper);
        var col = db.GetCollection<Customer>("customers");

        var act = async () => await col.Insert(new Customer
        {
            Id = "not-an-object-id",
            Name = "John",
            PartitionKey = "north"
        });

        await act.Should().ThrowAsync<LiteException>();
    }

    private static BsonMapper CreateMapper()
    {
        var mapper = new BsonMapper();

        mapper.Inheritance<EntityBase>()
            .Id(x => x.Id, BsonType.ObjectId, autoId: true)
            .Ignore(x => x.IgnoredA)
            .Ignore(x => x.IgnoredB)
            .Serialize(
                x => x.PartitionKey,
                value => value == null ? BsonValue.Null : new BsonValue("pk::" + value),
                bson => bson == null || bson.IsNull ? null : bson.AsString.Substring(4));

        return mapper;
    }

    public abstract class EntityBase
    {
        public string Id { get; set; }
        public string IgnoredA { get; set; }
        public string IgnoredB { get; set; }
        public string PartitionKey { get; set; }
    }

    public class Customer : EntityBase
    {
        public string Name { get; set; }
    }

    public class CustomerCtor : EntityBase
    {
        public CustomerCtor(string id, string partitionKey, string name)
        {
            Id = id;
            PartitionKey = partitionKey;
            Name = name;
        }

        public string Name { get; }
    }

    public class CustomerLink
    {
        public int Id { get; set; }

        [BsonRef("customers")]
        public Customer Customer { get; set; }
    }

    public class UnrelatedEntity
    {
        public string Id { get; set; }
        public string Name { get; set; }
    }
}

