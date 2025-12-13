# Documentation Index

**SharpCoreDB** - High-performance in-memory database for .NET 10

---

## 📚 Getting Started

- [README](../README.md) - Project overview and quick start
- [CONTRIBUTING](../CONTRIBUTING.md) - Contribution guidelines
- [CHANGELOG](../CHANGELOG.md) - Version history

---

## 📖 Guides

- [Examples](guides/EXAMPLES.md) - Code examples and usage patterns
- [EF Core Implementation](guides/EFCORE_IMPLEMENTATION.md) - Entity Framework Core integration
- [Benchmark Guide](guides/BENCHMARK_GUIDE.md) - ⚡ **NEW!** Performance testing and measurement

---

## 🏗️ Architecture

- [Architecture Overview](ARCHITECTURE.md) - System design and components
- [MVCC Implementation](MVCC.md) - Multi-version concurrency control
- [Query Optimization](QUERY_OPTIMIZATION.md) - Cost-based query planner
- [Caching Strategy](CACHING.md) - LRU query and version caching

---

## ⚡ Features

- [Performance Optimizations](features/PERFORMANCE_OPTIMIZATIONS.md) - ⚡ **NEW!** Complete optimization guide (40-60% improvement)
- [.NET 10 Optimizations](features/NET10_OPTIMIZATIONS.md) - Span<T>, SIMD, ValueTask
- [Adaptive WAL Batching](features/ADAPTIVE_WAL_BATCHING.md) - Dynamic batch tuning (+15-25% @ 32+ threads)
- [Memory-Mapped Files](features/MEMORY_MAPPED_FILES.md) - Persistent storage support
- [Encryption](features/ENCRYPTION.md) - AES-256-GCM data at rest
- [Columnar Storage](features/COLUMNAR_STORAGE.md) - SIMD-optimized analytics

---

## 📊 API Reference

- [Database API](api/DATABASE.md) - Core database operations
- [Transaction API](api/TRANSACTIONS.md) - MVCC transactions
- [PRAGMA Commands](api/PRAGMA.md) - Runtime configuration
- [EF Core API](api/EFCORE_API.md) - Entity Framework integration

---

## 📈 Status & Roadmap

- [Project Status](PROJECT_STATUS.md) - Current features and roadmap
- [EF Core Status](status/EFCORE_STATUS.md) - EF Core implementation status
- [Production Readiness](roadmap/PRODUCTION_READY.md) - Production improvements
- [Missing Features Roadmap](roadmap/MISSING_FEATURES_ROADMAP.md) - Planned enhancements

---

## 🔍 Analysis & Comparison

- [SQLite Comparison](comparison/SQLITE_VS_SHARPCOREDB.md) - Feature comparison
- [SQLite Feature Gap](analysis/SQLITE_FEATURE_GAP.md) - Missing SQLite features

---

## 🔧 Development

### Refactoring History
- [Table Refactoring](refactoring/REFACTORING_COMPLETE.md) - Table.cs modularization
- [Parser Refactoring](refactoring/ENHANCEDSQLPARSER_REFACTORING_COMPLETE.md) - SQL parser restructure
- [ColumnStore Refactoring](refactoring/COLUMNSTORE_REFACTORING_COMPLETE.md) - Columnar storage optimization

### Benchmarks
- [Benchmark Guide](guides/BENCHMARK_GUIDE.md) - **NEW!** How to run and analyze benchmarks
- [Database Comparison](benchmarks/DATABASE_COMPARISON.md) - 🆕 **Honest comparison** vs SQLite & LiteDB
- [Benchmark Implementation Guide](benchmarks/BENCHMARK_IMPLEMENTATION_GUIDE.md) - 🆕 How to create fair benchmarks
- [Benchmark README](../SharpCoreDB.Benchmarks/README.md) - Benchmark project setup
- [Comparison Benchmarks README](../SharpCoreDB.Benchmarks/COMPARISON_BENCHMARKS_README.md) - Running comparison tests
- [Performance Reports](../SharpCoreDB.Benchmarks/BenchmarkDotNet.Artifacts/results/) - HTML reports

---

## 🎯 Quick Links

| Topic | Link |
|-------|------|
| Installation | [README - Installation](../README.md#installation) |
| Quick Start | [Examples - Basic CRUD](guides/EXAMPLES.md#basic-crud) |
| Transactions | [Examples - Transactions](guides/EXAMPLES.md#transactions) |
| EF Core Setup | [EF Core Guide](guides/EFCORE_IMPLEMENTATION.md#quick-start) |
| **Performance Tuning** | [**Performance Optimizations**](features/PERFORMANCE_OPTIMIZATIONS.md) ⚡ |
| Configuration Presets | [Performance Optimizations - Configs](features/PERFORMANCE_OPTIMIZATIONS.md#5-new-workload-specific-configurations) |
| Adaptive Batching | [Adaptive WAL Batching](features/ADAPTIVE_WAL_BATCHING.md) |
| SIMD Aggregates | [Performance Optimizations - SIMD](features/PERFORMANCE_OPTIMIZATIONS.md#3-avx-512-simd-optimizations) |
| Benchmarking | [Benchmark Guide](guides/BENCHMARK_GUIDE.md) |
| Caching | [Caching Strategy](CACHING.md) |
| PRAGMA Commands | [PRAGMA API](api/PRAGMA.md) |

---

## 🚀 What's New in v1.0

### Performance Optimizations (40-60% improvement)
- **SQL Parser**: 20-40% faster with StringBuilder and HashSet optimizations
- **Lock-Free Indexing**: 30-50% better concurrency with ConcurrentDictionary
- **AVX-512 SIMD**: 2x faster aggregates on modern CPUs
- **Int64 MIN/MAX**: 5-8x faster with SIMD (was using LINQ)
- **WAL Buffer Reuse**: 10-15% less GC pressure
- **Console Output Removal**: 5-10% faster production queries

### New Configuration Presets
- `DatabaseConfig.ReadHeavy` - Optimized for analytics/reporting (50k page cache)
- `DatabaseConfig.WriteHeavy` - Optimized for logging/IoT (512x WAL multiplier)
- `DatabaseConfig.LowMemory` - Optimized for mobile/embedded (4MB footprint)
- `DatabaseConfig.HighPerformance` - General production workloads (adaptive WAL)

### Documentation
- Complete [Performance Optimizations Guide](features/PERFORMANCE_OPTIMIZATIONS.md)
- [Benchmark Guide](guides/BENCHMARK_GUIDE.md) with examples and best practices
- Configuration comparison matrices and decision trees

---

## 📞 Support

- **Issues**: [GitHub Issues](https://github.com/MPCoreDeveloper/SharpCoreDB/issues)
- **Discussions**: [GitHub Discussions](https://github.com/MPCoreDeveloper/SharpCoreDB/discussions)
- **Repository**: [github.com/MPCoreDeveloper/SharpCoreDB](https://github.com/MPCoreDeveloper/SharpCoreDB)

---

*Last Updated: January 2025 - v1.0 Performance Release*
