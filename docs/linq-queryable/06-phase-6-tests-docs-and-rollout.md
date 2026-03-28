# Phase 6 — Tests, Documentation, and Rollout

## Phase Goal

Finish the LINQ / `IQueryable<T>` effort with parity-driven tests, user-facing docs, diagnostics, and a staged rollout plan.

## Existing Files To Study

- `LiteDbX.Tests/Query/Where_Tests.cs`
- `LiteDbX.Tests/Query/OrderBy_Tests.cs`
- `LiteDbX.Tests/Query/Select_Tests.cs`
- `LiteDbX.Tests/Query/Aggregate_Tests.cs`
- `LiteDbX.Tests/Query/GroupBy_Tests.cs`
- `LiteDbX.Tests/Mapper/*`
- `README.md`

## Work Packages

### P6.1 — Build a parity-first test matrix

#### Goal
Tie every supported LINQ operator to existing native query tests or new provider-specific tests.

#### Recommended matrix categories
- translation-only tests
- result parity with `collection.Query()`
- parity with in-memory LINQ where feasible
- query plan/debug visibility tests
- unsupported-pattern diagnostics tests

#### Acceptance criteria
A written matrix that maps each supported operator to test coverage.

---

### P6.2 — Add provider-focused tests

#### Goal
Ensure the new LINQ layer is tested as a layer, not just through reused native builder tests.

#### Recommended coverage
- provider query composition
- async terminal execution
- projection materialization
- unsupported LINQ patterns
- grouped support if implemented

#### Acceptance criteria
New tests prove provider behavior rather than only native builder behavior.

---

### P6.3 — Write user-facing documentation

#### Required docs
- how to start a LINQ query
- when to use LINQ vs native `Query()`
- supported operators
- unsupported operators
- async terminal usage
- escape hatches for advanced scenarios

#### Acceptance criteria
The main repo docs make it impossible to mistake this for a complete replacement of the native query builder.

---

### P6.4 — Define rollout and compatibility strategy

#### Goal
Introduce the LINQ layer safely.

#### Recommended rollout notes
- launch as additive API surface
- keep native query builder examples in docs
- explicitly label support level for LINQ operators
- document likely future expansion areas separately from current guarantees

#### Acceptance criteria
There is a written statement of what is production-ready, experimental, or deferred.

## Deliverables

- test matrix
- new provider-specific tests
- documentation updates
- rollout notes and support boundaries

## Validation

- run focused query and mapper tests
- ensure no regression in current query behavior
- review docs for scope clarity and escape-hatch guidance

## Suggested Test Focus

- `Where` parity
- ordering parity
- projection parity
- async terminal parity
- unsupported operator diagnostics
- grouped coverage if applicable

## Out of Scope

- redesigning existing native query docs from scratch
- broadening supported LINQ scope beyond implemented features

## Exit Criteria

This phase is done when the LINQ layer has:

1. targeted automated coverage
2. clear user-facing documentation
3. explicit support boundaries
4. a rollout story that keeps the native query system clearly first-class

---

## Phase 6 Parity-First Test Matrix

The LINQ provider must be validated as a **translation layer on top of** the native query system, not as an independent execution stack.

That means every supported LINQ feature should be covered through one or more of:

- provider translation tests
- provider execution parity tests against `collection.Query()`
- parity checks against in-memory LINQ where ordering/semantics are well-defined
- mapper translation/evaluation tests for the lambda bodies the provider depends on
- diagnostics tests for unsupported LINQ shapes

### Coverage matrix

| Area | Native baseline | Provider-focused coverage | Notes |
|---|---|---|---|
| `Where` translation/parity | `LiteDbX.Tests/Query/Where_Tests.cs` | `Queryable_Translation_Tests.cs` | Includes multi-`Where`, string filters, and array `Contains` parity |
| ordering/parity | `LiteDbX.Tests/Query/OrderBy_Tests.cs` | `Queryable_Translation_Tests.cs`, `Queryable_Execution_Tests.cs` | Covers `OrderBy`, `ThenBy`, paging, and explain-plan parity |
| projection/parity | `LiteDbX.Tests/Query/Select_Tests.cs` | `Queryable_Translation_Tests.cs`, `Queryable_Execution_Tests.cs` | Covers scalar and object projections |
| async terminal parity | native async query terminals in `LiteQueryable<T>` | `Queryable_Execution_Tests.cs` | Covers `ToListAsync`, `ToArrayAsync`, `FirstAsync`, `SingleAsync`, `AnyAsync`, `CountAsync`, `LongCountAsync`, `GetPlanAsync` |
| grouped aggregate subset | `LiteDbX.Tests/Query/GroupBy_Tests.cs` | `GroupBy_Tests.cs`, `Queryable_Execution_Tests.cs` | Covers grouped key/aggregate projection, grouped `Where` → `Having`, grouped paging, and grouped explain-plan routing |
| sync execution rejection | n/a | `Queryable_Execution_Tests.cs` | Provider-backed sync execution must fail clearly |
| unsupported LINQ operators | n/a | `Queryable_Translation_Tests.cs`, `GroupBy_Tests.cs`, `Queryable_Execution_Tests.cs` | Covers deferred operators and unsupported grouped shapes |
| lambda/body translation boundary | `LiteDbX.Tests/Mapper/LinqBsonExpression_Tests.cs`, `LiteDbX.Tests/Mapper/LinqEval_Tests.cs` | mapper tests remain the proof point | Validates body translation reused by both `Find(...)` and the provider lowerer |

