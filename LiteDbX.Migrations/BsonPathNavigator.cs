using System;

namespace LiteDbX.Migrations;

public static class BsonPathNavigator
{
    public static bool PathsConflict(string sourcePath, string targetPath)
    {
        if (string.Equals(sourcePath, targetPath, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IsAncestorOrDescendant(sourcePath, targetPath) || IsAncestorOrDescendant(targetPath, sourcePath);
    }

    public static bool TryGet(BsonDocument document, string path, out BsonDocument parent, out string fieldName, out BsonValue value)
    {
        if (document == null) throw new ArgumentNullException(nameof(document));
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentNullException(nameof(path));

        parent = document;
        value = BsonValue.Null;

        var segments = path.Split('.');

        for (var i = 0; i < segments.Length - 1; i++)
        {
            var segment = segments[i];

            if (segment.Length == 0)
            {
                fieldName = string.Empty;
                parent = null;
                return false;
            }

            if (!parent.TryGetValue(segment, out var child) || child.IsNull || !child.IsDocument)
            {
                fieldName = segments[segments.Length - 1];
                parent = null;
                return false;
            }

            parent = child.AsDocument;
        }

        fieldName = segments[segments.Length - 1];

        if (fieldName.Length == 0)
        {
            parent = null;
            return false;
        }

        if (parent.TryGetValue(fieldName, out value))
        {
            return true;
        }

        value = BsonValue.Null;
        return false;
    }

    public static BsonPredicateContext CreateContext(BsonDocument document, string path, string collection, string migrationName)
    {
        return TryGet(document, path, out _, out _, out var value)
            ? new BsonPredicateContext(document, path, true, value, collection, migrationName)
            : BsonPredicateContext.Missing(document, path, collection, migrationName);
    }

    public static bool TryAdd(BsonDocument document, string path, BsonValue value, bool overwrite)
    {
        var exists = TryGet(document, path, out var parent, out var fieldName, out _);

        if (parent == null)
        {
            return false;
        }

        if (exists && overwrite == false)
        {
            return false;
        }

        parent[fieldName] = value ?? BsonValue.Null;
        return true;
    }

    public static bool TryReplace(BsonDocument document, string path, BsonValue value)
    {
        var exists = TryGet(document, path, out var parent, out var fieldName, out var current);

        if (!exists || parent == null)
        {
            return false;
        }

        value ??= BsonValue.Null;

        if (current == value)
        {
            return false;
        }

        parent[fieldName] = value;
        return true;
    }

    public static bool TryRemove(BsonDocument document, string path, bool pruneEmptyParents)
    {
        var exists = TryGet(document, path, out var parent, out var fieldName, out _);

        if (!exists || parent == null)
        {
            return false;
        }

        if (!parent.Remove(fieldName))
        {
            return false;
        }

        if (pruneEmptyParents)
        {
            PruneEmptyParents(document, path);
        }

        return true;
    }

    public static BsonValue CloneValue(BsonValue value)
    {
        if (value == null || value.IsNull)
        {
            return BsonValue.Null;
        }

        if (value.IsDocument)
        {
            var clone = new BsonDocument();

            foreach (var element in value.AsDocument)
            {
                clone[element.Key] = CloneValue(element.Value);
            }

            return clone;
        }

        if (value.IsArray)
        {
            var clone = new BsonArray();

            foreach (var item in value.AsArray)
            {
                clone.Add(CloneValue(item));
            }

            return clone;
        }

        if (value.IsBinary)
        {
            var bytes = value.AsBinary;
            var copy = new byte[bytes.Length];
            Array.Copy(bytes, copy, bytes.Length);
            return new BsonValue(copy);
        }

        switch (value.Type)
        {
            case BsonType.Int32:
                return new BsonValue(value.AsInt32);
            case BsonType.Int64:
                return new BsonValue(value.AsInt64);
            case BsonType.Double:
                return new BsonValue(value.AsDouble);
            case BsonType.Decimal:
                return new BsonValue(value.AsDecimal);
            case BsonType.String:
                return new BsonValue(value.AsString);
            case BsonType.ObjectId:
                return new BsonValue(new ObjectId(value.AsObjectId));
            case BsonType.Guid:
                return new BsonValue(value.AsGuid);
            case BsonType.Boolean:
                return new BsonValue(value.AsBoolean);
            case BsonType.DateTime:
                return new BsonValue(value.AsDateTime);
            case BsonType.Null:
                return BsonValue.Null;
            case BsonType.MinValue:
                return BsonValue.MinValue;
            case BsonType.MaxValue:
                return BsonValue.MaxValue;
            default:
                throw new NotSupportedException($"Unsupported BSON type '{value.Type}' for cloning.");
        }
    }

    private static void PruneEmptyParents(BsonDocument document, string path)
    {
        var segments = path.Split('.');

        for (var depth = segments.Length - 1; depth > 0; depth--)
        {
            var parentPath = string.Join(".", segments, 0, depth);

            if (!TryGet(document, parentPath, out var parent, out var fieldName, out var value) || !value.IsDocument)
            {
                continue;
            }

            if (value.AsDocument.Count > 0)
            {
                break;
            }

            parent?.Remove(fieldName);
        }
    }

    private static bool IsAncestorOrDescendant(string candidateAncestor, string candidateDescendant)
    {
        if (candidateAncestor.Length >= candidateDescendant.Length)
        {
            return false;
        }

        return candidateDescendant.StartsWith(candidateAncestor + ".", StringComparison.OrdinalIgnoreCase);
    }
}

