# Phase 5 — Grouping, Aggregates, and Advanced Translation

## Phase Goal

Expand the LINQ provider beyond the MVP while staying within the limits of the current LiteDbX query engine.

This phase is intentionally later because grouped translation is the highest-risk area.

## Existing Files To Study

- `LiteDbX/Client/Database/LiteQueryable.cs`
- `LiteDbX/Client/Structures/Query.cs`
- `LiteDbX/Engine/Query/QueryOptimization.cs`
- `LiteDbX/Engine/Query/Pipeline/GroupByPipe.cs`
- `LiteDbX.Tests/Query/Aggregate_Tests.cs`
- `LiteDbX.Tests/Query/GroupBy_Tests.cs`

## Core Principle

Support only the LINQ grouping and aggregate shapes that can be mapped cleanly onto the current `Query.GroupBy`, `Query.Having`, and grouped pipeline behavior.

Do **not** try to emulate full LINQ-to-Objects `IGrouping<TKey, TElement>` behavior unless the engine model genuinely supports it.

## Work Packages

### P5.1 — Define LiteDbX LINQ `GroupBy` semantics

#### Goal
Document what `GroupBy` means for this provider.

#### Recommended scope
Support grouped aggregate projections first, for patterns like:

- `GroupBy(key).Select(g => new { g.Key, Count = g.Count() })`
- grouped projections that lower naturally to current engine capabilities

#### Not recommended for initial grouped support
- raw enumeration of `IGrouping<TKey, TElement>`
- nested composition over group sequences
- group joins or multi-source group queries

#### Acceptance criteria
A written, narrow `GroupBy` contract aligned with existing engine behavior.

---

### P5.2 — Map grouped aggregates onto existing query capabilities

#### Candidate grouped aggregates
- `Count`
- possibly `Min` / `Max`
- possibly `Sum` / `Average` if current engine support and projection lowering make them safe

#### Important note
The current native `LiteQueryable<T>.Select<K>(...)` explicitly rejects grouped typed select, which means grouped LINQ projection will likely need a dedicated translation path.

#### Acceptance criteria
A documented grouped-projection lowering strategy and a shortlist of supported aggregate shapes.

---

### P5.3 — Add `Having`-style post-group filtering where feasible

#### Goal
Support grouped filtering only if it maps cleanly onto `Query.Having`.

#### Acceptance criteria
If supported, the provider has a defined translation strategy. If not, this phase should explicitly defer it.

---

### P5.4 — Explicitly defer hard operators

#### Operators to document as deferred unless proven easy
- `Join`
- `GroupJoin`
- `SelectMany`
- `Distinct` if it requires new execution semantics
- set operators such as `Union`, `Intersect`, `Except`
- nested queryable subqueries

#### Acceptance criteria
Every deferred operator has a documented failure mode and rationale.

## Deliverables

- grouped semantics contract
- supported grouped aggregate list
- clear defer list for advanced operators
- grouped translation backlog tied to current tests

## Validation

- compare grouped behavior to current `GroupBy` and aggregate capabilities
- mine `GroupBy_Tests.cs` for skipped or partial scenarios
- ensure grouped translation still routes through the current group-by pipeline

## Suggested Test Focus

- grouped key projection
- grouped count projection
- grouped aggregate projection parity
- unsupported grouping shapes fail clearly

## Out of Scope

- full LINQ grouping parity
- multi-source query composition
- comprehensive set-operator support

## Exit Criteria

This phase is done when the team has a precise, engine-aligned definition of which grouped and advanced LINQ patterns are supported, deferred, or rejected, and that definition is backed by targeted tests or a concrete implementation backlog.

---

## Phase 5 Supported LINQ GroupBy Contract

Phase 5 support is intentionally narrow and is defined by what already maps cleanly onto the existing native query engine:

- `Query.GroupBy`
- grouped `Query.Select`
- optional `Query.Having`
- existing grouped execution through `QueryOptimization` and `GroupByPipe`

