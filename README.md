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
</div>

---

SharpCoreDB is for .NET teams that want **SQLite-like simplicity**, **enterprise-grade security**, and **server-scale capabilities** without leaving the .NET ecosystem.

Use it when you need:
- Fast embedded storage with **AES-256-GCM encryption** and ACID guarantees
- A secure network database via **gRPC (HTTP/2 + HTTP/3)**
- Built-in **vector search**, **advanced analytics**, and **GraphRAG/graph algorithms**
- A production-focused stack validated by **2,000+ tests** and **backward compatibility**

> Full documentation: **`docs/INDEX.md`**

---

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
dotnet add package SharpCoreDB --version 1.8.0
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
dotnet add package SharpCoreDB.Server --version 1.8.0
dotnet add package SharpCoreDB.Client --version 1.8.0
```

---

## v1.8.0 highlights

- Synchronized package release across the full ecosystem (`1.8.0`)
- **Auto-ROWID**: tables without a `PRIMARY KEY` now get a hidden `_rowid` (ULID) column - SQLite-compatible rowid pattern
- **GRAPH_RAG SQL clause**: new top-level `GRAPH_RAG` SELECT syntax with `LIMIT`, `WITH SCORE > X`, `WITH CONTEXT`, and `TOP_K`
- **OPTIONALLY projection mode**: new `OPTIONALLY` keyword enables `Option<T>` mapping in ADO.NET readers
- **IS SOME / IS NONE predicates**: new null-safety predicates supported in parser and runtime
- **SIMD hot-loop optimization**: all 16 columnar aggregate methods use `Vector256.LoadUnsafe` - tighter codegen on AVX2
- **Major Viewer update**: multi-tab query editor, typed table designer (includes ULID/GUID), 6-language UI (EN/DE/FR/ES/IT/NL), server connection support
- **FluentMigrator reliability fixes**:
  - default SQLite-compatible generator + processor alignment in `AddSharpCoreDBFluentMigrator()`
  - no `UndefinedDefaultValue` leakage in generated SQL
  - no duplicate `PRIMARY KEY` generation for version table creation
  - SQLite-incompatible DDL now fails fast with clear `NotSupportedException` in default compatibility mode
- **Single-file parser compatibility fixes**: quoted identifiers in `CREATE TABLE` / `DROP TABLE` / table-level PK paths are covered and validated
- `Microsoft.Extensions.Logging.Abstractions` updated to **10.0.7** across all packages
- **2,000+ tests passing**, **zero breaking changes intended**, **100% backward compatible**

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

- **2,000+ tests passing**
- **100% backward compatible** across the v1.8.0 release line
- Zero breaking changes intended from v1.5.0 to v1.8.0

For deep technical details (audit reports, threat model, runbooks, compatibility matrices), use the docs hub: `docs/INDEX.md`.

---

## Available NuGet packages (v1.8.0)

```bash
# Core
dotnet add package SharpCoreDB --version 1.8.0

# Server/client
dotnet add package SharpCoreDB.Server --version 1.8.0
dotnet add package SharpCoreDB.Client --version 1.8.0

# Engines and extensions
dotnet add package SharpCoreDB.Analytics --version 1.8.0
dotnet add package SharpCoreDB.VectorSearch --version 1.8.0
dotnet add package SharpCoreDB.Graph --version 1.8.0
dotnet add package SharpCoreDB.Graph.Advanced --version 1.8.0
dotnet add package SharpCoreDB.Distributed --version 1.8.0
dotnet add package SharpCoreDB.Provider.Sync --version 1.8.0
dotnet add package SharpCoreDB.EntityFrameworkCore --version 1.8.0
dotnet add package SharpCoreDB.Extensions --version 1.8.0

# Optional architecture packages
dotnet add package SharpCoreDB.EventSourcing --version 1.8.0
dotnet add package SharpCoreDB.Projections --version 1.8.0
dotnet add package SharpCoreDB.CQRS --version 1.8.0

# Optional functional adapters
dotnet add package SharpCoreDB.Functional --version 1.8.0
dotnet add package SharpCoreDB.Functional.Dapper --version 1.8.0
dotnet add package SharpCoreDB.Functional.EntityFrameworkCore --version 1.8.0
```

---

## What’s new in v1.8.0

- **Auto-ROWID support**: hidden `_rowid` (ULID) on tables without an explicit primary key - mirrors SQLite rowid semantics
- **GRAPH_RAG SQL clause**: first-class `GRAPH_RAG` SELECT syntax for graph-augmented retrieval pipelines
- **OPTIONALLY** and **IS SOME / IS NONE**: new SQL keywords for `Option<T>`-aware null-safety patterns
- **SIMD columnar engine**: `Vector256.LoadUnsafe` across all 16 aggregate hot paths - eliminates Span allocation in AVX2 loops
- **SharpCoreDB.Viewer** major update: Avalonia UI revamp with multi-tab editor, typed table designer (ULID/GUID), multilingual UI (EN/DE/FR/ES/IT/NL), server-mode connection
- **FluentMigrator**: `AddSharpCoreDBFluentMigrator()` defaults both generator and processor to SQLite-compatible mode
- `Microsoft.Extensions.Logging.Abstractions` bumped to **10.0.7** for all packages
- Bug fixes: `IS NULL/IS NOT NULL` unification, parser `COALESCE()` support, LINQ Convert/ConvertChecked, PAGE_BASED mixed-predicate filtering

---

## Important documentation links

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



