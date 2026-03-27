using System;
using BenchmarkDotNet.Attributes;
using LiteDbX.Engine;

namespace LiteDbX.Benchmarks.Benchmarks.Serialization;

[BenchmarkCategory(Constants.Categories.SERIALIZATION)]
[MemoryDiagnoser]
public class BufferSerializationBenchmark
{
    private BufferSlice _contiguousRead;
    private BufferSlice[] _splitRead;
    private BufferSlice _contiguousWrite;
    private BufferSlice[] _splitWrite;

    [Params(128, 4096)]
    public int ValueCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var byteCount = ValueCount * sizeof(int);

        var contiguous = new byte[byteCount];
        using (var writer = new BufferWriter(contiguous))
        {
            for (var i = 0; i < ValueCount; i++)
            {
                writer.Write(i);
            }
        }

        _contiguousRead = new BufferSlice(contiguous, 0, contiguous.Length);

        var firstLength = Math.Max(1, byteCount / 2);
        var secondLength = byteCount - firstLength;

        if (secondLength == 0)
        {
            secondLength = 1;
            firstLength = Math.Max(1, byteCount - secondLength);
        }

        var segmentA = new BufferSlice(new byte[firstLength], 0, firstLength);
        var segmentB = new BufferSlice(new byte[secondLength], 0, secondLength);

        using (var writer = new BufferWriter(new[] { segmentA, segmentB }))
        {
            for (var i = 0; i < ValueCount; i++)
            {
                writer.Write(i);
            }
        }

        _splitRead = new[] { segmentA, segmentB };

        _contiguousWrite = new BufferSlice(new byte[byteCount], 0, byteCount);
        _splitWrite =
        [
            new BufferSlice(new byte[firstLength], 0, firstLength),
            new BufferSlice(new byte[secondLength], 0, secondLength)
        ];
    }

    [Benchmark]
    public int ReadInt32Contiguous()
    {
        var sum = 0;

        using (var reader = new BufferReader(_contiguousRead))
        {
            for (var i = 0; i < ValueCount; i++)
            {
                sum += reader.ReadInt32();
            }
        }

        return sum;
    }

    [Benchmark]
    public int ReadInt32Split()
    {
        var sum = 0;

        using (var reader = new BufferReader(_splitRead))
        {
            for (var i = 0; i < ValueCount; i++)
            {
                sum += reader.ReadInt32();
            }
        }

        return sum;
    }

    [Benchmark]
    public int WriteInt32Contiguous()
    {
        using (var writer = new BufferWriter(_contiguousWrite))
        {
            for (var i = 0; i < ValueCount; i++)
            {
                writer.Write(i);
            }

            return writer.Position;
        }
    }

    [Benchmark]
    public int WriteInt32Split()
    {
        using (var writer = new BufferWriter(_splitWrite))
        {
            for (var i = 0; i < ValueCount; i++)
            {
                writer.Write(i);
            }

            return writer.Position;
        }
    }
}

