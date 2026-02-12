# SharpCoreDB Migration Documentation

This directory contains comprehensive guides for **migrating to and within SharpCoreDB** from other databases and storage formats.

---

## 🎯 Migration Guides

### **[SQLite Vectors → SharpCoreDB](./SQLITE_VECTORS_TO_SHARPCORE.md)** ✅ PRODUCTION READY
**Complete 9-step guide for migrating vector search from SQLite to SharpCoreDB**

**Status:** ✅ Production Ready (v1.1.2+)  
**Performance:** **50-100x faster**, 5-10x less memory  
**Implementation:** HNSW indexes + quantization

**Contents:**
- Schema translation (FTS5 + sqlite-vec → VECTOR columns)
- Data migration strategies (batch, parallel, incremental)
- Query translation (SQL patterns + .NET API)
- Index configuration (HNSW parameters)
- Performance benchmarking
- Gradual rollout strategies
- Troubleshooting & FAQ

**Expected Results:**
- ⚡ **50-100x faster search** (0.5-2ms vs 50-100ms)
- 💾 **5-10x less memory** (1.2GB vs 6GB for 1M vectors)
- 🚀 **12-30x faster index build** (5s vs 60s for 1M vectors)
- 📈 **10-100x higher throughput** (5000+ qps vs 100 qps)

**For You If:**
- Currently using `sqlite-vec` or `fts5` vector extensions
- Building AI/RAG applications
- Need semantic search with high performance
- Scaling vector workloads

### **[Storage Format Migration](./MIGRATION_GUIDE.md)** ✅ PRODUCTION READY
**Guide for migrating between SharpCoreDB storage formats**

**Supported Migrations:**
- Directory format ↔ Single-File format
- Bidirectional migration
- Zero data loss
- Progress tracking
- Checksum verification

**Contents:**
- Migration API reference
- Step-by-step examples
- Best practices
- Performance expectations
- Troubleshooting

---

## 📊 Migration Decision Matrix

| Source | Destination | Status | Time | Data Loss | Downtime |
|--------|-------------|--------|------|-----------|----------|
| SQLite vector | SharpCoreDB vector | ✅ Complete | Hours-Days | No | Minimal |
| SharpCoreDB Dir | SharpCoreDB SingleFile | ✅ Complete | Minutes | No | Minimal |
| SQLite (RDBMS) | SharpCoreDB | ✅ Supported | Custom | No | Minimal |

---

## 🚀 Quick Start by Scenario

### Scenario 1: Migrate Vector Search from SQLite
**Goal:** Move embedding search to SharpCoreDB  
**Steps:** 5-9 (see SQLITE_VECTORS_TO_SHARPCORE.md)  
**Time:** 1-2 hours setup, 1-7 days migration (depends on dataset size)  
**Benefit:** 50-100x faster search

