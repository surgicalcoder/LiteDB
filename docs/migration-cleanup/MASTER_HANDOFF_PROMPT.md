# Master Handoff Prompt for Another LLM

Use the following prompt as the implementation handoff.

---

You are working in the `D:\Work\LiteDBX` repository.

Your task is to implement a new migration package called `LiteDbX.Migrations` and its tests, following the plan in:

- `docs/migration-cleanup/README.md`
- `docs/migration-cleanup/00-master-roadmap.md`
- `docs/migration-cleanup/01-invalid-objectid-policy.md`
- `docs/migration-cleanup/02-nested-paths-and-array-cleanup.md`
- `docs/migration-cleanup/03-bson-predicates-catalog.md`

## Context

The immediate production issue is that some old LiteDB data stored `_id` or `Id` as a BSON string instead of `LiteDbX.ObjectId`, which causes runtime failures such as:

```text
System.InvalidCastException: Unable to cast object of type 'System.String' to type 'LiteDbX.ObjectId'.
```

The migration framework must allow raw-document repair without typed entity deserialization.

## High-level goals

1. Add a new class library project `LiteDbX.Migrations`
2. Add a new test project `LiteDbX.Migrations.Tests`
3. Add both projects to `LiteDbX.slnx`
4. Build a fluent migration runner for raw `BsonDocument` collections
5. Support `ForCollection(...)` as a collection selector, including exact name and wildcard/glob-style matching
6. Persist durable old->new id mapping rows whenever `_id` is repaired with `GenerateNewId`
7. Implement two migration families first:
   - `ConvertField(...).FromStringToObjectId()`
   - `ConvertId().FromStringToObjectId()`
8. Implement predicate-driven cleanup:
   - `RemoveFieldWhen(path, predicate)`
9. Implement a reusable `BsonPredicates` helper catalog
10. Support nested paths in stages:
   - V1: top-level plus dotted nested document paths only
   - V2-ready internal design for arrays and wildcards
11. Add migration journaling and reporting
12. Test thoroughly

## Critical constraints

- Operate on raw `BsonDocument`, not typed entities, when executing migrations
- Preserve all unaffected fields exactly
- Support collection selection by exact name and wildcard pattern without accidentally touching system collections
- `_id` cannot be changed in place; use rebuild-and-swap for `_id` conversion
- Recreate secondary indexes during rebuild mode
- Support `InvalidObjectIdPolicy.GenerateNewId`
- Persist durable old->new `_id` remap records for every generated replacement id
- Do not leave the collection in a broken state
- Be idempotent where possible
- Prefer smallest repo-consistent changes

## Required public concepts

Design around types such as:

- `MigrationRunner`
- `MigrationDefinition`
- `MigrationContext`
- `MigrationReport`
- `CollectionSelector`
- `CollectionMigrationBuilder`
- `InvalidObjectIdPolicy`
- `BsonPredicates`
- path traversal helper such as `BsonPathNavigator`

## `InvalidObjectIdPolicy`

Must include:

```csharp
Fail,
SkipDocument,
LeaveUnchanged,
RemoveField,
GenerateNewId
```

Semantics:

- for `_id`, support `Fail`, `SkipDocument`, `GenerateNewId`
- for `_id`, do not allow `RemoveField`
- for `_id`, `LeaveUnchanged` should not be the default because it leaves the collection broken
- for `_id` with `GenerateNewId`, generate `ObjectId.NewObjectId()`, persist a durable old->new mapping row, and surface summary information in the report/journal
- for ordinary fields, `GenerateNewId` is allowed but should be explicit and carefully documented

## Collection selector scope

`ForCollection(...)` is not literal-only. It must support:

- exact name: `Settings`
- all user collections: `*`
- prefix match: `tenant_*`
- suffix match: `*_archive`
- infix/glob-style match: `*_settings_*`

Rules:

- match user collections only by default
- exclude `$` system collections and migration infrastructure collections such as `__migrations` and `__migration_id_mappings` unless explicitly opted in
- expand selectors deterministically before running steps
- report matched collections, unmatched selectors, and skipped collections

## Nested path scope

### V1

