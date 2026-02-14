# SharpCoreDB

**High-Performance Embedded Database for .NET 10**

![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg) ![.NET](https://img.shields.io/badge/.NET-10.0-blue.svg) ![NuGet](https://img.shields.io/badge/NuGet-1.3.0-blue.svg) ![Build](https://img.shields.io/badge/Build-✅_Passing-brightgreen.svg) ![Tests](https://img.shields.io/badge/Tests-800+_Passing-brightgreen.svg)

---

## 📌 Current Status (February 2026)

### ✅ Version 1.3.0 Released - Performance & Quality Improvements

**Latest Release**: v1.3.0 (February 14, 2026)

#### 🚀 Performance Improvements
- **28.6x faster extent allocator**: Replaced O(n log n) sorting with O(log n) SortedSet operations
- **Fragmentation handling**: Time ratio reduced from 309x to 10.81x (10,000 vs 100 extents)
- **Memory efficiency**: Improved allocation patterns for high-fragmentation scenarios

#### ✨ Enhanced Internationalization
- **Strict locale validation**: Rejects placeholder locales (xx-YY, zz-ZZ, iv)
- **Clear error messages**: Guides users to valid IETF locale names (en-US, de-DE, tr-TR)
- **Better collation support**: Validates culture DisplayName and TwoLetterISOLanguageName

#### 🔧 Bug Fixes
- **EF Core Collation**: CREATE TABLE now properly emits COLLATE clauses for UseCollation()
- **Locale Validation**: Non-existent locale names throw InvalidOperationException with helpful messages
- **Storage Engine**: Fixed ExtentAllocator CoalesceInternal for proper chain-merging

#### 📦 Quick Install
```bash
dotnet add package SharpCoreDB --version 1.3.0
```

---

### ✅ All Phases Complete — Phases 1-8 + DDL Extensions

| Area | Status |
|------|--------|
| **Phases 1-7** (Core → Query Optimization) | ✅ Complete |
| **Phase 8** (Time-Series: compression, buckets, downsampling) | ✅ Complete |
| **Phase 1.3** (Stored Procedures, Views) | ✅ Complete |
| **Phase 1.4** (Triggers) | ✅ Complete |
| **Build** | ✅ 0 errors |
| **Tests** | ✅ 800+ passing, 0 failures |
| **Production LOC** | ~78,000+ |

**Full documentation**: [https://github.com/MPCoreDeveloper/SharpCoreDB](https://github.com/MPCoreDeveloper/SharpCoreDB)

---

A high-performance, encrypted, embedded database engine for .NET 10 with **B-tree indexes**, **SIMD-accelerated analytics**, and **unlimited row storage**. Pure .NET implementation with enterprise-grade encryption and world-class analytics performance. **Beats SQLite AND LiteDB on INSERT!** 🏆

**Latest (v1.3.0):** 28.6x extent allocator speedup, enhanced locale validation, EF Core collation support ✅

- **License**: MIT
- **Platform**: .NET 10, C# 14
- **Status**: ✅ **Production Ready — All Phases (1-8) + DDL Extensions Complete**
- **Encryption**: AES-256-GCM at rest (**0% overhead, sometimes faster!** ✅)
- **Analytics**: **28,660x faster** than LiteDB with SIMD vectorization ✅
- **Analytics**: **682x faster** than SQLite with SIMD vectorization ✅
- **INSERT**: **43% faster** than SQLite, **44% faster** than LiteDB! ✅
- **SELECT**: **2.3x faster** than LiteDB for full table scans ✅
- **UPDATE**: **5.4x faster** Single-File mode with batch coalescing ✅
- **B-tree Indexes**: O(log n + k) range scans, ORDER BY, BETWEEN support ✅

---

## 🚀 Quickstart

Install the latest version:

```bash
# Install SharpCoreDB v1.3.0
dotnet add package SharpCoreDB --version 1.3.0

# Or use wildcard for latest
dotnet add package SharpCoreDB
```

Use (Async API - Recommended):

```csharp
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB;

var services = new ServiceCollection();
services.AddSharpCoreDB();
var provider = services.BuildServiceProvider();
var factory = provider.GetRequiredService<DatabaseFactory>();

using var db = factory.Create("./app_db", "StrongPassword!");

// Create table with B-tree index
await db.ExecuteSQLAsync("CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT, age INTEGER)");
await db.ExecuteSQLAsync("CREATE INDEX idx_age ON users(age) USING BTREE");

// Fast inserts with async API (recommended)
await db.ExecuteSQLAsync("INSERT INTO users VALUES (1, 'Alice', 30)");

// Fast queries with async batch API
var rows = await db.ExecuteQueryAsync("SELECT * FROM users WHERE age > 25");

// Support for large data (>256KB)
var largeData = new byte[10_000_000]; // 10MB
await db.ExecuteSQLAsync("INSERT INTO files VALUES (1, @data)");
```

> ⚠️ **API Migration Notice (v1.1.1)**: Legacy synchronous methods (`ExecuteSQL`, `ExecuteQuery`, `Flush`, `ForceSave`) are marked as `[Obsolete]`. Please migrate to async methods (`ExecuteSQLAsync`, `ExecuteQueryAsync`, `FlushAsync`, `ForceSaveAsync`) for better performance, cancellation support, and culture-independent behavior.

---

## ⭐ Key Features

### ⚡ Performance Excellence - Beats SQLite AND LiteDB! 🏆

- **SIMD Analytics**: **28,660x faster** aggregations than LiteDB (1.08µs vs 30.9ms)
- **SIMD Analytics**: **682x faster** than SQLite (1.08µs vs 737µs)
- **INSERT Operations**: **43% faster** than SQLite (3.68ms vs 5.70ms) ✅
- **INSERT Operations**: **44% faster** than LiteDB (3.68ms vs 6.51ms) ✅
- **SELECT Queries**: **2.3x faster** than LiteDB for full table scans
- **UPDATE Operations**: **5.4x faster** Single-File mode (60ms vs 325ms) ✅
- **UPDATE Memory**: **280x less** allocations (1.9MB vs 540MB) ✅
- **AVX-512/AVX2/SSE2**: Hardware-accelerated analytics with SIMD vectorization
- **NativeAOT-Ready**: Zero reflection, zero dynamic dispatch, aggressive inlining
- **Memory Efficient**: **52x less memory** than LiteDB for SELECT operations

### 🔒 Enterprise Security

- **Native AES-256-GCM**: Hardware-accelerated encryption with **0% overhead (or faster!)**
- **At-Rest Encryption**: All data encrypted on disk
- **Zero Configuration**: Automatic key management
- **GDPR/HIPAA Compliant**: Enterprise-grade security

### 🗄️ Row Overflow Storage

- ✅ **SCDB Phase 6 Complete**: No arbitrary size limits (256TB NTFS)
- **3-tier auto-selection**: Inline (4KB) / Overflow (256KB) / FileStream (∞)
- **Orphan detection** and cleanup tooling
- **Production quality** - SHA-256 checksums, atomic operations ✅

### 🏗️ Modern Architecture

- **Pure .NET**: No P/Invoke dependencies, fully managed code
- **Multiple Storage Engines**: PageBased (OLTP), Columnar (Analytics), AppendOnly (Logging), SCDB (Block-based)
- **Dual Index Types**: Hash indexes (O(1) point lookups), B-tree indexes (O(log n) range queries)
- **Crash Recovery**: REDO-only recovery with WAL
- **Async/Await**: First-class async support throughout
- **DI Integration**: Native Dependency Injection

### 🗃️ SQL Support

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

| Feature | Status | Details |
|---------|--------|---------|
| Stored Procedures | ✅ Complete | CREATE/DROP PROCEDURE, EXEC with IN/OUT/INOUT parameters |
| Views | ✅ Complete | CREATE VIEW, CREATE MATERIALIZED VIEW, DROP VIEW |
| Triggers | ✅ Complete | BEFORE/AFTER INSERT/UPDATE/DELETE, NEW/OLD binding |
| Time-Series | ✅ Complete | Gorilla, Delta-of-Delta, XOR codecs, Buckets & Downsampling |
| B-tree Indexes | ✅ Complete | Range queries, ORDER BY, BETWEEN, composite indexes |
| JOINs | ✅ Complete | INNER, LEFT, RIGHT, FULL OUTER, CROSS joins |
| Subqueries | ✅ Complete | Correlated, IN, EXISTS, scalar subqueries |
| Aggregates | ✅ Complete | COUNT, SUM, AVG, MIN, MAX, GROUP BY, HAVING |

---

## ⏱️ Time-Series (Phase 8) Features

SharpCoreDB includes **production-grade time-series support** with industry-standard compression:

### Compression Codecs
- **Gorilla Codec**: XOR-based floating-point compression (~80% space savings)
- **Delta-of-Delta Codec**: Integer timestamp compression with second-order deltas
- **XOR Float Codec**: Specialized IEEE 754 compression for measurements

### Capabilities
- **Automatic Bucketing**: Time-range partitioning for fast queries
- **Downsampling**: Aggregate high-frequency data into lower-resolution series
- **Retention Policies**: Automatic archival and cleanup of old data
- **Time-Range Indexes**: BRIN-style indexes for fast temporal lookups
- **Bloom Filters**: Efficient time-range filtering

### Example Usage
```csharp
// Create time-series table
await db.ExecuteSQLAsync(@"
    CREATE TABLE metrics (
        timestamp BIGINT,
        value REAL,
        tag TEXT,
        PRIMARY KEY (timestamp, tag)
    ) WITH TIMESERIES
");

// Insert compressed time-series data
await db.ExecuteSQLAsync("INSERT INTO metrics VALUES (@ts, @val, @tag)");

// Query with time-range filtering (automatic codec decompression)
var rows = await db.ExecuteQueryAsync(
    "SELECT * FROM metrics WHERE timestamp BETWEEN @start AND @end"
);

// Downsample to 1-minute buckets
var downsampled = await db.ExecuteQueryAsync(@"
    SELECT 
        bucket_timestamp(timestamp, 60000) as bucket,
        AVG(value) as avg_value,
        MAX(value) as max_value
    FROM metrics
    GROUP BY bucket
");
```

---

## 📖 Documentation

For comprehensive documentation, visit:
- **GitHub**: [https://github.com/MPCoreDeveloper/SharpCoreDB](https://github.com/MPCoreDeveloper/SharpCoreDB)
- **User Manual**: [https://github.com/MPCoreDeveloper/SharpCoreDB/blob/master/docs/USER_MANUAL.md](https://github.com/MPCoreDeveloper/SharpCoreDB/blob/master/docs/USER_MANUAL.md)
- **API Reference**: [https://github.com/MPCoreDeveloper/SharpCoreDB/tree/master/docs](https://github.com/MPCoreDeveloper/SharpCoreDB/tree/master/docs)

## 💡 Support

- **GitHub Issues**: [https://github.com/MPCoreDeveloper/SharpCoreDB/issues](https://github.com/MPCoreDeveloper/SharpCoreDB/issues)
- **Discussions**: [https://github.com/MPCoreDeveloper/SharpCoreDB/discussions](https://github.com/MPCoreDeveloper/SharpCoreDB/discussions)
- **Sponsor**: [https://github.com/sponsors/mpcoredeveloper](https://github.com/sponsors/mpcoredeveloper)

## 📜 License

SharpCoreDB is licensed under the MIT License. See [LICENSE](https://github.com/MPCoreDeveloper/SharpCoreDB/blob/master/LICENSE) for details.

---

**Copyright © 2026 MPCoreDeveloper**
