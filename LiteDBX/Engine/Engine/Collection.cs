using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static LiteDbX.Constants;

namespace LiteDbX.Engine;

public partial class LiteEngine
{
    /// <summary>
    /// Drop collection including all documents, indexes and extended pages (do not support transactions)
    /// </summary>
    public ValueTask<bool> DropCollection(string name, CancellationToken cancellationToken = default)
    {
        if (name.IsNullOrWhiteSpace())
        {
            throw new ArgumentNullException(nameof(name));
        }

        // DropCollection requires no active explicit transaction.
        if (LiteTransaction.HasActive)
        {
            throw LiteException.AlreadyExistsTransaction();
        }

        return AutoTransactionAsync(async (transaction, ct) =>
        {
            var snapshot = await transaction.CreateSnapshotAsync(LockMode.Write, name, false, ct).ConfigureAwait(false);

            // if collection do not exist, just exit
            if (snapshot.CollectionPage == null)
            {
                return false;
            }

            LOG($"drop collection `{name}`", "COMMAND");

            // call drop collection service
            snapshot.DropCollection(transaction.Safepoint);

            // remove sequence number (if exists)
            _sequences.TryRemove(name, out var dummy);

            return true;
        }, cancellationToken);
    }

    /// <summary>
    /// Rename a collection (do not support transactions)
    /// </summary>
    public ValueTask<bool> RenameCollection(string collection, string newName, CancellationToken cancellationToken = default)
    {
        if (collection.IsNullOrWhiteSpace())
        {
            throw new ArgumentNullException(nameof(collection));
        }

        if (newName.IsNullOrWhiteSpace())
        {
            throw new ArgumentNullException(nameof(newName));
        }

        if (LiteTransaction.HasActive)
        {
            throw LiteException.AlreadyExistsTransaction();
        }

        // check for collection name
        if (collection.Equals(newName, StringComparison.OrdinalIgnoreCase))
        {
            throw LiteException.InvalidCollectionName(newName, "New name must be different from current name");
        }

        // checks if newName are compatible
        CollectionService.CheckName(newName, _header);

        return AutoTransactionAsync(async (transaction, ct) =>
        {
            var currentSnapshot = await transaction.CreateSnapshotAsync(LockMode.Write, collection, false, ct).ConfigureAwait(false);
            var newSnapshot = await transaction.CreateSnapshotAsync(LockMode.Write, newName, false, ct).ConfigureAwait(false);

            if (currentSnapshot.CollectionPage == null)
            {
                return false;
            }

            // checks if do not already exists this collection name
            if (_header.GetCollectionPageID(newName) != uint.MaxValue)
            {
                throw LiteException.AlreadyExistsCollectionName(newName);
            }

            // rename collection and set page as dirty (there is no need to set IsDirty in HeaderPage)
            transaction.Pages.Commit += h => { h.RenameCollection(collection, newName); };

            return true;
        }, cancellationToken);
    }

    /// <summary>
    /// Returns all collection inside datafile
    /// </summary>
    public IEnumerable<string> GetCollectionNames()
    {
        return _header.GetCollections().Select(x => x.Key);
    }
}