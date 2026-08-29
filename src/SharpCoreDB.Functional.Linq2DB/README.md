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
- ✅ Native AOT and .NET 10/C# 14
- ✅ Railway-oriented error handling (`Fin<T>`)

---

## Installation

```bash
dotnet add package SharpCoreDB.Functional.Linq2DB --version 2.0.0
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

**Connection note**: Use `"Data Source=..."` for full linq2db/SQLite compatibility. SharpCoreDB `"Path=..."` works via the core provider but may require the underlying ADO.NET path for DDL.

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
- See also: `docs/FEATURE_MATRIX.md`, `docs/functional/OPTIONALLY_SQL_OPTION_SUPPORT_v1.7.2.md`, `docs/graphrag/LINQ_API_GUIDE.md`
- 🔗 [SharpCoreDB GitHub](https://github.com/MPCoreDeveloper/SharpCoreDB)

---

**License:** MIT
