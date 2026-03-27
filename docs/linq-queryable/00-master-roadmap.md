# LiteDbX LINQ / IQueryable Master Roadmap

## Goal

Add a LINQ / `IQueryable<T>` layer **on top of** the current LiteDbX query system.

The existing query engine stays in place:

- `ILiteQueryable<T>` / `LiteQueryable<T>` remain the native fluent API
- `Query` remains the structured query representation
- `QueryOptimization`, `QueryPlan`, `QueryExecutor`, `QueryPipe`, and `GroupByPipe` remain the execution core

The LINQ provider should be an adapter that translates `Queryable` method chains into the current query model.

---

## Architecture Summary

### Existing pieces that should be reused

#### Public/native query surface
- `LiteDbX/Client/Database/ILiteCollection.cs`
- `LiteDbX/Client/Database/ILiteQueryable.cs`
- `LiteDbX/Client/Database/LiteQueryable.cs`
- `LiteDbX/Client/Database/LiteRepository.cs`

#### Lambda translation layer
- `LiteDbX/Client/Mapper/BsonMapper.cs`
- `LiteDbX/Client/Mapper/Linq/LinqExpressionVisitor.cs`

#### Structured query model and execution
- `LiteDbX/Client/Structures/Query.cs`
- `LiteDbX/Engine/Query/QueryOptimization.cs`
- `LiteDbX/Engine/Query/Structures/QueryPlan.cs`
- `LiteDbX/Engine/Query/QueryExecutor.cs`
- `LiteDbX/Engine/Query/Pipeline/QueryPipe.cs`
- `LiteDbX/Engine/Query/Pipeline/GroupByPipe.cs`

### Target translation flow

`collection.AsQueryable()` root
→ `IQueryable<T>` expression tree
→ queryable-provider translation layer
→ normalized translation state / query specification
→ `Query` / native `LiteQueryable<T>`
→ existing optimizer and engine pipeline
→ async terminal execution

### Explicit non-goals

- replacing `LiteQueryable<T>`
- replacing `Query`
- bypassing the optimizer or pipes
- promising complete LINQ parity with EF Core or LINQ to Objects
- forcing synchronous materialization semantics into an async-first system
- making provider-backed sync enumeration/materialization silently work via sync-over-async

---

## Recommended Milestones

### Milestone 1 — Public surface and contract decisions
Lock the public entrypoint, sync/async contract, supported operator matrix, and non-goals.

### Milestone 2 — Queryable provider and state model
Design and implement the provider shell and internal translation state without broad operator support yet.

### Milestone 3 — MVP operator translation
Translate the highest-value LINQ operators into the current query model.

### Milestone 4 — Async terminals and builder interop
Make the queryable layer executable using async terminals and keep escape hatches into the native builder.

### Milestone 5 — Grouping, aggregates, and advanced translation
Expand into grouped queries and carefully-scoped advanced LINQ features.

### Milestone 6 — Tests, documentation, and rollout
Add parity tests, user-facing docs, diagnostics, and a staged rollout story.

---

## Milestone Dependencies

| Milestone | Depends On | Why |
|---|---|---|
| M1 | None | Must lock scope before building abstractions |
| M2 | M1 | Provider design depends on public contract |
| M3 | M2 | Operator translation depends on provider/state model |
| M4 | M3 | Async execution should be built on working translation |
| M5 | M3, M4 | Grouping/advanced operators depend on stable MVP flow |
| M6 | M3-M5 | Final testing and docs depend on implemented scope |

---

## Recommended MVP Scope

Ship these first:

- queryable root via `ILiteCollection<T>.AsQueryable()`
- transaction-aware root via `ILiteCollection<T>.AsQueryable(ILiteTransaction)`
- `Where`
- `Select`
- `OrderBy`
- `OrderByDescending`
- `ThenBy`
- `ThenByDescending`
- `Skip`
- `Take`
- async terminals such as:
  - `ToListAsync`
  - `ToArrayAsync`
  - `FirstAsync`
  - `FirstOrDefaultAsync`
  - `SingleAsync`
  - `SingleOrDefaultAsync`
  - `AnyAsync`
  - `CountAsync`
  - `LongCountAsync`

