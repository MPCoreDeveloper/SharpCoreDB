# SharpCoreDB v1.7.2 - Production Database Engine

**High-Performance Embedded AND Networked Database for .NET 10**

SharpCoreDB is a modern, encrypted, file-based database engine with SQL support, built for production applications. Now available as both embedded database and network server.


## What's New in v1.7.2

### v1.7.1 to v1.7.2

- **Auto-ROWID**: Tables without an explicit `PRIMARY KEY` get a hidden `_rowid` column (ULID). SQLite rowid semantics.
- **GRAPH_RAG SQL clause**: New `GRAPH_RAG` SELECT clause with `LIMIT`, `WITH SCORE > X`, `WITH CONTEXT`, `TOP_K`.
- **OPTIONALLY projection mode**: `OPTIONALLY` keyword enables `Option<T>` mapping in ADO.NET readers (SharpCoreDB.Functional).
- **IS SOME / IS NONE predicates**: Null-safety predicates in parser and runtime evaluators.
- **SIMD optimization**: All 16 columnar aggregate methods use `Vector256.LoadUnsafe` (eliminates Span allocation in AVX2 loops).
- **Viewer major update**: Multi-tab editor, typed table designer (ULID/GUID), 6-language UI (EN/DE/FR/ES/IT/NL), server connection.
- **FluentMigrator alignment**: `AddSharpCoreDBFluentMigrator()` defaults both generator and processor to SQLite-compatible mode.
- `Microsoft.Extensions.Logging.Abstractions` bumped to **10.0.7**.
- Bug fixes: IS NULL/IS NOT NULL unification, COALESCE() in SELECT, LINQ Convert, German locale matching, PAGE_BASED mixed-predicate scan.

### 🔄 Synchronized 1.7.2 Release
- **Unified Package Versioning** - Core, server, analytics, graph, event sourcing, projections, CQRS, and functional companion packages now ship on the same `1.7.2` release line
- **Documentation Refresh** - Installation guidance and package docs were updated to match the current feature and fix set
- **Optional Package Maturity** - Event Sourcing, Projections, and CQRS docs now highlight durable snapshots, checkpointing, persistent outbox support, retry handling, and hosted workers

### 🎉 Phase 11 Complete: Network Database Server
- **SharpCoreDB.Server** - Full network database server with gRPC, Binary TCP, HTTPS REST, WebSocket
- **Multi-Language Clients** - .NET, Python (PyPI), JavaScript/TypeScript (npm)
- **Enterprise Security** - JWT + Mutual TLS + RBAC
- **Cross-Platform Deploy** - Docker, Windows Service, Linux systemd

### 🐛 Critical Bug Fixes
- **Database Reopen:** Fixed edge case where closing and immediately reopening a database would fail
- **Metadata Handling:** Graceful empty JSON handling for new databases
- **Durability:** Immediate metadata flush ensures persistence on disk

### 📦 New Features
- **Brotli Compression:** 60-80% smaller metadata files with zero CPU overhead
- **Backward Compatible:** Auto-detects compressed vs raw JSON format
- **Enterprise Distributed:** Phase 10 complete with sync, replication, transactions

## 🚀 Key Features

✅ **Embedded Database** - Single-file storage, no server required  
✅ **Network Server Mode** - gRPC/HTTP/WebSocket protocols (NEW!)  
✅ **Encrypted** - AES-256-GCM encryption built-in  
✅ **SQL Support** - Full SQL syntax, prepared statements  
✅ **High Performance** - 6.5x faster than SQLite for bulk operations  
✅ **Modern C# 14** - Latest language features, NativeAOT ready  
✅ **Cross-Platform** - Windows, Linux, macOS, ARM64 native  
✅ **Production Ready** - 1,468+ tests, zero known critical bugs  
✅ **Multi-Language** - .NET, Python, JavaScript/TypeScript clients  

## 📊 Performance

- **Bulk Insert (1M rows):** 2.8 seconds
- **Analytics (COUNT 1M):** 682x faster than SQLite
- **Vector Search:** 50-100x faster than SQLite
- **Metadata Compression:** <1ms overhead
- **gRPC Query Latency:** 0.8-1.2ms (p50)
- **Concurrent Connections:** 1000+ (server mode)

## 🔗 Package Ecosystem

This package installs the core database engine. Extensions available:

**Functional Programming (NEW in v1.7.2):**
- **SharpCoreDB.Functional** - Functional façade with `Option<T>`, `Fin<T>`, and `Seq<T>`-style APIs
- **SharpCoreDB.Functional.Dapper** - Functional Dapper adapter module
- **SharpCoreDB.Functional.EntityFrameworkCore** - Functional EF Core adapter module

**Server Mode (NEW!):**
- **SharpCoreDB.Server** - Network database server with gRPC/HTTP/WebSocket
- **SharpCoreDB.Client** - .NET client library (ADO.NET-style)

