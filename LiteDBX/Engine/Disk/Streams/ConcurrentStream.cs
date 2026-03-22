using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace LiteDbX.Engine;

/// <summary>
/// Thread/async-safe stream wrapper that serialises access to a shared underlying stream
/// using a <see cref="SemaphoreSlim"/>.
///
/// Multiple <see cref="ConcurrentStream"/> instances wrapping the same underlying stream must
/// share the same <paramref name="sharedGate"/> so that all callers are mutually exclusive.
/// The gate is provided by <see cref="StreamFactory"/> which owns the underlying stream.
///
/// Phase 3: replaced <c>lock(_stream)</c> with <see cref="SemaphoreSlim"/> so that
/// <see cref="ReadAsync"/> and <see cref="WriteAsync"/> are truly non-blocking.
/// </summary>
internal class ConcurrentStream : Stream
{
    private readonly bool _canWrite;
    private readonly SemaphoreSlim _gate;
    private readonly bool _ownsGate;
    private readonly Stream _stream;

    private long _position;

    /// <param name="sharedGate">
    /// Optional semaphore shared with other <see cref="ConcurrentStream"/> instances that wrap the
    /// same underlying <paramref name="stream"/>.  When <c>null</c> a private gate is created
    /// (suitable when this is the only wrapper, e.g. in unit tests).
    /// </param>
    public ConcurrentStream(Stream stream, bool canWrite, SemaphoreSlim sharedGate = null)
    {
        _stream = stream;
        _canWrite = canWrite;
        if (sharedGate != null)
        {
            _gate = sharedGate;
            _ownsGate = false;
        }
        else
        {
            _gate = new SemaphoreSlim(1, 1);
            _ownsGate = true;
        }
    }

    public override bool CanRead => _stream.CanRead;
    public override bool CanSeek => _stream.CanSeek;
    public override bool CanWrite => _canWrite;
    public override long Length => _stream.Length;

    public override long Position
    {
        get => _position;
        set => _position = value;
    }

    public override void Flush() => _stream.Flush();

    public override Task FlushAsync(CancellationToken cancellationToken) =>
        _stream.FlushAsync(cancellationToken);

    public override void SetLength(long value) => _stream.SetLength(value);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _stream.Dispose();
            if (_ownsGate) _gate.Dispose();
        }
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        var position =
            origin == SeekOrigin.Begin ? offset :
            origin == SeekOrigin.Current ? _position + offset :
            _position - offset;

        _position = position;
        return _position;
    }

    // ── Sync read / write (kept for startup and legacy sync-bridge callers) ──

    public override int Read(byte[] buffer, int offset, int count)
    {
        _gate.Wait();
        try
        {
            _stream.Position = _position;
            var read = _stream.Read(buffer, offset, count);
            _position = _stream.Position;
            return read;
        }
        finally
        {
            _gate.Release();
        }
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        if (!_canWrite)
            throw new NotSupportedException("Current stream is readonly");

        _gate.Wait();
        try
        {
            _stream.Position = _position;
            _stream.Write(buffer, offset, count);
            _position = _stream.Position;
        }
        finally
        {
            _gate.Release();
        }
    }

    // ── Truly async read / write ─────────────────────────────────────────────

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _stream.Position = _position;
            var read = await _stream.ReadAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
            _position = _stream.Position;
            return read;
        }
        finally
        {
            _gate.Release();
        }
    }

    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        if (!_canWrite)
            throw new NotSupportedException("Current stream is readonly");

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _stream.Position = _position;
            await _stream.WriteAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
            _position = _stream.Position;
        }
        finally
        {
            _gate.Release();
        }
    }
}