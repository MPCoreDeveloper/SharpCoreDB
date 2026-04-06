# SharpCoreDB Migration Documentation

This directory contains comprehensive guides for **migrating to and within SharpCoreDB** from other databases and storage formats.

---

## 🎯 Migration Guides

### **[FluentMigrator — Embedded Mode](./FLUENTMIGRATOR_EMBEDDED_MODE_v1.7.0.md)** ✅ v1.7.0
**Detailed guide for local/in-process schema migrations with SharpCoreDB.Extensions**

**Best for:**
- Local embedded deployments
- In-process service startup migrations
- Direct engine-backed migration execution

**Includes:**
- Architecture and execution pipeline
- DI registration and startup patterns
- `__SharpMigrations` version-table behavior
- Troubleshooting and production checklist

### **[FluentMigrator — Server Mode](./FLUENTMIGRATOR_SERVER_MODE_v1.7.0.md)** ✅ v1.7.0
**Detailed guide for server deployments: in-process host and remote gRPC migration execution**

**Best for:**
- Server-host startup migrations
- Remote deployment migration jobs via `SharpCoreDB.Client`
- Secure TLS-based migration automation

**Includes:**
- In-process vs remote mode comparison
- gRPC registration and operational patterns
- Security recommendations and limitations
- Troubleshooting and production checklist

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

---

## 📊 Migration Decision Matrix

| Source | Destination | Status | Time | Data Loss | Downtime |
|--------|-------------|--------|------|-----------|----------|
| Embedded app schema | SharpCoreDB + FluentMigrator (embedded) | ✅ Complete | Minutes | No | Minimal |
| Server schema (local host) | SharpCoreDB + FluentMigrator (in-process) | ✅ Complete | Minutes | No | Minimal |
| Server schema (remote) | SharpCoreDB + FluentMigrator (gRPC) | ✅ Complete | Minutes | No | Minimal |
| SQLite vector | SharpCoreDB vector | ✅ Complete | Hours-Days | No | Minimal |
| SharpCoreDB Dir | SharpCoreDB SingleFile | ✅ Complete | Minutes | No | Minimal |
| SQLite (RDBMS) | SharpCoreDB | ✅ Supported | Custom | No | Minimal |

---

## 🚀 Quick Start by Scenario

### Scenario 1: FluentMigrator in Embedded Mode
**Goal:** Run schema migrations locally in app/service process  
**Guide:** [FLUENTMIGRATOR_EMBEDDED_MODE_v1.7.0.md](./FLUENTMIGRATOR_EMBEDDED_MODE_v1.7.0.md)  
**Benefit:** Minimal operational complexity, direct engine execution

### Scenario 2: FluentMigrator in Server Mode
**Goal:** Run schema migrations for server deployments (hosted or remote over gRPC)  
**Guide:** [FLUENTMIGRATOR_SERVER_MODE_v1.7.0.md](./FLUENTMIGRATOR_SERVER_MODE_v1.7.0.md)  
**Benefit:** Deployment-pipeline friendly and secure remote orchestration

### Scenario 3: Migrate Vector Search from SQLite
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

### Scenario 4: Change SharpCoreDB Storage Format
**Goal:** Switch from directory to single-file (or vice versa)  
**Steps:** API call + migration  
**Time:** Minutes  
**Benefit:** Simplified deployment

**Quick Link:** [MIGRATION_GUIDE.md](./MIGRATION_GUIDE.md)

### Scenario 5: Migrate Regular RDBMS Data
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
| [FLUENTMIGRATOR_EMBEDDED_MODE_v1.7.0.md](./FLUENTMIGRATOR_EMBEDDED_MODE_v1.7.0.md) | Embedded migration architecture and operations | Developers/Architects | 10-15 min |
| [FLUENTMIGRATOR_SERVER_MODE_v1.7.0.md](./FLUENTMIGRATOR_SERVER_MODE_v1.7.0.md) | Server migration architecture (host + gRPC) | DevOps/Architects | 12-18 min |
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
