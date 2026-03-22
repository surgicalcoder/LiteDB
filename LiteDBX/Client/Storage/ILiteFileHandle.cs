using System;
using System.Threading;
using System.Threading.Tasks;

namespace LiteDbX;

/// <summary>
/// An async-only handle to an open file entry in LiteDB file storage.
///
/// Replaces the synchronous <c>LiteFileStream&lt;TFileId&gt;</c> (which inherited <see cref="System.IO.Stream"/>)
/// in the public-facing contract. The underlying <c>Stream</c> type is not exposed because it carries
/// obligatory synchronous members (<c>Read</c>, <c>Write</c>, <c>Flush</c>) that violate the async-only contract.
///
/// Phase 5 (File Storage) is responsible for providing the concrete implementation.
/// </summary>
/// <typeparam name="TFileId">The file identifier type, typically <see cref="string"/>.</typeparam>
public interface ILiteFileHandle<TFileId> : IAsyncDisposable
{
    /// <summary>Metadata and properties for this file entry.</summary>
    LiteFileInfo<TFileId> FileInfo { get; }

    /// <summary>Whether the handle supports reading.</summary>
    bool CanRead { get; }

    /// <summary>Whether the handle supports writing.</summary>
    bool CanWrite { get; }

    /// <summary>Total byte length of the file.</summary>
    long Length { get; }

    /// <summary>Current read/write position within the file.</summary>
    long Position { get; }

    /// <summary>
    /// Read up to <paramref name="buffer"/>.Length bytes from the current position into <paramref name="buffer"/>.
    /// Returns the number of bytes actually read, or 0 at end-of-file.
    /// </summary>
    ValueTask<int> Read(Memory<byte> buffer, CancellationToken cancellationToken = default);

    /// <summary>
    /// Write all bytes in <paramref name="buffer"/> starting at the current position.
    /// </summary>
    ValueTask Write(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default);

    /// <summary>
    /// Flush any pending write buffers to storage.
    /// </summary>
    ValueTask Flush(CancellationToken cancellationToken = default);

    /// <summary>
    /// Set the current position within the file.
    /// Only supported for read handles.
    /// </summary>
    ValueTask Seek(long position, CancellationToken cancellationToken = default);
}

