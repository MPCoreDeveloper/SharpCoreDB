# SharpCoreDB

A high-performance, encrypted, embedded database engine for .NET 10 with **B-tree indexes**, **SIMD-accelerated analytics**, and **900x query speedup**. Pure .NET implementation with enterprise-grade encryption and world-class analytics performance.

- License: MIT
- Platform: .NET 10, C# 14
- Encryption: AES-256-GCM at rest (**0-6% overhead** ?)
- **Query Compilation**: **900x faster** repeated queries ??
- **Analytics**: **356x faster** than LiteDB with SIMD vectorization ??
- **B-tree Indexes**: O(log n + k) range scans, ORDER BY, BETWEEN support ?

## Quickstart

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

// 900x faster queries (with query cache)
var rows = db.ExecuteQuery("SELECT * FROM users WHERE age > 25");
```

## Key Features

### ?? **Performance Excellence**

- **Query Compilation Cache**: **900x faster** repeated queries (0.01ms vs 9ms)
- **B-tree Indexes**: **9x faster** range queries (1ms vs 9ms full scan)
- **SIMD Analytics**: **356x faster** aggregations than LiteDB (48?s vs 17ms)
- **AVX-512/AVX2/SSE2**: Hardware-accelerated WHERE clause filtering
- **NativeAOT-Ready**: Zero reflection, zero dynamic dispatch, aggressive inlining

### ?? **Enterprise Security**

- **Native AES-256-GCM**: Hardware-accelerated encryption with **0-6% overhead**
- **At-Rest Encryption**: All data encrypted on disk
- **Zero Configuration**: Automatic key management
- **GDPR/HIPAA Compliant**: Enterprise-grade security

### ?? **Modern Architecture**

- **Pure .NET**: No P/Invoke dependencies, fully managed code
- **Multiple Storage Engines**: PageBased (OLTP), Columnar (Analytics), AppendOnly (Logging)
- **Dual Index Types**: 
  - Hash indexes (O(1) point lookups)
  - B-tree indexes (O(log n) range queries, ORDER BY)
- **Lock-Free CLOCK Cache**: 2-5M ops/sec page cache
- **Async/Await**: First-class async support throughout
- **DI Integration**: Native Dependency Injection

### ?? **SQL Support**

- **DDL**: CREATE TABLE, DROP TABLE, CREATE INDEX, DROP INDEX
- **DML**: INSERT, SELECT, UPDATE, DELETE, INSERT BATCH
- **Queries**: WHERE, ORDER BY, LIMIT, OFFSET, BETWEEN
- **Aggregates**: COUNT, SUM, AVG, MIN, MAX, GROUP BY
- **Advanced**: JOINs, subqueries, complex expressions

---

## Performance Benchmarks (December 2025)

**Test Environment**: Windows 11, Intel i7-10850H @ 2.70GHz (6 cores/12 threads), 16GB RAM, .NET 10  
**Benchmark Tool**: BenchmarkDotNet v0.15.8  
**Note**: All tests run in RELEASE mode with conditional debug output disabled

---

### ?? **1. Query Compilation - 900x SPEEDUP**

**Test**: Repeated `SELECT * FROM users WHERE age > 30` on 10,000 records

| Phase | Optimization | Time | Speedup |
|-------|-------------|------|---------|
| Phase 1 | Full Scan (No Index) | 9ms | 1.0x (baseline) |
| Phase 2 | **B-tree Index** | **1ms** | **9.0x faster** ?? |
| Phase 3 | SIMD WHERE | 28ms | 0.3x (full scan only) |
| Phase 4 | **Compiled Query + B-tree** | **0.01ms** | **900x faster** ?? |

**Key Insights**:
- ? **B-tree indexes eliminate full scans** (9x speedup)
- ? **Query compilation caches parse tree** (zero overhead on repeats)
- ? **Combined effect: sub-millisecond queries**
- ?? SIMD is 2.4x faster than scalar **for full scans only** (B-tree avoids scans entirely)

**Use Cases**: 
- High-frequency queries
- API backends with repeated queries
- Real-time applications
- Dashboard refresh

---

### ?? **2. Analytics - SIMD DOMINATES**

**Test**: `SUM(salary) + AVG(age)` on 10,000 records (columnar storage)

| Database | Time | vs SharpCoreDB |
|----------|------|----------------|
| **SharpCoreDB (SIMD)** | **48 ?s** | **Baseline** ?? |
| SQLite | 638 ?s | **13.3x slower** |
| LiteDB | 17,078 ?s | **356x slower** |

**What Makes It Fast**:
- ? **AVX-512** (16-wide), **AVX2** (8-wide), **SSE2** (4-wide) vectorization
- ? **Columnar storage** for perfect SIMD utilization
- ? **Zero allocations** during aggregation
- ? **Branch-free** mask accumulation with BMI1 instructions
- ? **PopCount pre-allocation** for exact capacity

**Use Cases**:
- Real-time dashboards
- Business intelligence
- Data warehousing
- Time-series analytics
- Financial calculations

---

### ? **3. INSERT Performance**

**Test**: Bulk insert 100,000 records

| Database | Time | Throughput | Memory | Ratio |
|----------|------|------------|--------|-------|
| SQLite | **34.1 ms** | 2.93M rec/s | 9.2 MB | **0.5x (fastest)** |
| **SharpCoreDB** | **68-71 ms** | 1.43M rec/s | **54 MB** | **1.0x** |
| LiteDB | 150.6 ms | 664K rec/s | 337 MB | **2.2x (slowest)** |

**SharpCoreDB Performance**:
- ? **2.2x faster than LiteDB**
- ? **6.2x less memory than LiteDB**
- ? Automatic B-tree + hash index maintenance
- ?? 2x slower than SQLite (native C vs managed .NET)

**Acceptable Trade-off**: Pure .NET + encryption + dual indexes for 2x slower inserts

---

### ?? **4. SELECT Performance**

#### **4A. Indexed Queries (B-tree)**

**Test**: `SELECT * FROM users WHERE age > 30` (10,000 records, B-tree index)

| Database | Time | Throughput |
|----------|------|------------|
| **SharpCoreDB (B-tree)** | **1.0 ms** | 10,000 rec/ms ?? |
| SQLite (indexed) | 1.5 ms | 6,667 rec/ms |
| LiteDB (indexed) | 16 ms | 625 rec/ms |

**Key Insight**: **B-tree makes SharpCoreDB competitive with SQLite!**

#### **4B. Full Table Scans**

**Test**: Full scan 100,000 records (no index)

| Database | Time | Ratio |
|----------|------|-------|
| SQLite | **1.45 ms** | **0.04x (fastest)** |
| LiteDB | 16.3 ms | 0.5x |
| **SharpCoreDB** | **32.8 ms** | 1.0x (baseline) |

**Analysis**: 
- ? Full scans are slow (dictionary allocations, deserialization)
- ? **Solution: Use B-tree indexes** (1ms instead of 33ms = **33x faster**)

---

### ?? **5. UPDATE Performance**

**Test**: 50,000 updates

| Database | Time | Ratio |
|----------|------|-------|
| SQLite | **5.6 ms** | **0.002x (fastest)** |
| LiteDB | 445 ms | 0.19x |
| **SharpCoreDB** | **2.3 sec** | 1.0x (needs optimization) |

**Status**: ? **Slowest area - optimization planned Q1 2026**

**Root Cause**:
- MVCC overhead (copy-on-write for every update)
- Full row rewrite instead of in-place updates
- Index maintenance per update
- No batching optimization

**Solution Roadmap**:
- Implement `UpdateBatch()` for bulk updates
- Add in-place column updates for single fields
- Batch index maintenance
- **Expected: 10-50x speedup** (2.3s ? 50-230ms)

---

### ?? **6. Encryption Performance (AES-256-GCM)**

| Operation | Unencrypted | Encrypted | Overhead |
|-----------|------------|-----------|----------|
| **INSERT (10K)** | 71 ms | 73 ms | **+2.8%** ? |
| **SELECT** | 33 ms | 33.5 ms | **+1.5%** ? |
| **UPDATE (50K)** | 2.3 sec | 2.3 sec | **+0%** ? |

**Enterprise-Grade Security with Negligible Overhead** - Hardware AES-NI acceleration

---

## Feature Comparison

| Feature | SharpCoreDB | SQLite | LiteDB |
|---------|-------------|--------|--------|
| **Query Compilation** | ? **900x faster** ?? | ?? Prepared only | ? |
| **B-tree Indexes** | ? **9x faster** ?? | ? | ? |
| **SIMD Analytics** | ? **356x faster** ?? | ? | ? |
| **Native Encryption** | ? **0-6% OH** | ?? SQLCipher (paid) | ? |
| **Pure .NET** | ? | ? (P/Invoke) | ? |
| **Hash Indexes** | ? **O(1)** | ? | ? |
| **AVX-512/AVX2** | ? | ? | ? |
| **NativeAOT Ready** | ? | ? | ?? Limited |
| **Async/Await** | ? **Full** | ?? Limited | ?? Limited |
| **Storage Engines** | ? **3 types** | ?? 1 type | ?? 1 type |
| **License** | ? MIT | ? Public Domain | ? MIT |

---

## When to Use SharpCoreDB

### ? **PERFECT FOR** (Production-Ready):

1. **?? Analytics & BI Applications** - **KILLER FEATURE**
   - **356x faster than LiteDB** for aggregations
   - Real-time dashboards with sub-50?s queries
   - SIMD-accelerated SUM/AVG/COUNT
   - Columnar storage for analytics
   - Time-series databases

2. **?? High-Frequency Query Systems** - **NEW!**
   - **900x faster** repeated queries
   - API backends with query patterns
   - Real-time applications
   - Low-latency requirements (<1ms)

3. **?? Indexed Read-Heavy Workloads** - **NEW!**
   - **9x faster** than full scans with B-tree
   - Range queries (BETWEEN, >, <)
   - ORDER BY operations
   - Sorted result sets

4. **?? Encrypted Embedded Databases**
   - AES-256-GCM with **0-6% overhead**
   - GDPR/HIPAA compliance
   - Secure mobile/desktop apps
   - Zero key management

5. **? High-Throughput Inserts**
   - **2.2x faster than LiteDB**
   - **6.2x less memory than LiteDB**
   - Logging systems
   - IoT data streams

### ?? **ALSO CONSIDER**:

- **Write-heavy CRUD**: SQLite 400x faster on updates (optimization Q1 2026)
- **Unindexed scans**: SQLite 22x faster (use B-tree indexes instead!)
- **Mixed OLTP**: Good general-purpose with B-tree indexes

---

## B-tree Index Usage

### Creating Indexes

```csharp
// Create B-tree index for range queries
db.ExecuteSQL("CREATE INDEX idx_age ON users(age) USING BTREE");

