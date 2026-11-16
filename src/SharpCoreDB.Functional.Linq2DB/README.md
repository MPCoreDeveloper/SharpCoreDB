# SharpCoreDB.Functional.Linq2DB

linq2db adapter for `SharpCoreDB.Functional`.

**Package:** `SharpCoreDB.Functional.Linq2DB` (versioned with SharpCoreDB — see NuGet for the current release)

---

## Overview

**SharpCoreDB.Functional.Linq2DB** is the linq2db adapter for SharpCoreDB's functional stack. It delivers compile-time type safety, expressive LINQ, zero change-tracking overhead, and railway-oriented return types (`Option<T>`, `Fin<T>`, `Seq<T>`). 

**Perfect for:**
- ✅ High-throughput AI/agentic workflows and GraphRAG
- ✅ Compile-time safe queries with full IntelliSense
- ✅ Low memory/CPU overhead (no EF-style tracking)
- ✅ Bulk operations via `BulkCopyAsync`
- ✅ .NET 11 / C# 15
- ✅ Railway-oriented error handling (`Fin<T>`)

---

## Installation

```bash
dotnet add package SharpCoreDB.Functional.Linq2DB
```

---

## Quick Start

### 1. Basic LINQ (raw linq2db)

```csharp
using SharpCoreDB.Functional.Linq2DB;
using LinqToDB;

var conn = new SharpCoreDBDataConnection("Data Source=./app.scdb");

var users = await conn.GetTable<User>()
    .Where(u => u.IsActive)
    .OrderBy(u => u.Email)
    .ToListAsync();
```

### 2. Functional API (recommended — Option/Fin/Seq)

```csharp
var db = new FunctionalLinq2DbContext(conn);

// Option<T> for safe lookups
var user = await db.FindOneAsync<User>(u => u.Email == "test@example.com");

// Seq<T> for collections
var activeUsers = await db.QueryAsync<User>(u => u.IsActive);

// Fin<T> for mutations (railway-oriented)
var insertResult = await db.InsertBatchAsync(new[] { user1, user2 }); // uses BulkCopyAsync internally

insertResult.IfSucc(count => Console.WriteLine($"Inserted {count} rows"))
            .IfFail(err => Console.WriteLine($"Error: {err.Message}"));
```

**Connection note**: `Data Source=…` and `Path=…` are both SharpCoreDB connection strings
(`Data Source=./app.scdb;Password=…`). See *Storage and encryption* below for what the adapter does with them.

---

## Storage and encryption

The adapter is the SQLite **dialect**, not the SQLite **engine**:

- linq2db generates the SQL with its SQLite dialect — SharpCoreDB is SQLite-compatible at the SQL level.
- The SQL is executed on a `SharpCoreDBConnection` (from `SharpCoreDB.Data.Provider`), so the rows are stored
  by SharpCoreDB in its own file format and encrypted with AES-256-GCM when the connection string carries a
  `Password`.
- No SQLite file is created and no native SQLite library ships with the package. Its one SQLite dependency is
  `Microsoft.Data.Sqlite.Core` — the managed library only — because linq2db's SQLite dialect reflects over its types
  when it builds its provider; nothing ever opens a connection with it. `BulkCopy` uses `MultipleRows`, so the
  provider-specific native bulk-copy path is never taken. (Up to 2.1.4 the dependency was the `Microsoft.Data.Sqlite`
  metapackage, which also shipped the native `e_sqlite3` binaries for every platform.)

