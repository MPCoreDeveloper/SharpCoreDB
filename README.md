# SharpCoreDB

A lightweight, encrypted, file-based database engine for .NET 10 with SQL support. Designed for embedded apps like time-tracking, invoicing, and project management.

- License: MIT
- Platforms: .NET 10, C# 14
- Encryption: AES-256-GCM at rest

## Quickstart

Install:

```bash
dotnet add package SharpCoreDB
```

Use:

```csharp
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB;

var services = new ServiceCollection();
services.AddSharpCoreDB();
var provider = services.BuildServiceProvider();
var factory = provider.GetRequiredService<DatabaseFactory>();

using var db = factory.Create("./app_db", "StrongPassword!");
db.ExecuteSQL("CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT)");
db.ExecuteSQL("INSERT INTO users VALUES (1, 'Alice')");
var rows = db.ExecuteQuery("SELECT * FROM users");
```

## Features (concise)

- SQL: CREATE/INSERT/SELECT/UPDATE/DELETE, JOIN, aggregates
- Encryption: AES-256-GCM
- Storage engines: Append-only and Page-based
- WAL, page cache, query cache
- Hash indexes and primary keys
- Columnar analytics with SIMD aggregates
- Dependency Injection integration

## Benchmarks (December 2025)

Environment: Windows 11, Intel i7-10850H, .NET 10, dataset sizes as noted. Results are means over BenchmarkDotNet runs.

### Analytics (10k rows)
- SharpCoreDB Columnar SIMD SUM: 30.48 µs
- SQLite GROUP BY SUM: 593.64 µs (≈19x slower)
- LiteDB GROUP BY SUM: 15,438.98 µs (≈506x slower)

### Insert (100k rows)
- SQLite: 30,270 µs
- LiteDB: 142,402 µs
- SharpCoreDB AppendOnly: 677,065 µs
- SharpCoreDB PageBased: 685,624 µs

### Update (50k updates)
- SQLite: 5,075 µs
- LiteDB: 402,158 µs
- SharpCoreDB AppendOnly: 3,794,480 µs
- SharpCoreDB PageBased: 3,803,935 µs

### Full-scan SELECT (10k rows)
- SQLite: 1,340.57 µs
- SharpCoreDB AppendOnly: 14,340.40 µs
- SharpCoreDB PageBased: 14,527.73 µs
- LiteDB: 14,443.09 µs

### Encrypted (selected)
- Encrypted SELECT (PageBased): 61.06 µs
- Encrypted UPDATE (PageBased, 50k): 403,681 µs
- Encrypted INSERT (10k):
  - AppendOnly: 666,363 µs
  - PageBased: 679,408 µs

Notes:
- Columnar analytics is a clear strength (SIMD-accelerated).
- OLTP paths (inserts/updates/select materialization) currently allocate heavily and are slower than SQLite/LiteDB.

## Recent benchmark harness changes

- Added per-iteration setup for encrypted targets to ensure clean state
- Skipped pre-population for encrypted engines to avoid PK collisions
- INSERT benchmarks compute `startId = MAX(id) + 1` to avoid PK violations across iterations

## How to run benchmarks

```bash
cd SharpCoreDB.Benchmarks
dotnet run -c Release
```

Results are saved to `SharpCoreDB.Benchmarks/BenchmarkDotNet.Artifacts/results/` (HTML/Markdown/CSV/JSON).

## Roadmap (performance focus)

- Reduce allocations in insert/update pipelines (avoid `Dictionary<string, object>` per row)
- Add batched update API and projection/streaming for SELECT
- Buffer reuse along encrypted paths

## Disclaimer

Performance numbers are hardware and dataset dependent. The tables above reflect the latest automated runs in December 2025 and will be updated as optimizations land.
