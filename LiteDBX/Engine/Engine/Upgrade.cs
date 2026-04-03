using System;
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace LiteDbX.Engine;

public partial class LiteEngine
{
    private static readonly ArrayPool<byte> _bufferPool = ArrayPool<byte>.Shared;

    /// <summary>
    /// Detect and upgrade a v7 datafile to the current format before the disk service opens.
    /// Used by the explicit <c>LiteEngine.Open(...)</c> lifecycle.
    /// </summary>
    private async ValueTask TryUpgrade(CancellationToken cancellationToken)
    {
        var filename = _settings.Filename;

        if (!File.Exists(filename))
        {
            return;
        }

        const int bufferSize = 1024;
        var buffer = _bufferPool.Rent(bufferSize);

        try
        {
            using (var stream = new FileStream(
                       _settings.Filename,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.Read,
                       bufferSize,
                       FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                stream.Position = 0;

                var bytesRead = 0;
                while (bytesRead < bufferSize)
                {
                    var read = await stream.ReadAsync(buffer, bytesRead, bufferSize - bytesRead, cancellationToken)
                        .ConfigureAwait(false);

                    if (read == 0)
                    {
                        break;
                    }

                    bytesRead += read;
                }

                if (!FileReaderV7.IsVersion(buffer))
                {
                    return;
                }
            }

            await Recovery(_settings.Collation, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _bufferPool.Return(buffer, true);
        }
    }

}