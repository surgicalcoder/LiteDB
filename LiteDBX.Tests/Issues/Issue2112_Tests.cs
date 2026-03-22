using System.Collections.Generic;
using Xunit;

namespace LiteDbX.Tests.Issues;

public class Issue2112_Tests
{
    private readonly BsonMapper _mapper = new();

    [Fact]
    public void Serialize_covariant_collection_has_type()
    {
        IA a = new A { Bs = new List<B> { new() } };

        var docA = _mapper.Serialize(a).AsDocument;
        var docB = docA["Bs"].AsArray[0].AsDocument;

        Assert.True(docA.ContainsKey("_type"));
        Assert.True(docB.ContainsKey("_type"));
    }

    [Fact]
    public void Deserialize_covariant_collection_succeed()
    {
        IA a = new A { Bs = new List<B> { new() } };
        var serialized = _mapper.Serialize(a);

        var deserialized = _mapper.Deserialize<IA>(serialized);

        Assert.Equal(1, deserialized.Bs.Count);
    }

    private interface IA
    {
        // at runtime this will be a List<B>
        IReadOnlyCollection<IB> Bs { get; set; }
    }

    private class A : IA
    {
        public IReadOnlyCollection<IB> Bs { get; set; }
    }

    private interface IB { }

    private class B : IB { }
}