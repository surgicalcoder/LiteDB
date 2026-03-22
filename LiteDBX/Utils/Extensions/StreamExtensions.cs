using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace LiteDbX;

internal static class StreamExtensions
{
    /// <summary>
    /// If Stream are FileStream, flush content direct to disk (avoid OS cache)
    /// </summary>
    public static void FlushToDisk(this Stream stream)
    {
        if (stream is FileStream fstream)
        {
            fstream.Flush(true);
        }
        else
        {
            stream.Flush();
        }
    }

    /// <summary>
    /// Asynchronously flush stream data to the OS buffer (and, on supported platforms, to disk).
    ///
    /// Platform note: On .NET 6+ with <c>FileOptions.Asynchronous</c>, <c>FileStream.FlushAsync</c>
    /// issues a flush that reaches physical storage on most OS configurations.
    /// On older runtimes or non-file streams, this flushes to the OS write buffer but durability
    /// at the physical layer is not guaranteed by the async path alone.
    /// For hard durability requirements on older platforms, use <see cref="FlushToDisk"/> after
    /// the async write completes (acceptable on shutdown paths where blocking is tolerable).
    /// </summary>
    public static ValueTask FlushToDiskAsync(this Stream stream, CancellationToken cancellationToken = default)
    {
        return new ValueTask(stream.FlushAsync(cancellationToken));
    }
}