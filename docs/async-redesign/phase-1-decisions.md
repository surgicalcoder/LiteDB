# Phase 1 — Contract Decisions

_This document summarises key design choices made during Phase 1 of the async-only LiteDB redesign._

---

## 1. Return type conventions

| Scenario | Chosen type | Rationale |
|---|---|---|
| Single-result operations (find by id, insert, update, delete) | `ValueTask<T>` | Likely to complete without allocation in the fast path; suits tight CRUD loops |
| Aggregates (count, exists, min, max) | `ValueTask<int>` / `ValueTask<bool>` / `ValueTask<BsonValue>` | Same reasoning |
| Streaming results (find all, queries) | `IAsyncEnumerable<T>` | Natural async streaming; composable with LINQ via `System.Linq.Async` |
| Maintenance operations that return nothing meaningful | `ValueTask` | Void-equivalent, cheap allocation |
| Rebuild (returns final file size) | `ValueTask<long>` | Single scalar result |
| SQL execution | `ValueTask<IBsonDataReader>` | See section 3 |

---

## 2. Transaction model

**Decision: explicit `ILiteTransaction` scope objects.**

The former `BeginTrans()` / `Commit()` / `Rollback()` ambient per-thread model is removed from all
public interfaces. It was incompatible with `await`-based continuations (thread-affinity).

The new model:

```csharp
await using var tx = await db.BeginTransaction();
// ... operations within the transaction scope ...
await tx.Commit();
// DisposeAsync without prior Commit triggers implicit Rollback
```

`ILiteTransaction` is defined in `LiteDbX.Engine` and is referenced by both `ILiteEngine` and
`ILiteDatabase`.

**Deferred (Phase 2):** The mechanism by which individual engine operations are associated with
a specific transaction scope is not defined in Phase 1. Phase 2 (Transactions and Locking) must
decide between:
- `AsyncLocal<ILiteTransaction>` ambient context (simpler API, harder to reason about)
- Explicit transaction parameter on every write operation (more verbose, unambiguous)

---

## 3. `IBsonDataReader` — retained as async cursor (Option B)

`IBsonDataReader` is kept rather than replaced with `IAsyncEnumerable<BsonDocument>` because:

1. The `Collection` string property gives SQL callers the collection context of the result set.
2. Indexed field access (`reader["field"]`) is frequently useful in low-level and shell consumers.
3. It maps cleanly to the cursor pattern used in most database client libraries.

The interface is **fully redesigned** to be async-only:
- `Read()` → `ValueTask<bool> Read(CancellationToken = default)`
- `IDisposable` → `IAsyncDisposable`

`BsonDataReaderExtensions` is updated with async counterparts for `ToList`, `ToArray`, `First`,
`FirstOrDefault`, `Single`, `SingleOrDefault`, and a new `ToAsyncEnumerable` method.

**Deferred (Phase 4):** The concrete `BsonDataReader` implementation still uses synchronous
`IEnumerator<BsonValue>` internally. Phase 4 (Query Pipeline) must replace this with a genuinely
async pull source.

---

## 4. `ILiteDatabase.Execute` return type

`Execute(...)` returns `ValueTask<IBsonDataReader>` (not `IAsyncEnumerable<BsonDocument>`).

Rationale: SQL execution in LiteDB can involve multiple statements, and each statement result may
carry a different collection context. The two-phase async model (`await` to get the reader, then
`await reader.Read()`) mirrors `DbCommand.ExecuteReaderAsync()` and is familiar to ADO.NET users.

Callers that only want streaming can use the `ToAsyncEnumerable()` extension method.

---

## 5. `ILiteStorage<TFileId>` and `ILiteFileHandle<TFileId>`

**Decision: replace `LiteFileStream<TFileId> : Stream` in the public contract.**

`System.IO.Stream` exposes synchronous `Read`, `Write`, `Flush`, and `Seek` by design.
Continuing to expose it in a zero-sync-surface API would be self-defeating.

`ILiteFileHandle<TFileId>` is a new async-only interface with:
- `ValueTask<int> Read(Memory<byte> buffer, CancellationToken = default)`
- `ValueTask Write(ReadOnlyMemory<byte> buffer, CancellationToken = default)`
- `ValueTask Flush(CancellationToken = default)`
- `ValueTask Seek(long position, CancellationToken = default)`
- `IAsyncDisposable`

`Upload(...)` and `Download(...)` on `ILiteStorage<TFileId>` still accept `System.IO.Stream` as
an _input parameter_ for callers who have existing streams from the file system or network. This
is acceptable — `Stream` appears only as a caller-owned value flowing into the API, not as
something LiteDB constructs and returns.

`LiteFileStream<TFileId>` is **not deleted** in this phase. It will be retired or repurposed as
an internal implementation detail in Phase 5 (File Storage).

**Deferred (Phase 5):** Concrete `ILiteFileHandle<TFileId>` implementation and async chunk-based
read/write internals.

---

## 6. Query builder methods stay synchronous

Methods on `ILiteQueryable<T>` and `ILiteCollection<T>` that only compose a query plan — `Where`,
`OrderBy`, `Include`, `GroupBy`, `Having`, `Select`, `Limit`, `Skip`, `Offset`, `ForUpdate`,
`Query()` — remain synchronous.

Rationale (from design plan): "query builders remain cheap and synchronous to compose."
No I/O happens until a terminal operation (`ToList`, `First`, `Count`, `ToEnumerable`, etc.) is
awaited.

---

## 7. `GetCollection` / `GetStorage` stay synchronous

These are pure factory methods. They return a handle object; no I/O occurs when calling them.
Keeping them synchronous avoids unnecessary `await` noise at call sites.

---

## 8. Lifecycle — `IAsyncDisposable` on all long-lived resources

All interfaces that hold long-lived resources now implement `IAsyncDisposable`:

- `ILiteDatabase` (owns engine, WAL, file handles)
- `ILiteEngine` (owns disk, streams, locks)
- `ILiteRepository` (owns `ILiteDatabase`)
- `ILiteTransaction` (owns a write lock / snapshot)
- `ILiteFileHandle<TFileId>` (owns an open file handle or write buffer)

`IBsonDataReader` also implements `IAsyncDisposable` (cursor may hold an engine lock or lazy
enumerator that must be cleaned up).

---

## 9. `CancellationToken` on all async operations

A `CancellationToken cancellationToken = default` parameter is included on every async method.
This follows .NET async best practices and makes the API cooperative cancellation–friendly from
day one.

---

## 10. `netstandard2.0` compatibility

`IAsyncEnumerable<T>` and `IAsyncDisposable` are not part of `netstandard2.0` natively.
`Microsoft.Bcl.AsyncInterfaces` (version 8.0.0) is added as a package dependency.
This package provides those types for `netstandard2.0`; they are no-ops on `netstandard2.1` and
`net10.0` which provide them natively.

---

## Deferred items summary

| Item | Deferred to |
|---|---|
| Transaction–operation association mechanism | Phase 2 |
| Blocking lock primitives in engine (`ReaderWriterLockSlim`, `lock`, `Monitor`) | Phase 2 |
| Async disk I/O (WAL, page reads/writes) | Phase 3 |
| `BsonDataReader` concrete async implementation | Phase 4 |
| `LiteQueryable` / `LiteCollection` concrete async implementation | Phase 4 |
| `ILiteFileHandle<TFileId>` concrete implementation | Phase 5 |
| `LiteFileStream<TFileId>` retirement | Phase 5 |
| `SharedEngine` redesign | Phase 6 |
| Test / consumer migration | Phase 7 |

