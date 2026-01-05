# SharpCoreDB Documentation

Welcome to the SharpCoreDB documentation! This directory contains comprehensive guides for using, developing, and understanding SharpCoreDB.

---

## 📚 Documentation Structure

### 🎯 [Feature Status](./FEATURE_STATUS.md) **← START HERE**
Complete feature matrix with implementation status, performance data, and roadmap.

- **[FEATURE_STATUS.md](./FEATURE_STATUS.md)** - Comprehensive status of all features
  - Production-ready features
  - Performance benchmarks
  - Hardware requirements
  - API compatibility
  - Roadmap

### 🚀 [Performance & Optimization](.)
Performance-focused guides for maximum throughput.

- **[QUERY_PLAN_CACHE.md](./QUERY_PLAN_CACHE.md)** - Query plan caching guide (5-10x speedup)
- **[SIMD_OPTIMIZATION_SUMMARY.md](./SIMD_OPTIMIZATION_SUMMARY.md)** - SIMD acceleration guide (345x analytics)

### 🗂️ [SCDB Single-File Format](./scdb/)
Complete documentation for the SCDB single-file storage format.

- **[README](./scdb/README.md)** - Overview and quick start guide
- **[FILE_FORMAT_DESIGN](./scdb/FILE_FORMAT_DESIGN.md)** - Complete technical specification
- **[DESIGN_SUMMARY](./scdb/DESIGN_SUMMARY.md)** - Executive summary
- **[IMPLEMENTATION_STATUS](./scdb/IMPLEMENTATION_STATUS.md)** - Current implementation progress
- **[PHASE1_IMPLEMENTATION](./scdb/PHASE1_IMPLEMENTATION.md)** - Phase 1 technical details

### 🔄 [Migration](./migration/)
Database migration guides for converting between storage formats.

- **[MIGRATION_GUIDE](./migration/MIGRATION_GUIDE.md)** - Complete migration guide with examples

### 🛠️ [Development](./development/)
Internal development documentation for contributors.

- **[SCDB_COMPILATION_FIXES](./development/SCDB_COMPILATION_FIXES.md)** - Compilation error solutions (English)
- **[SCDB_COMPILATION_FIXES_NL](./development/SCDB_COMPILATION_FIXES_NL.md)** - Compilation error solutions (Dutch)

### 📝 [Project Documentation](.)
General project documentation.

- **[CHANGELOG](./CHANGELOG.md)** - Version history and release notes
- **[CONTRIBUTING](./CONTRIBUTING.md)** - Contribution guidelines
- **[DIRECTORY_STRUCTURE](./DIRECTORY_STRUCTURE.md)** - Repository organization

---

## 🚀 Quick Start

### For Users

1. **Getting Started**: See the main [README.md](../README.md) in the root directory
2. **Feature Overview**: See [FEATURE_STATUS.md](./FEATURE_STATUS.md) for complete capabilities
3. **Performance Optimization**: See [QUERY_PLAN_CACHE.md](./QUERY_PLAN_CACHE.md) and [SIMD_OPTIMIZATION_SUMMARY.md](./SIMD_OPTIMIZATION_SUMMARY.md)
4. **SCDB Format**: Start with [scdb/README.md](./scdb/README.md)
5. **Migration**: See [migration/MIGRATION_GUIDE.md](./migration/MIGRATION_GUIDE.md)

### For Developers

1. **Build Setup**: See [CONTRIBUTING.md](./CONTRIBUTING.md)
2. **Feature Status**: See [FEATURE_STATUS.md](./FEATURE_STATUS.md)
3. **SCDB Implementation**: See [scdb/IMPLEMENTATION_STATUS.md](./scdb/IMPLEMENTATION_STATUS.md)
4. **Compilation Issues**: See [development/SCDB_COMPILATION_FIXES.md](./development/SCDB_COMPILATION_FIXES.md)

---

## 📊 Feature Status

| Feature | Status | Documentation |
|---------|--------|---------------|
| **Query Plan Caching** | ✅ Production | [QUERY_PLAN_CACHE.md](./QUERY_PLAN_CACHE.md) |
| **SIMD Analytics** | ✅ Production | [SIMD_OPTIMIZATION_SUMMARY.md](./SIMD_OPTIMIZATION_SUMMARY.md) |
| **Single-File Storage (.scdb)** | ✅ 95% Complete | [scdb/](./scdb/) |
| **Block Persistence** | ✅ Complete | [scdb/PHASE1_IMPLEMENTATION.md](./scdb/PHASE1_IMPLEMENTATION.md) |
| **VACUUM Operations** | ✅ Complete | [scdb/PHASE1_IMPLEMENTATION.md](./scdb/PHASE1_IMPLEMENTATION.md) |
| **Database Migration** | ✅ Complete | [migration/MIGRATION_GUIDE.md](./migration/MIGRATION_GUIDE.md) |
| **Directory Storage** | ✅ Production | Main README |
| **JOINs** | 🚧 Partial | [FEATURE_STATUS.md](./FEATURE_STATUS.md) |
| **Subqueries** | 🚧 Partial | [FEATURE_STATUS.md](./FEATURE_STATUS.md) |
| **Triggers** | 🚧 Planned Q2 2026 | [FEATURE_STATUS.md](./FEATURE_STATUS.md) |