This changed in 2.1.3: before that, `SharpCoreDBDataConnection` passed the connection string straight to
`ProviderName.SQLite`, so a password was refused (*"the native SQLite library 'e_sqlite3' doesn't support
encryption"*) and the data landed in an unencrypted `SQLite format 3` file that SharpCoreDB cannot read.

`GetUnderlyingConnection()` returns the SharpCoreDB ADO.NET connection, which is the way to run DDL or
SharpCoreDB-specific SQL that linq2db does not model.

---

## Dependency injection, connections and mapping schemas

```csharp
// One SharpCoreDBDataConnection per scope, each with its own SharpCoreDB connection,
// and a FunctionalLinq2DbContext over it (a registration of your own is kept).
services.AddSharpCoreDBLinq2Db("Data Source=./app.scdb;Password=…");

// Or configure the options yourself. DataOptions is immutable, so the callback returns them.
services.AddSharpCoreDBLinq2Db(options => options
    .UseSharpCoreDB("Data Source=./app.scdb;Password=…")
    .UseAdditionalMappingSchema(myFluentMappings));
```

- The options built by `CreateOptions`, `UseSharpCoreDB` and `AddSharpCoreDBLinq2Db` hold a connection
  **factory**: every `SharpCoreDBDataConnection` opens its own `SharpCoreDBConnection` and disposes it with itself,
  so options can be reused safely. (Up to 2.1.4 they held one live connection that every DI scope shared.)
- Your own mapping schema — fluent mappings, attribute overrides, converters — is **kept** and takes priority over
  the SharpCoreDB type mappings. Add it with `UseAdditionalMappingSchema` (or `UseMappingSchema` before
  `UseSharpCoreDB`). `SharpCoreDBMappingSchema.ConfigureCustomMapping` is obsolete: it changed the process-wide
  `Instance` shared by every connection.
- `FunctionalLinq2DbContext.GetByIdAsync<T>(keys)` looks the row up by the entity's mapped primary key — one value
  per key column, in `PrimaryKey(order)` order — and throws `ArgumentException` on a key-count mismatch.

---

## Errors, cancellation and transactions

- Methods returning `Option<T>` or `Seq<T>` (`FindOneAsync`, `GetByIdAsync`, `QueryAsync`, `GetAllAsync`) **throw**
  when the query fails.
- Methods returning `Fin<T>` (inserts, updates, deletes, `CountAsync`, `ExistsAsync`, `TransactionAsync`) capture the
  failure as `Fail`.
- Cancellation always surfaces as `OperationCanceledException` — whether the token was cancelled before the call or
  during it — never as a `Fail`.

```csharp
// The action gets the context and the token, so the work is visibly bound to the transaction.
// linq2db's own methods on ctx.Connection throw on failure, which is what rolls the transaction back.
var result = await db.TransactionAsync(async (ctx, ct) =>
{
    await ctx.Connection.InsertAsync(order, token: ct);
    await ctx.Connection.BulkCopyAsync(lines, ct);
    return order.Id;
}, cancellationToken);
```

A throwing action rolls the transaction back and its own error is returned as `Fail` — the rollback does not use the
caller's token, so a cancelled token cannot replace the action's error. The context's own `Fin`-returning methods do
**not** throw: a `Fail` from `ctx.InsertAsync(…)` inside the action is a value, and the transaction still commits
unless the action throws when it sees one (`if (insert.IsFail) throw new InvalidOperationException(…)`) or uses the
throwing linq2db calls on `ctx.Connection`, as above.

`InsertBatchAsync(entities, options)` takes linq2db `BulkCopyOptions` (batch size, parameters, notifications);
`BulkCopyType.ProviderSpecific` is mapped to `MultipleRows`. A row count above `int.MaxValue` is a `Fail`, not a
wrapped number.

---

## Key Features

- **BulkCopy support** in `InsertBatchAsync` for high-speed ingestion (GraphRAG, analytics)
- **Type mapping** (ULID as text, GUID as hyphenated `D`-format text, dates read and written culture-invariantly,
  bool→int for SQLite compat)
- **Transaction support** with `TransactionAsync` (commit on success, rollback on failure)
- **DeleteWhereAsync**, `CountAsync`, `ExistsAsync`, `GetAllAsync`
- **Seamless with SharpCoreDB.Functional** (`Prelude`, `Option`, `Fin`, `Seq`, `Error`)
- Works alongside `SharpCoreDB.Functional.Dapper` and EF Core functional wrapper

## Documentation & Links

- Full API in `FunctionalLinq2DbContext.cs` and `Extensions.cs`
- Test coverage: `tests/SharpCoreDB.Functional.Linq2DB.Tests/`
- 🔗 [SharpCoreDB GitHub](https://github.com/MPCoreDeveloper/SharpCoreDB)

---

**License:** MIT
