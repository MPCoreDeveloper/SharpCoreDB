# SharpCoreDB v2.0.0-preview.2 - Performance-First Database Engine

**High-Performance Embedded AND Networked Database for .NET 10**

SharpCoreDB is a modern, encrypted, file-based database engine with SQL support, built for production applications. Now available as both embedded database and network server.

[![SonarCloud Quality Gate](https://img.shields.io/sonar/quality_gate/MPCoreDeveloper_SharpCoreDB?server=https%3A%2F%2Fsonarcloud.io&logo=sonarcloud)](https://sonarcloud.io/dashboard?id=MPCoreDeveloper_SharpCoreDB)

## What's New in v2.0.0-preview.2

### 🚀 Performance-first engine
- v2.0 closes the v1.x benchmark gap: point reads **beat SQLite** on the default engine, batch INSERTs
  are at parity, and the WP10–WP13 storage-engine work narrowed single-row UPDATE/DELETE to ~1–2x SQLite.
- Zero-allocation struct-row SQL reads via `ExecuteQueryStruct`.

### 🔒 Envelope encryption + full at-rest encryption
- **Password-based key model**: `DatabaseOptions.EncryptionPassword` creates a random per-file
  data-encryption-key (DEK) wrapped by a PBKDF2-HMAC-SHA256-derived key (per-file salt, OWASP-2024
  iteration default). Raw `EncryptionKey` mode remains fully supported.
- **Full at-rest metadata encryption** (beyond issue #341's block data): the block registry,
  free-space map and WAL are encrypted too (`EncryptionMode = 2`), so block/table names, offsets,
  lengths and allocation patterns are not visible in plaintext on disk.
- **Key & password rotation**: `IDatabase.ChangeEncryptionPasswordAsync(...)` re-wraps the DEK (O(1),
  no data rewrite); `IDatabase.RotateEncryptionKeyAsync(...)` fully re-keys the database via a
  crash-safe temp-file swap.
- Wrong key/password now fails loudly at open (GCM authentication failure).

### 📦 Block-level compression (Issue #344)
- Transparent per-block **Brotli/GZip** compression for single-file (`.scdb`) storage — applied before
  encryption on write, removed after decryption on read.
- Compressed and uncompressed blocks can coexist in one file; defaults to `None` (backward compatible).
- New options: `DatabaseOptions.BlockCompression` and `CompressionThreshold`.

### ⚙️ Configurable metadata sizing (Issue #345)
- The FSM, Block Registry and Table Directory are no longer hard-coded to 4 pages:
  `FsmSizePages`, `BlockRegistrySizePages` and `TableDirectorySizePages` support databases beyond 512 MB.
- Minimum file extension is now byte-based (~10 MB) regardless of `PageSize`.

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

**Functional Programming (NEW in v1.9.6):**
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
dotnet add package SharpCoreDB --version 2.0.0
```

**Optional companion packages introduced or highlighted in v1.9.6:**

```bash
dotnet add package SharpCoreDB.Functional --version 2.0.0
dotnet add package SharpCoreDB.Functional.Dapper --version 2.0.0
dotnet add package SharpCoreDB.Functional.EntityFrameworkCore --version 2.0.0
dotnet add package SharpCoreDB.Graph.Advanced --version 2.0.0
```

## 🔄 Upgrade from v1.3.5

**100% backward compatible** - No breaking changes!

```bash
dotnet add package SharpCoreDB --version 2.0.0
```

Your existing databases work as-is. New metadata is automatically compressed.

## 🐛 Bug Reporting

Found an issue? Report it on GitHub: https://github.com/MPCoreDeveloper/SharpCoreDB/issues

## 📄 License

MIT License - See LICENSE file in the repository

## 🙏 Contributing

We welcome contributions! Check the repository for contribution guidelines.

---

**Latest Version:** 2.0.0-preview.2 (August 30, 2026)  
**Target:** .NET 10 / C# 14  
**Tests:** 1,600+ (100% passing)  
**Status:** ✅ Preview 2 — performance-first v2.0 line




