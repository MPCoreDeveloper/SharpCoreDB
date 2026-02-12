# SharpCoreDB Documentation Hub

**Version:** 1.2.0  
**Last Updated:** January 28, 2025  
**Status:** ✅ Complete

---

## 📚 Welcome to SharpCoreDB Documentation

This is your central guide to all SharpCoreDB features, guides, and resources.

### Quick Navigation

- **New to SharpCoreDB?** → [Getting Started](../README.md)
- **Need Vector Search?** → [Vector Migration Guide](#vector-search)
- **Using Collations?** → [Collation Guide](#collations)
- **API Reference?** → [User Manual](../USER_MANUAL.md)
- **Performance?** → [Benchmarks](../BENCHMARK_RESULTS.md)

---

## 📋 Table of Contents

1. [Vector Search](#vector-search)
2. [Collation Support](#collations)
3. [Features & Phases](#features--phases)
4. [Migration Guides](#migration-guides)
5. [API & Configuration](#api--configuration)
6. [Performance & Tuning](#performance--tuning)
7. [Support & Community](#support--community)

---

## Vector Search

SharpCoreDB includes **production-ready vector search** with 50-100x performance improvements over SQLite.

### Documentation

| Document | Purpose | Read Time |
|----------|---------|-----------|
| [Vector Migration Guide](./vectors/VECTOR_MIGRATION_GUIDE.md) | Step-by-step migration from SQLite | 20 min |
| [Vector README](./vectors/README.md) | API reference, examples, configuration | 15 min |
| [Performance Benchmarks](./vectors/IMPLEMENTATION_COMPLETE.md) | Detailed performance analysis | 10 min |
| [Verification Report](../VECTOR_SEARCH_VERIFICATION_REPORT.md) | Benchmark verification and methodology | 15 min |

### Quick Facts

- **Index Type:** HNSW (Hierarchical Navigable Small World)
- **Distance Metrics:** Cosine, Euclidean, Dot Product, Hamming
- **Quantization:** Scalar (8-bit) and Binary (1-bit)
- **Performance:** 50-100x faster than SQLite
- **Encryption:** AES-256-GCM support
- **Status:** ✅ Production Ready

### Get Started

```csharp
// 1. Install
dotnet add package SharpCoreDB.VectorSearch

// 2. Create schema
await db.ExecuteSQLAsync(@"
    CREATE TABLE documents (
        id INTEGER PRIMARY KEY,
        embedding VECTOR(1536)
    )
");

// 3. Search
var results = await db.ExecuteQueryAsync(@"
    SELECT id FROM documents
    WHERE vec_distance('cosine', embedding, @query) > 0.8
    LIMIT 10
");
```

---

## Collations

Complete collation support with 4 types across 7 implementation phases.

### Documentation

| Document | Purpose | Read Time |
|----------|---------|-----------|
| [Collation Guide](./collation/COLLATION_GUIDE.md) | Complete reference for all collation types | 25 min |
| [Phase Implementation](./collation/PHASE_IMPLEMENTATION.md) | Technical details of all 7 phases | 20 min |
| [Phase 7: JOINs](./features/PHASE7_JOIN_COLLATIONS.md) | JOIN operations with collation support | 15 min |

### Collation Types

| Type | Behavior | Performance | Use Case |
|------|----------|-------------|----------|
| **BINARY** | Exact byte-by-byte | Baseline | Default, case-sensitive |
| **NOCASE** | Case-insensitive | +5% | Usernames, searches |
| **RTRIM** | Ignore trailing spaces | +3% | Legacy data |
| **UNICODE** | Accent-insensitive, international | +8% | Global applications |

### SQL Example

```sql
-- Case-insensitive search
SELECT * FROM users WHERE username = 'Alice' COLLATE NOCASE;

-- International sort
SELECT * FROM contacts ORDER BY name COLLATE UNICODE;

-- JOIN with collation
SELECT * FROM users u
JOIN orders o ON u.name COLLATE NOCASE = o.customer_name;
```

---

## Features & Phases

### All Phases Complete

| Phase | Feature | Status | Details |
|-------|---------|--------|---------|
| **1** | Core engine (tables, CRUD, indexes) | ✅ Complete | B-tree, Hash indexes |
| **2** | Storage (SCDB format, WAL, recovery) | ✅ Complete | Single-file, atomic operations |
| **3** | Page management (slotted pages, FSM) | ✅ Complete | Efficient space utilization |
| **4** | Transactions (ACID, checkpoint) | ✅ Complete | Group-commit WAL |
| **5** | Encryption (AES-256-GCM) | ✅ Complete | Zero overhead |
| **6** | Query engine (JOINs, subqueries) | ✅ Complete | All JOIN types |
| **7** | Optimization (SIMD, plan cache) | ✅ Complete | 682x aggregation speedup |
| **8** | Time-Series (compression, downsampling) | ✅ Complete | Gorilla codecs |
| **1.3** | Stored Procedures, Views | ✅ Complete | DDL support |
| **1.4** | Triggers | ✅ Complete | BEFORE/AFTER events |
| **7** | JOIN Collations | ✅ Complete | Collation-aware JOINs |
| **Vector** | Vector Search (HNSW) | ✅ Complete | 50-100x faster |

### Feature Matrix

See [Complete Feature Status](../COMPLETE_FEATURE_STATUS.md) for detailed information.

---

## Migration Guides

### From SQLite

| Source | Target | Guide | Time |
|--------|--------|-------|------|
| SQLite (RDBMS) | SharpCoreDB | [Data Migration](../migration/MIGRATION_GUIDE.md) | Custom |
| SQLite Vector | SharpCoreDB Vector | [Vector Migration](./vectors/VECTOR_MIGRATION_GUIDE.md) | 1-7 days |
| SQLite (Storage Format) | SharpCoreDB (Dir ↔ File) | [Storage Migration](../migration/README.md) | Minutes |

### From Other Databases

- [LiteDB Migration](../migration/README.md) - Similar architecture
- [Entity Framework](../EFCORE_COLLATE_COMPLETE.md) - Full EF Core support

---

## API & Configuration

### Getting Started

- **[User Manual](../USER_MANUAL.md)** - Complete API reference
- **[Quickstart Guide](../README.md#-quickstart)** - 5-minute intro
- **[ADO.NET Provider](../src/SharpCoreDB.Data.Provider)** - Standard data provider

### Configuration

```csharp
// Basic setup
services.AddSharpCoreDB();
var db = factory.Create("./app.db", "password");

// With Vector Search
services.AddSharpCoreDB()
    .UseVectorSearch(new VectorSearchOptions
    {
        EfConstruction = 200,
        EfSearch = 50
    });

// EF Core
services.AddDbContext<AppDbContext>(opts =>
    opts.UseSharpCoreDB("./app.db")
);
```

### Key APIs

| API | Purpose | Example |
|-----|---------|---------|
| `ExecuteSQLAsync()` | Execute SQL commands | `await db.ExecuteSQLAsync("INSERT ...")` |
| `ExecuteQueryAsync()` | Query data | `var rows = await db.ExecuteQueryAsync("SELECT ...")` |
| `InsertBatchAsync()` | Bulk insert | `await db.InsertBatchAsync("table", batch)` |
| `FlushAsync()` | Persist to disk | `await db.FlushAsync()` |
| `SearchAsync()` | Vector search | `var results = await idx.SearchAsync(query, k)` |

---

## Performance & Tuning

### Benchmarks

- **[Complete Benchmarks](../BENCHMARK_RESULTS.md)** - Detailed performance data
- **[Vector Performance](../VECTOR_SEARCH_VERIFICATION_REPORT.md)** - Vector search benchmarks
- **[Collation Performance](../collation/COLLATION_GUIDE.md#performance-implications)** - Collation overhead analysis

### Performance Summary

| Operation | Performance | vs SQLite | vs LiteDB |
|-----------|-------------|-----------|-----------|
| SIMD Aggregates | 1.08 µs | **682x faster** | **28,660x faster** |
| INSERT (1K batch) | 3.68 ms | **43% faster** | **44% faster** |
| Vector Search (1M) | 2-5 ms | **20-100x faster** | **N/A** |
| SELECT (full scan) | 814 µs | **Competitive** | **2.3x faster** |

### Tuning Guides

- **[Vector Index Tuning](./vectors/VECTOR_MIGRATION_GUIDE.md#index-configuration)** - HNSW parameters
- **[Collation Tuning](./collation/COLLATION_GUIDE.md#performance-implications)** - Collation overhead
- **[Index Strategy](../USER_MANUAL.md)** - Which index to use when

---

## Support & Community

### Documentation

| Resource | Purpose |
|----------|---------|
| **[Main README](../README.md)** | Project overview, features, installation |
| **[Complete Feature Status](../COMPLETE_FEATURE_STATUS.md)** | All features, status, performance |
| **[Project Status](../PROJECT_STATUS.md)** | Build status, test coverage |
| **[Contributing](../CONTRIBUTING.md)** | How to contribute |

### Get Help

| Channel | Use For |
|---------|---------|
| **GitHub Issues** | Bug reports, feature requests |
| **Discussions** | Questions, best practices |
| **Documentation** | API reference, guides |
| **Examples** | Code samples, patterns |

### Links

- **[GitHub Repository](https://github.com/MPCoreDeveloper/SharpCoreDB)**
- **[NuGet Package](https://www.nuget.org/packages/SharpCoreDB)**
- **[License (MIT)](../LICENSE)**

---

## Documentation Structure

```
docs/
├── INDEX.md (this file)
├── README.md                          Main project documentation
├── USER_MANUAL.md                     API reference & usage
├── BENCHMARK_RESULTS.md               Performance benchmarks
├── COMPLETE_FEATURE_STATUS.md         All features & status
├── PROJECT_STATUS.md                  Build & test status
│
├── vectors/                           Vector Search Documentation
│   ├── README.md                      Quick start & API
│   ├── VECTOR_MIGRATION_GUIDE.md      SQLite → SharpCoreDB migration
│   ├── IMPLEMENTATION_COMPLETE.md     Implementation report
│   ├── PERFORMANCE_TUNING.md          Optimization guide
│   └── TECHNICAL_SPEC.md              Architecture details
│
├── collation/                         Collation Documentation
│   ├── COLLATION_GUIDE.md             Complete collation reference
│   └── PHASE_IMPLEMENTATION.md        7-phase implementation details
│
├── features/                          Feature Documentation
│   ├── README.md                      Feature index
│   └── PHASE7_JOIN_COLLATIONS.md      JOIN with collations
│
├── migration/                         Migration Guides
│   ├── README.md                      Migration overview
│   ├── SQLITE_VECTORS_TO_SHARPCORE.md Vector migration
│   └── MIGRATION_GUIDE.md             Storage format migration
│
└── scdb/                              SCDB Implementation
    ├── README.md                      SCDB overview
    ├── PHASE1_COMPLETE.md             Phase 1 report
    └── PRODUCTION_GUIDE.md            Production deployment
```

---

## By User Type

### For Developers

1. **Start:** [Quickstart](../README.md#-quickstart)
2. **Learn:** [User Manual](../USER_MANUAL.md)
3. **Advanced:** [Technical Specs](./vectors/TECHNICAL_SPEC.md)
4. **Examples:** Check GitHub examples folder

### For DevOps/Architects

1. **Overview:** [Feature Status](../COMPLETE_FEATURE_STATUS.md)
2. **Deployment:** [SCDB Production Guide](../scdb/PRODUCTION_GUIDE.md)
3. **Migration:** [Migration Guides](../migration/README.md)
4. **Performance:** [Benchmarks](../BENCHMARK_RESULTS.md)

### For Database Admins

1. **Schema:** [Collation Guide](./collation/COLLATION_GUIDE.md)
2. **Migration:** [Storage Migration](../migration/MIGRATION_GUIDE.md)
3. **Tuning:** [Performance Guide](./vectors/VECTOR_MIGRATION_GUIDE.md#performance-tuning)
4. **Backup:** [User Manual - Backup](../USER_MANUAL.md)

### For Project Managers

1. **Status:** [Project Status](../PROJECT_STATUS.md)
2. **Features:** [Complete Feature Status](../COMPLETE_FEATURE_STATUS.md)
3. **Timeline:** [Phase Implementation](./collation/PHASE_IMPLEMENTATION.md)
4. **Roadmap:** [Future Enhancements](../COMPLETE_FEATURE_STATUS.md#roadmap)

---

## Quick Links

### Most Popular Topics

- [Vector Migration (SQLite → SharpCoreDB)](./vectors/VECTOR_MIGRATION_GUIDE.md)
- [Collation Reference](./collation/COLLATION_GUIDE.md)
- [Performance Benchmarks](../BENCHMARK_RESULTS.md)
- [User Manual & API](../USER_MANUAL.md)

### Quick Answers

**Q: How do I get started?**  
A: [5-minute Quickstart](../README.md#-quickstart)

**Q: How do I migrate from SQLite?**  
A: [Vector Migration Guide](./vectors/VECTOR_MIGRATION_GUIDE.md) or [Storage Migration](../migration/MIGRATION_GUIDE.md)

**Q: What collation should I use?**  
A: [Collation Guide](./collation/COLLATION_GUIDE.md#best-practices)

**Q: How fast is vector search?**  
A: [Vector Performance Report](../VECTOR_SEARCH_VERIFICATION_REPORT.md)

**Q: What versions are supported?**  
A: [Complete Feature Status](../COMPLETE_FEATURE_STATUS.md)

---

## Recent Updates (v1.2.0)

✅ **Added:** Vector search benchmarks  
✅ **Added:** Comprehensive collation guides  
✅ **Added:** Migration guides  
✅ **Enhanced:** Documentation structure  
✅ **Updated:** All version numbers to 1.2.0  

---

## Version Information

| Component | Version | Status |
|-----------|---------|--------|
| **SharpCoreDB** | 1.2.0 | ✅ Production Ready |
| **Vector Search** | 1.2.0+ | ✅ Production Ready |
| **.NET Target** | 10.0 | ✅ Current |
| **C# Language** | 14 | ✅ Latest |

---

## Feedback & Suggestions

Have a question or suggestion about the documentation?

- **Report Issues:** [GitHub Issues](https://github.com/MPCoreDeveloper/SharpCoreDB/issues)
- **Suggest Improvements:** [GitHub Discussions](https://github.com/MPCoreDeveloper/SharpCoreDB/discussions)
- **Submit Changes:** [Pull Requests Welcome](https://github.com/MPCoreDeveloper/SharpCoreDB/pulls)

---

**Last Updated:** January 28, 2025  
**Version:** 1.2.0  
**Status:** ✅ Complete & Current

Happy coding! 🚀
