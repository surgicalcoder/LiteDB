using System.Collections.Generic;
using FluentAssertions;
using Xunit;

namespace LiteDbX.Tests.Mapper;

public class DontSerializeEmptyCollections_Tests
{
    [Fact]
    public void Empty_Collections_Are_Still_Serialized_By_Default()
    {
        var mapper = new BsonMapper();
        var doc = mapper.ToDocument(new CollectionHolder
        {
            Id = 1,
            List = new List<int>(),
            Array = System.Array.Empty<int>(),
            Dictionary = new Dictionary<string, int>()
        });

        doc.ContainsKey(nameof(CollectionHolder.List)).Should().BeTrue();
        doc[nameof(CollectionHolder.List)].IsArray.Should().BeTrue();
        doc[nameof(CollectionHolder.List)].AsArray.Count.Should().Be(0);

        doc.ContainsKey(nameof(CollectionHolder.Array)).Should().BeTrue();
        doc[nameof(CollectionHolder.Array)].IsArray.Should().BeTrue();
        doc[nameof(CollectionHolder.Array)].AsArray.Count.Should().Be(0);

        doc.ContainsKey(nameof(CollectionHolder.Dictionary)).Should().BeTrue();
        doc[nameof(CollectionHolder.Dictionary)].IsDocument.Should().BeTrue();
        doc[nameof(CollectionHolder.Dictionary)].AsDocument.Count.Should().Be(0);
    }

    [Fact]
    public void Empty_Collection_Members_Are_Omitted_When_Enabled()
    {
        var mapper = new BsonMapper { DontSerializeEmptyCollections = true };
        var doc = mapper.ToDocument(new CollectionHolder
        {
            Id = 1,
            List = new List<int>(),
            Array = System.Array.Empty<int>(),
            Dictionary = new Dictionary<string, int>()
        });

        doc.ContainsKey(nameof(CollectionHolder.List)).Should().BeFalse();
        doc.ContainsKey(nameof(CollectionHolder.Array)).Should().BeFalse();
        doc.ContainsKey(nameof(CollectionHolder.Dictionary)).Should().BeFalse();
        doc["_id"].AsInt32.Should().Be(1);
    }

    [Fact]
    public void Non_Empty_Collections_Are_Still_Serialized_When_Enabled()
    {
        var mapper = new BsonMapper { DontSerializeEmptyCollections = true };
        var doc = mapper.ToDocument(new CollectionHolder
        {
            Id = 1,
            List = new List<int> { 10 },
            Array = new[] { 20 },
            Dictionary = new Dictionary<string, int> { ["x"] = 30 }
        });

        Assert.Single(doc[nameof(CollectionHolder.List)].AsArray);
        Assert.Equal(10, doc[nameof(CollectionHolder.List)][0].AsInt32);
        Assert.Single(doc[nameof(CollectionHolder.Array)].AsArray);
        Assert.Equal(20, doc[nameof(CollectionHolder.Array)][0].AsInt32);
        Assert.True(doc[nameof(CollectionHolder.Dictionary)].AsDocument.ContainsKey("x"));
        Assert.Equal(30, doc[nameof(CollectionHolder.Dictionary)]["x"].AsInt32);
    }

    [Fact]
    public void Empty_Collections_Are_Omitted_Even_When_SerializeNullValues_Is_True()
    {
        var mapper = new BsonMapper
        {
            DontSerializeEmptyCollections = true,
            SerializeNullValues = true
        };

        var doc = mapper.ToDocument(new CollectionHolder
        {
            Id = 1,
            List = new List<int>(),
            Array = System.Array.Empty<int>(),
            Dictionary = new Dictionary<string, int>()
        });

        doc.ContainsKey(nameof(CollectionHolder.List)).Should().BeFalse();
        doc.ContainsKey(nameof(CollectionHolder.Array)).Should().BeFalse();
        doc.ContainsKey(nameof(CollectionHolder.Dictionary)).Should().BeFalse();
    }

    [Fact]
    public void Null_Collections_Still_Respect_SerializeNullValues_When_False()
    {
        var mapper = new BsonMapper
        {
            DontSerializeEmptyCollections = true,
            SerializeNullValues = false
        };

        var doc = mapper.ToDocument(new CollectionHolder
        {
            Id = 1,
            List = null,
            Array = null,
            Dictionary = null
        });

        doc.ContainsKey(nameof(CollectionHolder.List)).Should().BeFalse();
        doc.ContainsKey(nameof(CollectionHolder.Array)).Should().BeFalse();
        doc.ContainsKey(nameof(CollectionHolder.Dictionary)).Should().BeFalse();
    }

