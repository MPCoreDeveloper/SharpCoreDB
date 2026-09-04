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
- A production-focused stack validated by a **1,777-test core suite** (0 failed; plus CQRS, VectorSearch, EF Core and Linq2DB suites) and **backward compatibility**

> **Current release: v2.0.0.2 (2026-09-04)** — the v2.x engine line: fixed-width record layout is the
> default for new PK tables, UPDATE/DELETE run in-place with commit-time tombstones, and a reopen
> data-integrity hardening batch closed silent-data-loss edge cases (empty-value overflow reload,
> single-file fixed-width arena markers, legacy 1.x delete-after-update purge).
>
> **Performance (fair-PK, median-of-3, tuned harness):** UPDATE **~163–245K ops/s**, DELETE
> **~106–172K ops/s**, INSERT **~125–152K ops/s**, READ **~72–110K ops/s** (SQLite reference:
> UPDATE ~270–315K, DELETE ~353–420K, INSERT ~186–190K, READ ~95–107K). Honest default-config
> caveat: the pure default config runs ~1.3–1.6x slower because the file-level wrapper still pays
> AES work while per-record at-rest encryption is off (`NoEncryptMode` root cause, P3d). Full
> report: [`docs/2.0.0.2_WHAT_CHANGED.md`](docs/2.0.0.2_WHAT_CHANGED.md) and
> [`docs/benchmarks/default-config-pk.md`](docs/benchmarks/default-config-pk.md).

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

## What's new in v2.x (current engine line)

The v1.x benchmark gap (point reads/updates/deletes **16–52x behind SQLite**) is closed. The
numbers below are the **fair-PK harness (median-of-3, tuned config, ascending-PK batches)** from
`docs/2.0.0.2_WHAT_CHANGED.md`:

| Operation (ops/s) | **SharpCoreDB v2.x (Columnar fixed-width)** | SQLite | gap |
|---|---:|---:|---:|
| **UPDATE** | **~163–245K** | ~270–315K | ~0.8–1.7x of SQLite |
| **DELETE** | **~106–172K** | ~353–420K | ~2.1–3.5x |
| **INSERT** | **~125–152K** | ~186–190K | competitive |
| **READ** | **~72–110K** | ~95–107K | competitive |

Honest notes: these are ascending-PK batches on the fixed-width Columnar fast path — other query
shapes fall back to correct-but-slower generic paths. The **pure default config** runs ~1.3–1.6x
slower than `NoEncryptMode=true` (documented `NoEncryptMode` root cause, see
`docs/benchmarks/default-config-pk.md`). SQLite comparison, honest guidance and the API ladder:
**[Performance Guide](docs/manual/performance.md)**. Roadmap:
**[`docs/performance/V2_PERFORMANCE_PLAN.md`](docs/performance/V2_PERFORMANCE_PLAN.md)**.

Headline changes:
- ⚡ **Fixed-width record layout (default for new PK tables)** — constant-stride records with an
  out-of-line `.ovf` overflow arena; UPDATE/DELETE are **in-place overwrites** with commit-time
  tombstone markers (no full-file rewrite)
- ⚡ **Single-pass contiguous UPDATE/DELETE (B8/B9)** — strictly ascending `pk = literal` batches
  resolved as ONE contiguous range with decode verification (no per-key B-tree search)
