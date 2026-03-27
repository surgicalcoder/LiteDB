using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using LiteDbX.Engine;
using Xunit;

namespace LiteDbX.Tests.Issues;

public class Issue2523_ReadFull_Tests
{
    [Fact]
    public void ReadFull_Must_See_Log_Page_Written_In_Same_Tick()
    {
        using var logStream = new DelayedPublishLogStream();
        using var dataStream = new MemoryStream();

        var settings = new EngineSettings
        {
            DataStream = dataStream,
            LogStream = logStream
        };

        using var disk = new DiskService(settings, new EngineState(null, settings), new[] { 10 });

        var page = disk.NewPage();
        page.Fill(0xAC);

        disk.WriteLogDiskSync(new[] { page });

        var logPages = disk.ReadFull(FileOrigin.Log).ToList();

        logPages.Should().HaveCount(1);
        logPages[0].All(0xAC).Should().BeTrue();
    }

    [Fact]
    public async Task ReadFullAsync_Must_See_Log_Page_Written_In_Same_Tick()
    {
        await using var logStream = new DelayedPublishLogStream();
        await using var dataStream = new MemoryStream();

        var settings = new EngineSettings
        {
            DataStream = dataStream,
            LogStream = logStream
        };

        using var disk = new DiskService(settings, new EngineState(null, settings), new[] { 10 });

        var page = disk.NewPage();
        page.Fill(0xAC);

        await disk.WriteLogDisk(new[] { page });

        var logPages = new List<PageBuffer>();
        await foreach (var buffer in disk.ReadFullAsync(FileOrigin.Log))
        {
            logPages.Add(buffer);
        }

        logPages.Should().HaveCount(1);
        logPages[0].All(0xAC).Should().BeTrue();
    }

    /// <summary>
    /// Accepts writes immediately but only exposes them to readers after Flush/FlushAsync.
    /// This reproduces the visibility gap fixed by flushing the WAL stream after writes.
    /// </summary>
    private sealed class DelayedPublishLogStream : Stream
    {
        private readonly MemoryStream _committed = new();
        private readonly List<(long Position, byte[] Data)> _pending = new();

        private long _writerLength;
        private long _visibleLength;
        private long _position;

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => true;
        public override long Length => _writerLength;

        public override long Position
        {
            get => _position;
            set => _position = value;
        }

        public override void Flush()
        {
            foreach (var (position, data) in _pending)
            {
                _committed.Position = position;
                _committed.Write(data, 0, data.Length);
            }

            _pending.Clear();
            _committed.Flush();
            _visibleLength = _writerLength;
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            Flush();
            return Task.CompletedTask;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_position >= _visibleLength)
                return 0;

            var available = (int)Math.Min(count, _visibleLength - _position);
            _committed.Position = _position;
            var read = _committed.Read(buffer, offset, available);
            _position += read;
            return read;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_position >= _visibleLength)
                return 0;

            var available = (int)Math.Min(buffer.Length, _visibleLength - _position);
            _committed.Position = _position;
            var read = await _committed.ReadAsync(buffer[..available], cancellationToken).ConfigureAwait(false);
            _position += read;
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            _position = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => _position + offset,
                SeekOrigin.End => _writerLength + offset,
                _ => throw new ArgumentOutOfRangeException(nameof(origin))
            };

            if (_position < 0)
                throw new IOException("Negative position.");

            return _position;
        }

        public override void SetLength(long value)
        {
            if (value < 0)
                throw new IOException("Negative length.");

            _writerLength = value;

            if (_visibleLength > value)
                _visibleLength = value;

            if (_committed.Length < value)
                _committed.SetLength(value);

            if (_position > value)
                _position = value;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            ArgumentNullException.ThrowIfNull(buffer);

            if ((uint)offset > buffer.Length)
                throw new ArgumentOutOfRangeException(nameof(offset));

            if ((uint)count > buffer.Length - offset)
                throw new ArgumentOutOfRangeException(nameof(count));

            var copy = new byte[count];
            Buffer.BlockCopy(buffer, offset, copy, 0, count);
            _pending.Add((_position, copy));

            _position += count;
            if (_position > _writerLength)
                _writerLength = _position;
        }

        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            Write(buffer, offset, count);
            await Task.CompletedTask;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _committed.Dispose();

            base.Dispose(disposing);
        }

        public override ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}


