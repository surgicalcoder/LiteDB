# Phase 2 — Queryable Provider and State Model

## Phase Goal

Introduce the provider-side architecture needed to parse `Queryable` method chains without disturbing the current query engine.

This phase locks the ownership model for:

- provider-backed query roots
- expression-tree parsing
- normalized translation state
- lowering into a fresh native `Query`
- mutability rules that avoid cross-query contamination

It does **not** attempt broad operator execution yet.

## Existing Files To Study

- `LiteDbX/Client/Database/Collections/Find.cs`
- `LiteDbX/Client/Database/ILiteCollection.cs`
- `LiteDbX/Client/Database/ILiteQueryable.cs`
- `LiteDbX/Client/Database/LiteQueryable.cs`
- `LiteDbX/Client/Mapper/BsonMapper.cs`
- `LiteDbX/Client/Mapper/Linq/LinqExpressionVisitor.cs`
- `LiteDbX/Client/Structures/Query.cs`

## Final Phase 2 Decisions

### 1. Provider/root types

#### Chosen types

- `ILiteCollection<T>.AsQueryable()` / `AsQueryable(ILiteTransaction)` remain the public LINQ roots
- `LiteDbXQueryable<T>` is the provider-backed wrapper implementing `IQueryable<T>` and `IOrderedQueryable<T>`
- `LiteDbXQueryProvider` owns provider-facing expression parsing and queryable creation
- `LiteDbXQueryRoot` holds immutable collection-root context

#### Why this shape was chosen

- It keeps the LINQ-facing wrapper clearly separate from the existing native `LiteQueryable<T>` builder.
- It lets `Query()` and `LiteQueryable<T>` remain the first-class native API.
- It keeps transaction context and collection identity attached to the LINQ root without leaking native mutable `Query` instances into expression composition.

#### Provider responsibility list
`LiteDbXQueryProvider` is responsible for:

1. accepting `Queryable.*` expression trees from the LINQ surface
2. translating those trees into normalized LiteDbX query state
3. creating new `LiteDbXQueryable<T>` wrappers for shaped queries
4. rejecting sync execution paths with clear diagnostics
5. exposing the lowering boundary from provider state into a fresh native `Query`

#### Queryable wrapper responsibility list
`LiteDbXQueryable<T>` is responsible for:

1. carrying `Expression`, `ElementType`, `Provider`, and immutable provider state
2. representing the current LINQ chain as an `IQueryable<T>`
3. preventing synchronous enumeration by failing fast

---

### 2. Translation boundary

#### Final rule
- `LiteDbXQueryProvider` / `LiteDbXQueryParser` own `Queryable.*` method-chain parsing
- `BsonMapper.GetExpression(...)` and `LinqExpressionVisitor` remain the owners of leaf lambda translation
- `LiteDbXQueryLowerer` owns replaying normalized provider state into a fresh native `Query`

#### Boundary detail

The provider parses shapes like:

- `Queryable.Where`
- `Queryable.Select`
- `Queryable.OrderBy`
- `Queryable.ThenBy`
- `Queryable.Skip`
- `Queryable.Take`

but it does **not** interpret lambda bodies itself. Instead, it stores the unwrapped `LambdaExpression` on normalized state and only later calls back into `BsonMapper.GetExpression(...)` when lowering to native query semantics.

#### Why this matters

`LinqExpressionVisitor` already knows how to translate bodies like `x => x.Age > 10` or `x => new { x.Id, x.Name }` into `BsonExpression` form. It should not become responsible for interpreting full `Queryable` method chains or provider execution rules.

---

### 3. Internal translation-state model

#### Chosen shape

Phase 2 uses an immutable/copy-on-write normalized state model split into these pieces:

##### `LiteDbXQueryRoot`
Immutable root context captured once at `AsQueryable()` creation time:

- engine reference
- mapper reference
- collection name
- root entity type
- inherited include paths from the collection
- optional explicit transaction

##### `LiteDbXQueryOperator`
An immutable descriptor for one parsed query-shaping operator:

- method kind (`Where`, `Select`, `OrderBy`, etc.)
- original `MethodCallExpression`
- optional `LambdaExpression`
- optional value expression (for `Skip` / `Take`)
- optional result type metadata

##### `LiteDbXQueryState`
An immutable snapshot of the current query chain:

- root context
- root entity type
- current result element type
- ordered operator list
- projection flags (`HasProjection`, scalar/document projection)
- grouping flag placeholder
- terminal intent placeholder
- original query expression for diagnostics/debugging

#### Lowering boundary

The provider state is the authoritative intermediate model during LINQ composition.

Only at the lowering boundary should LiteDbX create a fresh native `Query` and replay provider state into it. That replay currently belongs to `LiteDbXQueryLowerer`.

