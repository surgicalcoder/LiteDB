using System;
using System.Collections.Generic;

namespace LiteDbX.Migrations;

internal sealed class DocumentMigrationExecutionContext
{
    private readonly Dictionary<string, Dictionary<string, ObjectId>> _remapLookup;
    private int _repairedReferences;

    public DocumentMigrationExecutionContext(string collectionName, string migrationName, string runId, Dictionary<string, Dictionary<string, ObjectId>> remapLookup)
    {
        CollectionName = collectionName ?? throw new ArgumentNullException(nameof(collectionName));
        MigrationName = migrationName ?? throw new ArgumentNullException(nameof(migrationName));
        RunId = runId ?? throw new ArgumentNullException(nameof(runId));
        _remapLookup = remapLookup ?? new Dictionary<string, Dictionary<string, ObjectId>>(StringComparer.OrdinalIgnoreCase);
    }

    public string CollectionName { get; }

    public string MigrationName { get; }

    public string RunId { get; }

    public int RepairedReferences => _repairedReferences;

    public bool TryGetRemappedObjectId(string sourceCollection, string sourceMigrationName, string oldIdRaw, BsonType oldIdType, out ObjectId objectId)
    {
        objectId = null;

        if (string.IsNullOrWhiteSpace(sourceCollection) || string.IsNullOrEmpty(oldIdRaw))
        {
            return false;
        }

        if (!_remapLookup.TryGetValue(BuildLookupKey(sourceCollection, sourceMigrationName), out var mappings))
        {
            return false;
        }

        return mappings.TryGetValue(BuildIdKey(oldIdRaw, oldIdType), out objectId);
    }

    public BsonDocumentMutationContext ToPublicContext()
    {
        return new BsonDocumentMutationContext(CollectionName, MigrationName);
    }

    public void IncrementRepairedReferenceCount()
    {
        _repairedReferences++;
    }

    public static string BuildLookupKey(string sourceCollection, string sourceMigrationName)
    {
        return (sourceCollection ?? string.Empty) + "\u001f" + (sourceMigrationName ?? string.Empty);
    }

    public static string BuildIdKey(string oldIdRaw, BsonType oldIdType)
    {
        return oldIdType + "\u001f" + (oldIdRaw ?? string.Empty);
    }
}