Support async execution only for provider-backed `IQueryable<T>` queries. Sync enumeration and sync LINQ materializers should fail clearly with guidance to use the LiteDbX async terminals or fall back to `Query()`.

Do **not** make V1 depend on:

- `Join`
- `GroupJoin`
- `SelectMany`
- nested queryable subqueries
- set operators
- full LINQ `GroupBy` / `IGrouping<TKey, TElement>` semantics
- repository-specific LINQ entrypoints

---

## Key Decisions To Make Early

### 1. Public entrypoint shape
Phase 1 answer: introduce a separate queryable adapter on `ILiteCollection<T>` via `AsQueryable()` instead of making the current `LiteQueryable<T>` itself carry the full `IQueryable<T>` contract.

### 2. Sync enumeration policy
Phase 1 answer: do not silently support sync-over-async. Provider-backed LINQ queries support sync composition only and execute through async terminal extensions; unsupported sync execution paths must fail fast.

### 3. Translation boundary
Phase 1 answer: keep `BsonMapper.GetExpression(...)` and `LinqExpressionVisitor` focused on lambda/body translation; add a separate method-chain translator for `Queryable.*` calls.

### 4. GroupBy scope
Phase 1 answer: support grouped aggregate projections later, not full `IGrouping` semantics in the first release.

### 5. Repository convenience scope
Phase 1 answer: repository-level LINQ convenience is deferred. The collection-root entrypoint is the design anchor.

### 6. Mutable state safety
Phase 1 answer: later phases should avoid building the provider directly on shared mutable `LiteQueryable<T>` / `Query` instances; use separate translation state and lower into the native query model at the boundary.

---

## What To Validate Throughout The Project

1. Parity with `collection.Query()` behavior
2. Correct translation of supported lambdas through the existing mapper visitor
3. Execution through the existing `Query` pipeline
4. Async terminal correctness
5. Clear failure modes for unsupported LINQ shapes
6. Query plan visibility and optimizer parity where possible
7. No regression in current `LiteDbX.Tests/Query/*`

---

## Suggested Work Allocation Across Other LLMs

### Session A
- Milestone 1
- phase decisions only
- no deep execution changes

### Session B
- Milestone 2
- provider shell and translation-state design
- minimal code footprint

### Session C
- Milestone 3
- core operator translation
- focused translation tests

### Session D
- Milestone 4
- async terminal extensions and interop
- execution-path tests

### Session E
- Milestone 5
- grouping and aggregates
- only after MVP parity is stable

### Session F
- Milestone 6
- user docs, diagnostics, rollout notes, cleanup

---

## File Map For Future Implementers

### Existing source files most likely to change
- `LiteDbX/Client/Database/ILiteCollection.cs`
- `LiteDbX/Client/Database/LiteCollection.cs` and query partials
- `LiteDbX/Client/Database/ILiteQueryable.cs`
- `LiteDbX/Client/Database/LiteQueryable.cs`
- `LiteDbX/Client/Database/LiteRepository.cs`
- `LiteDbX/Client/Mapper/BsonMapper.cs`
- `LiteDbX/Client/Mapper/Linq/LinqExpressionVisitor.cs`
- `LiteDbX/Client/Structures/Query.cs`

### New types likely required
- provider type
- queryable root/wrapper type
- method-call translator
- translation-state or query-spec model
- async terminal extensions
- diagnostics / unsupported-expression helpers

---

## Reference Project Guidance

Use `https://github.com/mgernand/LiteDB.Queryable` as a design reference for:

- provider/queryable layering
- expression-chain translation techniques
- supported-operator prioritization
- ergonomics and examples

But adapt it to LiteDbX instead of copying it mechanically. LiteDbX already has:

- a fluent native query API
- an existing query AST in `Query`
- a Bson expression translator
- an async-only direction
- existing query and mapper tests

That means the right strategy is **integration**, not transplantation.

