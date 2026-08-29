<div align="center">
  <img src="https://raw.githubusercontent.com/MPCoreDeveloper/SharpCoreDB/master/SharpCoreDB.jpg" alt="SharpCoreDB Logo" width="180"/>

# SharpCoreDB

**High-Performance Encrypted Database for .NET 10**  
**Embedded engine + production gRPC server in one ecosystem**

[![GitHub stars](https://img.shields.io/github/stars/MPCoreDeveloper/SharpCoreDB?style=for-the-badge)](https://github.com/MPCoreDeveloper/SharpCoreDB/stargazers)
[![NuGet](https://img.shields.io/nuget/v/SharpCoreDB?style=for-the-badge)](https://www.nuget.org/packages/SharpCoreDB)
[![NuGet downloads](https://img.shields.io/nuget/dt/SharpCoreDB?style=for-the-badge)](https://www.nuget.org/packages/SharpCoreDB)
[![.NET 10](https://img.shields.io/badge/.NET-10-blue?style=for-the-badge)](https://dotnet.microsoft.com/download)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow?style=for-the-badge)](https://opensource.org/licenses/MIT)
[![Platforms](https://img.shields.io/badge/Platforms-Windows%20%7C%20Linux%20%7C%20macOS-informational?style=for-the-badge)](#)
[![Roadmap](https://img.shields.io/badge/Roadmap-View%20Plan-blueviolet?style=for-the-badge)](./ROADMAP.md)
[![SonarCloud Quality Gate](https://img.shields.io/sonar/quality_gate/MPCoreDeveloper_SharpCoreDB?server=https%3A%2F%2Fsonarcloud.io&style=for-the-badge&logo=sonarcloud)](https://sonarcloud.io/dashboard?id=MPCoreDeveloper_SharpCoreDB)
</div>

---

SharpCoreDB is for .NET teams that want **SQLite-like simplicity**, **enterprise-grade security**, and **server-scale capabilities** without leaving the .NET ecosystem.

Use it when you need:
- Fast embedded storage with **AES-256-GCM encryption** and ACID guarantees
- A secure network database via **gRPC (HTTP/2 + HTTP/3)**
- Built-in **vector search**, **advanced analytics**, and **GraphRAG/graph algorithms**
- A production-focused stack validated by **2,412 tests** and **backward compatibility**

> **v2.0 — the performance-first release.** Point reads **beat SQLite** on the default engine, batch INSERTs
> **beat SQLite** on the PageBased engine (**194–206K vs 109K ops/s**), every CRUD operation **beats LiteDB**,
> and the v2.0 storage-engine roadmap (WP10–WP13) narrowed single-row UPDATE/DELETE from ~5–7x behind SQLite
> to **~2.5–4x** (in-place field patches + unified delete core). SIMD columnar analytics run **~682x faster
> than SQLite** on `GROUP BY` SUM, and the engine is **Native AOT-ready**. Full results:
> [docs/manual/performance.md](docs/manual/performance.md).

> Full documentation: **`docs/INDEX.md`** · Manual: **`docs/manual/README.md`** · Performance: **`docs/manual/performance.md`**

---

> **🖥️ Looking for the graphical UI?** The `SharpCoreDB.Viewer` / `SharpCoreDB.WebViewer` projects have
> moved to the standalone repo **[MPCoreDeveloper/SCDMS](https://github.com/MPCoreDeveloper/SCDMS)**.
> This repository now contains only the engine, providers, and server. See [docs/SCDMS.md](docs/SCDMS.md).

## Why SharpCoreDB?

- **One stack, two deployment models**: embedded and server mode
- **Performance-first design**: SIMD acceleration, zero-allocation read paths, optimized query paths
- **Security by default**: TLS 1.2+, JWT, optional mTLS, RBAC, encrypted single-file storage
- **Modern .NET 10-native**: C# 14, optional ecosystem packages, production-ready modules

### Quick comparison

| Capability | SharpCoreDB | LiteDB | SQLite | RavenDB / MongoDB |
|---|---|---|---|---|
| .NET-native embedded experience | ✅ First-class | ✅ | ⚠️ via provider wrappers | ❌ network-first |
| Built-in encrypted single-file DB (AES-256-GCM) | ✅ | ⚠️ limited/variant approaches | ❌ (extensions/custom setup) | ❌ |
| Built-in gRPC server mode in same ecosystem | ✅ | ❌ | ❌ | ⚠️ different server architecture |
| Vector search + GraphRAG tooling | ✅ | ❌ | ⚠️ extension-dependent | ⚠️ feature varies by product/tier |
| Advanced analytics + SIMD focus | ✅ | ⚠️ basic querying | ⚠️ strong SQL, fewer .NET-specific SIMD paths | ⚠️ server-side analytics patterns |
| Optional Event Sourcing / CQRS packages | ✅ | ❌ | ❌ | ⚠️ usually external patterns |

---

## What's new in v2.0 (performance-first)

The benchmark gap that made v1.x **16–52x slower than SQLite** on point reads/updates/deletes is closed.
Latest runs (`tests/benchmarks/SharpCoreDB.Benchmarks.Comparative`, 100K inserts + 10K reads/updates/deletes,
2026-08-29): the default **AppendOnly** engine and the **PageBased** storage-engine roadmap (WP10–WP13):

| Operation (ops/sec, latest runs) | **v2.0 — AppendOnly** | **v2.0 — PageBased** | SQLite | LiteDB |
|---|---|---|---|---|
| **READ — Direct** | **114K** | 34K | 99K | 16K |
| **READ — SQL / StructRow** | 64–93K | 30–36K | 99K | 16K |
| **INSERT — batch (Direct/StructRow)** | 100–103K | **194–206K** | 109–144K | 75K |
| **UPDATE** | 42–50K | **58–65K** | 239–279K | 10–11K |
| **DELETE** | 29–128K | **107–129K** | 317–376K | 13–14K |

🔧 **Storage-engine roadmap (WP10–WP13) head-to-head** (PageBased vs AppendOnly, same API):
INSERT **+89%** (103K→194K Direct), UPDATE **+31–39%** (42–50K→58–65K, in-place field patches),
DELETE **+271%** on the SQL path (29K→107K, unified delete core). The remaining UPDATE/DELETE gap vs SQLite
(**~2.5–4x**) is the tracked v2.1 target in the [V2 performance plan](docs/performance/V2_PERFORMANCE_PLAN.md).

Headline changes:
- ⚡ **Zero-allocation reads** — first-class `ExecuteQueryStruct(sql, params)` API + `VariableLengthSchema` cache
- ⚡ **In-place UPDATE patches (WP11)** — single-row UPDATE overwrites changed fields at cached fixed column offsets
  (no deserialize → re-serialize round trip); growing records relocate across pages with correct PK/hash re-pointing
- ⚡ **Unified DELETE core (WP12)** — one shared delete path with key-only hash index cleanup
- ⚡ **Exact-size row serialization (WP13)** — one allocation per insert/update (no `ArrayPool.Rent` + `ToArray()` double allocation)
- ⚡ **Zero-reparse SELECT fast path** — `SimpleSelectPlan` resolves simple point-lookup plans without re-lexing
- ⚡ **SIMD numeric WHERE filters** — `Vector<T>` batch filtering for Integer/Long columns
- ⚡ **Compiled regexes everywhere** on hot paths; regex-free `NormalizeSql`; cached DI
- ⚡ **Removed hidden `D:\*.log` debug writes** that throttled every SELECT/execute/transaction/INSERT
- 🛡️ **Native AOT readiness** — AOT-safe `TypeConverter`, `Option<T>` reader, `[RequiresDynamicCode]` annotations,
  source-generated DTOs/JSON (`tools/SharpCoreDB.AotSmoke` publishes + runs, exit 0)
- ✅ **2,412 tests / 0 failures** across all 15 test projects; every operation beats LiteDB

Full details, honest guidance on where SQLite still wins, and the API ladder: **[Performance Guide](docs/manual/performance.md)**.
Track the v2.x plan: **[`docs/performance/V2_PERFORMANCE_PLAN.md`](docs/performance/V2_PERFORMANCE_PLAN.md)**.


## Quick Start (under 30 seconds)

### 1) Embedded mode

```bash
dotnet add package SharpCoreDB --version 2.0.0
```

```csharp
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB;

// DI-wired factory (recommended):
var services = new ServiceCollection();
services.AddSharpCoreDB();
using var provider = services.BuildServiceProvider();
var factory = provider.GetRequiredService<DatabaseFactory>();

using var db = factory.Create(@"C:\data\appdb", masterPassword: "s3cret!");
db.ExecuteSQL("CREATE TABLE Users (Id INT, Name TEXT)");
db.ExecuteBatchSQL([
    "INSERT INTO Users VALUES (1, 'Ada')",
    "INSERT INTO Users VALUES (2, 'Linus')"
]);
db.Flush();
```

> ⚡ v2.0 hot paths: read point lookups with `db.FindByPrimaryKey("Users", key: 1)` (Direct API,
> no SQL parsing) and batch-load with `db.InsertBatch("Users", rows)`. See the [Performance Guide](docs/manual/performance.md).

### 2) Server mode (gRPC-first)

```bash
dotnet run --project src/SharpCoreDB.Server -c Release
```

Health endpoint: `https://localhost:8443/health`  
gRPC endpoint: `https://localhost:5001`

Install client/server packages:

```bash
dotnet add package SharpCoreDB.Server --version 2.0.0
dotnet add package SharpCoreDB.Client --version 2.0.0
```


---

## Previous releases (v1.9.x)

- **v1.9.6** — fixed the critical `WHERE col IN (...)` regression (Issue #339); SonarCloud quality-gate onboarding; 2,404 tests passing.
- **v1.9.5** — full version synchronization across all packages; token-aware named-parameter binding fix (Issue #336); server parameter forwarding fix (Issue #337); standards-compliant Crockford Base32 ULID encoding; new `SharpCoreDB.Functional.Linq2DB` adapter.
- **v1.9.1** — EF Core provider with Guid-keyed entity CRUD + reliable two-query relationship materialization; 22/22 EF Core integration tests; runnable EF Core demo; version alignment across 25+ packages.

> Full history: [`docs/CHANGELOG.md`](docs/CHANGELOG.md)

---

## Available NuGet packages (v2.0.0)

```bash
# Core
dotnet add package SharpCoreDB --version 2.0.0

# Server/client
dotnet add package SharpCoreDB.Server --version 2.0.0
dotnet add package SharpCoreDB.Client --version 2.0.0

# Engines and extensions
dotnet add package SharpCoreDB.Analytics --version 2.0.0
dotnet add package SharpCoreDB.VectorSearch --version 2.0.0
dotnet add package SharpCoreDB.Graph --version 2.0.0
dotnet add package SharpCoreDB.Graph.Advanced --version 2.0.0
dotnet add package SharpCoreDB.Distributed --version 2.0.0
dotnet add package SharpCoreDB.Provider.Sync --version 2.0.0
dotnet add package SharpCoreDB.EntityFrameworkCore --version 2.0.0
dotnet add package SharpCoreDB.Extensions --version 2.0.0

# Optional architecture packages
dotnet add package SharpCoreDB.EventSourcing --version 2.0.0
dotnet add package SharpCoreDB.Projections --version 2.0.0
dotnet add package SharpCoreDB.CQRS --version 2.0.0

# Optional functional adapters
dotnet add package SharpCoreDB.Functional --version 2.0.0
dotnet add package SharpCoreDB.Functional.Dapper --version 2.0.0
dotnet add package SharpCoreDB.Functional.EntityFrameworkCore --version 2.0.0
dotnet add package SharpCoreDB.Functional.Linq2DB --version 2.0.0
```

---

## Important documentation links

- **Manual (start here):** [`docs/manual/README.md`](docs/manual/README.md) — full feature manual with index
- **Performance Guide:** [`docs/manual/performance.md`](docs/manual/performance.md) — when SharpCoreDB is fastest + measured v2.0 numbers
- **Roadmap:** [`ROADMAP.md`](./ROADMAP.md) — shipped features, near-term plans, long-term vision
- Documentation hub: `docs/INDEX.md`
- Feature matrix: `docs/FEATURE_MATRIX.md`
- Server docs: `docs/server/README.md` · Server quick start: `docs/server/QUICKSTART.md`
- GraphRAG docs: `docs/graphrag/00_START_HERE.md`
- SQL dialect extensions: `docs/sql/SQL_DIALECT_EXTENSIONS_v1.7.2.md`
- Migration docs: `docs/migration/README.md`
- Single-file SQL support and limits: `docs/storage/SINGLE_FILE_SQL_LIMITATIONS.md`
- EF Core provider docs: `src/SharpCoreDB.EntityFrameworkCore/README.md`, `src/SharpCoreDB.EntityFrameworkCore/USAGE.md`
- Functional adapters: `src/SharpCoreDB.Functional.Linq2DB/README.md` and sibling projects
- Optional architecture packages: `src/SharpCoreDB.EventSourcing/README.md`, `src/SharpCoreDB.Projections/README.md`, `src/SharpCoreDB.CQRS/README.md`
- Package publish/readme guidance: `NuGet.README.md` and per-package `src/<package>/NuGet.README.md`


---

## Contributing

Contributions are welcome. Please open an issue for ideas, bug reports, and feature proposals, or submit a PR directly.

---

## License

This project is licensed under the MIT License. See `LICENSE` for details.

---

**Made with ❤️ for the .NET community**