**Analytics & Search:**
- **SharpCoreDB.Analytics** - 100+ aggregate & window functions (150-680x faster)
- **SharpCoreDB.VectorSearch** - SIMD-accelerated semantic search (50-100x faster)
- **SharpCoreDB.Graph** - Lightweight graph traversal (30-50% faster)

**Distributed Features:**
- **SharpCoreDB.Distributed** - Multi-master replication, sharding, transactions
- **SharpCoreDB.Provider.Sync** - Dotmim.Sync integration (bidirectional sync)

**Optional Integrations:**
- **SharpCoreDB.EntityFrameworkCore** - EF Core provider
- **SharpCoreDB.Extensions** - Helper methods and utilities
- **SharpCoreDB.Serilog.Sinks** - Database logging sink

## 🌐 Multi-Language Support

**Python Client (PyPI):**
```bash
pip install pysharpcoredb
```

**JavaScript/TypeScript (npm):**
```bash
npm install @sharpcoredb/client
```

## 📚 Documentation

**Full docs:** https://github.com/MPCoreDeveloper/SharpCoreDB/blob/master/docs/INDEX.md

**Server Quick Start:** https://github.com/MPCoreDeveloper/SharpCoreDB/blob/master/docs/server/QUICKSTART.md

**Canonical package docs:**
- [Core documentation index](https://github.com/MPCoreDeveloper/SharpCoreDB/blob/master/docs/README.md)
- [Event Sourcing package guide](https://github.com/MPCoreDeveloper/SharpCoreDB/blob/master/src/SharpCoreDB.EventSourcing/README.md)
- [Projections package guide](https://github.com/MPCoreDeveloper/SharpCoreDB/blob/master/src/SharpCoreDB.Projections/README.md)
- [CQRS package guide](https://github.com/MPCoreDeveloper/SharpCoreDB/blob/master/docs/cqrs/README.md)

## 💻 Quick Example

```csharp
using SharpCoreDB;

// Create database
var factory = new DatabaseFactory();
var db = factory.Create("myapp.scdb", "master-password");

// Execute SQL
db.ExecuteSQL("CREATE TABLE users (id INT PRIMARY KEY, name TEXT)");
db.ExecuteSQL("INSERT INTO users VALUES (1, 'Alice')");

// Query data
var results = db.ExecuteQuery("SELECT * FROM users WHERE id = 1");
foreach (var row in results)
{
    Console.WriteLine($"{row["id"]}: {row["name"]}");
}

db.Flush(); // Persist to disk
```

## 🏆 Production Features

- **ACID Compliance** - Full transaction support with WAL
- **Backup & Recovery** - Point-in-time recovery, checkpoint management
- **Concurrency** - Thread-safe operations, connection pooling
- **Multi-Tenant** - Row-level security, schema isolation
- **Enterprise Sync** - Bidirectional sync with PostgreSQL, SQL Server, MySQL
- **Monitoring** - Health checks, metrics, performance stats

## 🔒 Security

- AES-256-GCM encryption for sensitive data
- Password-based key derivation (PBKDF2)
- No plaintext passwords or keys in memory
- Audit logging support

## 📈 Performance Optimizations

- Tiered JIT with PGO (1.2-2x improvement)
- SIMD vectorization where applicable
- Memory-mapped I/O for fast reads
- Batched writes for high throughput
- Query plan caching

## 🛠️ Use Cases

- **Time Tracking Apps** - Embedded, encrypted, offline-first
- **Invoicing Systems** - Multi-tenant, backup-friendly
- **AI/RAG Agents** - Vector search, knowledge base
- **IoT/Edge Devices** - ARM64 native, minimal footprint
- **Mobile Apps** - Sync with cloud database
- **Desktop Applications** - Single-file deployment

## 📦 Installation

```bash
dotnet add package SharpCoreDB --version 1.7.2
```

**Optional companion packages introduced or highlighted in v1.7.2:**

```bash
dotnet add package SharpCoreDB.Functional --version 1.7.2
dotnet add package SharpCoreDB.Functional.Dapper --version 1.7.2
dotnet add package SharpCoreDB.Functional.EntityFrameworkCore --version 1.7.2
dotnet add package SharpCoreDB.Graph.Advanced --version 1.7.2
```

## 🔄 Upgrade from v1.3.5

**100% backward compatible** - No breaking changes!

```bash
dotnet add package SharpCoreDB --version 1.7.2
```

Your existing databases work as-is. New metadata is automatically compressed.

## 🐛 Bug Reporting

Found an issue? Report it on GitHub: https://github.com/MPCoreDeveloper/SharpCoreDB/issues

## 📄 License

MIT License - See LICENSE file in the repository

## 🙏 Contributing

We welcome contributions! Check the repository for contribution guidelines.

---

**Latest Version:** 1.7.2 (April 26, 2026)  
**Target:** .NET 10 / C# 14  
**Tests:** 1,468+ (100% passing)  
**Status:** ✅ Production Ready


