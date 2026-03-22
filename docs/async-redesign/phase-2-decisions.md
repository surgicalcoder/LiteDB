# Phase 2 — Transaction and Locking Decisions

_This document records key design choices made during Phase 2 of the async-only LiteDB redesign._

---

## 1. Transaction ownership model — `AsyncLocal<T>` ambient context

**Decision: `AsyncLocal<LiteTransaction>` for explicit transaction propagation.**

`ThreadLocal<TransactionService>` is removed entirely. Thread identity (`Environment.CurrentManagedThreadId`)
is never used in the transaction path.

`LiteTransaction` stores the current explicit transaction in an `AsyncLocal<LiteTransaction>` field:

```csharp
private static readonly AsyncLocal<LiteTransaction> _currentAmbient = new AsyncLocal<LiteTransaction>();
```

`AsyncLocal<T>` flows the value through logical async execution contexts, so a transaction started before
an `await` is still visible when execution resumes on a different managed thread after the `await`.

**Lifetime contract:**
- Created by `LiteEngine.BeginTransaction()` (writes the ambient).
- Auto-operations (`AutoTransactionAsync`) check `LiteTransaction.CurrentAmbient` and reuse it without
  creating a new transaction gate slot.
- Disposing the `LiteTransaction` calls `Rollback` if needed, releases the transaction gate, and clears
  the ambient to `null`.
- Only one explicit transaction per async execution context is allowed at a time.

**Risk acknowledged (Risks and Traps §1):** `AsyncLocal` propagates the value to child contexts.
If new `Task` instances are spawned inside a transaction scope without awaiting them, they will
inherit the ambient and could incorrectly participate in the parent transaction. Callers must not
fire-and-forget tasks inside a `LiteTransaction` scope.

---

## 2. Lock service — `AsyncReaderWriterLock` replaces `ReaderWriterLockSlim`

`LockService` has three lock concepts:

| Concept | Old primitive | New primitive |
|---|---|---|
| Transaction gate (concurrent readers / exclusive writer) | `ReaderWriterLockSlim` | `AsyncReaderWriterLock` (custom, see below) |
| Per-collection write lock | `Monitor.TryEnter` / `lock` | `SemaphoreSlim(1,1)` per collection |
| Exclusive (checkpoint / rebuild) | `ReaderWriterLockSlim` write lock | `AsyncReaderWriterLock` write slot |

**`AsyncReaderWriterLock` design:**

Built on two `SemaphoreSlim` primitives:
- `_readerGate` (1,1) — protects the `_readerCount` increment/decrement; held only for the
  duration of the counter update (no I/O, no await inside).
- `_writeLock` (1,1) — held by the first reader (and released by the last), or exclusively by
  a writer.

**Fairness note:** writers can be starved by a continuous stream of new readers. This is accepted
for the LiteDB use case because exclusive operations (checkpoint, rebuild) are rare and can tolerate
brief starvation; the checkpoint pragma threshold provides a natural back-pressure. If writer
starvation becomes an issue in practice, Phase 3/4 can introduce a writer-priority flag.

**Exit is synchronous:** `ExitRead()`, `ExitLock()`, `ExitExclusive()` are void and safe to call
from `finally` blocks, synchronous dispose paths, and `IAsyncDisposable.DisposeAsync` continuations.
The brief `_readerGate.Wait()` inside `ExitRead()` protects only an integer decrement — no I/O
or async waits occur inside that critical section.

---

## 3. Engine command flow — `AutoTransactionAsync` replaces `AutoTransaction`

All write and read engine commands (`Insert`, `Update`, `Delete`, `Upsert`, `EnsureIndex`,
`DropIndex`, `DropCollection`, `RenameCollection`, `Pragma(write)`) use `AutoTransactionAsync`:

```csharp
private async ValueTask<T> AutoTransactionAsync<T>(
    Func<TransactionService, CancellationToken, ValueTask<T>> fn,
    CancellationToken ct = default)
```

