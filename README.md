<div align="center">
  <img src="https://raw.githubusercontent.com/MPCoreDeveloper/SharpCoreDB/master/SharpCoreDB.jpg" alt="SharpCoreDB Logo" width="200"/>
  
  # SharpCoreDB
  
  **High-Performance Embedded & Network Database for .NET 10**
  
  [![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
  [![.NET](https://img.shields.io/badge/.NET-10.0-blue.svg)](https://dotnet.microsoft.com/download)
  [![NuGet](https://img.shields.io/badge/NuGet-1.7.0-blue.svg)](https://www.nuget.org/packages/SharpCoreDB)
  [![Build](https://img.shields.io/badge/Build-✅_Passing-brightgreen.svg)](https://github.com/MPCoreDeveloper/SharpCoreDB)
  [![Tests](https://img.shields.io/badge/Tests-1490+_Passing-brightgreen.svg)](https://github.com/MPCoreDeveloper/SharpCoreDB)
  [![C#](https://img.shields.io/badge/C%23-14-purple.svg)](https://learn.microsoft.com/en-us/dotnet/csharp/)
</div>

---

## 📌 **Current Status — v1.7.0 (April 6, 2026)**

### ✅ **Production-Ready Core + Server (.NET) / SDK Parity In Progress**

**SharpCoreDB v1.7.0 is the synchronized package release for the current feature and fix set: all NuGet packages now ship on the same version, package documentation has been refreshed, and optional Event Sourcing, Projections, and CQRS packages remain first-class companions to the core engine.**

#### 🎉 **Major Milestone: Core Features + Server Delivered**

SharpCoreDB provides a **production-ready embedded and networked .NET database stack** with analytics, vector search, graph algorithms, GraphRAG analytics, distributed capabilities, and server mode.

**Latest package milestone:** 🚀 **`SharpCoreDB.Graph.Advanced` delivered**  
Advanced graph analytics and GraphRAG package with community detection, centrality metrics, subgraph analysis, and graph-aware semantic ranking.

**Server Features Delivered (.NET stack):**
- ✅ gRPC protocol (HTTP/2 + HTTP/3, primary protocol)
- ✅ Binary TCP protocol handler
- ✅ HTTPS REST API (DatabaseController)
- ✅ WebSocket streaming protocol (real-time query streaming)
- ✅ JWT authentication + Role-Based Access Control
- ✅ Mutual TLS (certificate-based authentication)
- ✅ Multi-database registry with system databases
- ✅ Connection pooling (1000+ concurrent connections)
- ✅ Health checks & metrics (Prometheus-compatible)
- ✅ .NET Client library (ADO.NET-style)
- ⚠️ Python client (PySharpDB) currently contains partial transport implementations and is tracked as in-progress
- ✅ JavaScript/TypeScript SDK (published to npm)
- ✅ Docker + Docker Compose deployment
- ✅ Cross-platform installers (Windows Service, Linux systemd)
- ✅ Complete server documentation and examples

**Current implementation audit:** `docs/IMPLEMENTATION_AUDIT_v1.7.0.md`  
**Project status:** `docs/PROJECT_STATUS.md`

**See documentation:** `docs/INDEX.md`

**Tool compatibility matrix:** `docs/server/TOOL_COMPATIBILITY_MATRIX_v1.7.0.md`  
**Tool compatibility limitations:** `docs/server/TOOL_COMPATIBILITY_LIMITATIONS_v1.7.0.md`  
**Multi-tenant SaaS reference:** `docs/server/MULTITENANT_SAAS_REFERENCE_v1.7.0.md`  
**Threat model:** `docs/server/MULTITENANT_THREAT_MODEL_v1.7.0.md`  
**Operations runbook:** `docs/server/MULTITENANT_OPERATIONS_RUNBOOK_v1.7.0.md`  
**Reference sample:** `Examples/Server/SharpCoreDB.MultiTenantSaaSSample/`

### ✅ Previously Known Limitation — Resolved

- `SingleFileDatabase.ExecuteCompiled` with parameterized plans previously hung due to an infinite loop in the SQL lexer (`?` parameter placeholder). Fixed: FastSqlLexer, EnhancedSqlParser, QueryCompiler. Full `IAsyncDisposable` lifecycle also implemented.

### 📈 Performance Improvements (March 20, 2026)

After the `IAsyncDisposable` lifecycle refactor and SQL lexer/parser fixes, benchmarks show **zero regressions** and significant gains:

| Benchmark | Before | After | Improvement |
|-----------|-------:|------:|:------------|
| Single-File SELECT (Unencrypted) | 4.01 ms | **1.81 ms** | **55% faster** |
| Single-File SELECT (Encrypted) | 2.74 ms | **1.57 ms** | **43% faster** |
| AppendOnly UPDATE | 143.42 ms | **70.36 ms** | **51% faster** |
| Dir Encrypted UPDATE | 9.16 ms | **7.91 ms** | **14% faster** |

All other benchmarks (25 total) remain stable. Full results: [`docs/BENCHMARK_RESULTS.md`](docs/BENCHMARK_RESULTS.md)

### ⚡ SIMD Columnar Engine Optimization (April 2026)

All 16 columnar SIMD aggregate methods now use `Vector256.LoadUnsafe` instead of `Vector256.Create(data.AsSpan(...))`, eliminating per-iteration `Span<T>` construction and bounds checking overhead in AVX2 hot loops. This applies to both single-threaded and parallel SIMD paths for SUM, MIN, and MAX across `int`, `long`, and `double` column types. The companion performance test (`ColumnStore_Average_10kRecords_Under2ms`) was hardened with multi-iteration best-time measurement to prevent false failures under concurrent test load.

### 📚 Documentation Policy

- Canonical documentation entry points are `docs/INDEX.md` and `docs/README.md`.
- Topic-level canonical entry points are maintained under:
  - `docs/server/README.md`
  - `docs/scdb/README_INDEX.md`
  - `docs/graphrag/00_START_HERE.md`
- Obsolete phase-status, kickoff, completion, and superseded planning documents are periodically removed.
- Historical snapshots are not treated as canonical product documentation.

---

#### 🎯 Latest Release (v1.5.0 → v1.7.0)

### New in v1.7.0 compared to v1.5.0

- **Functional package family introduced**
  - `SharpCoreDB.Functional` adds a functional façade over `Database` / `IDatabase`
  - `SharpCoreDB.Functional.Dapper` adds modular Dapper adapters with `Option<T>`, `Fin<Unit>`, and `Seq<T>`-based APIs
  - `SharpCoreDB.Functional.EntityFrameworkCore` adds modular EF Core adapters with the same functional programming model
  - All functional packages are designed as optional modules and keep production dependencies flowing through transitive NuGet references

- **Advanced graph analytics and GraphRAG package delivered**
  - New `SharpCoreDB.Graph.Advanced` package
  - Community detection, centrality metrics, subgraph analysis, graph-aware ranking, and profiling helpers

- **Event Sourcing / Projections / CQRS package maturity improvements**
  - Optional package guidance now clearly documents durable snapshots, projection checkpointing, persistent outbox workflows, retry handling, dead-letter support, and hosted background workers
  - These capabilities are now presented as first-class companion modules in the documentation and package ecosystem

- **Core engine and packaging improvements**
  - Synchronized `1.7.0` package versioning across the SharpCoreDB ecosystem
  - Refreshed package documentation for the core engine and companion packages
  - Brotli compression for metadata with backward-compatible format detection
  - Immediate metadata flush and reopen-path fixes for improved durability and startup reliability
  - SQL lexer / parser fixes for parameterized compiled-query execution paths

- **Quality and validation**
  - New functional module tests added for core, Dapper, and EF Core adapters
  - Zero breaking changes intended from `1.5.0` to `1.7.0`
  - Workspace and targeted adapter test suites validate the new modules successfully
  
- **📊 Quality Metrics**
  - **1,490+ tests** (was 850+ in v1.3.5)
  - **100% backward compatible**
  - **All 12 phases production-ready**

---

#### 🚀 Complete Feature Set (Phases 1-12)

**Phase 12: Advanced Graph Analytics & GraphRAG** ✅
- `SharpCoreDB.Graph.Advanced` package for advanced graph analytics
- GraphRAG search with semantic + graph-aware ranking
- Community detection: Louvain, Label Propagation, Connected Components
- Centrality metrics: Degree, Betweenness, Closeness, Eigenvector, Clustering
- Subgraph queries: K-core, clique detection, triangle detection
- SQL integration, result caching, and profiling utilities

**Phase 11: SharpCoreDB.Server (Network Database Server)** ✅
- gRPC protocol (HTTP/2 + HTTP/3) - primary, high-performance protocol
- Binary TCP protocol - PostgreSQL wire protocol compatibility
- HTTPS REST API - web browser and simple integration support
- WebSocket streaming - real-time query streaming
- JWT + Mutual TLS authentication
- Role-Based Access Control (Admin/Writer/Reader)
- Multi-database support with system databases
- Connection pooling (1000+ concurrent connections)
- Health checks & Prometheus metrics
- .NET, Python, JavaScript/TypeScript client libraries
- Docker + cross-platform installers (Windows/Linux)
- Complete documentation and examples

**Phase 10: Enterprise Distributed Features** ✅
- Multi-master replication with vector clocks (Phase 10.2)
- Distributed transactions with 2PC protocol (Phase 10.3)
- Dotmim.Sync integration for cloud sync (Phase 10.1)

**Phase 9: Advanced Analytics** ✅
- 100+ aggregate functions (COUNT, SUM, AVG, STDDEV, VARIANCE, PERCENTILE, CORRELATION)
- Window functions (ROW_NUMBER, RANK, DENSE_RANK, LAG, LEAD)
- **150-680x faster than SQLite**

**Phase 8: Vector Search** ✅
- HNSW indexing with SIMD acceleration
- **50-100x faster than SQLite**
- Production-tested with 10M+ vectors

**Phase 6: Graph Algorithms** ✅
- A* pathfinding (30-50% improvement)
- Graph traversal (BFS, DFS, bidirectional search)
- ROWREF data type for graph edges
- GRAPH_TRAVERSE() SQL function

**Phases 1-5: Core Engine** ✅
- Single-file encrypted database (AES-256-GCM)
- SQL support with advanced query optimization
- ACID transactions with WAL
- B-tree and hash indexing
- Full-text search
- SIMD-accelerated operations (`Vector256.LoadUnsafe` optimized)
- Memory pooling and JIT optimizations

---

#### 📦 Installation

```bash
# Core database
dotnet add package SharpCoreDB --version 1.7.0

# Server mode (network database server)
dotnet add package SharpCoreDB.Server --version 1.7.0
dotnet add package SharpCoreDB.Client --version 1.7.0

# Distributed features
dotnet add package SharpCoreDB.Distributed --version 1.7.0

# Analytics engine
dotnet add package SharpCoreDB.Analytics --version 1.7.0

# Vector search
dotnet add package SharpCoreDB.VectorSearch --version 1.7.0

# Sync integration
dotnet add package SharpCoreDB.Provider.Sync --version 1.7.0

# Graph algorithms
dotnet add package SharpCoreDB.Graph --version 1.7.0

# Advanced graph analytics and GraphRAG
dotnet add package SharpCoreDB.Graph.Advanced --version 1.7.0

# Optional integrations
dotnet add package SharpCoreDB.EntityFrameworkCore --version 1.7.0
dotnet add package SharpCoreDB.Extensions --version 1.7.0

# Functional programming adapters (optional packages)
dotnet add package SharpCoreDB.Functional --version 1.7.0
dotnet add package SharpCoreDB.Functional.Dapper --version 1.7.0
dotnet add package SharpCoreDB.Functional.EntityFrameworkCore --version 1.7.0
```

---

## ⚡ Event Sourcing + CQRS Foundation (v1.7.0 / V 1.70)

`SharpCoreDB.EventSourcing` is now available as a dedicated optional package for append-only streams, global ordering, replay, and snapshot persistence in both embedded and server-backed deployments.
`SharpCoreDB.Projections` is available as an optional projection scaffold package for registration, checkpointing, runner primitives, and OpenTelemetry-ready projection metrics.
`SharpCoreDB.CQRS` is available as an optional CQRS scaffold package for command dispatching, aggregate root base support, and outbox primitives.

### Which package should I use?

| Package | Use it for | What it does exactly | What it does not do |
|---------|------------|----------------------|---------------------|
| `SharpCoreDB.EventSourcing` | Persisting and replaying business events | Stores append-only events per stream, exposes a global ordered feed, supports snapshots, aggregate rehydration, and optional upcasting | Does not dispatch commands or run projections |
| `SharpCoreDB.Projections` | Building read models from stored events | Registers projections, runs them inline or in the background, persists checkpoints, and exposes projection metrics | Does not store events or handle command dispatch |
| `SharpCoreDB.CQRS` | Modeling the command side and reliable message publication | Defines commands and handlers, dispatches commands, collects aggregate events, and manages an outbox with retry/dead-letter support | Does not persist event streams or execute projections |

### How the three packages fit together

- Use `SharpCoreDB.EventSourcing` to **store the truth as events**.
- Use `SharpCoreDB.Projections` to **turn those events into read models**.
- Use `SharpCoreDB.CQRS` to **handle commands and publish integration messages reliably**.

### What is available now

- ✅ Append-only per-stream event storage
- ✅ Global ordered event feed
- ✅ In-memory and persistent (`SharpCoreDbEventStore`) implementations
- ✅ Point-in-time reconstruction by stream sequence
- ✅ Snapshot persistence APIs
- ✅ Snapshot-aware aggregate loading (`LoadWithSnapshotAsync`)
- ✅ Optional snapshot policy helpers (`SnapshotPolicy`, `AppendEventsWithSnapshotPolicyAsync`)
- ✅ Optional projection package scaffold (`ProjectionBuilder`, checkpoints, inline runner)
- ✅ Optional OpenTelemetry projection metrics (`OpenTelemetryProjectionMetrics`, meter `SharpCoreDB.Projections`)
- ✅ Optional CQRS scaffold (`ICommand`, `ICommandHandler<TCommand>`, `AggregateRoot`, `IOutboxStore`)

### Current scope

- ES primitives are production-ready.
- CQRS orchestration, projection hosting, and MediatR integration remain optional follow-up packages.

### Quickstart

```csharp
using SharpCoreDB.EventSourcing;

var eventStore = new InMemoryEventStore();
var streamId = new EventStreamId("order-1001");

await eventStore.AppendEventAsync(
    streamId,
    new EventAppendEntry(
        EventType: "OrderCreated",
        Payload: """{"orderId":"order-1001","total":199.99}"""u8.ToArray(),
        Metadata: ReadOnlyMemory<byte>.Empty,
        TimestampUtc: DateTimeOffset.UtcNow));

var loadResult = await eventStore.LoadWithSnapshotAsync(
    streamId,
    fromEvents: static events => OrderAggregate.FromEventStream(events),
    fromSnapshot: static snapshotData => OrderAggregate.FromSnapshot(snapshotData),
    replayFromSnapshot: static (aggregate, events) => aggregate.Replay(events));
```

### ES docs and examples

- Package docs: `src/SharpCoreDB.EventSourcing/README.md`
- In-memory demo: `Examples/EventSourcing/OrderManagement`
- Persistent demo: `Examples/EventSourcing/OrderManagement.PersistentDemo`
- ES tests: `tests/SharpCoreDB.EventSourcing.Tests`

---

## 🌐 Server Mode: Run SharpCoreDB as a Real Network Database Server

SharpCoreDB is no longer only an embedded database. In `Server` mode it can run as a **real multi-database network server** with secure remote access over your LAN, datacenter, or cloud network.

### What you get in Server mode

- **Primary protocol: gRPC (HTTPS, HTTP/2 + HTTP/3)** for high-throughput and streaming scenarios
- **Secondary protocols:** HTTPS REST API and WebSocket streaming
- **Strict security defaults:** TLS 1.2+, JWT auth, optional mTLS, RBAC
- **Multi-database hosting:** system databases + user databases in one server process
- **Production operations:** health checks, metrics, connection pooling, graceful shutdown

### Quick network setup

1. Install packages:
   - `SharpCoreDB.Server`
   - `SharpCoreDB.Client`
2. Configure TLS certificate and server settings in `appsettings.json`.
3. Start the server:

```bash
dotnet run --project src/SharpCoreDB.Server -c Release
```

4. Verify endpoints:
   - Health: `https://localhost:8443/health`
   - gRPC endpoint: `https://localhost:5001`

### Installers and deployment options

- **Windows Service installer:** `installers/windows/install-service.ps1`
- **Linux systemd installer:** `installers/linux/install.sh`
- **macOS launchd installer:** `installers/macos/install.sh`
- **Docker / Docker Compose:** `src/SharpCoreDB.Server/docker-compose.yml`

### Server documentation

- Quick start: `docs/server/QUICKSTART.md`
- Installation and installers: `docs/server/INSTALLATION.md`
- Configuration reference: `docs/server/CONFIGURATION_SCHEMA.md`
- Security hardening: `docs/server/SECURITY.md`
- Client usage: `docs/server/CLIENT_GUIDE.md`

---

## 📖 Documentation

Start here:
- `docs/INDEX.md`
- `docs/README.md`

Server-specific:
- `docs/server/README.md`
- `docs/server/QUICKSTART.md`
- `docs/server/INSTALLATION.md`


