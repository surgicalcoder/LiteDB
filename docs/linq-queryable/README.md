# LiteDbX LINQ / IQueryable Roadmap — Handoff Pack

This folder contains a milestone-based plan for adding LINQ / `IQueryable<T>` support **on top of** the existing `LiteDbX` query system.

It is intentionally structured so you can hand one file at a time to another LLM or engineer without losing the architectural guardrails.

## Core Design Rules

These rules apply to every phase unless a later decision document explicitly replaces them:

1. LINQ / `IQueryable<T>` is an **adapter layer**, not a replacement for the current query system.
2. `ILiteQueryable<T>`, `LiteQueryable<T>`, `Query`, `QueryOptimization`, `QueryPlan`, `QueryExecutor`, `QueryPipe`, and `GroupByPipe` remain the canonical execution path.
3. `BsonMapper.GetExpression(...)` and `LinqExpressionVisitor` remain **leaf lambda translators**; they should not become the full `Queryable` provider.
4. The implementation must respect the project's **async-only execution** direction.
5. Do not degrade or remove the current fluent query API.
6. Unsupported LINQ constructs must fail clearly and predictably.
7. Favor parity with existing query-builder behavior over broad but fragile LINQ coverage.

## Phase 1 Contract Snapshot

The following contract decisions are frozen by Phase 1 and should be treated as baseline assumptions for later phases:

1. LINQ starts from `ILiteCollection<T>.AsQueryable()`.
2. A transaction-aware root should also exist via `ILiteCollection<T>.AsQueryable(ILiteTransaction)`.
3. `Query()` remains the first-class native builder and advanced escape hatch.
4. Provider-backed `IQueryable<T>` supports synchronous composition only; execution is async-only.
5. Sync enumeration and sync LINQ materializers are not supported for provider-backed LiteDbX queries and must fail clearly.
6. The LINQ adapter may expose `*Async` terminal extensions as an interoperability exception, even though the native async-only LiteDbX API keeps plain method names.
7. Phase 1 MVP scope is limited to `Where`, `Select`, `OrderBy`, `OrderByDescending`, `ThenBy`, `ThenByDescending`, `Skip`, `Take`, plus async terminals such as `ToListAsync`, `FirstAsync`, `AnyAsync`, `CountAsync`, and `LongCountAsync`.

## Recommended Execution Order

1. `00-master-roadmap.md`
2. `01-phase-1-public-surface-and-contracts.md`
3. `02-phase-2-queryable-provider-and-state-model.md`
4. `03-phase-3-mvp-operator-translation.md`
5. `04-phase-4-async-terminals-and-builder-interop.md`
6. `05-phase-5-grouping-aggregates-and-advanced-translation.md`
7. `06-phase-6-tests-docs-and-rollout.md`

## What Success Looks Like

At the end of this roadmap, LiteDbX should support a practical LINQ experience such as:

- `await collection.AsQueryable().Where(x => x.Age > 18).ToListAsync()`
- `await collection.AsQueryable().OrderBy(x => x.Name).Select(x => new { x.Id, x.Name }).ToArrayAsync()`
- async terminals such as `ToListAsync`, `FirstAsync`, `CountAsync`, and `AnyAsync`

while still preserving the existing native query builder:

- `collection.Query().Where(...)`
- `collection.Query().GroupBy(...)`
- `collection.Query().GetPlan()`
- direct `BsonExpression`-based escape hatches

## What This Roadmap Is Not

This roadmap is **not** for:

- replacing the existing query engine
- rebuilding the optimizer or execution pipeline from scratch
- promising EF Core-level LINQ coverage
- introducing sync-over-async as the default execution model
- making `LiteQueryable<T>` itself become the full `IQueryable<T>` provider

## Suggested Inputs For Any Future Session

When farming out a phase, provide:

- `docs/linq-queryable/MASTER_HANDOFF_PROMPT.md`
- the relevant phase doc from this folder
- `docs/linq-queryable/00-master-roadmap.md`
- any already-completed phase docs or decision notes
- the current versions of the touched source files

## Primary Existing Symbols To Anchor To

- `LiteDbX/Client/Database/ILiteCollection.cs`
- `LiteDbX/Client/Database/ILiteQueryable.cs`
- `LiteDbX/Client/Database/LiteQueryable.cs`
- `LiteDbX/Client/Database/LiteRepository.cs`
- `LiteDbX/Client/Mapper/BsonMapper.cs`
- `LiteDbX/Client/Mapper/Linq/LinqExpressionVisitor.cs`
- `LiteDbX/Client/Structures/Query.cs`
- `LiteDbX/Engine/Query/QueryOptimization.cs`
- `LiteDbX/Engine/Query/Structures/QueryPlan.cs`
- `LiteDbX/Engine/Query/QueryExecutor.cs`
- `LiteDbX/Engine/Query/Pipeline/QueryPipe.cs`
- `LiteDbX/Engine/Query/Pipeline/GroupByPipe.cs`
- `LiteDbX.Tests/Query/*`

## Reference Project

Use `https://github.com/mgernand/LiteDB.Queryable` as a source of ideas for:

- provider structure
- expression-chain translation patterns
- supported operator sets
- ergonomics for queryable entrypoints

Do **not** assume it can be transplanted verbatim. LiteDbX already has a different query surface, a different async direction, and its own execution pipeline.

