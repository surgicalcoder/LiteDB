# LiteDB - A .NET NoSQL Document Store in a single data file

[![NuGet Version](https://img.shields.io/nuget/v/LiteDB)](https://www.nuget.org/packages/LiteDB/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/LiteDB)](https://www.nuget.org/packages/LiteDB/)
[![](https://dcbadge.limes.pink/api/server/u8seFBH9Zu?style=flat-square)](https://discord.gg/u8seFBH9Zu)

[![NuGet Version](https://img.shields.io/nuget/vpre/LiteDB)](https://www.nuget.org/packages/LiteDB/absoluteLatest)
[![Build status](https://img.shields.io/github/actions/workflow/status/litedb-org/LiteDB/publish-prerelease.yml)](https://github.com/litedb-org/LiteDB/actions/workflows/publish-prerelease.yml)
<!--[![Build status](https://ci.appveyor.com/api/projects/status/sfe8he0vik18m033?svg=true)](https://ci.appveyor.com/project/mbdavid/litedb) -->
LiteDB is a small, fast and lightweight .NET NoSQL embedded database. 

- Serverless NoSQL Document Store
- Simple API, similar to MongoDB
- 100% C# code for .NET 4.5 / NETStandard 1.3/2.0 in a single DLL (less than 450kb)
- Thread-safe
- ACID with full transaction support
- Data recovery after write failure (WAL log file)
- Datafile encryption using DES (AES) cryptography
- Map your POCO classes to `BsonDocument` using attributes or fluent mapper API
- Store files and stream data (like GridFS in MongoDB)
- Single data file storage (like SQLite)
- Index document fields for fast search
- LINQ predicate translation plus a provider-backed `IQueryable<T>` subset for supported query shapes
- SQL-Like commands to access/transform data
- [LiteDB Studio](https://github.com/mbdavid/LiteDB.Studio) - Nice UI for data access 
- Open source and free for everyone - including commercial use
- Install from NuGet: `Install-Package LiteDB`


## New v5

- New storage engine
- No locks for `read` operations (multiple readers)
- `Write` locks per collection (multiple writers)
- Internal/System collections 
- New `SQL-Like Syntax`
- New query engine (support projection, sort, filter, query)
- Partial document load (root level)
- and much, much more!

## Lite.Studio

New UI to manage and visualize your database:


![LiteDB.Studio](https://www.litedb.org/images/banner.gif)

## Documentation

Visit [the Wiki](https://github.com/mbdavid/LiteDB/wiki) for full documentation. For simplified chinese version, [check here](https://github.com/lidanger/LiteDB.wiki_Translation_zh-cn).

## LiteDB Community

Help LiteDB grow its user community by answering this [simple survey](https://docs.google.com/forms/d/e/1FAIpQLSc4cNG7wyLKXXcOLIt7Ea4TlXCG6s-51_EfHPu2p5WZ2dIx7A/viewform?usp=sf_link)

## How to use LiteDB

A quick example for storing and searching documents:

```C#
// Create your POCO class
public class Customer
{
    public int Id { get; set; }
    public string Name { get; set; }
    public int Age { get; set; }
    public string[] Phones { get; set; }
    public bool IsActive { get; set; }
}

// Open database (or create if doesn't exist)
using(var db = new LiteDatabase(@"MyData.db"))
{
    // Get customer collection
    var col = db.GetCollection<Customer>("customers");

    // Create your new customer instance
    var customer = new Customer
    { 
        Name = "John Doe", 
        Phones = new string[] { "8000-0000", "9000-0000" }, 
        Age = 39,
        IsActive = true
    };

    // Create unique index in Name field
    col.EnsureIndex(x => x.Name, true);

    // Insert new customer document (Id will be auto-incremented)
    col.Insert(customer);

    // Update a document inside a collection
    customer.Name = "Joana Doe";

    col.Update(customer);

    // Use a predicate expression to query documents
    var results = col.Find(x => x.Age > 20);
}
```

Using fluent mapper and cross document reference for more complex data models

```C#
// DbRef to cross references
public class Order
{
    public ObjectId Id { get; set; }
    public DateTime OrderDate { get; set; }
    public Address ShippingAddress { get; set; }
    public Customer Customer { get; set; }
    public List<Product> Products { get; set; }
}        

// Re-use mapper from global instance
var mapper = BsonMapper.Global;

// "Products" and "Customer" are from other collections (not embedded document)
mapper.Entity<Order>()
    .DbRef(x => x.Customer, "customers")   // 1 to 1/0 reference
    .DbRef(x => x.Products, "products")    // 1 to Many reference
    .Field(x => x.ShippingAddress, "addr"); // Embedded sub document
            
using(var db = new LiteDatabase("MyOrderDatafile.db"))
{
    var orders = db.GetCollection<Order>("orders");
        
    // When query Order, includes references
    var query = orders
        .Include(x => x.Customer)
        .Include(x => x.Products) // 1 to many reference
        .Find(x => x.OrderDate <= DateTime.Now);

    // Each instance of Order will load Customer/Products references
    foreach(var order in query)
    {
        var name = order.Customer.Name;
        ...
    }
}

```

## Query APIs: native `Query()` and provider-backed LINQ

LiteDbX exposes **two complementary query surfaces**:

- `collection.Query()` — the native, first-class LiteDbX query builder
- `collection.AsQueryable()` — an additive LINQ / `IQueryable<T>` adapter for supported query shapes

`AsQueryable()` does **not** replace `Query()`.

Provider-backed LINQ queries are translated back into the same native LiteDbX query model and execution pipeline used by `Query()`.

### When to use `Query()`

Prefer `Query()` when you need:

- the full native LiteDbX query surface
- direct `BsonExpression` control
- advanced/manual grouped queries
- the clearest escape hatch for unsupported LINQ shapes

### When to use `AsQueryable()`

Use `AsQueryable()` when you want supported single-source LINQ composition over a collection root and are happy to execute it through LiteDbX async queryable terminals.

### Starting a LINQ query

Provider-backed LINQ starts from a collection root via `ILiteCollection<T>.AsQueryable()`.
It does not currently start from `LiteRepository`; repository convenience remains centered on the native `Query<T>()` path.

```csharp
var rows = await customers
    .AsQueryable()
    .Where(x => x.IsActive)
    .OrderBy(x => x.Name)
    .Select(x => new { x.Id, x.Name })
    .ToListAsync();
```

Transaction-aware roots are also available:

```csharp
await using var tx = await db.BeginTransaction();

var names = await customers
    .AsQueryable(tx)
    .Where(x => x.IsActive)
    .Select(x => x.Name)
    .ToArrayAsync();
```

### Async-only execution for provider-backed `IQueryable<T>`

Provider-backed LINQ queries compose synchronously but execute asynchronously.

Use LiteDbX async terminals such as:

- `ToListAsync()`
- `ToArrayAsync()`
- `FirstAsync()`
- `FirstOrDefaultAsync()`
- `SingleAsync()`
- `SingleOrDefaultAsync()`
- `AnyAsync()`
- `CountAsync()`
- `LongCountAsync()`
- `GetPlanAsync()`

Do **not** rely on synchronous enumeration/materialization for provider-backed `IQueryable<T>` queries. Those paths are expected to fail clearly rather than silently using sync-over-async execution.

### Supported LINQ subset

The provider is intentionally narrower than full LINQ-to-Objects or EF-style providers.

Supported core operators include:

- `Where`
- `Select`
- `OrderBy`
- `OrderByDescending`
- `ThenBy`
- `ThenByDescending`
- `Skip`
- `Take`

Supported grouped LINQ is intentionally **narrow** and engine-aligned:

- `GroupBy(key)`
- optional grouped `Where(...)` lowering to native `HAVING`
- grouped aggregate projections such as:
  - `Select(g => new { g.Key, Count = g.Count() })`
  - `Select(g => new { g.Key, Sum = g.Sum(x => x.SomeField) })`

### Unsupported or deferred LINQ shapes

Use `collection.Query()` instead for shapes such as:

- `Join`
- `GroupJoin`
- `SelectMany`
- set operators (`Union`, `Intersect`, `Except`)
- nested queryable subqueries
- raw `IGrouping<TKey, TElement>` materialization
- nested grouped composition / grouped element projection
- advanced/manual grouped queries beyond grouped aggregate projections

### Native `Query()` remains the advanced escape hatch

For full control, use the native query builder directly:

```csharp
var rows = await customers.Query()
    .Where(x => x.IsActive)
    .OrderBy(x => x.Name)
    .Select(x => new { x.Id, x.Name })
    .ToArray();
```

For grouped/manual queries, prefer the native builder explicitly:

```csharp
var grouped = await customers.Query()
    .GroupBy(BsonExpression.Create("$.Age"))
    .Having(BsonExpression.Create("COUNT(*._id) >= 2"))
    .Select(BsonExpression.Create("{ Age: @key, Count: COUNT(*._id) }"))
    .ToArray();
```

### Debugging translated LINQ queries

Use `GetPlanAsync()` to inspect how a provider-backed query will execute:

```csharp
var plan = await customers
    .AsQueryable()
    .Where(x => x.IsActive)
    .OrderBy(x => x.Name)
    .Select(x => new { x.Id, x.Name })
    .GetPlanAsync();
```

### Rollout / support guidance

- `Query()` remains the primary native query API
- `LiteRepository` remains centered on native `Query<T>()`; repository-level LINQ convenience is intentionally deferred
- `AsQueryable()` is additive and production-oriented for the documented supported subset
- grouped LINQ should be understood as a conservative subset, not full `IGrouping<TKey, TElement>` parity
- unsupported LINQ patterns are expected to fail clearly and point callers back to `Query()`

## Where to use?

- Desktop/local small applications
- Application file format
- Small web sites/applications
- One database **per account/user** data store

## Plugins

- A GUI viewer tool: https://github.com/falahati/LiteDBViewer (v4)
- A GUI editor tool: https://github.com/JosefNemec/LiteDbExplorer (v4)
- Lucene.NET directory: https://github.com/sheryever/LiteDBDirectory
- LINQPad support: https://github.com/adospace/litedbpad
- F# Support: https://github.com/Zaid-Ajaj/LiteDB.FSharp (v4)
- UltraLiteDB (for Unity or IOT): https://github.com/rejemy/UltraLiteDB
- OneBella - cross platform (windows, macos, linux) GUI tool : https://github.com/namigop/OneBella
- LiteDB.Migration: Framework that makes schema migrations easier: https://github.com/JKamsker/LiteDB.Migration/

## Changelog

Change details for each release are documented in the [release notes](https://github.com/mbdavid/LiteDB/releases).

## Code Signing

LiteDB is digitally signed courtesy of [SignPath](https://www.signpath.io)

<a href="https://www.signpath.io">
    <img src="https://about.signpath.io/assets/signpath-logo.svg" width="150">
</a>

## License

[MIT](http://opensource.org/licenses/MIT)
