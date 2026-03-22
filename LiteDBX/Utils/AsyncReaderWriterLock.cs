using System;
using System.Threading;
using System.Threading.Tasks;

namespace LiteDbX;

/// <summary>
/// Async-safe readers/writer lock built on <see cref="SemaphoreSlim"/>.
///
/// Multiple readers can hold the lock concurrently.
/// A writer waits for all active readers to release before acquiring exclusive access.
///
/// Fairness note: writers can be starved by a continuous stream of new readers.
/// This is acceptable for LiteDB because exclusive locks (checkpoint/rebuild) are rare operations.
///
/// Phase 2 — replaces <c>ReaderWriterLockSlim</c> in <see cref="LiteDbX.Engine.LockService"/>.
/// </summary>
internal sealed class AsyncReaderWriterLock : IDisposable
{
    // Protects the reader counter; held only for the duration of the counter increment/decrement.
    private readonly SemaphoreSlim _readerGate = new SemaphoreSlim(1, 1);

    // Held by the first reader (and released by the last), or held exclusively by a writer.
    private readonly SemaphoreSlim _writeLock = new SemaphoreSlim(1, 1);

    private int _readerCount;

    // ── Read lock ─────────────────────────────────────────────────────────────

    /// <summary>Asynchronously enter read mode. Allows concurrent readers.</summary>
    public async ValueTask EnterReadAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        if (!await _readerGate.WaitAsync(timeout, ct).ConfigureAwait(false))
            throw new TimeoutException();

        try
        {
            _readerCount++;
            if (_readerCount == 1)
            {
                // First reader: block any incoming writers.
                if (!await _writeLock.WaitAsync(timeout, ct).ConfigureAwait(false))
                {
                    _readerCount--;
                    throw new TimeoutException();
                }
            }
        }
        finally
        {
            _readerGate.Release();
        }
    }

    /// <summary>
    /// Release the read lock. Can be called synchronously; <see cref="SemaphoreSlim.Release"/> is always safe.
    /// Note: the brief <c>_readerGate.Wait()</c> here protects only an integer decrement — no I/O or awaits occur inside.
    /// Phase 3 will eliminate this by ensuring all callers are async-disposable.
    /// </summary>
    public void ExitRead()
    {
        _readerGate.Wait(); // brief, non-I/O critical section
        try
        {
            _readerCount--;
            if (_readerCount == 0)
                _writeLock.Release();
        }
        finally
        {
            _readerGate.Release();
        }
    }

    // ── Write lock ────────────────────────────────────────────────────────────

    /// <summary>Asynchronously enter exclusive write mode. Waits for all readers to exit first.</summary>
    public async ValueTask EnterWriteAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        if (!await _writeLock.WaitAsync(timeout, ct).ConfigureAwait(false))
            throw new TimeoutException();
    }

    /// <summary>
    /// Try to enter write mode immediately without waiting.
    /// Returns <c>false</c> if any readers or another writer holds the lock.
    /// </summary>
    public bool TryEnterWrite()
    {
        return _writeLock.Wait(0);
    }

    /// <summary>Release the exclusive write lock.</summary>
    public void ExitWrite()
    {
        _writeLock.Release();
    }

    // ── Sync bridge ──────────────────────────────────────────────────────────

    /// <summary>
    /// Phase 3 bridge: synchronously enter read mode. Blocks the calling thread briefly.
    /// Used by <c>WalIndexService.GetPageIndex</c> which is still called from sync paths.
    /// Replace callers with <see cref="EnterReadAsync"/> when those paths are converted.
    /// </summary>
    public void EnterReadSync(TimeSpan timeout)
    {
        if (!_readerGate.Wait(timeout))
            throw new TimeoutException();
        try
        {
            _readerCount++;
            if (_readerCount == 1)
            {
                if (!_writeLock.Wait(timeout))
                {
                    _readerCount--;
                    throw new TimeoutException();
                }
            }
        }
        finally
        {
            _readerGate.Release();
        }
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public void Dispose()
    {
        _readerGate.Dispose();
        _writeLock.Dispose();
    }
}

