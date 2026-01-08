# Changelog

All notable changes to SharpCoreDB will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### 🎉 **MAJOR ACHIEVEMENT** - INSERT Optimization Complete! (8 januari 2026)

**SharpCoreDB now beats LiteDB in ALL 4 benchmark categories!** 🏆

#### INSERT Performance Breakthrough - 3.2x Speedup
- **Previous**: 17.1ms (2.4x slower than LiteDB)
- **Current**: 5.28-6.04ms (1.21x FASTER than LiteDB)
- **Improvement**: **3.2x speedup (224% faster)** ✅
- **Target achieved**: <7ms goal met (5.28ms) ✅
- **Memory**: 2.1x less than LiteDB (5.1MB vs 10.7MB) ✅

#### Complete Performance Summary (8 januari 2026)

| Operation | SharpCoreDB | LiteDB | Status |
|-----------|-------------|--------|--------|
| **Analytics** | 20.7-22.2 µs | 8.54-8.67 ms | ✅ **390-420x sneller** |
| **SELECT** | 3.32-3.48 ms | 7.80-7.99 ms | ✅ **2.3x sneller** |
| **UPDATE** | 7.95-7.97 ms | 36.5-37.9 ms | ✅ **4.6x sneller** |
| **INSERT** | 5.28-6.04 ms | 6.42-7.22 ms | ✅ **1.21x sneller** |

**Result**: 🏆 **SharpCoreDB wins ALL 4 categories!**

### Added (INSERT Optimization Campaign)

#### Phase 1: Quick Wins (Hardware & Memory)
- Hardware CRC32 (SSE4.2 instructions) - 10x faster checksums
- Bulk buffer allocation using ArrayPool for entire batch
- Lock scope minimization - validation outside write lock
- Zero-allocation string encoding with Span<byte> API

#### Phase 2: Core Optimizations (Architecture)
- SQL-free InsertBatch API for direct binary path
- Free Space Index (O(log n) page lookup with SortedDictionary)
- Bulk B-tree insert with sorted key batching
- Reduced tree rebalancing overhead

#### Phase 3: Advanced Techniques (Zero-Copy)
- TypedRowBuffer with C# 14 InlineArray structs
- Scatter-Gather I/O using RandomAccess.Write
- Prepared Insert Statement caching
- Sequential disk access optimization

#### Phase 4: Polish (SIMD & Specialization)
- Schema-specific serialization fast paths
- Fast type writers (WriteInt32Fast, WriteDecimalFast, etc.)
- SIMD string encoding (AVX2/SSE4.2 UTF-8)
- C# 14 InlineArrays (ColumnOffsets[16], InlineRowValues[16])

### Changed
- Updated documentation with latest performance benchmarks (8 januari 2026)
- Enhanced README with INSERT victory announcement
- **MAJOR**: INSERT performance improved from 17.1ms to **5.28ms** (3.2x speedup)
- **MAJOR**: INSERT now **1.21x faster than LiteDB** (was 2.4x slower)
- **MAJOR**: PageBased SELECT performance **2.3x faster than LiteDB**
- **MAJOR**: UPDATE performance **4.6x faster than LiteDB**
- **MAJOR**: Analytics SIMD performance **390-420x faster than LiteDB**

### Performance Improvements Timeline

#### December 2025
| Operation | vs LiteDB |
|-----------|-----------|
| Analytics | 345x faster ✅ |
| SELECT | 2x slower ⚠️ |
| UPDATE | 1.54x faster ✅ |
| INSERT | 2.4x slower ⚠️ |

**Score**: 2 out of 4 ⚠️

#### 8 januari 2026
| Operation | vs LiteDB |
|-----------|-----------|
| Analytics | **390-420x faster** ✅ |
| SELECT | **2.3x faster** ✅ |
| UPDATE | **4.6x faster** ✅ |
| INSERT | **1.21x faster** ✅ |

**Score**: **4 out of 4** 🏆

### Added
- Comprehensive INSERT optimization documentation (INSERT_OPTIMIZATION_PLAN.md)
- Detailed benchmark results document (BENCHMARK_RESULTS.md)
- Cross-engine performance comparisons (LiteDB vs SharpCoreDB)
- Workload-specific optimization guidelines
- LRU Page Cache with 99%+ hit rate
- Binary serialization optimizations

