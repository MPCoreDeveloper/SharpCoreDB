# 1. Quick Start

> ⚡ For maximum throughput, jump straight to the [Performance Guide](performance.md) after this
> page. The quick start covers the idiomatic v2.0 API surface.

---

## 1.1 Install

```bash
dotnet add package SharpCoreDB --version 2.0.0
```

The engine targets **.NET 10** and **C# 14**. No native dependencies, no external processes —
a single managed assembly (optional RID-specific packages for x64/ARM64).

## 1.2 Open a database

```csharp
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB;

var services = new ServiceCollection();
services.AddSharpCoreDB();
using var provider = services.BuildServiceProvider();

var factory = provider.GetRequiredService<DatabaseFactory>();

// Directory-based, encrypted database (AES-256-GCM)
using var db = factory.Create(@"C:\data\appdb", masterPassword: "s3cret!");

// Or single-file encrypted database (.scdb)
// using var db = factory.Create(@"C:\data\appdb.scdb", masterPassword: "s3cret!");
```

**No encryption?** Pass a `DatabaseConfig` with `NoEncryptMode = true` (development, or when the
volume is already encrypted at the OS level).

```csharp
var config = new DatabaseConfig { NoEncryptMode = true };
using var db = factory.Create(@"C:\data\devdb", masterPassword: "x", config: config);
```

## 1.3 First CRUD

```csharp
db.ExecuteSQL("CREATE TABLE customers (id INTEGER PRIMARY KEY, name TEXT NOT NULL, email TEXT, age INTEGER, score REAL)");

// Batch insert — the fastest way to load data (see Performance Guide)
var batch = new List<Dictionary<string, object>>();
for (int i = 0; i < 10_000; i++)
{
    batch.Add(new Dictionary<string, object>
    {
        ["name"]  = $"Customer{i}",
        ["email"] = $"c{i}@example.com",
        ["age"]   = 20 + i % 60,
        ["score"] = i * 0.1,
    });
}
db.InsertBatch("customers", batch);
db.Flush();

// Parameterized query (Dictionary path — full SQL support)
var rows = db.ExecuteQuery(
    "SELECT * FROM customers WHERE name = @name",
    new Dictionary<string, object?> { ["@name"] = "Customer42" });

// ⚡ Zero-allocation read path (v2.0) — fastest SQL reads, no per-row Dictionary
foreach (var row in db.ExecuteQueryStruct(
    "SELECT * FROM customers WHERE name = @name",
    new Dictionary<string, object?> { ["@name"] = "Customer42" }))
{
    int age = row.GetValue<int>("age");   // StructRow.GetValue<T>(columnName)
    string name = row.GetValue<string>("name");
}

// ⚡ Or the Direct API — zero SQL parsing, fastest point reads:
var direct = db.FindByPrimaryKey("customers", key: 42);   // Dictionary<string,object>? or null
var byEmail = db.FindByIndex("customers", "email", "c42@example.com");

// Update / delete
db.ExecuteSQL("UPDATE customers SET score = 9.5 WHERE name = 'Customer42'");
db.ExecuteSQL("DELETE FROM customers WHERE age > 70");
db.Flush();
```

## 1.4 What you get for free

| Feature | Notes |
|---------|-------|
| SQL engine | `SELECT/INSERT/UPDATE/DELETE`, `CREATE TABLE/INDEX/VIEW/TRIGGER/PROCEDURE` |
| Aggregates & window functions | 100+ aggregates, `ROW_NUMBER`, `RANK`, `LAG`, `LEAD`, … |
| Indexes | hash (O(1) point), B-tree (ranges), expression, partial, unique |
| Encryption | AES-256-GCM at rest, encrypted single-file or per-record |
| Vector search | HNSW + SIMD, cosine/euclidean/dot |
| Columnar analytics | SIMD aggregates (SUM/AVG/MIN/MAX on millions of rows in ms) |
| Time-series | buckets, downsampling, retention, Gorilla/XOR codecs |
| GraphRAG | graph algorithms + hybrid retrieval |
| Server mode | gRPC/REST, JWT, RBAC, multitenancy, observability |

## 1.5 Next steps

- See the [Feature Overview](overview.md) for the complete picture.
- See the [Performance Guide](performance.md) to learn **when SharpCoreDB is fastest** and how
  to write queries that get the best numbers.
- See [docs/INDEX.md](../INDEX.md) for the full documentation hub.
