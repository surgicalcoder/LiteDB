using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LiteDbX;

/// <summary>
/// Async extension methods for <see cref="IBsonDataReader"/>.
/// </summary>
public static class BsonDataReaderExtensions
{
    /// <summary>
    /// Streams all values from <paramref name="reader"/> as an <see cref="IAsyncEnumerable{BsonValue}"/>.
    /// The reader is disposed when the enumeration completes or is abandoned.
    /// </summary>
    public static async IAsyncEnumerable<BsonValue> ToAsyncEnumerable(
        this IBsonDataReader reader,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using (reader)
        {
            while (await reader.Read(cancellationToken).ConfigureAwait(false))
            {
                yield return reader.Current;
            }
        }
    }

    /// <summary>
    /// Materialise all values into a list.
    /// </summary>
    public static async ValueTask<List<BsonValue>> ToList(
        this IBsonDataReader reader,
        CancellationToken cancellationToken = default)
    {
        var list = new List<BsonValue>();
        await using (reader)
        {
            while (await reader.Read(cancellationToken).ConfigureAwait(false))
            {
                list.Add(reader.Current);
            }
        }
        return list;
    }

    /// <summary>
    /// Materialise all values into an array.
    /// </summary>
    public static async ValueTask<BsonValue[]> ToArray(
        this IBsonDataReader reader,
        CancellationToken cancellationToken = default)
    {
        return (await reader.ToList(cancellationToken).ConfigureAwait(false)).ToArray();
    }

    /// <summary>
    /// Return the first value, throwing if the result set is empty.
    /// </summary>
    public static async ValueTask<BsonValue> First(
        this IBsonDataReader reader,
        CancellationToken cancellationToken = default)
    {
        await using (reader)
        {
            if (await reader.Read(cancellationToken).ConfigureAwait(false))
            {
                return reader.Current;
            }
        }
        throw new System.InvalidOperationException("Sequence contains no elements.");
    }

    /// <summary>
    /// Return the first value, or <see cref="BsonValue.Null"/> if the result set is empty.
    /// </summary>
    public static async ValueTask<BsonValue> FirstOrDefault(
        this IBsonDataReader reader,
        CancellationToken cancellationToken = default)
    {
        await using (reader)
        {
            if (await reader.Read(cancellationToken).ConfigureAwait(false))
            {
                return reader.Current;
            }
        }
        return BsonValue.Null;
    }

    /// <summary>
    /// Return the single value, throwing if the result set is empty or contains more than one value.
    /// </summary>
    public static async ValueTask<BsonValue> Single(
        this IBsonDataReader reader,
        CancellationToken cancellationToken = default)
    {
        await using (reader)
        {
            if (!await reader.Read(cancellationToken).ConfigureAwait(false))
            {
                throw new System.InvalidOperationException("Sequence contains no elements.");
            }

            var value = reader.Current;

            if (await reader.Read(cancellationToken).ConfigureAwait(false))
            {
                throw new System.InvalidOperationException("Sequence contains more than one element.");
            }

            return value;
        }
    }

    /// <summary>
    /// Return the single value, or <see cref="BsonValue.Null"/> if the result set is empty.
    /// Throws if the result set contains more than one value.
    /// </summary>
    public static async ValueTask<BsonValue> SingleOrDefault(
        this IBsonDataReader reader,
        CancellationToken cancellationToken = default)
    {
        await using (reader)
        {
            if (!await reader.Read(cancellationToken).ConfigureAwait(false))
            {
                return BsonValue.Null;
            }

            var value = reader.Current;

            if (await reader.Read(cancellationToken).ConfigureAwait(false))
            {
                throw new System.InvalidOperationException("Sequence contains more than one element.");
            }

            return value;
        }
    }
}