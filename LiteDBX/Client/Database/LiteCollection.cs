using System;
using System.Collections.Generic;
using LiteDbX.Engine;

namespace LiteDbX;

public sealed partial class LiteCollection<T> : ILiteCollection<T>
{
    private readonly ILiteEngine _engine;
    private readonly MemberMapper _id;
    private readonly List<BsonExpression> _includes;
    private readonly BsonMapper _mapper;

    internal LiteCollection(string name, BsonAutoId autoId, ILiteEngine engine, BsonMapper mapper)
    {
        Name = name ?? mapper.ResolveCollectionName(typeof(T));
        _engine = engine;
        _mapper = mapper;
        _includes = new List<BsonExpression>();

        // if strong typed collection, get _id member mapped (if exists)
        if (typeof(T) == typeof(BsonDocument))
        {
            EntityMapper = null;
            _id = null;
            AutoId = autoId;
        }
        else
        {
            EntityMapper = mapper.GetEntityMapper(typeof(T));
            EntityMapper.WaitForInitialization();

            _id = EntityMapper.Id;

            if (_id != null && _id.AutoId)
            {
                AutoId =
                    _id.DataType == typeof(int) || _id.DataType == typeof(int?) ? BsonAutoId.Int32 :
                    _id.DataType == typeof(long) || _id.DataType == typeof(long?) ? BsonAutoId.Int64 :
                    _id.DataType == typeof(Guid) || _id.DataType == typeof(Guid?) ? BsonAutoId.Guid :
                    BsonAutoId.ObjectId;
            }
            else
            {
                AutoId = autoId;
            }
        }
    }

    /// <summary>
    /// Get collection name
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Get collection auto id type
    /// </summary>
    public BsonAutoId AutoId { get; }

    /// <summary>
    /// Getting entity mapper from current collection. Returns null if collection are BsonDocument type
    /// </summary>
    public EntityMapper EntityMapper { get; }
}