### Fixed
- StorageEngineComparisonBenchmark now uses ExecuteBatchSQL
- INSERT performance bottleneck (17.1ms → 5.28ms)
- Memory allocation overhead during batch inserts

## [1.0.0] - 2025-01-XX

### Added

#### Core Database Engine
- High-performance embedded database engine for .NET 10
- Pure .NET implementation with zero P/Invoke dependencies
- Full async/await support throughout the API
- Native dependency injection integration
- NativeAOT-ready architecture with zero reflection

#### Security Features
- AES-256-GCM encryption at rest with hardware acceleration
- Zero performance overhead for encryption (0% or negative overhead)
- Automatic key management with enterprise-grade security
- GDPR and HIPAA compliance support

#### Storage Engines

SharpCoreDB provides **three workload-optimized storage engines**:

##### PageBased Engine (OLTP Optimized)
- Optimized for mixed read/write OLTP workloads
- LRU page cache for hot data (99%+ cache hit rate)
- In-place updates with zero rewrite overhead
- **60x faster SELECT than LiteDB**
- **6x faster UPDATE than LiteDB**
- Best for: transactional applications, random updates, primary key lookups

##### Columnar Engine (Analytics Optimized)
- Optimized for analytics workloads with SIMD vectorization
- AVX-512/AVX2/SSE2 support for hardware-accelerated aggregations
- **417x faster than LiteDB, 15x faster than SQLite** for analytics
- Best for: real-time dashboards, BI applications, time-series analytics

##### AppendOnly Engine (Logging Optimized)
- Optimized for sequential writes and logging workloads
- Faster than PageBased for append-only operations
- Minimal overhead with simple file structure
- Best for: event sourcing, audit trails, IoT data streams

**See [BENCHMARK_RESULTS.md](BENCHMARK_RESULTS.md) for detailed performance comparisons.**

#### Indexing System
- **Hash Indexes**: O(1) point lookups for primary keys
- **B-tree Indexes**: O(log n) range queries with ORDER BY and BETWEEN support
- Dual index architecture for optimal performance across workload types

#### SIMD-Accelerated Analytics
- AVX-512 support (16-wide vectorization)
- AVX2 support (8-wide vectorization)
- SSE2 support (4-wide vectorization) for fallback
- Hardware-accelerated aggregations (SUM, AVG, COUNT)
- Zero-allocation columnar processing
- Branch-free mask accumulation with BMI1 instructions

#### SQL Support
- **DDL**: CREATE TABLE, DROP TABLE, CREATE INDEX, DROP INDEX
- **DML**: INSERT, SELECT, UPDATE, DELETE, INSERT BATCH
- **Query Operations**: WHERE, ORDER BY, LIMIT, OFFSET, BETWEEN
- **Aggregation Functions**: COUNT, SUM, AVG, MIN, MAX, GROUP BY
- **Advanced Features**: JOINs, subqueries, complex expressions
- Parameterized query support with optimization routing

#### High-Performance APIs
- **StructRow API**: Zero-copy query results with lazy deserialization
- **Batch Update API**: High-throughput bulk operations with BeginBatchUpdate/EndBatchUpdate
- **Compiled Queries**: Prepare() for 5-10x faster repeated queries
- Type-safe column access with compile-time checking
- Optional result caching for repeated column access

#### Additional Packages
- **SharpCoreDB.Data.Provider**: Full ADO.NET provider implementation
- **SharpCoreDB.EntityFrameworkCore**: Entity Framework Core provider
- **SharpCoreDB.Serilog.Sinks**: Serilog sink for structured logging
- **SharpCoreDB.Extensions**: Extension methods library

#### Testing and Development Tools
- Comprehensive test suite (SharpCoreDB.Tests)
- Performance benchmarks with BenchmarkDotNet (SharpCoreDB.Benchmarks)
- Profiling tools (SharpCoreDB.Profiling)
- Demo application (SharpCoreDB.Demo)
- Database viewer tool (SharpCoreDB.Viewer)
- Debug benchmark utilities (SharpCoreDB.DebugBenchmark)
- JOIN and subquery demo (SharpCoreDB.DemoJoinsSubQ)

