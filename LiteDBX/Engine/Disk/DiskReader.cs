using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using static LiteDbX.Constants;

namespace LiteDbX.Engine;

/// <summary>
/// Memory file reader — must call Dispose/DisposeAsync after use to return reader streams into
/// the pool.
/// This class is not ThreadSafe — must have 1 instance per async execution context (obtained
/// from <see cref="DiskService.GetReader"/>).
///
/// Phase 3: <see cref="ReadPageAsync"/> delegates to the async MemoryCache page-load methods
/// so that cache misses issue genuine async stream reads rather than blocking a thread.
/// </summary>
internal class DiskReader : IDisposable, IAsyncDisposable
{
    private readonly MemoryCache _cache;
    private readonly StreamPool _dataPool;
    private readonly Lazy<Stream> _dataStream;
    private readonly StreamPool _logPool;
    private readonly Lazy<Stream> _logStream;
    private readonly EngineState _state;

    public DiskReader(EngineState state, MemoryCache cache, StreamPool dataPool, StreamPool logPool)
    {
        _state = state;
        _cache = cache;
        _dataPool = dataPool;
        _logPool = logPool;

        _dataStream = new Lazy<Stream>(() => _dataPool.Rent());
        _logStream = new Lazy<Stream>(() => _logPool.Rent());
    }

    /// <summary>
    /// When dispose, return stream to pool (synchronous path).
    /// </summary>
    public void Dispose()
    {
        if (_dataStream.IsValueCreated)
            _dataPool.Return(_dataStream.Value);

        if (_logStream.IsValueCreated)
            _logPool.Return(_logStream.Value);
    }

    /// <summary>
    /// Async dispose — returns streams to their pools. Stream return itself is non-blocking so
    /// this completes synchronously; the method exists for composability with IAsyncDisposable
    /// disposal chains.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        Dispose();
        return default;
    }

    // ── Sync read (Phase 4 bridge — called from QueryExecutor legacy path) ────

    public PageBuffer ReadPage(long position, bool writable, FileOrigin origin)
    {
        ENSURE(position % PAGE_SIZE == 0, "invalid page position");

        var stream = origin == FileOrigin.Data ? _dataStream.Value : _logStream.Value;

        var page = writable
            ? _cache.GetWritablePage(position, origin, (pos, buf) => ReadStream(stream, pos, buf))
            : _cache.GetReadablePage(position, origin, (pos, buf) => ReadStream(stream, pos, buf));

#if DEBUG
        _state.SimulateDiskReadFail?.Invoke(page);
#endif

        return page;
    }

    // ── Async read (Phase 3 primary path) ─────────────────────────────────────

    /// <summary>
    /// Read a page asynchronously. On a cache miss, the page bytes are loaded from the underlying
    /// stream using <see cref="Stream.ReadAsync"/> so no thread-pool thread is blocked for I/O.
    /// </summary>
    public async ValueTask<PageBuffer> ReadPageAsync(long position, bool writable, FileOrigin origin,
        CancellationToken cancellationToken = default)
    {
        ENSURE(position % PAGE_SIZE == 0, "invalid page position");

        var stream = origin == FileOrigin.Data ? _dataStream.Value : _logStream.Value;

        ValueTask ReadStreamAsync(long pos, BufferSlice buf) =>
            ReadStreamAsyncCore(stream, pos, buf, cancellationToken);

        var page = writable
            ? await _cache.GetWritablePageAsync(position, origin, ReadStreamAsync, cancellationToken).ConfigureAwait(false)
            : await _cache.GetReadablePageAsync(position, origin, ReadStreamAsync, cancellationToken).ConfigureAwait(false);

#if DEBUG
        _state.SimulateDiskReadFail?.Invoke(page);
#endif

        return page;
    }

    // ── Stream helpers ────────────────────────────────────────────────────────

    private void ReadStream(Stream stream, long position, BufferSlice buffer)
    {
        stream.Position = position;
        stream.Read(buffer.Array, buffer.Offset, buffer.Count);
        DEBUG(!buffer.All(0), "check if are not reading out of file length");
    }

    private async ValueTask ReadStreamAsyncCore(Stream stream, long position, BufferSlice buffer,
        CancellationToken cancellationToken)
    {
        stream.Position = position;
        await stream.ReadAsync(buffer.Array, buffer.Offset, buffer.Count, cancellationToken).ConfigureAwait(false);
        DEBUG(!buffer.All(0), "check if are not reading out of file length");
    }

    /// <summary>
    /// Request for a empty, writable non-linked page (same as DiskService.NewPage)
    /// </summary>
    public PageBuffer NewPage() => _cache.NewPage();
}