---

## 🎯 Key Concepts

### Storage Modes

SharpCoreDB supports two storage modes:

1. **Directory Mode** (Legacy)
   - Multi-file format
   - One file per block
   - 100% backward compatible
   - Best for: Large databases (>10GB), legacy systems

2. **Single-File Mode** (.scdb)
   - All data in one file
   - SSD-optimized with page alignment
   - 10x faster startup
   - Best for: Desktop apps, embedded systems, <10GB databases

### Performance Optimization

SharpCoreDB provides multiple optimization techniques:

1. **Query Plan Caching** - Automatic (5-10x speedup)
2. **SIMD Analytics** - Hardware-accelerated (345x vs LiteDB, 11.5x vs SQLite)
3. **StructRow API** - Zero-copy iteration (10x less memory)
4. **B-tree Indexes** - O(log n) range queries
5. **Compiled Queries** - Prepare once, execute many times
6. **Batch Operations** - Bulk inserts/updates (1.5x faster)

### Migration

Bidirectional migration between formats is supported:
- Directory → .scdb: [Migration Guide](./migration/MIGRATION_GUIDE.md)
- .scdb → Directory: [Migration Guide](./migration/MIGRATION_GUIDE.md)

### Performance

| Operation | Directory | Single-File | Improvement |
|-----------|-----------|-------------|-------------|
| Startup | 100ms | 10ms | 10x faster |
| Write | 50k/s | 100k/s | 2x faster |
| VACUUM | 60s | 600ms | 100x faster |
| Analytics (SIMD) | 49.5µs | 49.5µs | 345x vs LiteDB |

---

## 📖 Documentation Conventions

### File Naming

- **UPPERCASE.md** - Major documentation files
- **README.md** - Directory overview files
- **lowercase.md** - Supplementary guides

### Language

- Primary documentation in **English**
- Translations available for some files (suffix: `_NL` = Dutch)

### Status Markers

- ✅ **Complete** - Feature implemented and tested
- ⚠️ **Partial** - Feature partially implemented
- ❌ **Planned** - Feature not yet implemented
- 🚧 **In Progress** - Currently being worked on

---

## 🤝 Contributing to Documentation

See [CONTRIBUTING.md](./CONTRIBUTING.md) for guidelines on:

- Writing new documentation
- Updating existing docs
- Code examples and formatting
- Translation guidelines

---

## 📄 License

All documentation is licensed under the MIT License. See [LICENSE](../LICENSE) for details.

---

## 📚 Documentation Index

### Core Documentation
- [FEATURE_STATUS.md](./FEATURE_STATUS.md) - Complete feature matrix
- [QUERY_PLAN_CACHE.md](./QUERY_PLAN_CACHE.md) - Query caching guide
- [SIMD_OPTIMIZATION_SUMMARY.md](./SIMD_OPTIMIZATION_SUMMARY.md) - SIMD acceleration
- [CHANGELOG.md](./CHANGELOG.md) - Version history
- [CONTRIBUTING.md](./CONTRIBUTING.md) - Contribution guide
- [DIRECTORY_STRUCTURE.md](./DIRECTORY_STRUCTURE.md) - Repository structure

### SCDB Format
- [scdb/README.md](./scdb/README.md) - SCDB overview
- [scdb/FILE_FORMAT_DESIGN.md](./scdb/FILE_FORMAT_DESIGN.md) - Technical specification
- [scdb/IMPLEMENTATION_STATUS.md](./scdb/IMPLEMENTATION_STATUS.md) - Implementation progress

### Migration
- [migration/MIGRATION_GUIDE.md](./migration/MIGRATION_GUIDE.md) - Migration guide

### Development
- [development/SCDB_COMPILATION_FIXES.md](./development/SCDB_COMPILATION_FIXES.md) - Compilation fixes

---

**Last Updated:** 2026-01-XX  
**Documentation Version:** 2.0.0  
**SharpCoreDB Version:** 2.x
