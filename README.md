<div align="center">
  <img src="https://raw.githubusercontent.com/MPCoreDeveloper/SharpCoreDB/master/SharpCoreDB.jpg" alt="SharpCoreDB Logo" width="200"/>
  
  # SharpCoreDB
  
  **High-Performance Embedded & Network Database for .NET 10**
  
  [![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
  [![.NET](https://img.shields.io/badge/.NET-10.0-blue.svg)](https://dotnet.microsoft.com/download)
  [![NuGet](https://img.shields.io/badge/NuGet-1.5.0-blue.svg)](https://www.nuget.org/packages/SharpCoreDB)
  [![Build](https://img.shields.io/badge/Build-✅_Passing-brightgreen.svg)](https://github.com/MPCoreDeveloper/SharpCoreDB)
  [![Tests](https://img.shields.io/badge/Tests-1490+_Passing-brightgreen.svg)](https://github.com/MPCoreDeveloper/SharpCoreDB)
  [![C#](https://img.shields.io/badge/C%23-14-purple.svg)](https://learn.microsoft.com/en-us/dotnet/csharp/)
</div>

---

## 📌 **Current Status — v1.5.0 (March 8, 2026)**

### ✅ **Production-Ready: ALL Phase 1-11 Features Complete (100%)**

**SharpCoreDB v1.5.0 delivers critical bug fixes, 60-80% metadata compression, enterprise-scale distributed features, and a fully functional network database server.**

#### 🎉 **Major Milestone: All Core Features + Server Complete**

**Phase 1-11 (100% Complete):** SharpCoreDB is now a **fully-featured, production-ready embedded AND networked database** with advanced analytics, vector search, graph algorithms, distributed capabilities, and server mode.

**Latest Achievement:** 🚀 **Phase 11 - SharpCoreDB.Server COMPLETE (100%)**  
SharpCoreDB has been successfully transformed from embedded database into a **network-accessible database server** with gRPC, Binary Protocol, HTTP REST API, and WebSocket streaming support.

**Server Features Delivered:**
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
- ✅ Python client (PySharpDB - published to PyPI)
- ✅ JavaScript/TypeScript SDK (published to npm)
- ✅ Docker + Docker Compose deployment
- ✅ Cross-platform installers (Windows Service, Linux systemd)
- ✅ Complete server documentation and examples

**See documentation:** `docs/INDEX.md`

### ✅ Previously Known Limitation — Resolved

- `SingleFileDatabase.ExecuteCompiled` with parameterized plans previously hung due to an infinite loop in the SQL lexer (`?` parameter placeholder). Fixed: FastSqlLexer, EnhancedSqlParser, QueryCompiler. Full `IAsyncDisposable` lifecycle also implemented.

### 📈 Performance Improvements (March 14, 2026)

After the `IAsyncDisposable` lifecycle refactor and SQL lexer/parser fixes, benchmarks show **zero regressions** and significant gains:

| Benchmark | Before | After | Improvement |
|-----------|-------:|------:|:------------|
| Single-File SELECT (Unencrypted) | 4.01 ms | **1.81 ms** | **55% faster** |
| Single-File SELECT (Encrypted) | 2.74 ms | **1.57 ms** | **43% faster** |
| AppendOnly UPDATE | 143.42 ms | **70.36 ms** | **51% faster** |
| Dir Encrypted UPDATE | 9.16 ms | **7.91 ms** | **14% faster** |

All other benchmarks (25 total) remain stable. Full results: [`docs/BENCHMARK_RESULTS.md`](docs/BENCHMARK_RESULTS.md)

### 📚 Documentation Policy

- Canonical documentation entry points are `docs/INDEX.md` and `docs/README.md`.
- Topic-level canonical entry points are maintained under:
  - `docs/server/README.md`
  - `docs/scdb/README_INDEX.md`
  - `docs/graphrag/00_START_HERE.md`
- Obsolete phase-status, kickoff, completion, and superseded planning documents are periodically removed.
- Historical snapshots are not treated as canonical product documentation.

---

#### 🎯 Latest Release (v1.4.0 → v1.5.0)

- **🐛 Critical Bug Fixes**
  - Database reopen edge case fixed (graceful empty JSON handling)
  - Immediate metadata flush ensures durability
  - Enhanced error messages with JSON preview
  
- **📦 New Features**
  - Brotli compression for JSON metadata (60-80% size reduction)
  - Backward compatible format detection
  - Zero breaking changes
  
- **📊 Quality Metrics**
  - **1,490+ tests** (was 850+ in v1.3.5)
  - **100% backward compatible**
  - **All 11 phases production-ready**

---

#### 🚀 Complete Feature Set (Phases 1-11)

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
- SIMD-accelerated operations
- Memory pooling and JIT optimizations

---

#### 📦 Installation

```bash
# Core database
dotnet add package SharpCoreDB --version 1.5.0

# Server mode (network database server)
dotnet add package SharpCoreDB.Server --version 1.5.0
dotnet add package SharpCoreDB.Client --version 1.5.0

# Distributed features
dotnet add package SharpCoreDB.Distributed --version 1.5.0

# Analytics engine
dotnet add package SharpCoreDB.Analytics --version 1.5.0

# Vector search
dotnet add package SharpCoreDB.VectorSearch --version 1.5.0

# Sync integration
dotnet add package SharpCoreDB.Provider.Sync --version 1.5.0

# Graph algorithms
dotnet add package SharpCoreDB.Graph --version 1.5.0

# Optional integrations
dotnet add package SharpCoreDB.EntityFrameworkCore --version 1.5.0
dotnet add package SharpCoreDB.Extensions --version 1.5.0
```

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


