# SharedEngine Reentrant Shared-Mode Refactor

## Problem

`SharedEngine` currently serializes all operations with a single `SemaphoreSlim` that is acquired for the full lifetime of a streamed query enumeration. That means patterns like this deadlock in `ConnectionType.Shared`:

```csharp
await foreach (var doc in collection.FindAll())
{
    await collection.Insert(new BsonDocument());
    await collection.Delete(doc["_id"]);
    await collection.Query().Count();
}
```

The outer `FindAll()` holds the shared gate until enumeration completes. Nested operations on the same database instance try to acquire the same gate again, and `SemaphoreSlim` is not reentrant.

## Recommendation

Refactor `SharedEngine` from a one-shot gate/open/close wrapper into a lease-based shared-session model.

### Core ideas

1. **Keep process-wide serialization for unrelated callers**
   - External callers should still serialize through the existing per-instance gate.
   - Shared mode should remain single-owner per engine instance.

2. **Allow nested operations inside the same logical async flow**
   - Non-query operations can still reuse the active shared session inside the same call chain.
   - However, queries consumed via `await foreach` must not depend on `AsyncLocal` state set inside the producer iterator being visible in the consumer body.

3. **Pin the shared session for streamed queries**
   - Shared-mode queries should acquire a lease, materialize the result set, release the lease, and only then yield documents to the caller.
   - This ensures nested `Insert` / `Delete` / `Update` / query operations in the consumer's loop body do not deadlock on the shared gate.

4. **Route all operations through the same lease path**
   - `Query`, `Insert`, `Update`, `Delete`, schema operations, maintenance operations, and `Pragma` should all acquire a lease from the same internal helper.

## Recommended internal structure

Add a small private lease model inside `SharedEngine`:

- `LeaseContext`
  - Optional helper stored in `AsyncLocal`
  - Useful for same-call-chain reuse, but **not sufficient on its own** across `IAsyncEnumerable` consumer boundaries

- `SharedSession`
  - Represents the active root shared-mode session
  - Holds a reference count for active leases

- `Lease`
  - Returned by an internal `AcquireLeaseAsync(...)`
  - Exposes the open `LiteEngine`
  - On dispose, decrements the session ref-count and closes the engine / releases the gate when the last lease exits

## Lifecycle

### Root lease

The first operation in an async flow:

1. waits on `_gate`
2. opens `_engine`
3. creates a `SharedSession`
4. publishes the ambient `LeaseContext`

### Reentrant child lease

A nested operation in the same async flow:

1. sees the ambient context for the same `SharedEngine` in the same call chain
2. increments the shared session ref-count
3. reuses the existing `_engine`
4. does **not** wait on `_gate`

### Final release

When the last lease exits:

1. dispose the inner `LiteEngine`
2. clear the active session
3. release `_gate`

## Implementation adjustment

The original recommendation assumed the ambient lease context would seamlessly flow from the producer async iterator into the consumer's `await foreach` body. In practice, that assumption is not reliable enough for shared-mode query reentrancy.

So the implemented design uses this rule for shared-mode queries:

1. acquire shared lease
2. run the inner `LiteEngine.Query(...)`
3. materialize the results into memory
4. release the shared lease
5. yield buffered documents to the caller

This keeps nested loop-body operations seamless while preserving the current public API.

## Why this design

### Compared with buffering query results

Buffering the entire result set before releasing the gate would avoid the deadlock, but it would also:

- break true streaming semantics
- increase memory usage on large result sets
- silently change timing/locking behavior

### Compared with an actor/queue redesign

A background coordinator could also solve the deadlock, but it would be a much larger architecture change and would make streamed `IAsyncEnumerable` support much more complex.

### Benefits of the implemented lease + buffering model

- preserves public APIs
- keeps the fix localized mostly to `SharedEngine`
- supports nested `Insert`, `Delete`, `Update`, and nested query operations during enumeration
- keeps serialization for unrelated shared-mode callers

### Trade-off

- shared-mode `Query(...)` no longer yields rows directly from the live engine cursor; it materializes them first

## Edge cases to cover with tests

1. `Update` inside `FindAll()` in shared mode
2. `Insert` + `Delete` inside `FindAll()` in shared mode
3. nested `Query().Count()` inside `FindAll()` in shared mode
4. breaking early from `await foreach` should release the shared lease cleanly
5. exceptions/cancellation during enumeration should still release the shared lease

## Scope boundaries

This refactor is intentionally limited to **reentrant shared-mode leasing**.

Still deferred:

- explicit `BeginTransaction()` support in `SharedEngine`
- cross-process coordination beyond the current in-process shared gate
- guarantees for parallel fan-out work spawned from inside an active shared session

## Acceptance criteria

- Nested operations during `FindAll()` no longer deadlock in `ConnectionType.Shared`
- Independent callers still serialize through the shared gate
- Early-disposed enumerations release the shared session correctly
- Public consumer APIs remain unchanged

