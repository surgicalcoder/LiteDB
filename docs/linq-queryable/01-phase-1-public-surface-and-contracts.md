# Phase 1 — Public Surface and Contracts

## Phase Goal

Define the Phase 1 contract for LINQ / `IQueryable<T>` support in LiteDbX before any provider implementation work expands scope.

This phase freezes:

- how LINQ queries start
- how provider-backed queries execute
- which operators are in MVP
- which semantics are explicitly deferred or rejected
- which existing LiteDbX query components remain canonical

## Why This Phase Comes First

Without a fixed contract, later work will drift into:

- accidental replacement of the native `Query()` / `LiteQueryable<T>` API
- accidental sync-over-async behavior through `IQueryable<T>`
- overcommitment to unsupported LINQ semantics
- fragile provider designs that reuse mutable native query state incorrectly

## Existing Files To Study

- `LiteDbX/Client/Database/ILiteCollection.cs`
- `LiteDbX/Client/Database/ILiteQueryable.cs`
- `LiteDbX/Client/Database/LiteQueryable.cs`
- `LiteDbX/Client/Database/LiteRepository.cs`
- `docs/ASYNC_ONLY_REDESIGN_PLAN.md`
- `docs/async-redesign/phase-1-decisions.md`

## Final Phase 1 Decisions

### 1. Public entrypoint

#### Final decision
The primary LINQ entrypoint is a separate adapter on the collection surface:

- `ILiteCollection<T>.AsQueryable()`
- `ILiteCollection<T>.AsQueryable(ILiteTransaction transaction)`

#### Why this shape was chosen

- `ILiteCollection<T>` is already the natural query root for the native API.
- `Query()` already means “give me the native LiteDbX fluent builder”. Reusing that entrypoint for full `IQueryable<T>` semantics would blur responsibilities.
- `LiteQueryable<T>` already models the native builder and exposes async-only terminals such as `ToList(...)`, `First(...)`, and `Count(...)`. It should stay the native query surface rather than absorbing `IQueryable<T>` compatibility concerns.
- A separate `AsQueryable()` root keeps LINQ additive and makes the fallback to `Query()` obvious.

#### Repository stance
No repository-level LINQ entrypoint is required in Phase 1.

If repository convenience is added later, it should be a thin wrapper over `GetCollection<T>(...).AsQueryable()` and must not become the primary design anchor.

#### Transaction stance
The LINQ entrypoint should mirror the existing transaction-aware native builder shape by supporting an explicit transaction overload on the collection root.

#### Non-goal
`AsQueryable()` does **not** replace `Query()`. The native `Query()` API remains first-class for:

- direct `BsonExpression` authoring
- advanced query features not supported by the LINQ provider
- plan inspection / explain workflows
- explicit grouped/manual query construction

---

### 2. Sync vs async execution policy

#### Final decision
Provider-backed `IQueryable<T>` is a **synchronous composition surface** with **async-only execution**.

That means:

- supported: `Queryable` composition over expression trees
- supported: LiteDbX async terminal extensions/helpers for provider-backed queries
- unsupported: synchronous enumeration or synchronous LINQ materializers over provider-backed LiteDbX queries

#### Required failure mode
If a caller tries to execute a provider-backed query through synchronous `IQueryable` / `IEnumerable` paths such as:

- `foreach` / `GetEnumerator()`
- `System.Linq.Enumerable.ToList(...)`
- `System.Linq.Queryable.Count(...)`
- other sync materializers/operators that require provider-side execution

the provider must fail fast with a clear exception that explains:

- LiteDbX LINQ execution is async-only
- the caller should use LiteDbX async terminals such as `ToListAsync()` / `CountAsync()`
- the caller can fall back to `Query()` if they need the native builder

#### Naming policy reconciliation
The native LiteDbX async-only API keeps plain names (`ToList`, `First`, `Count`, etc.) in line with the broader async redesign direction.

The LINQ adapter is an interoperability exception: its execution surface should use `*Async` terminal extension names because plain LINQ terminal names are already strongly associated with synchronous `IQueryable` / `IEnumerable` execution in .NET.

This is an adapter-specific compatibility decision, not a reversal of the native API naming direction.

---

### 3. MVP operator matrix

The MVP scope is intentionally conservative and should map directly onto current native builder behavior.