#### Why not mutate `Query` directly during parsing?

- `LiteQueryable<T>` already demonstrates shared-state hazards by mutating `_query` directly.
- Some native aggregate paths temporarily replace `Select`, then restore it.
- Directly mutating `Query` while parsing expression trees would make query reuse and branching far more fragile.

---

### 4. Mutability / cloning rules

#### Final decision
Use immutable root context plus copy-on-write query state snapshots.

#### Rules

1. `LiteDbXQueryRoot` is immutable and shared safely.
2. `LiteDbXQueryState` is immutable; every appended operator returns a new snapshot.
3. Parsed operators are immutable value-like descriptors.
4. Lowering always creates a **fresh** native `Query`.
5. Provider composition must never reuse a mutable `LiteQueryable<T>` or mutable `Query` instance as its working state.

#### Result
Branching query chains such as:

```csharp
var baseQuery = collection.AsQueryable().Where(x => x.Active);
var ordered = baseQuery.OrderBy(x => x.Name);
var projected = baseQuery.Select(x => x.Id);
```

can be represented without one branch corrupting another.

## Phase 2 Scaffold Added

The current scaffold is intentionally narrow and architecture-focused:

- `ILiteCollection<T>.AsQueryable()`
- `ILiteCollection<T>.AsQueryable(ILiteTransaction)`
- `LiteDbXQueryable<T>`
- `LiteDbXQueryProvider`
- `LiteDbXQueryRoot`
- `LiteDbXQueryOperator`
- `LiteDbXQueryState`
- `LiteDbXQueryParser`
- `LiteDbXQueryLowerer`
- lambda/value-expression helper utilities for parser/lowering support

What this scaffold does now:

- creates provider-backed query roots
- parses supported query-shaping methods into normalized immutable state
- provides a fresh-`Query` lowering path for the parsed state
- fails fast for synchronous provider execution

What this scaffold does **not** claim yet:

- broad operator support beyond Phase 1 MVP shapes
- async terminal execution implementation
- public user-facing LINQ terminal extensions
- full grouping support

## Sample Flow: `Where -> OrderBy -> Select -> CountAsync`

This is the intended ownership flow for later phases:

1. `collection.AsQueryable()` creates a `LiteDbXQueryRoot`, `LiteDbXQueryProvider`, and root `LiteDbXQueryable<T>`.
2. `Queryable.Where(...)` calls into `LiteDbXQueryProvider.CreateQuery(...)`.
3. `LiteDbXQueryParser` parses the `Where` method call and appends a `LiteDbXQueryOperator` describing that step.
4. `Queryable.OrderBy(...)` repeats the process, producing a new immutable `LiteDbXQueryState` with an additional ordering operator.
5. `Queryable.Select(...)` adds projection metadata and stores the quoted projection lambda on state.
6. `CountAsync()` in a later phase should mark terminal intent, lower the normalized state through `LiteDbXQueryLowerer`, and then execute through the native query path.
7. During lowering, each stored lambda is translated through `BsonMapper.GetExpression(...)`, producing native `BsonExpression` values for a fresh `Query`.
8. That `Query` then flows through the existing `QueryOptimization` → `QueryPlan` → `QueryExecutor` / pipeline path.

## Why This Preserves the Current System

This design preserves the current LiteDbX architecture because:

1. `Query()` and `LiteQueryable<T>` remain untouched as the native fluent API.
2. The provider lowers into a native `Query` instead of bypassing the engine.
3. Lambda translation still goes through the existing mapper/visitor path.
4. The provider works with separate immutable translation state, avoiding the mutable `_query` behavior of the native builder during LINQ composition.
5. Sync execution is still rejected rather than hidden behind sync-over-async.

## Deliverables Completed By This Phase

- provider/root architecture scaffold
- explicit translation-boundary ownership
- immutable translation-state model
- fresh-`Query` lowering boundary
- copy-on-write mutability rules

## Validation

Use these checks while reviewing this phase:

- trace `Where -> OrderBy -> Select -> CountAsync`
- verify parsing, lambda translation, and lowering each have distinct owners
- verify the lowering target is native `Query`, not a replacement execution path
- verify the provider shell does not force `LiteQueryable<T>` to become the only `IQueryable<T>` implementation

## Out of Scope

- full operator support
- async terminal execution implementation
- grouping implementation
- provider result materialization
- user-facing docs beyond architecture notes

## Exit Criteria

This phase is done when another implementer can explain:

1. which type owns expression-tree parsing
2. which type owns lambda-to-`BsonExpression` translation
3. how a partially-built LINQ query is stored safely
4. how the final state becomes a native LiteDbX query