- ⚡ **Reopen data-integrity hardening (2.0.0.2)** — empty-value overflow reload, single-file
  fixed-width arena markers, and legacy 1.x delete-after-update purge (`ReopenRoundTripMatrixTests`)
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
- ✅ validated by a **1,777-test core suite (0 failed)** plus CQRS (64), VectorSearch (143), EF Core (116) and Functional.Linq2DB (24)
- 🛡️ **Envelope encryption + full at-rest metadata encryption** — password-based per-file DEK (PBKDF2-HMAC-SHA256), encrypted block registry / FSM / WAL, key & password rotation APIs (#341 follow-on)
- 🗜️ **Block-level Brotli/GZip compression** for single-file (`.scdb`) storage — transparent, per-block, applied before encryption / removed after decryption (#344)
- ⚙️ **Configurable metadata region sizing** — `FsmSizePages` / `BlockRegistrySizePages` / `TableDirectorySizePages` remove the 512 MB single-file ceiling; byte-based file extension (~10 MB regardless of `PageSize`) (#345)

## Quick Start (under 30 seconds)

### 1) Embedded mode

```bash
dotnet add package SharpCoreDB --version 2.0.0.2
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

> ⚡ v2.x hot paths: read point lookups with `db.FindByPrimaryKey("Users", key: 1)` (Direct API,
> no SQL parsing) and batch-load with `db.InsertBatch("Users", rows)`. See the [Performance Guide](docs/manual/performance.md).

### 2) Server mode (gRPC-first)

```bash
dotnet run --project src/SharpCoreDB.Server -c Release
```

Health endpoint: `https://localhost:8443/health`  
gRPC endpoint: `https://localhost:5001`

Install client/server packages:

```bash
dotnet add package SharpCoreDB.Server --version 2.0.0.2
dotnet add package SharpCoreDB.Client --version 2.0.0.2
```


---

## Previous releases (v1.9.x)

- **v1.9.6** — fixed the critical `WHERE col IN (...)` regression (Issue #339); SonarCloud quality-gate onboarding; 2,404 tests passing.
- **v1.9.5** — full version synchronization across all packages; token-aware named-parameter binding fix (Issue #336); server parameter forwarding fix (Issue #337); standards-compliant Crockford Base32 ULID encoding; new `SharpCoreDB.Functional.Linq2DB` adapter.
- **v1.9.1** — EF Core provider with Guid-keyed entity CRUD + reliable two-query relationship materialization; 22/22 EF Core integration tests; runnable EF Core demo; version alignment across 25+ packages.

> Full history: [`docs/CHANGELOG.md`](docs/CHANGELOG.md)

---

## Available NuGet packages (v2.0.0.2)

```bash
# Core
dotnet add package SharpCoreDB --version 2.0.0.2

# Server/client
dotnet add package SharpCoreDB.Server --version 2.0.0.2
dotnet add package SharpCoreDB.Client --version 2.0.0.2

# Engines and extensions
dotnet add package SharpCoreDB.Analytics --version 2.0.0.2
dotnet add package SharpCoreDB.VectorSearch --version 2.0.0.2
dotnet add package SharpCoreDB.Graph --version 2.0.0.2
dotnet add package SharpCoreDB.Graph.Advanced --version 2.0.0.2
dotnet add package SharpCoreDB.Distributed --version 2.0.0.2
dotnet add package SharpCoreDB.Provider.Sync --version 2.0.0.2
dotnet add package SharpCoreDB.EntityFrameworkCore --version 2.0.0.2
dotnet add package SharpCoreDB.Extensions --version 2.0.0.2

# Optional architecture packages
dotnet add package SharpCoreDB.EventSourcing --version 2.0.0.2
dotnet add package SharpCoreDB.Projections --version 2.0.0.2
dotnet add package SharpCoreDB.CQRS --version 2.0.0.2

# Optional functional adapters
dotnet add package SharpCoreDB.Functional --version 2.0.0.2
dotnet add package SharpCoreDB.Functional.Dapper --version 2.0.0.2
dotnet add package SharpCoreDB.Functional.EntityFrameworkCore --version 2.0.0.2
dotnet add package SharpCoreDB.Functional.Linq2DB --version 2.0.0.2
```

---

## Important documentation links

- **Manual (start here):** [`docs/manual/README.md`](docs/manual/README.md) — full feature manual with index
- **Performance Guide:** [`docs/manual/performance.md`](docs/manual/performance.md) — when SharpCoreDB is fastest + measured v2.x numbers
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