Implement:

- top-level fields
- dotted nested document paths, e.g. `Profile.CustomerId`
- optional parent pruning after removal

Do not implement in V1:

- array indices
- wildcards
- recursive descent

These restrictions apply to BSON field paths, not collection selectors.

### V2-ready design

Structure the path engine so the following can be added later without changing the public API:

- `Items[0].LegacyId`
- `Items[*].LegacyId`
- recursive cleanup passes

## `BsonPredicates`

At minimum, implement these built-ins:

- `Null`
- `Missing`
- `NullOrMissing`
- `EmptyArray`
- `EmptyDocument`
- `EmptyString`
- `WhiteSpaceString`
- `NullOrWhiteSpaceString`
- `ZeroNumber`
- `FalseBoolean`
- `EmptyGuid`
- `EmptyObjectId`
- `Default(BsonValue value)`
- `AnyOfDefaults(params BsonValue[] values)`
- `NullLike`
- `StructurallyEmpty`
- combinators: `And`, `Or`, `Not`

## Migration behavior requirements

### In-place field conversion

- stream raw docs
- convert only targeted field
- preserve every other field
- update only changed docs

### `_id` conversion

- create shadow collection
- convert `_id`
- on invalid strings, honor policy including `GenerateNewId`
- if `GenerateNewId` is used, persist a durable remap row containing at least migration name, run id, collection, old id raw value/type, and new `ObjectId`
- recreate indexes
- validate counts/uniqueness
- rename original to backup
- rename shadow to original
- journal everything

### Cleanup

- remove targeted field if predicate matches
- support nested dotted paths in V1
- optionally prune empty parent containers after removal

## Suggested implementation order

### Stage 1 - scaffolding

Deliver:

- new projects
- solution updates
- base migration runner types
- migration journal types
- collection selector abstraction and expansion/reporting
- initial docs/comments as needed

Stop and verify:

- projects build
- tests project references are correct

### Stage 2 - path engine + predicates + in-place steps

Deliver:

- `BsonPathNavigator` for V1 dotted document paths
- `BsonPredicates`
- `RemoveFieldWhenStep`
- `ConvertFieldTypeStep`

Stop and verify:

- tests for top-level and nested paths
- tests for empty arrays, empty strings, and default cleanup

### Stage 3 - rebuild `_id` migration

Deliver:

- `ConvertIdStep`
- rebuild/swap executor
- index replay
- backup naming
- `GenerateNewId` support
- durable remap persistence such as `__migration_id_mappings`

Stop and verify:

- tests for valid and invalid `_id`
- tests for generated replacement ids
- tests for index preservation

### Stage 4 - reporting and polish

Deliver:

- migration reports
- dry-run support if feasible
- old->new id mapping report
- idempotency verification

Stop and verify:

- rerun behavior is safe
- journal prevents duplicate execution unless explicitly overridden

## Test expectations

Add thorough integration tests using the repository’s existing xUnit + FluentAssertions style.

Required cases include:

- valid string `_id` becomes `ObjectId`
- invalid string `_id` + `GenerateNewId` repairs the row
- invalid string `_id` + `GenerateNewId` writes a durable old->new mapping row
- invalid string `_id` + `Fail` aborts safely
- non-id field converts while all unrelated fields remain untouched
- empty arrays and empty strings are removed correctly
- dotted nested path cleanup works
- parent pruning works when enabled
- rerunning completed migration is safe
- `ForCollection("*")` excludes system and migration infrastructure collections by default
- prefix/suffix/infix collection selectors expand deterministically

## Implementation notes

- Read the current `LiteDbX` raw collection/query/update APIs before coding
- Reuse existing index metadata/query facilities where possible
- Avoid typed mapper deserialization in migration execution paths
- Keep public API clean and fluent
- Prefer a library-first implementation over a CLI-first implementation

## Deliverable format

When you work, proceed stage by stage and validate after each stage. After each stage, summarize:

- files added/changed
- what was implemented
- what was tested
- any remaining risk before moving to the next stage

If tokens are getting tight, stop after a stage boundary and hand off using the same roadmap docs.

---

End of prompt.