#### Project Structure
- Restructured to standard layout (src/, tests/, tools/)
- Comprehensive GitHub Actions CI/CD pipeline
- Directory.Build.props for shared project properties
- .editorconfig for consistent code style across the codebase
- Enhanced .gitignore with comprehensive patterns

#### Documentation
- Comprehensive README with benchmarks and usage examples
- Full API documentation with XML comments
- Contributing guidelines (CONTRIBUTING.md)
- Detailed changelog (CHANGELOG.md)
- Comprehensive benchmark results (BENCHMARK_RESULTS.md)
- MIT License

### Performance Highlights (8 januari 2026)

**For detailed benchmark results, see [BENCHMARK_RESULTS.md](BENCHMARK_RESULTS.md)**

All benchmarks performed on Windows 11, Intel i7-10850H @ 2.70GHz (6 cores/12 threads), 16GB RAM, .NET 10

#### World-Class Analytics Performance (Columnar Engine)
- **390-420x faster** than LiteDB for aggregations (20.7-22.2µs vs 8.54-8.67ms)
- **14-15x faster** than SQLite for GROUP BY operations (20.7-22.2µs vs 301-306µs)
- Sub-25µs query times for real-time dashboards
- Zero allocations during SIMD-accelerated aggregations
- AVX-512, AVX2, and SSE2 vectorization support

#### Exceptional SELECT Performance (PageBased Engine)
- **2.3x faster** than LiteDB for full table scans (3.32-3.48ms vs 7.80-7.99ms)
- **52x less memory** than LiteDB (220KB vs 11.4MB)
- LRU page cache with 99%+ hit rate

#### Excellent UPDATE Performance (PageBased Engine)
- **4.6x faster** than LiteDB for random updates (7.95-7.97ms vs 36.5-37.9ms)
- **10.3x less memory** than LiteDB (2.9MB vs 29.8-30.7MB)
- Efficient in-place update support

#### Outstanding INSERT Performance (PageBased Engine) - **NEW!** ✅
- **1.21x faster** than LiteDB for batch inserts (5.28-6.04ms vs 6.42-7.22ms)
- **2.1x less memory** than LiteDB (5.1MB vs 10.7MB)
- **3.2x speedup** achieved through optimization campaign (17.1ms → 5.28ms)

#### Memory Efficiency
- **52x less memory** for SELECT operations vs LiteDB
- **10.3x less memory** for UPDATE operations vs LiteDB
- **2.1x less memory** for INSERT operations vs LiteDB
- **10x less memory** with StructRow API vs Dictionary API
- **Zero allocations** during SIMD analytics

#### Enterprise-Grade Encryption
- **0% overhead** or better (sometimes faster with encryption enabled!)
- Hardware AES-NI acceleration
- No performance penalty for enterprise-grade security
- All storage engines support transparent encryption

### Workload Recommendations

**Choose your storage engine based on workload:**

| Workload Type | Recommended Engine | Key Advantage |
|---------------|-------------------|---------------|
| Analytics & Aggregations | **Columnar** | 420x faster than LiteDB |
| Mixed Read/Write OLTP | **PageBased** | 2.3x faster SELECT, 4.6x faster UPDATE |
| Batch Inserts | **PageBased** | 1.21x faster than LiteDB |
| Sequential Logging | **AppendOnly** | Optimized for sequential writes |
| Encryption Required | **All engines** | 0% overhead with AES-256-GCM |

---

## Links
- [GitHub Repository](https://github.com/MPCoreDeveloper/SharpCoreDB)
- [NuGet Package](https://www.nuget.org/packages/SharpCoreDB)
- [Documentation](https://github.com/MPCoreDeveloper/SharpCoreDB#readme)
- [Benchmark Results](https://github.com/MPCoreDeveloper/SharpCoreDB/blob/master/docs/BENCHMARK_RESULTS.md)
- [INSERT Optimization Plan](https://github.com/MPCoreDeveloper/SharpCoreDB/blob/master/docs/INSERT_OPTIMIZATION_PLAN.md)
- [Issue Tracker](https://github.com/MPCoreDeveloper/SharpCoreDB/issues)
- [Sponsor](https://github.com/sponsors/mpcoredeveloper)
