using System.Collections.Generic;
using static LiteDbX.Constants;

namespace LiteDbX.Engine;

/// <summary>
/// Implement basic document loader based on data service/bson reader
/// </summary>
internal class DatafileLookup : IDocumentLookup
{
    protected readonly DataService _data;
    protected readonly HashSet<string> _fields;
    protected readonly bool _utcDate;

    public DatafileLookup(DataService data, bool utcDate, HashSet<string> fields)
    {
        _data = data;
        _utcDate = utcDate;
        _fields = fields;
    }

    public virtual BsonDocument Load(IndexNode node)
    {
        ENSURE(node.DataBlock != PageAddress.Empty, "data block must be a valid block address");

        return Load(node.DataBlock);
    }

    public virtual BsonDocument Load(PageAddress rawId)
    {
        using (var reader = new BufferReader(_data.Read(rawId), _utcDate))
        {
            var doc = reader.ReadDocument(_fields).GetValue();

            doc.RawId = rawId;

            return doc;
        }
    }
}