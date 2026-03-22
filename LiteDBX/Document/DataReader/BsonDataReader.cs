using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteDbX.Engine;

namespace LiteDbX;

/// <summary>
/// Reads a void, single, or enumerable sequence of <see cref="BsonValue"/> results.
///
/// Phase 2 bridge: implements the async <see cref="IBsonDataReader"/> contract using a synchronous
/// <see cref="IEnumerator{T}"/> source. The <see cref="Read"/> method completes synchronously and
/// wraps the result in a completed <see cref="ValueTask{T}"/>.
///
/// Phase 4 (Query Pipeline) will replace this with a genuinely async pull source.
/// </summary>
public class BsonDataReader : IBsonDataReader
{
    private readonly IEnumerator<BsonValue> _source;
    private readonly EngineState _state;

    private bool _disposed;
    private bool _isFirst;

    /// <summary>Initialize with no value.</summary>
    internal BsonDataReader()
    {
        HasValues = false;
    }

    /// <summary>Initialize with a single value.</summary>
    internal BsonDataReader(BsonValue value, string collection = null)
    {
        Current = value;
        _isFirst = HasValues = true;
        Collection = collection;
    }

    /// <summary>Initialize with an IEnumerable data source.</summary>
    internal BsonDataReader(IEnumerable<BsonValue> values, string collection, EngineState state)
    {
        Collection = collection;
        _source = values.GetEnumerator();
        _state = state;

        try
        {
            _state.Validate();

            if (_source.MoveNext())
            {
                HasValues = _isFirst = true;
                Current = _state.ReadTransform(Collection, _source.Current);
            }
        }
        catch (Exception ex)
        {
            _state.Handle(ex);
            throw;
        }
    }

    public bool HasValues { get; }
    public BsonValue Current { get; private set; }
    public string Collection { get; }

    public BsonValue this[string field] => Current.AsDocument[field] ?? BsonValue.Null;

    // ── IBsonDataReader (async contract) ──────────────────────────────────────

    /// <summary>
    /// Advance the cursor to the next result.
    /// Phase 2 bridge: the underlying source is still synchronous; the result is wrapped in a
    /// completed <see cref="ValueTask{T}"/>. Phase 4 will provide a genuinely async implementation.
    /// </summary>
    public ValueTask<bool> Read(CancellationToken cancellationToken = default)
    {
        return new ValueTask<bool>(ReadSync());
    }

    /// <summary>
    /// Internal synchronous read for legacy paths (QueryExecutor, RebuildContent).
    /// Phase 4 will eliminate callers of this method.
    /// </summary>
    internal bool ReadSync()
    {
        if (!HasValues) return false;

        if (_isFirst)
        {
            _isFirst = false;
            return true;
        }

        if (_source != null)
        {
            _state.Validate();

            try
            {
                var read = _source.MoveNext();
                Current = _state.ReadTransform(Collection, _source.Current);
                return read;
            }
            catch (Exception ex)
            {
                _state.Handle(ex);
                throw;
            }
        }

        return false;
    }

    // ── IAsyncDisposable ──────────────────────────────────────────────────────

    public ValueTask DisposeAsync()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
        return default;
    }

    // ── Legacy sync dispose (kept for internal/bridge callers) ─────────────────

    /// <summary>
    /// Synchronous dispose — kept for internal callers that have not yet been converted to async.
    /// Phase 4 will convert all callers to <see cref="DisposeAsync"/>.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    ~BsonDataReader()
    {
        Dispose(false);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;
        _disposed = true;
        if (disposing) _source?.Dispose();
    }
}