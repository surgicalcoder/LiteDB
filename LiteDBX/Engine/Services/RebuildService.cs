using System.IO;
using static LiteDbX.Constants;

namespace LiteDbX.Engine;

/// <summary>
/// Rebuilds a LiteDB database by reading from the existing file and writing a fresh copy.
///
/// Phase 3 bridge: all engine method calls here use <c>.GetAwaiter().GetResult()</c> because
/// <see cref="Rebuild"/> runs synchronously on a dedicated non-async path. Phase 3 (Disk and
/// Streams) will convert this whole flow to async.
/// [ThreadSafe]
/// </summary>
internal class RebuildService
{
    private readonly int _fileVersion;
    private readonly EngineSettings _settings;

    public RebuildService(EngineSettings settings)
    {
        _settings = settings;

        // test for prior version
        var bufferV7 = ReadFirstBytes(false);

        if (FileReaderV7.IsVersion(bufferV7))
        {
            _fileVersion = 7;

            return;
        }

        // open, read first 16kb, and close data file
        var buffer = ReadFirstBytes();

        // test for valid reader to use
        _fileVersion = FileReaderV8.IsVersion(buffer) ? 8 : throw LiteException.InvalidDatabase();
    }

    public long Rebuild(RebuildOptions options)
    {
        var backupFilename = FileHelper.GetSuffixFile(_settings.Filename, "-backup");
        var backupLogFilename = FileHelper.GetSuffixFile(FileHelper.GetLogFile(_settings.Filename), "-backup");
        var tempFilename = FileHelper.GetSuffixFile(_settings.Filename);

        // open file reader
        using (var reader = _fileVersion == 7 ? new FileReaderV7(_settings) : (IFileReader)new FileReaderV8(_settings, options.Errors))
        {
            // open file reader and ready to import to new temp engine instance
            reader.Open();

            // open new engine to receive all data read from FileReader
            using (var engine = new LiteEngine(new EngineSettings
                   {
                       Filename = tempFilename,
                       Collation = options.Collation,
                       Password = options.Password
                   }))
            {
                // copy all database to new Log file with NO checkpoint during all rebuild
                // Phase 3 bridge: .GetAwaiter().GetResult() — Pragma is now async (ValueTask<bool>).
                engine.Pragma(Pragmas.CHECKPOINT, 0).GetAwaiter().GetResult();

                // rebuild all content from reader into new engine
                engine.RebuildContent(reader);

                // insert error report
                if (options.IncludeErrorReport && options.Errors.Count > 0)
                {
                    var report = options.GetErrorReport();

                    // Phase 3 bridge: .GetAwaiter().GetResult() — Insert is now async (ValueTask<int>).
                    engine.Insert("_rebuild_errors", report, BsonAutoId.Int32).GetAwaiter().GetResult();
                }

                // update pragmas — Phase 3 bridge: .GetAwaiter().GetResult() on each async Pragma call
                var pragmas = reader.GetPragmas();

                engine.Pragma(Pragmas.CHECKPOINT, pragmas[Pragmas.CHECKPOINT]).GetAwaiter().GetResult();
                engine.Pragma(Pragmas.TIMEOUT, pragmas[Pragmas.TIMEOUT]).GetAwaiter().GetResult();
                engine.Pragma(Pragmas.LIMIT_SIZE, pragmas[Pragmas.LIMIT_SIZE]).GetAwaiter().GetResult();
                engine.Pragma(Pragmas.UTC_DATE, pragmas[Pragmas.UTC_DATE]).GetAwaiter().GetResult();
                engine.Pragma(Pragmas.USER_VERSION, pragmas[Pragmas.USER_VERSION]).GetAwaiter().GetResult();

                // after rebuild, copy log bytes into data file
                // Phase 3 bridge: .GetAwaiter().GetResult() — Checkpoint is now async (ValueTask<int>).
                engine.Checkpoint().GetAwaiter().GetResult();
            }
        }

        // if log file exists, rename as backup file
        var logFile = FileHelper.GetLogFile(_settings.Filename);

        if (File.Exists(logFile))
        {
            File.Move(logFile, backupLogFilename);
        }

        // rename source filename to backup name
        FileHelper.Exec(5, () => { File.Move(_settings.Filename, backupFilename); });

        // rename temp file into filename
        File.Move(tempFilename, _settings.Filename);

        // get difference size
        return
            new FileInfo(backupFilename).Length -
            new FileInfo(_settings.Filename).Length;
    }

    /// <summary>
    /// Read first 16kb (2 PAGES) in bytes
    /// </summary>
    private byte[] ReadFirstBytes(bool useAesStream = true)
    {
        var buffer = new byte[PAGE_SIZE * 2];
        var factory = _settings.CreateDataFactory(useAesStream);

        using (var stream = factory.GetStream(false, true))
        {
            stream.Position = 0;
            stream.Read(buffer, 0, buffer.Length);
        }

        return buffer;
    }
}