| Area | Operator / capability | Phase 1 status | Notes |
|---|---|---|---|
| Query root | `AsQueryable()` | MVP | Primary LINQ entrypoint on `ILiteCollection<T>` |
| Query root | `AsQueryable(ILiteTransaction)` | MVP | Mirrors `Query(ILiteTransaction)` symmetry |
| Filter | `Where` | MVP | Must lower to the same semantics as `Query().Where(...)` |
| Projection | `Select` | MVP | Only for shapes already supported by mapper/query translation |
| Ordering | `OrderBy` | MVP | Reuse native ordering semantics |
| Ordering | `OrderByDescending` | MVP | Reuse native ordering semantics |
| Ordering | `ThenBy` | MVP | Reuse native ordering semantics |
| Ordering | `ThenByDescending` | MVP | Reuse native ordering semantics |
| Paging | `Skip` | MVP | Lowers to native offset semantics |
| Paging | `Take` | MVP | Lowers to native limit semantics |
| Async terminal | `ToListAsync` | MVP | Provider-specific async execution surface |
| Async terminal | `ToArrayAsync` | MVP | Provider-specific async execution surface |
| Async terminal | `FirstAsync` | MVP | Provider-specific async execution surface |
| Async terminal | `FirstOrDefaultAsync` | MVP | Provider-specific async execution surface |
| Async terminal | `SingleAsync` | MVP | Provider-specific async execution surface |
| Async terminal | `SingleOrDefaultAsync` | MVP | Provider-specific async execution surface |
| Async terminal | `AnyAsync` | MVP | Maps to native existence/count semantics |
| Async terminal | `CountAsync` | MVP | Maps to native count semantics |
| Async terminal | `LongCountAsync` | MVP | Maps to native long-count semantics |
| Include | Provider-specific include helpers | Deferred | Not a standard LINQ operator; may be added later as LiteDbX-specific extension(s) |
| Aggregates | `Min`, `Max`, `Sum`, `Average` | Deferred | Only after clear parity with engine-supported aggregate semantics |
| Grouping | grouped aggregate projections | Deferred | Only where they map cleanly to `Query.GroupBy` / `GroupByPipe` |
| Grouping | full `GroupBy` returning `IGrouping<TKey, TElement>` | Unsupported in initial design | Do not imply LINQ-to-Objects grouping parity |
| Joins | `Join`, `GroupJoin` | Unsupported in initial design | No engine-aligned MVP path |
| Flattening | `SelectMany` | Unsupported in initial design | Too broad for MVP and risks client-side semantics |
| Subqueries | nested queryable subqueries | Unsupported in initial design | Explicitly out of V1 scope |
| Set ops | `Distinct`, `Union`, `Intersect`, `Except`, `Concat` | Unsupported in initial design | Do not promise without engine-aligned lowering |
| Execution | sync LINQ materialization / sync enumeration | Unsupported by policy | Must fail clearly |

### Projection boundary for MVP
`Select` support in the MVP is limited to projection bodies that already map cleanly through existing LiteDbX expression translation.

Do **not** promise:

- arbitrary client-evaluated projection logic
- arbitrary nested subquery projection
- arbitrary post-materialization `Select` behavior inside provider translation

---

### 4. Architecture guardrails

The following rules are frozen by Phase 1 and must stay visible in later phases:

1. LINQ is an adapter layered on top of the existing LiteDbX query system.
2. `ILiteQueryable<T>` / `LiteQueryable<T>` remain the first-class native query API.
3. `Query` remains the canonical structured query representation.
4. `QueryOptimization`, `QueryPlan`, `QueryExecutor`, `QueryPipe`, and `GroupByPipe` remain the canonical execution path.
5. `BsonMapper.GetExpression(...)` and `LinqExpressionVisitor` remain leaf lambda translators, not the full `IQueryable` provider.
6. Provider translation must lower into native LiteDbX query state; it must not bypass the optimizer or execution pipeline.
7. Unsupported LINQ shapes must fail clearly and tell users when to fall back to `Query()`.
8. The provider must not rely on shared mutable `LiteQueryable<T>` / `Query` instances during composition. Later phases should use separate provider translation state and only lower to native query objects at the appropriate boundary.

## Validation Against Current LiteDbX Contracts

### Consistency with `ILiteCollection<T>`

- `ILiteCollection<T>.Query()` is already documented as a synchronous composition surface with deferred async execution.
- A sibling `AsQueryable()` entrypoint is therefore additive and conceptually consistent.
- The collection surface already has explicit transaction-aware query creation via `Query(ILiteTransaction)`, so a transaction-aware LINQ entrypoint is a natural symmetry point.

### Consistency with `ILiteQueryable<T>` / `LiteQueryable<T>`

- `ILiteQueryable<T>` composition methods are synchronous and terminal operations are async-only.
- `LiteQueryable<T>` mutates an in-memory `Query` instance and temporarily rewrites `Select` for some aggregates.
- Because of that mutable native-builder behavior, the LINQ provider should stay architecturally separate rather than making `LiteQueryable<T>` itself carry `IQueryable<T>` responsibilities.

### Consistency with the async-only redesign direction

- The async redesign keeps query builders synchronous and execution async-only.
- The Phase 1 LINQ contract matches that model exactly: sync composition, async execution, no hidden sync-over-async.
- The only nuance is naming: `*Async` extensions are allowed for the LINQ adapter because the unsuffixed names are already occupied by conventional synchronous LINQ expectations.

## Explicit Non-Goals

Phase 1 does **not**:

- implement provider/root types
- add operator translation code
- change the execution engine
- make `LiteQueryable<T>` become the LINQ provider
- promise complete `IQueryable<T>` parity with LINQ to Objects or EF Core
- promise synchronous provider execution

## Deliverables Completed By This Phase

- finalized public-surface decision: `ILiteCollection<T>.AsQueryable()` (+ explicit transaction overload)
- finalized sync/async policy: sync composition only, async provider execution only, fail-fast sync materialization
- finalized MVP operator matrix
- finalized architecture guardrails and non-goals

## Exit Criteria

This phase is complete when another implementer can answer these questions unambiguously:

1. How do users start a LINQ query?
2. Is sync execution supported?
3. Which operators and terminals are in the MVP?
4. What stays first-class in the native LiteDbX query API?

