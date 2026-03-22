using System;
using System.IO;

namespace LiteDbX.Tests;

public class TempFile : IDisposable
{
    public TempFile()
    {
        var path = Path.GetTempPath();
        var name = "litedb-" + Guid.NewGuid().ToString("d").Substring(0, 5) + ".db";

        Filename = Path.Combine(path, name);
    }

    public TempFile(string original)
    {
        var rnd = Guid.NewGuid().ToString("d").Substring(0, 5);
        var path = Path.GetTempPath();
        var name = $"litedb-{rnd}.db";
        var filename = Path.Combine(path, name);

        File.Copy(original, filename, true);

        Filename = filename;
    }

    public string Filename { get; }

    public long Size => new FileInfo(Filename).Length;

    public string ReadAsText()
    {
        return File.ReadAllText(Filename);
    }

    public override string ToString()
    {
        return Filename;
    }

    #region Dispose

    public static implicit operator string(TempFile value)
    {
        return value.Filename;
    }

    private bool _disposed;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    ~TempFile()
    {
        Dispose(false);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            // free other managed objects that implement
            // IDisposable only
        }

        // check file integrity

        File.Delete(Filename);

        _disposed = true;
    }

    #endregion
}