The LINQ provider is **not** attempting to expose general-purpose `IGrouping<TKey, TElement>` semantics.

### Supported grouped query shape

The supported provider shape is:

- optional pre-group `Where(...)`
- one `GroupBy(source, keySelector)`
- optional grouped `Where(...)` that lowers to `HAVING`
- one grouped `Select(...)`
- optional `Skip(...)` / `Take(...)`
- async materialization terminals such as `ToListAsync`, `ToArrayAsync`, `FirstAsync`, `FirstOrDefaultAsync`, `SingleAsync`, and `SingleOrDefaultAsync`

In practice, the intended happy path is:

- `GroupBy(key).Select(g => new { g.Key, Count = g.Count() })`
- `GroupBy(key).Where(g => g.Count() >= 2).Select(g => new { g.Key, Count = g.Count() })`
- `GroupBy(key).Select(g => new { g.Key, Sum = g.Sum(x => x.SomeNumber) })`

### Supported grouped projection members

Grouped `Select(...)` is limited to projections composed from:

- `g.Key`
- direct grouped aggregates over the group sequence:
  - `g.Count()`
  - `g.Sum(x => x.Field)`
  - `g.Min(x => x.Field)`
  - `g.Max(x => x.Field)`
  - `g.Average(x => x.Field)`
- document/object projections that only combine the above values

Examples that fit the intended scope:

- `GroupBy(x => x.Age).Select(g => new { Age = g.Key, Count = g.Count() })`
- `GroupBy(x => x.Date.Year).Select(g => new { Year = g.Key, Sum = g.Sum(x => x.Age) })`

### Supported grouped filtering (`HAVING`)

Grouped `Where(...)` is supported only **after** `GroupBy(...)` and **before** the grouped `Select(...)`, and only when it can lower cleanly to `Query.Having`.

Supported grouped predicate building blocks are limited to:

- comparisons over `g.Key`
- comparisons over direct grouped aggregates
- boolean combinations of those comparisons

Examples:

- `GroupBy(x => x.Age).Where(g => g.Key >= 30)`
- `GroupBy(x => x.Age).Where(g => g.Count() >= 2 && g.Key >= 30)`

### Engine-aligned behavior notes

- grouped LINQ queries still lower into a fresh native `Query`
- grouped execution still routes through `QueryOptimization` and `GroupByPipe`
- grouped ordering follows native engine behavior; the provider does **not** add general post-group ordering support
- `collection.Query()` remains the escape hatch for advanced/manual grouped queries

## Explicitly Unsupported / Rejected in Phase 5

The provider must fail clearly for shapes that imply more than the native engine currently guarantees.

### Unsupported grouped shapes

- raw `IGrouping<TKey, TElement>` materialization
- `GroupBy` overloads with:
  - element selector
  - result selector
  - comparer
- nested grouped composition such as:
  - `g.Where(...)`
  - `g.Select(...)`
  - `g.ToArray()` / `g.ToList()`
  - projecting raw grouped elements
- array/list aggregation projections over grouped contents
- multiple grouped projection stages
- grouped `OrderBy` / `ThenBy`
- nested `GroupBy`
- grouped `AnyAsync`, `CountAsync`, and `LongCountAsync` over grouped result rows

### Explicitly deferred advanced operators

- `Join`
- `GroupJoin`
- `SelectMany`
- set operators (`Union`, `Intersect`, `Except`)
- nested queryable subqueries
- any multi-source query composition

## Native Builder Escape Hatch

When callers need grouped shapes outside the narrow contract above, the expected path is still the native builder:

- `collection.Query()`
- direct `Query.GroupBy(...)`
- direct `Query.Having(...)`
- manual grouped `BsonExpression` projections

This is a design goal, not a temporary workaround: the LINQ provider remains additive and intentionally narrower than the native query API.

