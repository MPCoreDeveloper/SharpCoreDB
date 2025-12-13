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

---

## 🏗️ Architecture

- [Architecture Overview](ARCHITECTURE.md) - System design and components
- [MVCC Implementation](MVCC.md) - Multi-version concurrency control
- [Query Optimization](QUERY_OPTIMIZATION.md) - Cost-based query planner
- [Caching Strategy](CACHING.md) - LRU query and version caching

---

## ⚡ Features

- [.NET 10 Optimizations](features/NET10_OPTIMIZATIONS.md) - Span<T>, SIMD, ValueTask
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

---

## 🔧 Development

### Refactoring History
- [Table Refactoring](refactoring/REFACTORING_COMPLETE.md) - Table.cs modularization
- [Parser Refactoring](refactoring/ENHANCEDSQLPARSER_REFACTORING_COMPLETE.md) - SQL parser restructure
- [ColumnStore Refactoring](refactoring/COLUMNSTORE_REFACTORING_COMPLETE.md) - Columnar storage optimization

### Benchmarks
- [Benchmark README](../SharpCoreDB.Benchmarks/README.md) - How to run benchmarks
- [Performance Reports](../SharpCoreDB.Benchmarks/BenchmarkDotNet.Artifacts/results/) - HTML reports

---

## 🎯 Quick Links

| Topic | Link |
|-------|------|
| Installation | [README - Installation](../README.md#installation) |
| Quick Start | [Examples - Basic CRUD](guides/EXAMPLES.md#basic-crud) |
| Transactions | [Examples - Transactions](guides/EXAMPLES.md#transactions) |
| EF Core Setup | [EF Core Guide](guides/EFCORE_IMPLEMENTATION.md#quick-start) |
| Performance Tuning | [.NET 10 Optimizations](features/NET10_OPTIMIZATIONS.md) |
| Caching | [Caching Strategy](CACHING.md) |
| PRAGMA Commands | [PRAGMA API](api/PRAGMA.md) |

---

## 📞 Support

- **Issues**: [GitHub Issues](https://github.com/MPCoreDeveloper/SharpCoreDB/issues)
- **Discussions**: [GitHub Discussions](https://github.com/MPCoreDeveloper/SharpCoreDB/discussions)
- **Repository**: [github.com/MPCoreDeveloper/SharpCoreDB](https://github.com/MPCoreDeveloper/SharpCoreDB)

---

*Last Updated: 2025-12-13*
