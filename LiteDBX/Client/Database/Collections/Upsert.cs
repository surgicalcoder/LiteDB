using System;
using System.Collections.Generic;

namespace LiteDbX;

public partial class LiteCollection<T>
{
    /// <summary>
    /// Insert or Update a document in this collection.
    /// </summary>
    public bool Upsert(T entity)
    {
        if (entity == null)
        {
            throw new ArgumentNullException(nameof(entity));
        }

        return Upsert(new[] { entity }) == 1;
    }

    /// <summary>
    /// Insert or Update all documents
    /// </summary>
    public int Upsert(IEnumerable<T> entities)
    {
        if (entities == null)
        {
            throw new ArgumentNullException(nameof(entities));
        }

        return _engine.Upsert(Name, GetBsonDocs(entities), AutoId);
    }

    /// <summary>
    /// Insert or Update a document in this collection.
    /// </summary>
    public bool Upsert(BsonValue id, T entity)
    {
        if (entity == null)
        {
            throw new ArgumentNullException(nameof(entity));
        }

        if (id == null || id.IsNull)
        {
            throw new ArgumentNullException(nameof(id));
        }

        // get BsonDocument from object
        var doc = _mapper.ToDocument(entity);

        // set document _id using id parameter
        doc["_id"] = id;

        return _engine.Upsert(Name, new[] { doc }, AutoId) > 0;
    }
}