**Quick Links:**
- [Step 1: Understand Schema](./SQLITE_VECTORS_TO_SHARPCORE.md#step-1-understand-your-current-sqlite-schema)
- [Step 2: Create Vector Schema](./SQLITE_VECTORS_TO_SHARPCORE.md#step-2-create-sharpcore-db-vector-schema)
- [Step 3: Migrate Data](./SQLITE_VECTORS_TO_SHARPCORE.md#step-3-migrate-vector-data)
- [Step 4: Update Queries](./SQLITE_VECTORS_TO_SHARPCORE.md#step-4-update-vector-search-queries)
- [Complete Guide](./SQLITE_VECTORS_TO_SHARPCORE.md)

### Scenario 2: Change SharpCoreDB Storage Format
**Goal:** Switch from directory to single-file (or vice versa)  
**Steps:** API call + migration  
**Time:** Minutes  
**Benefit:** Simplified deployment

**Quick Link:** [MIGRATION_GUIDE.md](./MIGRATION_GUIDE.md)

### Scenario 3: Migrate Regular RDBMS Data
**Goal:** Move relational data from SQLite/LiteDB  
**Approach:** Custom SQL script using INSERT/SELECT  
**Resources:** [User Manual](../USER_MANUAL.md)

---

## 📋 Pre-Migration Checklist

### For Vector Search Migration
- [ ] Analyze current vector data volume
- [ ] Identify all tables with embeddings
- [ ] Review current query patterns
- [ ] Check for custom vector functions
- [ ] Plan batch operation size (1000-10000 rows)
- [ ] Identify performance thresholds

### General Best Practices
- [ ] Backup source database
- [ ] Test migration in development first
- [ ] Plan for incremental rollout (dual-write initially)
- [ ] Validate data after migration
- [ ] Performance test before production
- [ ] Have rollback plan ready

---

## 📖 Documentation Index

| Document | Purpose | Audience | Read Time |
|----------|---------|----------|-----------|
| [SQLITE_VECTORS_TO_SHARPCORE.md](./SQLITE_VECTORS_TO_SHARPCORE.md) | Vector migration (9 steps) | DevOps/Architects | 15-20 min |
| [MIGRATION_GUIDE.md](./MIGRATION_GUIDE.md) | Storage format migration | DevOps | 10-15 min |
| [../USER_MANUAL.md](../USER_MANUAL.md) | General database usage | Developers | 30-40 min |
| [../Vectors/README.md](../Vectors/README.md) | Vector API & features | Developers | 20-30 min |
| [../features/PHASE7_JOIN_COLLATIONS.md](../features/PHASE7_JOIN_COLLATIONS.md) | JOIN & collation support | Developers | 10-15 min |

---

## 🔄 Migration Workflow

```
┌─────────────────────────────────┐
│   Plan Migration                 │
│ - Analyze current schema         │
│ - Size estimation                │
│ - Downtime calculation           │
└────────────┬────────────────────┘
             │
┌────────────▼────────────────────┐
│   Test in Development            │
│ - Create SharpCoreDB schema      │
│ - Migrate sample data (1%)       │
│ - Validate query translations    │
└────────────┬────────────────────┘
             │
┌────────────▼────────────────────┐
│   Plan Rollout Strategy          │
│ - Dual-write pattern (optional)  │
│ - Batch sizing                   │
│ - Rollback plan                  │
└────────────┬────────────────────┘
             │
┌────────────▼────────────────────┐
│   Execute Migration              │
│ - Batch data transfer            │
│ - Monitor progress               │
│ - Validate checksums             │
└────────────┬────────────────────┘
             │
┌────────────▼────────────────────┐
│   Validate & Test                │
│ - Data integrity checks          │
│ - Query validation               │
│ - Performance testing            │
└────────────┬────────────────────┘
             │
┌────────────▼────────────────────┐
│   Production Cutover             │
│ - Switch application to new DB   │
│ - Monitor for issues             │
│ - Archive old database           │
└─────────────────────────────────┘
```

---

## ⚡ Performance Tips

### Vector Data Migration
- **Batch size:** 1,000-10,000 rows per batch
- **Parallelism:** Up to 4 parallel jobs
- **HNSW build:** Use `ef_construction=200` for balance
- **Quantization:** Use scalar (8-bit) for 8x memory savings

### Storage Format Migration
- **Best time:** Off-peak hours
- **Disk space needed:** 2x current database size (temporary)
- **Verification:** Always verify checksums

---

## 🆘 Troubleshooting

### Vector Migration Issues
**Q: Vector dimensions don't match**  
A: Check source embedding size. OpenAI=1536, local models vary. Update `VECTOR(N)` accordingly.

**Q: Migration is slow**  
A: Increase batch size to 5000-10000. Use parallel jobs (up to 4).

**Q: Out of memory**  
A: Reduce batch size, enable quantization, or add more RAM.

### Storage Format Migration Issues
**Q: Checksum mismatch**  
A: Verify source data is not being modified. Retry migration.

---

## 📞 Support

- **Questions?** See the specific guide (SQLITE_VECTORS_TO_SHARPCORE.md or MIGRATION_GUIDE.md)
- **Issues?** [GitHub Issues](https://github.com/MPCoreDeveloper/SharpCoreDB/issues)
- **Performance help?** See [Performance Tuning](../Vectors/PERFORMANCE_TUNING.md)

---

## 🔗 Related Documentation

- [Vector Search Feature Guide](../Vectors/README.md)
- [Phase 7: JOIN Operations](../features/PHASE7_JOIN_COLLATIONS.md)
- [SharpCoreDB User Manual](../USER_MANUAL.md)
- [Project Status](../PROJECT_STATUS.md)

---

**Last Updated:** January 28, 2025  
**All Guides:** Production Ready  