### Required provider-specific test categories

The provider layer must have dedicated tests for:

1. `AsQueryable()` root composition
2. lowering to native `Query`
3. async terminal delegation through the existing engine path
4. explicit unsupported-pattern diagnostics
5. grouped translation only within the engine-supported subset
6. transaction-aware query root binding via `AsQueryable(ILiteTransaction)`

## Manual Validation Focus

Phase 6 validation should stay focused and incremental.

Recommended manual suites:

- provider translation tests
- provider execution tests
- grouped LINQ tests
- mapper LINQ translation/evaluation tests
- existing native query tests most directly related to the supported LINQ operator set

The goal is not to prove full LINQ parity with LINQ-to-Objects or EF-style providers. The goal is to prove that supported provider-backed queries continue to lower into the current LiteDbX query model and execute with parity to `collection.Query()`.

## Diagnostics Coverage Expectations

Unsupported LINQ shapes must fail with messages that do two things:

1. explain that the shape is unsupported by the current LiteDbX LINQ provider
2. direct callers back to `collection.Query()` for advanced/manual queries

### Unsupported-pattern test categories

Diagnostics coverage should explicitly cover:

- `Join`
- `GroupJoin`
- `SelectMany`
- set operators such as `Union`
- post-projection shaping
- multiple primary `OrderBy` clauses
- invalid paging shapes such as `Take(...).Skip(...)`
- unsupported `GroupBy` overloads
- grouped ordering
- raw `IGrouping<TKey, TElement>` materialization
- grouped nested composition / grouped array projection
- grouped aggregate terminals that do not map cleanly to grouped result rows
- synchronous provider-backed execution

## Support-Level and Rollout Notes

The rollout must make it clear that LINQ is additive and narrower than the native query API.

### Production-ready surface

The following areas are the primary rollout target for production use:

- `ILiteCollection<T>.AsQueryable()`
- `ILiteCollection<T>.AsQueryable(ILiteTransaction)`
- `Where`
- `Select`
- `OrderBy` / `OrderByDescending`
- `ThenBy` / `ThenByDescending`
- `Skip` / `Take`
- async terminals:
  - `ToListAsync`
  - `ToArrayAsync`
  - `FirstAsync`
  - `FirstOrDefaultAsync`
  - `SingleAsync`
  - `SingleOrDefaultAsync`
  - `AnyAsync`
  - `CountAsync`
  - `LongCountAsync`
  - `GetPlanAsync`

### Conservative / narrow-support surface

These features may be documented as supported, but only within tightly constrained semantics:

- grouped aggregate projections
- grouped `Where(...)` lowering to `HAVING`
- grouped paging after grouped projection

Docs and release notes should describe these as a **narrow grouped subset**, not as general `IGrouping<TKey, TElement>` support.

### Explicitly deferred surface

These remain deferred and must stay documented as such:

- full LINQ `GroupBy` / raw `IGrouping<TKey, TElement>` semantics
- `Join`
- `GroupJoin`
- `SelectMany`
- set operators
- nested queryable subqueries
- multi-source composition
- repository-wide LINQ convenience entrypoints

## Recommended Staged Rollout

### Stage 0 — internal validation

- keep the feature additive
- focus on parity tests and diagnostics
- compare provider-backed behavior against `collection.Query()`
- confirm docs do not overpromise LINQ coverage

### Stage 1 — documented additive release

- publish `AsQueryable()` documentation
- document async-only terminal usage prominently
- keep `Query()` examples first-class in README/docs
- present grouped LINQ as limited/engine-aligned

### Stage 2 — production hardening

- watch for gaps reported by users around diagnostics and unsupported-shape discoverability
- expand only when parity with the native query pipeline remains obvious
- keep unsupported features deferred rather than emulated poorly

### Stage 3 — future expansion backlog

- revisit deferred operators only if they can be lowered cleanly into the existing engine
- continue treating `Query()` as the advanced escape hatch even if LINQ support expands

## Documentation Rules for Release Readiness

All user-facing docs should make these points unmistakable:

1. LINQ starts at `collection.AsQueryable()`
2. provider-backed queries compose synchronously but execute asynchronously
3. `collection.Query()` remains the native first-class query API
4. advanced/manual scenarios should continue to use `Query()`
5. supported LINQ is a subset, not a replacement for the native query system

