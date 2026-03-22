using System;
using System.Collections.Generic;
using System.IO;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace LiteDbX;

/// <summary>
/// Async-only file storage contract.
///
/// The synchronous <c>LiteFileStream&lt;TFileId&gt; : Stream</c> is no longer part of the public API
/// because <see cref="System.IO.Stream"/> carries obligatory synchronous members that violate the
/// async-only contract. It is replaced by <see cref="ILiteFileHandle{TFileId}"/>, which exposes
/// only async read/write/flush/seek operations.
///
/// Phase 5 (File Storage) is responsible for providing concrete implementations of
/// <see cref="ILiteFileHandle{TFileId}"/> and migrating the internal chunk-based storage layer.
/// </summary>
public interface ILiteStorage<TFileId>
{
    // ── Lookup ────────────────────────────────────────────────────────────────

    /// <summary>Find a file entry by id. Returns <c>null</c> if not found.</summary>
    ValueTask<LiteFileInfo<TFileId>> FindById(TFileId id, CancellationToken cancellationToken = default);

    /// <summary>Stream file entries matching a BsonExpression predicate.</summary>
    IAsyncEnumerable<LiteFileInfo<TFileId>> Find(BsonExpression predicate, CancellationToken cancellationToken = default);

    /// <summary>Stream file entries matching a parameterised predicate.</summary>
    IAsyncEnumerable<LiteFileInfo<TFileId>> Find(string predicate, BsonDocument parameters, CancellationToken cancellationToken = default);

    /// <summary>Stream file entries matching a predicate with positional args.</summary>
    IAsyncEnumerable<LiteFileInfo<TFileId>> Find(string predicate, params BsonValue[] args);

    /// <summary>Stream file entries matching a LINQ predicate.</summary>
    IAsyncEnumerable<LiteFileInfo<TFileId>> Find(Expression<Func<LiteFileInfo<TFileId>, bool>> predicate, CancellationToken cancellationToken = default);

    /// <summary>Stream all file entries.</summary>
    IAsyncEnumerable<LiteFileInfo<TFileId>> FindAll(CancellationToken cancellationToken = default);

    /// <summary>Returns <c>true</c> if a file with the given id exists.</summary>
    ValueTask<bool> Exists(TFileId id, CancellationToken cancellationToken = default);

    // ── Open handles ─────────────────────────────────────────────────────────

    /// <summary>
    /// Open or create a file for writing. Returns an async write handle.
    /// The caller must dispose the handle to flush and commit the write.
    /// </summary>
    ValueTask<ILiteFileHandle<TFileId>> OpenWrite(TFileId id, string filename, BsonDocument metadata = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Open an existing file for reading. Returns an async read handle.
    /// </summary>
    ValueTask<ILiteFileHandle<TFileId>> OpenRead(TFileId id, CancellationToken cancellationToken = default);

    // ── Upload ────────────────────────────────────────────────────────────────

    /// <summary>Upload file content from a <see cref="Stream"/>. Returns the resulting file metadata.</summary>
    ValueTask<LiteFileInfo<TFileId>> Upload(TFileId id, string filename, Stream stream, BsonDocument metadata = null, CancellationToken cancellationToken = default);

    /// <summary>Upload file content from the local file system. Returns the resulting file metadata.</summary>
    ValueTask<LiteFileInfo<TFileId>> Upload(TFileId id, string filename, CancellationToken cancellationToken = default);

    // ── Metadata ──────────────────────────────────────────────────────────────

    /// <summary>Update the metadata for an existing file. Returns <c>false</c> if the file was not found.</summary>
    ValueTask<bool> SetMetadata(TFileId id, BsonDocument metadata, CancellationToken cancellationToken = default);

    // ── Download ──────────────────────────────────────────────────────────────

    /// <summary>Download file content into a <see cref="Stream"/>. Returns the file metadata.</summary>
    ValueTask<LiteFileInfo<TFileId>> Download(TFileId id, Stream stream, CancellationToken cancellationToken = default);

    /// <summary>Download file content to the local file system. Returns the file metadata.</summary>
    ValueTask<LiteFileInfo<TFileId>> Download(TFileId id, string filename, bool overwritten, CancellationToken cancellationToken = default);

    // ── Delete ────────────────────────────────────────────────────────────────

    /// <summary>Delete a file and all its associated chunks. Returns <c>true</c> if the file existed.</summary>
    ValueTask<bool> Delete(TFileId id, CancellationToken cancellationToken = default);
}