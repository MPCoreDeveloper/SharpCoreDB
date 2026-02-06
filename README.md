<div align="center">
  <img src="https://raw.githubusercontent.com/MPCoreDeveloper/SharpCoreDB/master/SharpCoreDB.jpg" alt="SharpCoreDB Logo" width="200"/>
  
  # SharpCoreDB
  
  **High-Performance Embedded Database for .NET 10**
  
  [![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
  [![.NET](https://img.shields.io/badge/.NET-10.0-blue.svg)](https://dotnet.microsoft.com/download)
  [![NuGet](https://img.shields.io/badge/NuGet-1.0.0-blue.svg)](https://www.nuget.org/packages/SharpCoreDB)
  [![Build](https://img.shields.io/badge/Build-✅_Passing-brightgreen.svg)](https://github.com/MPCoreDeveloper/SharpCoreDB)
  [![Tests](https://img.shields.io/badge/Tests-772_Passing-brightgreen.svg)](https://github.com/MPCoreDeveloper/SharpCoreDB)
  [![Sponsor](https://img.shields.io/badge/Sponsor-❤️-ea4aaa?logo=githubsponsors&logoColor=white)](https://github.com/sponsors/mpcoredeveloper)
</div>

---

## 📌 **Current Status (February 2026)**

### ✅ **All Phases Complete — Phases 1-8 + DDL Extensions**

| Area | Status |
|------|--------|
| **Phases 1-7** (Core → Query Optimization) | ✅ Complete |
| **Phase 8** (Time-Series: compression, buckets, downsampling) | ✅ Complete |
| **Phase 1.3** (Stored Procedures, Views) | ✅ Complete |
| **Phase 1.4** (Triggers) | ✅ Complete |
| **Build** | ✅ 0 errors |
| **Tests** | ✅ 772 passing, 0 failures |
| **Production LOC** | ~77,700 |

See: [Project Status](docs/PROJECT_STATUS.md)

---

A high-performance, encrypted, embedded database engine for .NET 10 with **B-tree indexes**, **SIMD-accelerated analytics**, and **unlimited row storage**. Pure .NET implementation with enterprise-grade encryption and world-class analytics performance. **Beats SQLite AND LiteDB on INSERT!** 🏆

- **License**: MIT
- **Platform**: .NET 10, C# 14
- **Status**: ✅ **Production Ready — All Phases (1-8) + DDL Extensions Complete**
- **Encryption**: AES-256-GCM at rest (**0% overhead, sometimes faster!** ✅)
- **Analytics**: **28,660x faster** than LiteDB with SIMD vectorization ✅
- **Analytics**: **682x faster** than SQLite with SIMD vectorization ✅
- **INSERT**: **43% faster** than SQLite, **44% faster** than LiteDB! ✅
- **SELECT**: **2.3x faster** than LiteDB for full table scans ✅
- **UPDATE**: **5.4x faster** Single-File mode with batch coalescing ✅ **NEW!**
- **UPDATE**: Competitive with LiteDB (60ms vs 65ms) ✅ **NEW!**
- **B-tree Indexes**: O(log n + k) range scans, ORDER BY, BETWEEN support ✅
- **Row Overflow (Phase 6)**: Planned FILESTREAM support for multi-gigabyte rows

---

## 🚀 Quickstart

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

// Create table with B-tree index
db.ExecuteSQL("CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT, age INTEGER)");
db.ExecuteSQL("CREATE INDEX idx_age ON users(age) USING BTREE");

// Fast inserts
db.ExecuteSQL("INSERT INTO users VALUES (1, 'Alice', 30)");

// Fast queries with batch API
var rows = db.ExecuteQuery("SELECT * FROM users WHERE age > 25");

// Support for large data (>256KB)
var largeData = new byte[10_000_000]; // 10MB
db.ExecuteSQL("INSERT INTO files VALUES (1, @data)");
```

---

## ⭐ Key Features

### ⚡ **Performance Excellence - Beats SQLite AND LiteDB!** 🏆

- **SIMD Analytics**: **28,660x faster** aggregations than LiteDB (1.08µs vs 30.9ms)
- **SIMD Analytics**: **682x faster** than SQLite (1.08µs vs 737µs)
- **INSERT Operations**: **43% faster** than SQLite (3.68ms vs 5.70ms) ✅
- **INSERT Operations**: **44% faster** than LiteDB (3.68ms vs 6.51ms) ✅
- **SELECT Queries**: **2.3x faster** than LiteDB for full table scans
- **UPDATE Operations**: **5.4x faster** Single-File mode (60ms vs 325ms) ✅ **NEW!**
- **UPDATE Memory**: **280x less** allocations (1.9MB vs 540MB) ✅ **NEW!**
- **AVX-512/AVX2/SSE2**: Hardware-accelerated analytics with SIMD vectorization
- **NativeAOT-Ready**: Zero reflection, zero dynamic dispatch, aggressive inlining
- **Memory Efficient**: **52x less memory** than LiteDB for SELECT operations

### 🔒 **Enterprise Security**

- **Native AES-256-GCM**: Hardware-accelerated encryption with **0% overhead (or faster!)**
- **At-Rest Encryption**: All data encrypted on disk
- **Zero Configuration**: Automatic key management
- **GDPR/HIPAA Compliant**: Enterprise-grade security

### 🗄️ **Row Overflow Storage**

- ✅ **SCDB Phase 6 Complete**: No arbitrary size limits (256TB NTFS)
- **3-tier auto-selection**: Inline (4KB) / Overflow (256KB) / FileStream (∞)
- **Orphan detection** and cleanup tooling
- **Production quality** - SHA-256 checksums, atomic operations ✅

### 🏗️ **Modern Architecture**

- **Pure .NET**: No P/Invoke dependencies, fully managed code
- **Multiple Storage Engines**: PageBased (OLTP), Columnar (Analytics), AppendOnly (Logging), SCDB (Block-based)
- **Dual Index Types**: 
  - Hash indexes (O(1) point lookups)
  - B-tree indexes (O(log n) range queries, ORDER BY)
- **Crash Recovery**: REDO-only recovery with WAL
- **Async/Await**: First-class async support throughout
- **DI Integration**: Native Dependency Injection

### 🗃️ **SQL Support**

- **DDL**: CREATE TABLE, DROP TABLE, ALTER TABLE, CREATE INDEX, DROP INDEX
- **DDL**: CREATE/DROP PROCEDURE, CREATE/DROP VIEW, CREATE/DROP TRIGGER
- **DML**: INSERT, SELECT, UPDATE, DELETE, INSERT BATCH, EXEC
- **Queries**: WHERE, ORDER BY, LIMIT, OFFSET, BETWEEN
- **Aggregates**: COUNT, SUM, AVG, MIN, MAX, GROUP BY, HAVING
- **JOINs**: ✅ **INNER, LEFT, RIGHT, FULL OUTER, CROSS**
- **Subqueries**: ✅ **WHERE, FROM, SELECT, IN, EXISTS, Correlated**
- **Stored Procedures**: ✅ **CREATE PROCEDURE, EXEC with IN/OUT/INOUT parameters**
- **Views**: ✅ **CREATE VIEW, CREATE MATERIALIZED VIEW**
- **Triggers**: ✅ **BEFORE/AFTER INSERT/UPDATE/DELETE with NEW/OLD binding**
- **Advanced**: Complex expressions, multi-table queries, query plan caching

## 🧭 RDBMS Feature Status

| Feature | Status | Notes |
|---------|--------|-------|
| Stored Procedures | ✅ Complete | CREATE/DROP PROCEDURE, EXEC with parameter binding |
| Views | ✅ Complete | CREATE VIEW, CREATE MATERIALIZED VIEW, DROP VIEW |
| Triggers | ✅ Complete | BEFORE/AFTER INSERT/UPDATE/DELETE, NEW/OLD binding |
| Time-Series | ✅ Complete | Gorilla/Delta-of-Delta/XOR codecs, buckets, downsampling |

---

## 📚 Documentation

### Project Status
- 📖 [Project Status](docs/PROJECT_STATUS.md)
- 📖 [Changelog](docs/CHANGELOG.md)
- 📖 [Benchmark Results](docs/BENCHMARK_RESULTS.md)

### SCDB Reference
- 📖 [SCDB Implementation Status](docs/scdb/IMPLEMENTATION_STATUS.md)
- 📖 [SCDB Phase 1–6 Complete](docs/scdb/)
- 📖 [Serialization & Storage Guide](docs/serialization/SERIALIZATION_AND_STORAGE_GUIDE.md)

### Guides
- 📖 [Contributing](docs/CONTRIBUTING.md)
- 📖 [Use Cases](docs/UseCases.md)
- 📖 [Embedded & Distributed Guide](docs/SHARPCOREDB_EMBEDDED_DISTRIBUTED_GUIDE.md)
- 📖 [Migration Guide](docs/migration/MIGRATION_GUIDE.md)
- 📖 [Query Plan Cache](docs/QUERY_PLAN_CACHE.md)

---

## 🎯 Getting Started

### Installation
```bash
dotnet add package SharpCoreDB
```

### Basic Usage
```csharp
// Initialize with DI
var services = new ServiceCollection();
services.AddSharpCoreDB();
var factory = services.BuildServiceProvider()
    .GetRequiredService<DatabaseFactory>();

// Create or open database
using var db = factory.Create("./mydb", "password");

// Create schema
db.ExecuteSQL("CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT)");

// Insert data
db.ExecuteSQL("INSERT INTO users VALUES (1, 'Alice')");

// Query data
var rows = db.ExecuteQuery("SELECT * FROM users");

// Planned: FILESTREAM storage for large payloads (SCDB Phase 6)
var largeData = new byte[50_000_000]; // 50MB
db.ExecuteSQL("INSERT INTO data VALUES (@blob)");
```

---

## 🔄 SCDB Architecture Overview

```
┌─────────────────────────────────────────────────────┐
│         SharpCoreDB Application Layer                │
├─────────────────────────────────────────────────────┤
│  Database.Core + Query Executor + Index Manager      │
├─────────────────────────────────────────────────────┤
│  DDL Extensions: Procedures | Views | Triggers       │
├─────────────────────────────────────────────────────┤
│         SCDB Storage Engine (8 Phases - Complete)    │
├────────┬────────┬────────┬────────┬────────┬────────┤
│ Ph.1-3 │ Ph.4   │ Ph.5   │ Ph.6   │ Ph.7   │ Ph.8   │
│Block   │Migrate │Harden  │Row     │Query   │Time    │
│Reg/WAL │ation   │ing     │Overflow│Optimize│Series  │
├────────┴────────┴────────┴────────┴────────┴────────┤
│  IStorage: File persistence with encryption          │
├─────────────────────────────────────────────────────┤
│  Disk: Database file + WAL + Overflow + Blobs        │
└─────────────────────────────────────────────────────┘
```

## ✅ Project Snapshot

| Metric | Value | Status |
|--------|-------|--------|
| **SCDB Phases Complete** | Phases 1-8 + DDL Extensions | ✅ 100% |
| **Phase 8 (Time-Series)** | Codecs, Buckets, Downsampling | ✅ Complete |
| **Stored Procedures / Views / Triggers** | Phase 1.3-1.4 | ✅ Complete |
| **Performance Optimization** | 7,765x faster | ✅ Complete |
| **Advanced SQL** | JOINs + Subqueries + Aggregates | ✅ Complete |
| **Build Status** | 0 errors, 0 warnings | ✅ Success |
| **Tests** | 772 passing, 0 failures | ✅ All Passing |
| **Production LOC** | ~77,700 | ✅ |

---

## 🏆 Highlights

- **INSERT Performance**: 43% faster than SQLite, 44% faster than LiteDB ✅
- **Analytics Speed**: 682x faster than SQLite, 28,660x faster than LiteDB ✅
- **UPDATE Optimization**: 5.4x faster Single-File mode, 280x less memory ✅
- **Encryption**: AES-256-GCM with 0% overhead ✅

---

## 📜 License

MIT License - see LICENSE file for details

---

**Ready to use?** Download from [NuGet](https://www.nuget.org/packages/SharpCoreDB) or clone from [GitHub](https://github.com/MPCoreDeveloper/SharpCoreDB)

**Questions?** See the [docs](docs/) folder or create an [issue](https://github.com/MPCoreDeveloper/SharpCoreDB/issues)

---

**SharpCoreDB** - High-Performance .NET Database for the Modern Era 🚀

