# SharpCoreDB.Functional.Linq2DB

linq2db adapter for `SharpCoreDB.Functional`.

**Version:** `v1.9.5` (production-ready)  
**Package:** `SharpCoreDB.Functional.Linq2DB`

---

## Overview

**SharpCoreDB.Functional.Linq2DB** is the **production-ready** linq2db adapter for SharpCoreDB's functional stack. It delivers compile-time type safety, expressive LINQ, zero change-tracking overhead, and railway-oriented return types (`Option<T>`, `Fin<T>`, `Seq<T>`). 

**Perfect for:**
- ✅ High-throughput AI/agentic workflows and GraphRAG
- ✅ Compile-time safe queries with full IntelliSense
- ✅ Low memory/CPU overhead (no EF-style tracking)
- ✅ Bulk operations via `BulkCopyAsync`
- ✅ Native AOT and .NET 11/C# 15
- ✅ Railway-oriented error handling (`Fin<T>`)

---

## Installation

```bash
dotnet add package SharpCoreDB.Functional.Linq2DB --version 2.0.0.0
```

---

## Quick Start (Production)

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
- No SQLite file is created and the native `e_sqlite3` library is never opened. Microsoft.Data.Sqlite remains a
  package dependency because linq2db's SQLite dialect resolves it for its provider plumbing; `BulkCopy` is set to
  `MultipleRows` so the provider-specific native bulk-copy path is never taken.

This changed in 2.1.3: before that, `SharpCoreDBDataConnection` passed the connection string straight to
`ProviderName.SQLite`, so a password was refused (*"the native SQLite library 'e_sqlite3' doesn't support
encryption"*) and the data landed in an unencrypted `SQLite format 3` file that SharpCoreDB cannot read.

`GetUnderlyingConnection()` returns the SharpCoreDB ADO.NET connection, which is the way to run DDL or
SharpCoreDB-specific SQL that linq2db does not model.

---

## Key Production Features

- **BulkCopy support** in `InsertBatchAsync` for high-speed ingestion (GraphRAG, analytics)
- **Full type mapping** (ULID as string, GUID as compact string, DateTime ISO, bool→int for SQLite compat)
- **Transaction support** with `TransactionAsync<TResult>`
- **DeleteWhereAsync**, `CountAsync`, `ExistsAsync`, `GetAllAsync`
- **Seamless with SharpCoreDB.Functional** (`Prelude`, `Option`, `Fin`, `Seq`, `Error`)
- Works alongside `SharpCoreDB.Functional.Dapper` and EF Core functional wrapper

## Documentation & Links

- Full API in `FunctionalLinq2DbContext.cs` and `Extensions.cs`
- Test coverage: `tests/SharpCoreDB.Functional.Linq2DB.Tests/`
- 🔗 [SharpCoreDB GitHub](https://github.com/MPCoreDeveloper/SharpCoreDB)

---

**License:** MIT
