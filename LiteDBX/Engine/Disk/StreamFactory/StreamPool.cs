using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace LiteDbX.Engine;

/// <summary>
/// Manage multiple open readonly Stream instances from same source (file).
/// Support single writer instance.
/// Close all Stream on dispose.
/// [ThreadSafe]
///
/// Phase 3: implements <see cref="IAsyncDisposable"/> so that streams can be flushed
/// and closed without blocking a thread-pool thread on shutdown/close paths.
/// </summary>
internal class StreamPool : IDisposable, IAsyncDisposable
{
    private readonly IStreamFactory _factory;
    private readonly ConcurrentBag<Stream> _pool = new();

    public StreamPool(IStreamFactory factory, bool appendOnly)
    {
        _factory = factory;

        Writer = new Lazy<Stream>(() => _factory.GetStream(true, appendOnly), true);
    }

    /// <summary>
    /// Get single Stream writer instance
    /// </summary>
    public Lazy<Stream> Writer { get; }

    /// <summary>
    /// Close all Stream instances (readers/writer) — synchronous path.
    /// </summary>
    public void Dispose()
    {
        if (!_factory.CloseOnDispose)
        {
            return;
        }

        foreach (var stream in _pool)
        {
            stream.Dispose();
        }

        if (Writer.IsValueCreated)
        {
            Writer.Value.Dispose();
        }
    }

    /// <summary>
    /// Asynchronously close all Stream instances. Preferred on engine shutdown paths to avoid
    /// blocking the calling thread while OS flush operations complete.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (!_factory.CloseOnDispose)
        {
            return;
        }

        foreach (var stream in _pool)
        {
            if (stream is IAsyncDisposable asyncStream)
                await asyncStream.DisposeAsync().ConfigureAwait(false);
            else
                stream.Dispose();
        }

        if (Writer.IsValueCreated)
        {
            var writer = Writer.Value;
            if (writer is IAsyncDisposable asyncWriter)
                await asyncWriter.DisposeAsync().ConfigureAwait(false);
            else
                writer.Dispose();
        }
    }

    /// <summary>
    /// Rent a Stream reader instance
    /// </summary>
    public Stream Rent()
    {
        if (!_pool.TryTake(out var stream))
        {
            stream = _factory.GetStream(false, false);
        }

        return stream;
    }

    /// <summary>
    /// After use, return Stream reader instance
    /// </summary>
    public void Return(Stream stream)
    {
        _pool.Add(stream);
    }
}