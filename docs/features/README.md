# SharpCoreDB Feature Documentation

Welcome to SharpCoreDB documentation! Here you'll find comprehensive guides for all features.

---

## 📚 Feature Guides

### Database Features

#### [Phase 7: JOIN Operations with Collation Support](./PHASE7_JOIN_COLLATIONS.md)
**Status:** ✅ **Production Ready**  
**Highlights:**
- ✅ INNER, LEFT, RIGHT, FULL, CROSS JOINs
- ✅ Collation-aware string comparisons
- ✅ Binary, NoCase, RTrim, Unicode collations
- ✅ Multi-column JOINs
- ✅ Automatic collation resolution

**Quick Links:**
- [Usage Examples](./PHASE7_JOIN_COLLATIONS.md#usage-examples)
- [Collation Resolution Rules](./PHASE7_JOIN_COLLATIONS.md#collation-resolution-rules)
- [Performance Impact](./PHASE7_JOIN_COLLATIONS.md#performance-impact)

#### Collation Support (All Phases)
**Status:** ✅ **Complete (Phases 1-7)**

| Phase | Feature | Status |
|-------|---------|--------|
| Phase 1 | Schema support (CREATE TABLE COLLATE) | ✅ Complete |
| Phase 2 | Parser & Storage integration | ✅ Complete |
| Phase 3 | WHERE clause filtering | ✅ Complete |
| Phase 4 | ORDER BY, GROUP BY, DISTINCT | ✅ Complete |
| Phase 5 | Runtime optimization | ✅ Complete |
| Phase 6 | Schema migration (ALTER TABLE) | ✅ Complete |
| Phase 7 | JOIN operations | ✅ Complete |

### Time-Series Features (Phase 8)
**Status:** ✅ **Production Ready**
- ✅ Gorilla/Delta-of-Delta/XOR codecs
- ✅ Automatic time-range bucketing
- ✅ Downsampling to lower resolutions
- ✅ Retention policies
- ✅ BRIN-style time-range indexes

---

## 🔍 Vector Search & Embeddings

### [Vector Search Documentation](../Vectors/README.md)
**Status:** ✅ **Production Ready (v1.1.2+)**  
**Performance:** 50-100x faster than SQLite

**Highlights:**
- ✅ HNSW indexes for fast similarity search
- ✅ Multiple distance metrics (Cosine, Euclidean, Dot Product, Hamming)
- ✅ Scalar & Binary quantization (8-16x memory savings)
- ✅ Flat indexes for exact search
- ✅ SQL integration (`vec_distance()` function)
- ✅ AES-256-GCM encryption support
- ✅ Fully async API

**Quick Links:**
- [Quick Start](../Vectors/README.md#quick-start)
- [API Reference](../Vectors/README.md#api-reference)
- [Configuration](../Vectors/README.md#configuration)
- [Examples](../Vectors/README.md#examples)
- [Implementation Complete Report](../Vectors/IMPLEMENTATION_COMPLETE.md)

### [Migration Guide: SQLite Vectors → SharpCoreDB](../migration/SQLITE_VECTORS_TO_SHARPCORE.md)
**Status:** ✅ **Production Ready**  
**Benefits:** 50-100x faster search, 5-10x less memory, 12-30x faster index builds

**Highlights:**
- ✅ 9-step migration process
- ✅ Data migration strategies
- ✅ Query translation (SQLite → SharpCoreDB)
- ✅ Index tuning and optimization
- ✅ Performance benchmarking
- ✅ Troubleshooting guide

**For You If:**
- ✅ Currently using SQLite vector extensions
- ✅ Want 50-100x performance improvement
- ✅ Need native .NET vector support
- ✅ Scaling vector search workloads

**Quick Links:**
- [Step 1: Understand Schema](../migration/SQLITE_VECTORS_TO_SHARPCORE.md#step-1-understand-your-current-sqlite-schema)
- [Step 2: Create Vector Schema](../migration/SQLITE_VECTORS_TO_SHARPCORE.md#step-2-create-sharpcore-db-vector-schema)
- [Step 3: Migrate Data](../migration/SQLITE_VECTORS_TO_SHARPCORE.md#step-3-migrate-vector-data)
- [Step 4: Update Queries](../migration/SQLITE_VECTORS_TO_SHARPCORE.md#step-4-update-vector-search-queries)
- [Performance Tuning](../migration/SQLITE_VECTORS_TO_SHARPCORE.md#step-7-performance-tuning)

---

## 🚀 Performance Highlights

### Vector Search Performance
| Operation | SharpCoreDB | SQLite | Speedup |
|-----------|------------|--------|---------|
| k-NN search (1M vectors) | 2ms | 100ms | **50x** |
| Index build (1M vectors) | 5s | 60s | **12x** |
| Memory (1M vectors) | 1.2GB | 6GB | **5x less** |

### JOIN Performance
| JOIN Type | Collation Overhead |
|-----------|-------------------|
| Hash JOIN (Binary) | Baseline |
| Hash JOIN (NoCase) | +1-2% |
| Nested Loop (NoCase) | +5-10% |

### Aggregate Performance
- SIMD Analytics: **682x faster** than SQLite
- SIMD Analytics: **28,660x faster** than LiteDB

---

## 🆕 Recently Completed Features

### Phase 1.3: Stored Procedures & Views
**Status:** ✅ **Complete**
- CREATE PROCEDURE with IN/OUT/INOUT parameters
- CREATE VIEW and CREATE MATERIALIZED VIEW
- EXEC command with parameter binding

### Phase 1.4: Triggers
**Status:** ✅ **Complete**
- BEFORE/AFTER triggers
- INSERT/UPDATE/DELETE events
- NEW/OLD column binding
- Trigger body execution

---

## 📖 Documentation by Use Case

### New to SharpCoreDB?
1. **Start here:** [Main README](../../README.md)
2. **Learn the API:** [User Manual](../USER_MANUAL.md)
3. **Understand features:** This page

### Using Vector Search?
1. **Quick start:** [Vector README](../Vectors/README.md#quick-start)
2. **Configuration:** [Vector Configuration](../Vectors/README.md#configuration)
3. **Migration:** [From SQLite](../migration/SQLITE_VECTORS_TO_SHARPCORE.md)
4. **Performance:** [Tuning Guide](../Vectors/PERFORMANCE_TUNING.md)

### Using JOINs & Collations?
1. **How it works:** [Phase 7 Guide](./PHASE7_JOIN_COLLATIONS.md)
2. **Examples:** [Usage Examples](./PHASE7_JOIN_COLLATIONS.md#usage-examples)
3. **Rules:** [Collation Resolution](./PHASE7_COLLATIONS.md#collation-resolution-rules)

### Migrating from Another Database?
1. **From SQLite:** [Vectors Migration](../migration/SQLITE_VECTORS_TO_SHARPCORE.md)
2. **Storage format:** [Migration Guide](../migration/MIGRATION_GUIDE.md)
3. **Performance tuning:** [Benchmark results](../../docs/BENCHMARK_RESULTS.md)

---

## 🎓 Learning Path

### Beginner (New Users)
1. Main README overview
2. Quick Start examples
3. Basic CRUD operations
4. First SQL query

### Intermediate (Developers)
1. Vector search basics
2. JOIN operations
3. Collation support
4. Performance tuning

### Advanced (Architects)
1. Vector migration strategies
2. Time-series compression
3. Index tuning
4. Scaling to production

---

## 📊 Feature Matrix

| Feature | Status | Version | Performance | Security |
|---------|--------|---------|-------------|----------|
| Vector Search (HNSW) | ✅ Complete | 1.1.2+ | 50-100x SQLite | AES-256 ✅ |
| Vector Quantization | ✅ Complete | 1.1.2+ | 8-16x memory | ✅ |
| JOIN Collations | ✅ Complete | 1.1.2 | +1-10% | ✅ |
| Time-Series | ✅ Complete | 1.1.1+ | 80% compression | ✅ |
| Stored Procedures | ✅ Complete | 1.1.0+ | N/A | ✅ |
| Views & Materialized Views | ✅ Complete | 1.1.0+ | 2-100x | ✅ |
| Triggers | ✅ Complete | 1.1.0+ | 0-5% | ✅ |

---

## Need Help?

- **Questions?** Check [FAQ](../FAQ.md) or open a GitHub issue
- **Performance issues?** See [Tuning Guide](../Vectors/PERFORMANCE_TUNING.md)
- **Troubleshooting?** See [Vector Troubleshooting](../Vectors/README.md#troubleshooting)
- **API reference?** See [Vector API Docs](../Vectors/README.md#api-reference)

---

**Last Updated:** January 28, 2025  
**All Features:** Production Ready  
**Build Status:** ✅ Passing