Rules:
- If an explicit `LiteTransaction` is ambient, it is reused (no new gate slot, no commit on exit).
- Otherwise a new auto-transaction is created, used, committed, and released atomically within
  the method.
- On error: if `_state.Handle(ex)` returns `true`, the transaction is rolled back and released.

---

## 4. Query cursor lifetime — `IAsyncEnumerable<BsonDocument>` with `try/finally` disposal

`ILiteEngine.Query` returns `IAsyncEnumerable<BsonDocument>`. The implementation separates eager
argument validation (public outer method) from the async iterator (`QueryCore`):

```csharp
public IAsyncEnumerable<BsonDocument> Query(string collection, Query query, CancellationToken ct)
{
    // eager validation — throws immediately on bad args
    ...
    return QueryCore(collection, query, source, ct);
}

private async IAsyncEnumerable<BsonDocument> QueryCore(..., [EnumeratorCancellation] CancellationToken ct)
{
    var reader = exec.ExecuteQuery(); // Phase 4 bridge: acquires transaction gate via sync entry
    try
    {
        while (await reader.Read(ct).ConfigureAwait(false))
            yield return reader.Current.AsDocument;
    }
    finally
    {
        await reader.DisposeAsync().ConfigureAwait(false); // releases cursor + transaction gate
    }
}
```

**Cursor lifetime guarantee:** `DisposeAsync` on the `BsonDataReader` triggers the `OnDispose`
callbacks registered by `QueryExecutor`, which remove the cursor from `transaction.OpenCursors`
and call `_monitor.ReleaseTransaction`. This happens whether the caller fully consumes the
sequence, breaks early, or cancels via the `CancellationToken`.

**Phase 4 deferred:** The inner `QueryExecutor.ExecuteQuery()` and `BsonDataReader` still use a
synchronous enumerator under the hood. Phase 4 (Query Pipeline) will replace this with a truly
async pull source.

---

## 5. `TransactionMonitor` — explicit tracking, no thread slots

`TransactionMonitor` tracks all open `TransactionService` instances in a `Dictionary<uint,
TransactionService>` protected by `object _lock` (CPU-bound dictionary access only, never held
across awaits).

**Key entry points:**

| Method | Used by |
|---|---|
| `GetOrCreateTransactionAsync` | primary async path (all Phase 2+ callers) |
| `CreateExplicitTransactionAsync` | `BeginTransaction` only |
| `GetOrCreateTransactionSync` | Phase 4 bridge for `QueryExecutor` and `RebuildContent` |
| `ReleaseTransaction` | all paths on commit/rollback/dispose |

`GetAmbientTransaction()` returns `LiteTransaction.CurrentAmbient?.Service` and is used by system
collections that need to attach to the running transaction.

---

## 6. `TransactionService` — thread-affinity removed

`TransactionID` is the sole identity for a transaction. The former `ThreadID` property is deleted.

`CreateSnapshotAsync` is the Phase 2 primary path for snapshot acquisition. `CreateSnapshot`
(sync) is retained as a Phase 3 bridge for `QueryExecutor` (called via `GetOrCreateTransactionSync`).

`Commit()` and `Rollback()` remain synchronous internally. Phase 3 (Disk and Streams) will make
the WAL write and page discard paths truly async.

The GC finalizer that called `_monitor.ReleaseTransaction` was removed. Leaking a `LiteTransaction`
without calling `DisposeAsync` will not automatically release the transaction gate. This is
intentional — the finalizer behaviour was inherently thread-unsafe in the async model.

---

## 7. `ILiteEngine` interface — fully aligned

All `LiteEngine` partial-class methods now match `ILiteEngine`:

| Method | Return type |
|---|---|
| `Checkpoint` | `ValueTask<int>` |
| `Rebuild` | `ValueTask<long>` |
| `BeginTransaction` | `ValueTask<ILiteTransaction>` |
| `Query` | `IAsyncEnumerable<BsonDocument>` |
| `Pragma(string)` | `ValueTask<BsonValue>` |
| `Pragma(string, BsonValue)` | `ValueTask<bool>` |
| All write ops (`Insert`, `Update`, …) | `ValueTask<int>` / `ValueTask<bool>` |
| `DisposeAsync` | `ValueTask` |

