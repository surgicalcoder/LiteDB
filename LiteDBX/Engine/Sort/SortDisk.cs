using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using static LiteDbX.Constants;

namespace LiteDbX.Engine;

/// <summary>
/// Single instance of TempDisk managing read/write access to temporary disk — used in merge sort.
/// [ThreadSafe]
///
/// Phase 3: <see cref="Write"/> is now async; <c>lock(writer)</c> replaced with
/// <see cref="SemaphoreSlim"/> so the sort merge path does not block threads during disk writes.
/// </summary>
internal class SortDisk : IDisposable
{
    private readonly IStreamFactory _factory;
    private readonly ConcurrentBag<long> _freePositions = new();
    private readonly StreamPool _pool;
    private readonly EnginePragmas _pragmas;
    private readonly SemaphoreSlim _writeGate = new SemaphoreSlim(1, 1);
    private long _lastContainerPosition;

    public SortDisk(IStreamFactory factory, int containerSize, EnginePragmas pragmas)
    {
        ENSURE(containerSize % PAGE_SIZE == 0, "size must be PAGE_SIZE multiple");

        _factory = factory;
        ContainerSize = containerSize;
        _pragmas = pragmas;

        _lastContainerPosition = -containerSize;

        _pool = new StreamPool(_factory, false);
    }

    public int ContainerSize { get; }

    public void Dispose()
    {
        _writeGate.Dispose();
        _pool.Dispose();
        _factory.Delete();
    }

    /// <summary>Get a reader stream from the pool. Must be returned after use.</summary>
    public Stream GetReader() => _pool.Rent();

    /// <summary>Return an open reader stream for reuse.</summary>
    public void Return(Stream stream) => _pool.Return(stream);

    /// <summary>Return a used disk container position for reuse.</summary>
    public void Return(long position) => _freePositions.Add(position);

    /// <summary>
    /// Get the next available disk position — either reused from freed containers or a newly
    /// extended slot.  Thread-safe via <see cref="Interlocked"/>.
    /// </summary>
    public long GetContainerPosition()
    {
        if (_freePositions.TryTake(out var position))
            return position;

        position = Interlocked.Add(ref _lastContainerPosition, ContainerSize);
        return position;
    }

    /// <summary>
    /// Asynchronously write buffer container data to disk.
    /// Uses <see cref="SemaphoreSlim"/> instead of <c>lock</c> so the merge sort pipeline can
    /// await the write without blocking a thread-pool thread.
    /// </summary>
    public async ValueTask Write(long position, BufferSlice buffer, CancellationToken cancellationToken = default)
    {
        var writer = _pool.Writer.Value;

        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            for (var i = 0; i < ContainerSize / PAGE_SIZE; ++i)
            {
                writer.Position = position + i * PAGE_SIZE;
                await writer.WriteAsync(buffer.Array, buffer.Offset + i * PAGE_SIZE, PAGE_SIZE, cancellationToken)
                            .ConfigureAwait(false);
            }
        }
        finally
        {
            _writeGate.Release();
        }
    }

    /// <summary>
    /// Phase 4 bridge: synchronous write for code paths not yet converted to async.
    /// </summary>
    internal void WriteSync(long position, BufferSlice buffer)
    {
        var writer = _pool.Writer.Value;

        _writeGate.Wait();
        try
        {
            for (var i = 0; i < ContainerSize / PAGE_SIZE; ++i)
            {
                writer.Position = position + i * PAGE_SIZE;
                writer.Write(buffer.Array, buffer.Offset + i * PAGE_SIZE, PAGE_SIZE);
            }
        }
        finally
        {
            _writeGate.Release();
        }
    }
}