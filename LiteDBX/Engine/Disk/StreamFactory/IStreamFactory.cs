using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace LiteDbX.Engine;

/// <summary>
/// Interface factory to provider new Stream instances for datafile/walfile resources. It's useful to multiple threads can
/// read same datafile
///
/// Phase 3: <see cref="GetStreamAsync"/> added as the preferred async-first acquisition path.
/// File-stream constructors are inherently synchronous in .NET, so <see cref="GetStreamAsync"/>
/// implementations typically complete synchronously; the key benefit is that the returned
/// stream must be opened with <c>FileOptions.Asynchronous</c> so I/O operations on it are
/// genuinely non-blocking.
/// </summary>
internal interface IStreamFactory
{
    /// <summary>
    /// Get Stream name (filename)
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Indicate that factory must be dispose on finish
    /// </summary>
    bool CloseOnDispose { get; }

    /// <summary>
    /// Get new Stream instance (synchronous; used for startup and legacy sync-bridge paths).
    /// </summary>
    Stream GetStream(bool canWrite, bool sequencial);

    /// <summary>
    /// Async-first stream acquisition.
    /// The returned <see cref="Stream"/> is opened with <c>FileOptions.Asynchronous</c> where
    /// applicable so that <see cref="Stream.ReadAsync"/> / <see cref="Stream.WriteAsync"/> dispatch
    /// genuine async I/O rather than blocking a thread-pool thread.
    ///
    /// Implementation note: FileStream construction is synchronous in all current .NET runtimes;
    /// implementations may return a completed <see cref="ValueTask{T}"/>.
    /// </summary>
    ValueTask<Stream> GetStreamAsync(bool canWrite, bool sequential, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get file length
    /// </summary>
    long GetLength();

    /// <summary>
    /// Checks if file exists
    /// </summary>
    bool Exists();

    /// <summary>
    /// Delete physical file on disk
    /// </summary>
    void Delete();

    /// <summary>
    /// Test if this file are used by another process
    /// </summary>
    bool IsLocked();
}