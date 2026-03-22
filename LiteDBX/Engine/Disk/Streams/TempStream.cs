using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using static LiteDbX.Constants;

namespace LiteDbX.Engine;

/// <summary>
/// Implement a temporary stream that uses MemoryStream until get LIMIT bytes, then copy all to temporary disk file and
/// delete on dispose.
///
/// Phase 3: <see cref="WriteAsync"/> checks the spill threshold and performs the in-memory →
/// file copy asynchronously via <see cref="Stream.CopyToAsync"/>, ensuring the hot sort/temp
/// write path does not block a thread-pool thread.
/// <see cref="DisposeAsync"/> is provided so the file deletion can be awaited in async contexts.
/// </summary>
public class TempStream : Stream, IAsyncDisposable
{
    private readonly long _maxMemoryUsage;
    private Stream _stream = new MemoryStream();

    public TempStream(string filename = null, long maxMemoryUsage = 10485760 /* 10MB */)
    {
        _maxMemoryUsage = maxMemoryUsage;
        Filename = filename;
    }

    /// <summary>
    /// Indicate that stream are all in memory
    /// </summary>
    public bool InMemory => _stream is MemoryStream;

    /// <summary>
    /// Indicate that stream is now on disk
    /// </summary>
    public bool InDisk => _stream is FileStream;

    /// <summary>
    /// Get temp disk filename (if null will be generated only when creating file)
    /// </summary>
    public string Filename { get; private set; }

    public override bool CanRead => _stream.CanRead;
    public override bool CanSeek => _stream.CanWrite;
    public override bool CanWrite => _stream.CanWrite;
    public override long Length => _stream.Length;

    public override long Position
    {
        get => _stream.Position;
        set => Seek(value, SeekOrigin.Begin);
    }

    public override void Flush() => _stream.Flush();

    public override Task FlushAsync(CancellationToken cancellationToken) =>
        _stream.FlushAsync(cancellationToken);

    public override int Read(byte[] buffer, int offset, int count) =>
        _stream.Read(buffer, offset, count);

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        _stream.ReadAsync(buffer, offset, count, cancellationToken);

    public override void SetLength(long value) => _stream.SetLength(value);

    public override void Write(byte[] buffer, int offset, int count)
    {
        _stream.Write(buffer, offset, count);
    }

    /// <summary>
    /// Async write. When the current stream position would exceed <see cref="_maxMemoryUsage"/>
    /// and the stream is still in memory, the memory contents are copied to a temporary file
    /// asynchronously before the write is dispatched.
    /// </summary>
    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        if (_stream.Position + count > _maxMemoryUsage && InMemory)
        {
            await SpillToFileAsync(cancellationToken).ConfigureAwait(false);
        }

        await _stream.WriteAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        var position =
            origin == SeekOrigin.Begin ? offset :
            origin == SeekOrigin.Current ? _stream.Position + offset :
            _stream.Position - offset;

        // Sync spill path (used when position is set before a sync Write).
        if (position > _maxMemoryUsage && InMemory)
        {
            SpillToFile();
        }

        return _stream.Seek(offset, origin);
    }

    // ── Sync spill (used by the existing sync Seek path) ─────────────────────

    private void SpillToFile()
    {
        Filename = Filename ?? Path.Combine(Path.GetTempPath(), "litedb_" + Guid.NewGuid() + ".db");

        var file = new FileStream(Filename, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, PAGE_SIZE,
            FileOptions.RandomAccess | FileOptions.Asynchronous);

        _stream.Position = 0;
        _stream.CopyTo(file);
        _stream.Dispose();
        _stream = file;
    }

    // ── Async spill (used by WriteAsync path) ─────────────────────────────────

    private async Task SpillToFileAsync(CancellationToken cancellationToken)
    {
        Filename = Filename ?? Path.Combine(Path.GetTempPath(), "litedb_" + Guid.NewGuid() + ".db");

        var file = new FileStream(Filename, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, PAGE_SIZE,
            FileOptions.RandomAccess | FileOptions.Asynchronous);

        var previousPosition = _stream.Position;
        _stream.Position = 0;
        await _stream.CopyToAsync(file, PAGE_SIZE, cancellationToken).ConfigureAwait(false);
        file.Position = previousPosition;

        // MemoryStream.Dispose() is synchronous and allocates no OS resources — safe to call here.
        _stream.Dispose();
        _stream = file;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        _stream.Dispose();

        if (InDisk && Filename != null)
        {
            File.Delete(Filename);
        }
    }

    /// <summary>
    /// Async dispose — flushes and closes the underlying stream, then deletes any spill file.
    /// Uses an <see cref="IAsyncDisposable"/> runtime check so the code compiles and works
    /// correctly on both netstandard2.0 (sync Stream disposal) and .NET 5+ (async FileStream flush).
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_stream is IAsyncDisposable asyncDisposable)
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
        else
            _stream.Dispose();

        if (Filename != null && File.Exists(Filename))
            File.Delete(Filename);

        GC.SuppressFinalize(this);
    }
}