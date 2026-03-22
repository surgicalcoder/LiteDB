using System.IO;
using System.Threading;
using System.Threading.Tasks;
using static LiteDbX.Constants;

namespace LiteDbX.Engine;

/// <summary>
/// FileStream disk implementation of disk factory
/// [ThreadSafe]
///
/// Phase 3: all FileStream instances are now opened with <c>FileOptions.Asynchronous</c> so that
/// <see cref="Stream.ReadAsync"/> / <see cref="Stream.WriteAsync"/> dispatch genuine OS-level
/// async I/O (IOCP on Windows, io_uring / epoll on Linux) instead of blocking a thread-pool thread.
/// </summary>
internal class FileStreamFactory : IStreamFactory
{
    private readonly string _filename;
    private readonly bool _hidden;
    private readonly string _password;
    private readonly bool _readonly;
    private readonly bool _useAesStream;

    public FileStreamFactory(string filename, string password, bool readOnly, bool hidden, bool useAesStream = true)
    {
        _filename = filename;
        _password = password;
        _readonly = readOnly;
        _hidden = hidden;
        _useAesStream = useAesStream;
    }

    /// <summary>
    /// Get data filename
    /// </summary>
    public string Name => Path.GetFileName(_filename);

    /// <summary>
    /// Create new data file FileStream instance based on filename.
    /// Opens with <c>FileOptions.Asynchronous</c> (combined with sequential/random hint) so that
    /// all read and write operations on the returned stream use async I/O paths.
    /// </summary>
    public Stream GetStream(bool canWrite, bool sequencial)
    {
        var write = canWrite && !_readonly;

        var fileMode = _readonly ? FileMode.Open : FileMode.OpenOrCreate;
        var fileAccess = write ? FileAccess.ReadWrite : FileAccess.Read;
        var fileShare = write ? FileShare.Read : FileShare.ReadWrite;

        // Phase 3: always include FileOptions.Asynchronous so ReadAsync/WriteAsync use
        // OS-level async I/O instead of falling back to synchronous thread-pool dispatches.
        var fileOptions = FileOptions.Asynchronous |
                          (sequencial ? FileOptions.SequentialScan : FileOptions.RandomAccess);

        var isNewFile = write && !Exists();

        var stream = new FileStream(_filename,
            fileMode,
            fileAccess,
            fileShare,
            PAGE_SIZE,
            fileOptions);

        if (isNewFile && _hidden)
        {
            File.SetAttributes(_filename, FileAttributes.Hidden);
        }

        return _password == null || !_useAesStream ? stream : new AesStream(_password, stream);
    }

    /// <summary>
    /// Async-compatible stream acquisition.
    /// FileStream construction is synchronous in all current .NET runtimes, so this completes
    /// synchronously; however the returned stream uses <c>FileOptions.Asynchronous</c> so its
    /// read/write operations are genuinely non-blocking.
    /// </summary>
    public ValueTask<Stream> GetStreamAsync(bool canWrite, bool sequential, CancellationToken cancellationToken = default)
    {
        return new ValueTask<Stream>(GetStream(canWrite, sequential));
    }

    /// <summary>
    /// Get file length using FileInfo. Crop file length if not length % PAGE_SIZE
    /// </summary>
    public long GetLength()
    {
        // if not file do not exists, returns 0
        if (!Exists())
        {
            return 0;
        }

        // get physical file length from OS
        var length = new FileInfo(_filename).Length;

        // if file length are not PAGE_SIZE module, maybe last save are not completed saved on disk
        // crop file removing last uncompleted page saved
        if (length % PAGE_SIZE != 0)
        {
            length = length - length % PAGE_SIZE;

            using (var fs = new FileStream(
                       _filename,
                       FileMode.Open,
                       FileAccess.Write,
                       FileShare.None,
                       PAGE_SIZE,
                       FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                fs.SetLength(length);
                fs.FlushToDisk();
            }
        }

        // if encrypted must remove salt first page (only if page contains data)
        return length > 0 ? length - (_password == null ? 0 : PAGE_SIZE) : 0;
    }

    /// <summary>
    /// Check if file exists (without open it)
    /// </summary>
    public bool Exists()
    {
        return File.Exists(_filename);
    }

    /// <summary>
    /// Delete file (must all stream be closed)
    /// </summary>
    public void Delete()
    {
        File.Delete(_filename);
    }

    /// <summary>
    /// Test if this file are locked by another process
    /// </summary>
    public bool IsLocked()
    {
        return Exists() && FileHelper.IsFileLocked(_filename);
    }

    /// <summary>
    /// Close all stream on end
    /// </summary>
    public bool CloseOnDispose => true;
}