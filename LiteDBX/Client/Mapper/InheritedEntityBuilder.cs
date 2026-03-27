using System;
using System.Linq.Expressions;

namespace LiteDbX;

/// <summary>
/// Helper class used to configure inheritance-aware member conventions for a base type.
/// </summary>
public class InheritedEntityBuilder<TBase>
{
    private readonly BsonMapper _mapper;

    internal InheritedEntityBuilder(BsonMapper mapper)
    {
        _mapper = mapper;
    }

    /// <summary>
    /// Configure the inherited id member for all descendants of <typeparamref name="TBase"/>.
    /// </summary>
    public InheritedEntityBuilder<TBase> Id<TMember>(Expression<Func<TBase, TMember>> member, BsonType storageType, bool autoId = true)
    {
        _mapper.RegisterInheritedIdConvention(typeof(TBase), _mapper.GetMemberFromExpression(member), storageType, autoId);

        return this;
    }

    /// <summary>
    /// Configure the conventional inherited Id member for all descendants of <typeparamref name="TBase"/>.
    /// </summary>
    public InheritedEntityBuilder<TBase> Id(BsonType storageType, bool autoId = true)
    {
        _mapper.RegisterInheritedIdConvention(typeof(TBase), _mapper.GetInheritedDefaultIdMember(typeof(TBase)), storageType, autoId);

        return this;
    }

    /// <summary>
    /// Ignore an inherited member for all descendants of <typeparamref name="TBase"/>.
    /// </summary>
    public InheritedEntityBuilder<TBase> Ignore<TMember>(Expression<Func<TBase, TMember>> member)
    {
        _mapper.RegisterInheritedIgnoreConvention(typeof(TBase), _mapper.GetMemberFromExpression(member));

        return this;
    }

    /// <summary>
    /// Configure a custom serializer/deserializer for an inherited member for all descendants of <typeparamref name="TBase"/>.
    /// </summary>
    public InheritedEntityBuilder<TBase> Serialize<TMember>(
        Expression<Func<TBase, TMember>> member,
        Func<TMember, BsonValue> serialize,
        Func<BsonValue, TMember> deserialize)
        => Serialize(member, (value, _) => serialize(value), (value, _) => deserialize(value));

    /// <summary>
    /// Configure a custom serializer/deserializer for an inherited member for all descendants of <typeparamref name="TBase"/>.
    /// </summary>
    public InheritedEntityBuilder<TBase> Serialize<TMember>(
        Expression<Func<TBase, TMember>> member,
        Func<TMember, BsonMapper, BsonValue> serialize,
        Func<BsonValue, BsonMapper, TMember> deserialize)
    {
        if (serialize == null)
        {
            throw new ArgumentNullException(nameof(serialize));
        }

        if (deserialize == null)
        {
            throw new ArgumentNullException(nameof(deserialize));
        }

        _mapper.RegisterInheritedSerializationConvention(
            typeof(TBase),
            _mapper.GetMemberFromExpression(member),
            (value, mapper) => serialize((TMember)value, mapper),
            (value, mapper) => deserialize(value, mapper));

        return this;
    }
}
