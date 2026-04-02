using FluentAssertions;
using Xunit;

namespace LiteDbX.Tests.Mapper;

public class LowerCaseDelimiter_Tests
{
    [Theory]
    [InlineData("CustomerName", '_', "customer_name")]
    [InlineData("URLValue", '_', "u_r_l_value")]
    [InlineData("IPAddressV4", '_', "i_p_address_v4")]
    [InlineData("alreadyLowerCase", '_', "already_lower_case")]
    [InlineData("X", '_', "x")]
    [InlineData("", '_', "")]
    [InlineData("CustomerName", '-', "customer-name")]
    public void UseLowerCaseDelimiter_Resolves_Field_Names_As_Currently_Implemented(string memberName, char delimiter, string expected)
    {
        var mapper = new BsonMapper().UseLowerCaseDelimiter(delimiter);

        mapper.ResolveFieldName(memberName).Should().Be(expected);
    }

    [Fact]
    public void UseLowerCaseDelimiter_Applies_To_Serialized_Documents_And_Preserves_Id_Field()
    {
        var mapper = new BsonMapper().UseLowerCaseDelimiter();

        var doc = mapper.ToDocument(new DelimitedEntity
        {
            Id = 1,
            FirstName = "John",
            URLValue = "https://example.test"
        });

        doc["_id"].Should().Be(1);
        doc["first_name"].Should().Be("John");
        doc["u_r_l_value"].Should().Be("https://example.test");
        doc.ContainsKey(nameof(DelimitedEntity.FirstName)).Should().BeFalse();
        doc.ContainsKey(nameof(DelimitedEntity.URLValue)).Should().BeFalse();
    }

    [Fact]
    public void UseLowerCaseDelimiter_Applies_To_Deserialization_From_Delimited_Field_Names()
    {
        var mapper = new BsonMapper().UseLowerCaseDelimiter();

        var entity = mapper.ToObject<DelimitedEntity>(new BsonDocument
        {
            ["_id"] = 7,
            ["first_name"] = "Jane",
            ["u_r_l_value"] = "https://contoso.test"
        });

        entity.Id.Should().Be(7);
        entity.FirstName.Should().Be("Jane");
        entity.URLValue.Should().Be("https://contoso.test");
    }

    private class DelimitedEntity
    {
        public int Id { get; set; }

        public string FirstName { get; set; }

        public string URLValue { get; set; }
    }
}