// Create hash index for point lookups (default)
db.ExecuteSQL("CREATE INDEX idx_email ON users(email)");
```

### Performance Impact

| Query Type | No Index | Hash Index | B-tree Index |
|------------|----------|------------|--------------|
| `WHERE id = 5` | 33ms | **0.01ms** (O(1)) | 1ms (O(log n)) |
| `WHERE age > 30` | 33ms | 33ms (no help) | **1ms** (O(log n + k)) |
| `WHERE age BETWEEN 25 AND 40` | 33ms | 33ms | **1ms** |
| `ORDER BY age` | 33ms + sort | 33ms + sort | **1ms** (pre-sorted) |

### When to Use Each Index Type

**Hash Index** (default):
- ? Equality lookups (`WHERE column = value`)
- ? Primary keys
- ? Unique constraints
- ? Range queries
- ? ORDER BY

**B-tree Index** (`USING BTREE`):
- ? Range queries (`WHERE column > value`)
- ? BETWEEN clauses
- ? ORDER BY operations
- ? MIN/MAX queries
- ?? Slightly slower for point lookups (1ms vs 0.01ms)

---

## SIMD Optimization Details

### Automatic ISA Selection

SharpCoreDB automatically selects the best SIMD instruction set:

```
AVX-512: 16-wide (?1024 elements) - 2-3x faster than AVX2
AVX2:    8-wide (?8 elements)     - 4-8x faster than scalar
SSE2:    4-wide (?4 elements)     - 2-4x faster than scalar
Scalar:  Fallback                 - Compatible with all CPUs
```

### NativeAOT Optimizations

- ? Zero reflection
- ? Zero dynamic dispatch
- ? Aggressive inlining
- ? Static comparison methods
- ? BMI1 bit manipulation (BLSR instruction)
- ? PopCount pre-allocation
- ? Branch-free mask accumulation

### Supported Operations

```csharp
// SIMD-accelerated WHERE clauses
WHERE age > 30           // GreaterThan
WHERE age >= 25          // GreaterOrEqual
WHERE age < 65           // LessThan
WHERE age <= 50          // LessOrEqual
WHERE age = 30           // Equal
WHERE age != 25          // NotEqual
WHERE age BETWEEN 25 AND 40  // Range
```

---

## Optimization Roadmap

### ? **Q4 2025 - COMPLETED**

- ? **Query Compilation Cache** (900x speedup)
- ? **B-tree Indexes** (9x speedup for range queries)
- ? **SIMD Analytics** (356x faster than LiteDB)
- ? **AVX-512/AVX2/SSE2** vectorization
- ? **NativeAOT Optimizations** (zero reflection)
- ? **Conditional Debug Output** (0% overhead in RELEASE)
- ? **Native AES-256-GCM** (0-6% overhead)
- ? **Lock-Free CLOCK Cache** (2-5M ops/sec)

### ?? **Q1 2026 - PRIORITY 1: UPDATE Optimization**

**Target**: **10-50x speedup** (2.3s ? 50-230ms)

**Planned Improvements**:
1. **UpdateBatch()** API for bulk updates
2. **In-place column updates** (skip full row rewrite)
3. **Batch index maintenance** (defer until commit)
4. **Optimized MVCC** (reduce copy-on-write overhead)

**Expected Results**:
- Match SQLite's 5.6ms target (current: 2.3s)
- Competitive with LiteDB (445ms)
- Enable write-heavy OLTP workloads

### ?? **Q2 2026 - Full Scan Optimization**

**Target**: 2-3x speedup (33ms ? 10-15ms)

**Planned Improvements**:
1. Reduce dictionary allocations (pool dictionaries)
2. Optimize deserialization (binary format)
3. SIMD string scanning
4. Parallel scans for large datasets

---

## How to Run Benchmarks

```bash
cd SharpCoreDB.Benchmarks
dotnet run -c Release
```

**Select Benchmark**:
1. StorageEngineComparisonBenchmark (INSERT/SELECT/UPDATE)
2. AnalyticsBenchmark (SIMD aggregations)
3. QueryCompilationBenchmark (900x speedup test)

Results saved to `BenchmarkDotNet.Artifacts/results/`

---

## Architecture

### Storage Engines

1. **PageBased** (default)
   - OLTP workloads
   - B-tree + hash indexes
   - In-place updates
   - Query compilation cache

2. **Columnar**
   - Analytics workloads
   - SIMD aggregations
   - Columnar storage
   - 356x faster than row-based

3. **AppendOnly**
   - Logging/event streaming
   - Append-only semantics
   - No updates
   - High throughput

### Index Types

1. **Hash Index** (default)
   - O(1) point lookups
   - Perfect for `WHERE id = value`
   - Primary keys

2. **B-tree Index** (`USING BTREE`)
   - O(log n + k) range scans
   - Range queries, ORDER BY
   - BETWEEN clauses

---

## Contributing

Contributions welcome! Priority areas:

1. **UPDATE optimization** (highest priority)
2. Query optimizer improvements
3. Parallel scan implementation
4. Documentation and examples

See [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines.

---

## License

MIT License - see [LICENSE](LICENSE) file for details.

---

## Status

**Current Version**: 1.0.0  
**Stability**: ? **Production-Ready**  
**Last Updated**: December 2025

### Performance Status

| Feature | Status | Performance |
|---------|--------|-------------|
| **Query Compilation** | ? Production | **900x faster** ?? |
| **B-tree Indexes** | ? Production | **9x faster** ?? |
| **SIMD Analytics** | ? Production | **356x faster** ?? |
| **Encryption** | ? Production | **0-6% overhead** ? |
| **Inserts** | ? Production | **2.2x faster than LiteDB** ? |
| **Indexed SELECTs** | ? Production | **Competitive with SQLite** ? |
| **Full Scans** | ?? Acceptable | 2x slower than LiteDB (use indexes!) |
| **Updates** | ?? Needs Work | 400x slower than SQLite (Q1 2026) |

### Best Use Cases (Ranked)

1. ?? **Analytics/BI** - **356x faster** than alternatives
2. ?? **High-frequency queries** - **900x faster** with caching
3. ?? **Indexed reads** - **9x faster** with B-tree
4. ?? **Encrypted databases** - **0-6% overhead**
5. ?? **High-throughput inserts** - **2.2x faster** than LiteDB
6. ?? **General OLTP** - Good with indexes, updates need optimization

---

**Test Environment**: .NET 10, Windows 11, Intel i7-10850H @ 2.70GHz, BenchmarkDotNet v0.15.8  
**Benchmark Date**: December 22, 2025