`LiteEngine` implements both `ILiteEngine` (async) and `IDisposable` (sync bridge for internal
callers and `using` statements that predate the async redesign).

---

## 8. `RebuildService` — Phase 3 bridge

`RebuildService.Rebuild()` runs on a dedicated synchronous path (called from
`LiteEngine.Rebuild(RebuildOptions, CancellationToken)` which immediately wraps the result in a
completed `ValueTask<long>`).

Inside `RebuildService`, async engine calls use `.GetAwaiter().GetResult()`:

```csharp
engine.Pragma(Pragmas.CHECKPOINT, 0).GetAwaiter().GetResult();
engine.Insert("_rebuild_errors", report, BsonAutoId.Int32).GetAwaiter().GetResult();
engine.Checkpoint().GetAwaiter().GetResult();
```

This is safe because:
- `RebuildService` is always called off the async hot path (no `SynchronizationContext` is
  captured; it runs in `LiteEngine.Rebuild` which is itself a completed-ValueTask wrapper).
- Phase 3 (Disk and Streams) will convert `RebuildService.Rebuild` to a proper async method.

---

## 9. `RebuildContent` — per-collection transactions

The original `RebuildContent` opened one transaction for all collections using the thread-local
`GetTransaction()` API. The new implementation uses one transaction per collection for document
insertion, then separate `EnsureIndex` auto-transactions for index creation.

This avoids nesting a second transaction acquisition inside an open outer transaction (which was
only safe in the old thread-local model because `AutoTransaction` could detect the existing
thread-local transaction and reuse it).

Behavioural difference: each collection is now committed separately rather than all-or-nothing.
A crash mid-rebuild would leave some collections fully inserted and others not. This is acceptable
because:
1. The rebuild always works on a temporary file (renamed to the real file only on success).
2. Per-collection commits reduce peak memory usage on large databases.

---

## 10. `WalIndexService._indexLock` — deferred to Phase 3

`WalIndexService` retains a `ReaderWriterLockSlim _indexLock` to protect the WAL dictionary
(`_index`) and `_currentReadVersion`. This lock is:
- Held only for pure CPU operations (dictionary reads and writes).
- Never held across any `await` or disk I/O.

It is **not** in violation of Phase 2's constraint against blocking primitives in async
operational paths because it is never held when the calling code suspends. It is a fine-grained
data-structure guard, not a coordination primitive.

**Phase 3 action:** When Phase 3 makes WAL writes async, the write path must ensure the lock is
not acquired before an `await` that could span disk I/O. Consider replacing it with a lightweight
async-compatible guard at that point.

---

## 11. `SharedEngine` — deferred to Phase 6

`SharedEngine` implements `ILiteEngine` but retains its old synchronous mutex-based pattern. Its
methods do not yet match the `ILiteEngine` async signatures introduced in Phase 1/2.

**Phase 6 action:** `SharedEngine` must be fully redesigned. Its current `Mutex` + per-call
`OpenDatabase/CloseDatabase` pattern is incompatible with the async transaction model. Design
options include:
- An async-aware named semaphore guard wrapping the full `LiteEngine` instance.
- A dedicated `SharedLiteEngine` that holds the lock for the duration of an async transaction
  scope rather than per-call.

---

## Deferred items summary

| Item | Deferred to |
|---|---|
| Truly async `Commit()` / `Rollback()` (WAL write) | Phase 3 |
| Truly async `Checkpoint()` | Phase 3 |
| Truly async `Rebuild()` / `RebuildService` | Phase 3 |
| `WalIndexService._indexLock` replacement | Phase 3 |
| `QueryExecutor` async pipeline | Phase 4 |
| `BsonDataReader` async source | Phase 4 |
| `SharedEngine` redesign | Phase 6 |

