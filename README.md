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
A production-focused stack validated by **2,223 tests** and **backward compatibility**

> Full documentation: **`docs/INDEX.md`**

---

> **🖥️ Looking for the graphical UI?** The `SharpCoreDB.Viewer` / `SharpCoreDB.WebViewer` projects have
> moved to the standalone repo **[MPCoreDeveloper/SCDMS](https://github.com/MPCoreDeveloper/SCDMS)**.
> This repository now contains only the engine, providers, and server. See [docs/SCDMS.md](docs/SCDMS.md).

## Why SharpCoreDB?

- **One stack, two deployment models**: embedded and server mode
- **Performance-first design**: SIMD acceleration, memory pooling, optimized query paths
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

## Quick Start (under 30 seconds)

### 1) Embedded mode

```bash
dotnet add package SharpCoreDB --version 1.9.6
```

```csharp
using SharpCoreDB;

var db = new Database("app.scdb");
db.ExecuteSQL("CREATE TABLE Users (Id INT, Name TEXT)");
db.ExecuteBatchSQL([
    "INSERT INTO Users VALUES (1, 'Ada')",
    "INSERT INTO Users VALUES (2, 'Linus')"
]);
db.Flush();
db.ForceSave();
```

### 2) Server mode (gRPC-first)

```bash
dotnet run --project src/SharpCoreDB.Server -c Release
```

Health endpoint: `https://localhost:8443/health`  
gRPC endpoint: `https://localhost:5001`

Install client/server packages:

```bash
dotnet add package SharpCoreDB.Server --version 1.9.6
dotnet add package SharpCoreDB.Client --version 1.9.6
```

---

## v1.9.1 highlights (previous release)

- **Entity Framework Core Provider**: Full Guid-keyed entity CRUD support with reliable two-query pattern for relationships
- **New EF Core Demo**: Runnable console app (`Examples/SharpCoreDB.EFCoreCrudDemo`) demonstrating complete company/vacancy CRUD workflow
- Version standardization to 1.9.1 across all packages, NuGet metadata, and documentation
- EF Core provider fully validated: 22/22 integration tests passing (CRUD, transactions, relationships, Guid entities)
- Build and core test suite confirmed stable for release
- Continued synchronization of optional modules (Event Sourcing, CQRS, Projections, Analytics)
  - no `UndefinedDefaultValue` leakage in generated SQL
  - no duplicate `PRIMARY KEY` generation for version table creation
  - SQLite-incompatible DDL now fails fast with clear `NotSupportedException` in default compatibility mode
- **Single-file parser compatibility fixes**: quoted identifiers in `CREATE TABLE` / `DROP TABLE` / table-level PK paths are covered and validated
- `Microsoft.Extensions.Logging.Abstractions` updated to **10.0.7** across all packages
- **2,000+ tests passing**, **zero breaking changes intended**, **100% backward compatible**

---

## v1.9.6 release (current)

- **Fixed a critical `WHERE col IN (...)` regression** (Issue #339): `IN` / `NOT IN` filters were silently
  ignored (returning **ALL rows**) because the predicate evaluators did not recognize the `IN` operator. Fixed
  in every evaluation path — single-file (`.scdb`) and directory storage modes, string and non-string columns,
  literal and parameterized lists. Single-file parameterized queries (`IN (@p0, @p1)`) also no longer throw
  "Missing required parameter" (parameter-key normalization now routes through `ParameterBinder.Bind`).
- **Now continuously analyzed on SonarCloud** 🎉: quality gate, bug, vulnerability and code-smell tracking are
  part of the normal workflow from this release onward — see the [SonarCloud dashboard](https://sonarcloud.io/dashboard?id=MPCoreDeveloper_SharpCoreDB).
- **Tests**: full suite passes — **2,404 tests, 0 failures** across all 15 test projects, plus 16 JS and 17 Python
  tests — including new regression tests for `IN` / `NOT IN` (literal + parameterized) in `WhereInRegressionTests`
  and `WhereInRegressionEfCoreTests`.
- **100% backward compatible** with the v1.9.5 release line.

---

## v1.9.5 release

- **Full version synchronization to 1.9.5** across all packages (core, Server, Client, Analytics, VectorSearch, Graph, EF Core provider, Identity, EventSourcing, Projections, CQRS, Functional family including the new `SharpCoreDB.Functional.Linq2DB`, and more), internal project references, PackageReleaseNotes, and test projects.
- **Bug fixes**:
  - **Parameterized INSERT bound values to the wrong columns** (Issue #336): named-parameter binding is now token-aware, so a parameter name that is a prefix of another (e.g. `@t` vs `@tid`) no longer corrupts the longer placeholder.
  - **SharpCoreDB.Server dropped `request.Parameters`** (Issue #337): parameters are now forwarded on the gRPC `ExecuteQuery`/`ExecuteNonQuery`, the binary (PostgreSQL) protocol and the WebSocket handler.
  - **ULID encoding is now standards-compliant**: the Crockford Base32 encoder/decoder follows the ULID specification (first character carries 3 significant bits), so generated ULIDs are interchangeable with Python/Java/Go implementations; decoding rejects values above the 128-bit range and timestamps above 2^48−1 are rejected. *(Note: ULIDs generated before 1.9.5 are not spec-compliant; this mirrors the `posseth.global.ulid` v2.0.0 fix.)* **Upgrade path:** use `Ulid.FromLegacy(string)` / `Ulid.TryFromLegacy(...)` to convert pre-1.9.5 ULIDs to the current encoding — the 128-bit value (timestamp + randomness) is preserved exactly, so existing `_rowid` values and ULID columns can be migrated one-to-one. Legacy databases are detected automatically: `Database.NeedsLegacyUlidMigration()` reports whether a database was created before 1.9.5 (the ULID encoding generation is recorded in the database metadata for directory mode and in the file-header feature flags for single-file `.scdb` mode), and `Database.MigrateLegacyUlids()` rewrites every ULID value in every ULID-typed column of every table (including hidden `_rowid` primary keys) and permanently marks the database as migrated. Run it once right after upgrading, before writing new rows.
- **UI moved to SCDMS**: the graphical UI (formerly `SharpCoreDB.Viewer` / `SharpCoreDB.WebViewer`) has moved to the standalone repo [MPCoreDeveloper/SCDMS](https://github.com/MPCoreDeveloper/SCDMS); this repository now contains only the engine, providers and server. See `docs/SCDMS.md`.
- **Documentation is now English-only** and updated to the 1.9.5 state (root README, package READMEs/NuGet.README.md files, USAGE guides, script client docs for Python/JS, hub files and the changelog).
- **NuGet dependencies updated** to their latest stable versions (Aspire 13.5.3, AWSSDK.Core, BLite, MessagePack, EF Core InMemory 10.0.11, Aspire.AppHost.Sdk 13.5.3); unused Avalonia-related package pins from the removed viewer were deleted.
- **Tests**: the full test suite passes — **2,491 tests, 0 failures** — including new regression tests for parameter binding, server parameter pass-through, ULID specification compatibility and the automatic legacy-ULID migration.
- Changes from 1.9.1 to 1.9.5 (including the above bug resolutions, dependency updates, and documentation overhaul) are detailed in this section and `docs/CHANGELOG.md`.
- **100% backward compatible** with the exception of ULID strings generated before 1.9.5 (see the ULID note above).

---

## Performance snapshot

Recent benchmark improvements after parser/lifecycle refactors:

| Benchmark | Before | After | Improvement |
|---|---:|---:|---:|
| Single-File SELECT (Unencrypted) | 4.01 ms | **1.81 ms** | **55% faster** |
| Single-File SELECT (Encrypted) | 2.74 ms | **1.57 ms** | **43% faster** |
| AppendOnly UPDATE | 143.42 ms | **70.36 ms** | **51% faster** |
| Dir Encrypted UPDATE | 9.16 ms | **7.91 ms** | **14% faster** |

Additional SIMD optimization: columnar aggregate paths now use `Vector256.LoadUnsafe` to reduce per-iteration overhead in AVX2 hot loops.

Full benchmark details: `docs/BENCHMARK_RESULTS.md`

---

## Complete feature set

### Core database engine (embedded)

- Single-file encrypted database with **AES-256-GCM**
- SQL support with advanced query optimization
- ACID transactions with WAL
- B-tree and hash indexing
- Full-text search
- SIMD-accelerated operations (including `Vector256.LoadUnsafe` optimizations)
- Memory pooling and JIT-oriented performance optimizations
- Metadata durability improvements (flush + reopen reliability)

### Network server (`SharpCoreDB.Server`)

- **Primary protocol:** gRPC over HTTPS (HTTP/2 + HTTP/3)
- Secondary protocols: Binary TCP handler, HTTPS REST API, WebSocket streaming
- Multi-database hosting with system databases
- Security stack: TLS 1.2+, JWT auth, RBAC (Admin/Writer/Reader), optional mTLS
- Connection pooling (1000+ concurrent connections)
- Health checks and Prometheus-compatible metrics
- Graceful shutdown and production deployment support
- Deployment options: Docker/Docker Compose, Windows Service, Linux systemd, macOS launchd

### Clients and SDKs

- .NET client library (`SharpCoreDB.Client`, ADO.NET-style)
- JavaScript/TypeScript SDK (npm)
- Python client (`PySharpDB`) with partial transport parity in progress

### Analytics and query capabilities

- 100+ aggregate functions (COUNT, SUM, AVG, STDDEV, VARIANCE, PERCENTILE, CORRELATION)
- Window functions (ROW_NUMBER, RANK, DENSE_RANK, LAG, LEAD)
- Proven high-throughput analytics performance in benchmark suite

### Vector search

- HNSW indexing with SIMD acceleration
- Production-tested with large vector workloads (10M+ vectors)
- Performance-oriented semantic retrieval workflows

### Graph and GraphRAG

- Graph traversal: BFS, DFS, bidirectional search
- A* pathfinding improvements
- `ROWREF` data type support for graph edges
- `GRAPH_TRAVERSE()` SQL function integration
- Advanced graph analytics via `SharpCoreDB.Graph.Advanced`:
  - Community detection (Louvain, Label Propagation, Connected Components)
  - Centrality metrics (Degree, Betweenness, Closeness, Eigenvector, Clustering)
  - Subgraph analysis (K-core, clique, triangle detection)
  - Graph-aware semantic ranking and profiling helpers

### Distributed and sync features

- Multi-master replication with vector clocks
- Distributed transactions with 2PC protocol
- Dotmim.Sync integration for cloud/data sync scenarios

### Event Sourcing, Projections, and CQRS (optional packages)

- `SharpCoreDB.EventSourcing`:
  - Append-only per-stream event storage
  - Global ordered event feed
  - In-memory and persistent event store implementations
  - Snapshot persistence and snapshot-aware aggregate loading
  - Optional snapshot policy helpers
- `SharpCoreDB.Projections`:
  - Projection registration and runner scaffolding
  - Checkpoint persistence
  - OpenTelemetry-ready projection metrics
- `SharpCoreDB.CQRS`:
  - Command/handler abstractions
  - Aggregate root support
  - Outbox primitives with retry/dead-letter workflow support

### Quality and compatibility

- **2,223 tests passing**
- **100% backward compatible** across the v1.9.5 release line
- Zero breaking changes intended from v1.5.0 to v1.9.6

For deep technical details (audit reports, threat model, runbooks, compatibility matrices), use the docs hub: `docs/INDEX.md`.

---

## Available NuGet packages (v1.9.6)

```bash
# Core
dotnet add package SharpCoreDB --version 1.9.6

# Server/client
dotnet add package SharpCoreDB.Server --version 1.9.6
dotnet add package SharpCoreDB.Client --version 1.9.6

# Engines and extensions
dotnet add package SharpCoreDB.Analytics --version 1.9.6
dotnet add package SharpCoreDB.VectorSearch --version 1.9.6
dotnet add package SharpCoreDB.Graph --version 1.9.6
dotnet add package SharpCoreDB.Graph.Advanced --version 1.9.6
dotnet add package SharpCoreDB.Distributed --version 1.9.6
dotnet add package SharpCoreDB.Provider.Sync --version 1.9.6
dotnet add package SharpCoreDB.EntityFrameworkCore --version 1.9.6
dotnet add package SharpCoreDB.Extensions --version 1.9.6

# Optional architecture packages
dotnet add package SharpCoreDB.EventSourcing --version 1.9.6
dotnet add package SharpCoreDB.Projections --version 1.9.6
dotnet add package SharpCoreDB.CQRS --version 1.9.6

# Optional functional adapters
dotnet add package SharpCoreDB.Functional --version 1.9.6
dotnet add package SharpCoreDB.Functional.Dapper --version 1.9.6
dotnet add package SharpCoreDB.Functional.EntityFrameworkCore --version 1.9.6
dotnet add package SharpCoreDB.Functional.Linq2DB --version 1.9.6
```

---

## What’s new in v1.9.1 (prior release)

- **Entity Framework Core Provider**: Full Guid-keyed entity support with stable CRUD (insert/update/select/delete) and reliable two-query relationship materialization pattern for `Company`/`Vacancy` style scenarios
- **New runnable EF Core demo**: `Examples/SharpCoreDB.EFCoreCrudDemo` – complete console application demonstrating end-to-end CRUD on the companies/vacancies seed dataset using the recommended repository pattern
- **Complete v1.9.1 release alignment**: Every project `<Version>`, every internal `PackageReference`, and every `PackageReleaseNotes` updated to 1.9.1 across 25+ packages so NuGet pack/publish produces correct artifacts
- **Documentation standardization**: Root `README.md` + all 25 component `README.md` files updated to current release with correct package examples and highlights
- EF Core integration tests: 22/22 passing (including full end-to-end seed CRUD with Guid primary keys and relationships)
- All builds and test packages validated (`SharpCoreDB.1.9.1.nupkg` successfully produced)
- Zero breaking changes – 100% backward compatible with previous 1.9.x line

> For changes in the current 1.9.6 release (critical WHERE IN regression fix, SonarCloud onboarding, version bump), see the v1.9.6 section above and docs/CHANGELOG.md.

---

## Important documentation links

- **Roadmap:** [`ROADMAP.md`](./ROADMAP.md) — shipped features, near-term plans, and long-term vision
- Documentation hub: `docs/INDEX.md`
- Project docs index: `docs/README.md`
- Feature matrix: `docs/FEATURE_MATRIX_v1.7.2.md`
- Server docs: `docs/server/README.md`
- Server quick start: `docs/server/QUICKSTART.md`
- GraphRAG docs: `docs/graphrag/00_START_HERE.md`
- SQL dialect extensions: `docs/sql/SQL_DIALECT_EXTENSIONS_v1.7.2.md`
- Migration docs: `docs/migration/README.md`
- Single-file SQL support and limits: `docs/storage/SINGLE_FILE_SQL_LIMITATIONS.md`
- EF Core provider docs: `src/SharpCoreDB.EntityFrameworkCore/README.md`, `src/SharpCoreDB.EntityFrameworkCore/USAGE.md`
- **New in v1.9.5**: `SharpCoreDB.Functional.Linq2DB` — production-ready linq2db adapter with `Option<T>`/`Fin<T>`/`Seq<T>` APIs, `BulkCopyAsync` batching, full type mapping (ULID/GUID/DateTime), and compile-time safe LINQ queries. Ideal for high-throughput AI/agentic and GraphRAG workloads. See `src/SharpCoreDB.Functional.Linq2DB/README.md`.
- Optional architecture packages: `src/SharpCoreDB.EventSourcing/README.md`, `src/SharpCoreDB.Projections/README.md`, `src/SharpCoreDB.CQRS/README.md`
- Package publish/readme guidance: `nuget/README.md`, `NuGet.README.md`

---

## Contributing

Contributions are welcome. Please open an issue for ideas, bug reports, and feature proposals, or submit a PR directly.

---

## License

This project is licensed under the MIT License. See `LICENSE` for details.

---

**Made with ❤️ for the .NET community**