    [Fact]
    public void Null_Collections_Still_Respect_SerializeNullValues_When_True()
    {
        var mapper = new BsonMapper
        {
            DontSerializeEmptyCollections = true,
            SerializeNullValues = true
        };

        var doc = mapper.ToDocument(new CollectionHolder
        {
            Id = 1,
            List = null,
            Array = null,
            Dictionary = null
        });

        doc.ContainsKey(nameof(CollectionHolder.List)).Should().BeTrue();
        doc[nameof(CollectionHolder.List)].IsNull.Should().BeTrue();
        doc.ContainsKey(nameof(CollectionHolder.Array)).Should().BeTrue();
        doc[nameof(CollectionHolder.Array)].IsNull.Should().BeTrue();
        doc.ContainsKey(nameof(CollectionHolder.Dictionary)).Should().BeTrue();
        doc[nameof(CollectionHolder.Dictionary)].IsNull.Should().BeTrue();
    }

    [Fact]
    public void Top_Level_Empty_Collections_Are_Not_Suppressed()
    {
        var mapper = new BsonMapper { DontSerializeEmptyCollections = true };

        var array = mapper.Serialize(new List<int>());
        var dictionary = mapper.Serialize(new Dictionary<string, int>());
        var typedArray = mapper.Serialize(typeof(int[]), System.Array.Empty<int>());

        Assert.True(array.IsArray);
        Assert.Empty((IEnumerable<BsonValue>)array.AsArray);
        Assert.True(dictionary.IsDocument);
        Assert.Empty((IEnumerable<KeyValuePair<string, BsonValue>>)dictionary.AsDocument);
        Assert.True(typedArray.IsArray);
        Assert.Empty((IEnumerable<BsonValue>)typedArray.AsArray);
    }

    [Fact]
    public void Empty_String_Is_Not_Affected_By_DontSerializeEmptyCollections()
    {
        var mapper = new BsonMapper
        {
            DontSerializeEmptyCollections = true,
            EmptyStringToNull = false
        };

        var doc = mapper.ToDocument(new StringHolder { Id = 1, Text = string.Empty });

        doc.ContainsKey(nameof(StringHolder.Text)).Should().BeTrue();
        doc[nameof(StringHolder.Text)].AsString.Should().BeEmpty();
    }

    [Fact]
    public void Omitted_Empty_Collections_RoundTrip_To_Null_Without_Initializer()
    {
        var mapper = new BsonMapper { DontSerializeEmptyCollections = true };
        var doc = mapper.ToDocument(new CollectionHolder
        {
            Id = 1,
            List = new List<int>(),
            Array = System.Array.Empty<int>(),
            Dictionary = new Dictionary<string, int>()
        });

        var roundTrip = mapper.ToObject<CollectionHolder>(doc);

        roundTrip.List.Should().BeNull();
        roundTrip.Array.Should().BeNull();
        roundTrip.Dictionary.Should().BeNull();
    }

    [Fact]
    public void Omitted_Empty_Collections_Preserve_Property_Initializers_On_RoundTrip()
    {
        var mapper = new BsonMapper { DontSerializeEmptyCollections = true };
        var doc = mapper.ToDocument(new InitializedCollectionHolder());

        var roundTrip = mapper.ToObject<InitializedCollectionHolder>(doc);

        roundTrip.Items.Should().NotBeNull();
        roundTrip.Items.Should().BeEmpty();
        roundTrip.Tags.Should().NotBeNull();
        roundTrip.Tags.Should().BeEmpty();
    }

    private class CollectionHolder
    {
        public int Id { get; set; }
        public List<int> List { get; set; }
        public int[] Array { get; set; }
        public Dictionary<string, int> Dictionary { get; set; }
    }

    private class StringHolder
    {
        public int Id { get; set; }
        public string Text { get; set; }
    }

    private class InitializedCollectionHolder
    {
        public List<int> Items { get; set; } = new();
        public Dictionary<string, int> Tags { get; set; } = new();
    }
}

