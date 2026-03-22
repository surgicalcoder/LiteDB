using System;
using System.Collections.Concurrent;
using System.IO;

namespace LiteDbX.Engine;

/// <summary>
/// Manage multiple open readonly Stream instances from same source (file).
/// Support single writer instance
/// Close all Stream on dispose
/// [ThreadSafe]
/// </summary>
internal class StreamPool : IDisposable
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
    /// Close all Stream instances (readers/writer)
    /// </summary>
    public void Dispose()
    {
        // dipose stream only implement on factory
        if (!_factory.CloseOnDispose)
        {
            return;
        }

        // dispose all reader stream
        foreach (var stream in _pool)
        {
            stream.Dispose();
        }

        // do writer dispose (wait async writer thread)
        if (Writer.IsValueCreated)
        {
            Writer.Value.Dispose();
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