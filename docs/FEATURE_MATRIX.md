# SharpCoreDB Feature Matrix

**Version:** 1.4.1  
**Last Updated:** January 28, 2026  
**Status:** All Phase 1-10 Features Production-Ready ✅

---

## 🎯 Quick Status Overview

| Phase | Status | Version | Progress |
|-------|--------|---------|----------|
| **Phase 1-5: Core Engine** | ✅ Complete | 1.0.0 - 1.1.5 | 100% |
| **Phase 6: Graph Algorithms** | ✅ Complete | 1.2.0 | 100% |
| **Phase 7: Replication** | ✅ Complete | 1.2.5 | 100% |
| **Phase 8: Vector Search** | ✅ Complete | 1.2.0 | 100% |
| **Phase 9: Analytics Engine** | ✅ Complete | 1.3.5 | 100% |
| **Phase 10: Distributed & Sync** | ✅ Complete | 1.4.0 | 100% |
| **v1.5.0: Network Server** | 📅 Planned | Q2 2026 | 0% |

---

## 📊 Detailed Feature List

### ✅ Core Database Engine (Phases 1-5)

| Feature | Status | Version | Package | Notes |
|---------|--------|---------|---------|-------|
| **SQL Support** |
| SELECT | ✅ Complete | 1.0.0 | SharpCoreDB | Full SQL syntax |
| INSERT | ✅ Complete | 1.0.0 | SharpCoreDB | Bulk inserts supported |
| UPDATE | ✅ Complete | 1.0.0 | SharpCoreDB | Multi-row updates |
| DELETE | ✅ Complete | 1.0.0 | SharpCoreDB | Cascading deletes |
| JOIN (INNER, LEFT, RIGHT, FULL) | ✅ Complete | 1.0.0 | SharpCoreDB | Optimized join algorithms |
| Subqueries | ✅ Complete | 1.0.0 | SharpCoreDB | Nested queries |
| Common Table Expressions (WITH) | ✅ Complete | 1.0.0 | SharpCoreDB | Recursive CTEs |
| **DDL** |
| CREATE TABLE | ✅ Complete | 1.0.0 | SharpCoreDB | All data types |
| ALTER TABLE | ✅ Complete | 1.0.0 | SharpCoreDB | Add/drop columns |
| DROP TABLE | ✅ Complete | 1.0.0 | SharpCoreDB | CASCADE support |
| CREATE INDEX | ✅ Complete | 1.0.0 | SharpCoreDB | B-tree & hash |
| CREATE VIEW | ✅ Complete | 1.0.0 | SharpCoreDB | Materialized views |
| CREATE TRIGGER | ✅ Complete | 1.0.0 | SharpCoreDB | BEFORE/AFTER |
| IF EXISTS / IF NOT EXISTS | ✅ Complete | 1.5 | SharpCoreDB | Safe DDL |
| **Transactions** |
| BEGIN TRANSACTION | ✅ Complete | 1.0.0 | SharpCoreDB | ACID compliance |
| COMMIT | ✅ Complete | 1.0.0 | SharpCoreDB | Durable commits |
| ROLLBACK | ✅ Complete | 1.0.0 | SharpCoreDB | Full rollback |
| SAVEPOINT | ✅ Complete | 1.0.0 | SharpCoreDB | Nested transactions |
| Isolation Levels | ✅ Complete | 1.0.0 | SharpCoreDB | READ COMMITTED, SERIALIZABLE |
| MVCC | ✅ Complete | 1.0.0 | SharpCoreDB | Multi-version concurrency |
| **Storage** |
| Single-File Database | ✅ Complete | 1.0.0 | SharpCoreDB | .db file |
| Directory Storage | ✅ Complete | 1.0.0 | SharpCoreDB | Multi-file |
| Columnar Storage | ✅ Complete | 1.1.0 | SharpCoreDB | Analytics optimized |
| Write-Ahead Logging (WAL) | ✅ Complete | 1.0.5 | SharpCoreDB | Crash recovery |
| AES-256-GCM Encryption | ✅ Complete | 1.0.0 | SharpCoreDB | At-rest encryption |
| Compression (LZ4, Brotli) | ✅ Complete | 1.0.0 | SharpCoreDB | Automatic |
| Metadata Compression (Brotli) | ✅ Complete | 1.4.1 | SharpCoreDB | 60-80% reduction |
| **Indexing** |
| B-tree Index | ✅ Complete | 1.0.0 | SharpCoreDB | Range queries |
| Hash Index | ✅ Complete | 1.0.0 | SharpCoreDB | Equality lookups |
| Automatic Indexing | ✅ Complete | 1.0.0 | SharpCoreDB | Query-driven |
| Composite Indexes | ✅ Complete | 1.0.0 | SharpCoreDB | Multi-column |
| Full-Text Search Index | ✅ Complete | 1.0.0 | SharpCoreDB | Text search |
| **Query Optimization** |
| Cost-Based Optimizer | ✅ Complete | 1.0.0 | SharpCoreDB | Statistics-driven |
| Query Plan Caching | ✅ Complete | 1.1.5 | SharpCoreDB | Compiled plans |
| Join Optimization | ✅ Complete | 1.0.0 | SharpCoreDB | Hash/merge/nested loop |
| Predicate Pushdown | ✅ Complete | 1.0.0 | SharpCoreDB | Filter early |
| **Data Types** |
| INTEGER, LONG | ✅ Complete | 1.0.0 | SharpCoreDB | 32/64-bit |
| REAL, DECIMAL | ✅ Complete | 1.0.0 | SharpCoreDB | Floating point |
| STRING (VARCHAR) | ✅ Complete | 1.0.0 | SharpCoreDB | UTF-8 |
| BLOB | ✅ Complete | 1.0.0 | SharpCoreDB | Binary data |
| BOOLEAN | ✅ Complete | 1.0.0 | SharpCoreDB | TRUE/FALSE |
| DATETIME | ✅ Complete | 1.0.0 | SharpCoreDB | ISO 8601 |
| GUID | ✅ Complete | 1.0.0 | SharpCoreDB | UUID |
| ULID | ✅ Complete | 1.0.0 | SharpCoreDB | Sortable UUID |
| ROWREF | ✅ Complete | 1.4.0 | SharpCoreDB | Graph edges |
| VECTOR | ✅ Complete | 1.2.0 | SharpCoreDB | Embeddings |
| **Performance** |
| SIMD Acceleration | ✅ Complete | 1.1.5 | SharpCoreDB | AVX-512, AVX2, SSE |
| Memory Pooling | ✅ Complete | 1.1.5 | SharpCoreDB | ArrayPool<T> |
| Dynamic PGO | ✅ Complete | 1.1.5 | SharpCoreDB | JIT optimization |

---

### ✅ Graph & GraphRAG (Phase 6)

| Feature | Status | Version | Package | Notes |
|---------|--------|---------|---------|-------|
| **ROWREF Data Type** | ✅ Complete | 1.4.0 | SharpCoreDB | Direct row references |
| **Graph Traversal** |
| BFS (Breadth-First Search) | ✅ Complete | 1.2.0 | SharpCoreDB.Graph | Standard traversal |
| DFS (Depth-First Search) | ✅ Complete | 1.2.0 | SharpCoreDB.Graph | Stack-based |
| Bidirectional Search | ✅ Complete | 1.4.0 | SharpCoreDB.Graph | Meet-in-middle |
| Dijkstra Shortest Path | ✅ Complete | 1.4.0 | SharpCoreDB.Graph | Weighted graphs |
| A* Pathfinding | ✅ Complete | 1.2.0 | SharpCoreDB.Graph | Heuristic search |
| Custom Heuristics | ✅ Complete | 1.2.0 | SharpCoreDB.Graph | Pluggable |
| **SQL Integration** |
| GRAPH_TRAVERSE() Function | ✅ Complete | 1.4.0 | SharpCoreDB.Graph | SQL syntax |
| EF Core LINQ GraphTraverse() | ✅ Complete | 1.4.0 | SharpCoreDB.Graph | LINQ integration |
| **Performance** |
| Parallel Traversal | ✅ Complete | 1.4.0 | SharpCoreDB.Graph | Multi-threaded |
| Traversal Plan Caching | ✅ Complete | 1.4.0 | SharpCoreDB.Graph | Query optimization |
| Cycle Detection | ✅ Complete | 1.2.0 | SharpCoreDB.Graph | Prevent infinite loops |
| **Hybrid Queries** |
| Graph + Vector Hybrid | ✅ Complete | 1.4.0 | SharpCoreDB.Graph | GraphRAG queries |
| **Monitoring** |
| Metrics Collection | ✅ Complete | 1.4.0 | SharpCoreDB.Graph | Observability |
| OpenTelemetry Integration | ✅ Complete | 1.4.0 | SharpCoreDB.Graph | Distributed tracing |

---

### ✅ Vector Search & Semantic Search (Phase 8)

| Feature | Status | Version | Package | Notes |
|---------|--------|---------|---------|-------|
| **Vector Data Type** | ✅ Complete | 1.2.0 | SharpCoreDB | Fixed-dimension float32[] |
| **Distance Metrics** |
| Cosine Similarity | ✅ Complete | 1.2.0 | SharpCoreDB.VectorSearch | SIMD-accelerated |
| Euclidean Distance (L2) | ✅ Complete | 1.2.0 | SharpCoreDB.VectorSearch | SIMD-accelerated |
| Dot Product | ✅ Complete | 1.2.0 | SharpCoreDB.VectorSearch | SIMD-accelerated |
| Manhattan Distance (L1) | ✅ Complete | 1.2.0 | SharpCoreDB.VectorSearch | CPU optimized |
| **Indexing** |
| HNSW Index | ✅ Complete | 1.2.0 | SharpCoreDB.VectorSearch | Hierarchical graph |
| Flat Index (Brute-Force) | ✅ Complete | 1.2.0 | SharpCoreDB.VectorSearch | Small datasets |
| HNSW Persistence | ✅ Complete | 1.2.0 | SharpCoreDB.VectorSearch | Serialize/deserialize |
| **Quantization** |
| Scalar Quantization (float32→uint8) | ✅ Complete | 1.2.0 | SharpCoreDB.VectorSearch | 4× memory reduction |
| Binary Quantization (1-bit) | ✅ Complete | 1.2.0 | SharpCoreDB.VectorSearch | 32× memory reduction |
| **SQL Functions** |
| vec_distance_cosine() | ✅ Complete | 1.2.0 | SharpCoreDB.VectorSearch | SQL syntax |
| vec_distance_l2() | ✅ Complete | 1.2.0 | SharpCoreDB.VectorSearch | SQL syntax |
| vec_distance_dot() | ✅ Complete | 1.2.0 | SharpCoreDB.VectorSearch | SQL syntax |
| vec_from_float32() | ✅ Complete | 1.2.0 | SharpCoreDB.VectorSearch | Array to vector |
| vec_to_json() | ✅ Complete | 1.2.0 | SharpCoreDB.VectorSearch | JSON export |
| vec_normalize() | ✅ Complete | 1.2.0 | SharpCoreDB.VectorSearch | Unit vector |
| vec_dimensions() | ✅ Complete | 1.2.0 | SharpCoreDB.VectorSearch | Metadata |
| **Performance** |
| SIMD (AVX-512, AVX2, SSE) | ✅ Complete | 1.2.0 | SharpCoreDB.VectorSearch | Hardware dispatch |
| 50-100x faster than SQLite | ✅ Complete | 1.2.0 | SharpCoreDB.VectorSearch | Benchmarked |
| 10M+ vectors tested | ✅ Complete | 1.2.0 | SharpCoreDB.VectorSearch | Production scale |

---

### ✅ Analytics Engine (Phase 9)

| Feature | Status | Version | Package | Notes |
|---------|--------|---------|---------|-------|
| **Basic Aggregates** |
| COUNT(*), COUNT(DISTINCT) | ✅ Complete | 1.3.5 | SharpCoreDB.Analytics | 682x faster than SQLite |
| SUM(column) | ✅ Complete | 1.3.5 | SharpCoreDB.Analytics | Numeric sum |
| AVG(column) | ✅ Complete | 1.3.5 | SharpCoreDB.Analytics | Arithmetic mean |
| MIN(column), MAX(column) | ✅ Complete | 1.3.5 | SharpCoreDB.Analytics | Min/max values |
| **Statistical Aggregates** |
| STDDEV(column) | ✅ Complete | 1.3.5 | SharpCoreDB.Analytics | Standard deviation |
| VARIANCE(column) | ✅ Complete | 1.3.5 | SharpCoreDB.Analytics | Population/sample |
| PERCENTILE(column, p) | ✅ Complete | 1.3.5 | SharpCoreDB.Analytics | P50, P90, P95, P99 |
| MEDIAN(column) | ✅ Complete | 1.3.5 | SharpCoreDB.Analytics | 50th percentile |
| MODE(column) | ✅ Complete | 1.3.5 | SharpCoreDB.Analytics | Most frequent value |
| **Bivariate Aggregates** |
| CORRELATION(col1, col2) | ✅ Complete | 1.3.5 | SharpCoreDB.Analytics | Pearson correlation |
| COVARIANCE(col1, col2) | ✅ Complete | 1.3.5 | SharpCoreDB.Analytics | Population/sample |
| **Frequency Aggregates** |
| HISTOGRAM(column, bucket_size) | ✅ Complete | 1.3.5 | SharpCoreDB.Analytics | Value distribution |
| **Window Functions** |
| ROW_NUMBER() | ✅ Complete | 1.3.5 | SharpCoreDB.Analytics | Row numbering |
| RANK() | ✅ Complete | 1.3.5 | SharpCoreDB.Analytics | Ranking with gaps |
| DENSE_RANK() | ✅ Complete | 1.3.5 | SharpCoreDB.Analytics | Ranking without gaps |
| LAG(column, offset) | ✅ Complete | 1.3.5 | SharpCoreDB.Analytics | Previous row |
| LEAD(column, offset) | ✅ Complete | 1.3.5 | SharpCoreDB.Analytics | Next row |
| FIRST_VALUE(column) | ✅ Complete | 1.3.5 | SharpCoreDB.Analytics | First in partition |
| LAST_VALUE(column) | ✅ Complete | 1.3.5 | SharpCoreDB.Analytics | Last in partition |
| PARTITION BY | ✅ Complete | 1.3.5 | SharpCoreDB.Analytics | Grouping |
| ORDER BY (in window) | ✅ Complete | 1.3.5 | SharpCoreDB.Analytics | Sorting |
| **Time-Series** |
| DATE_BUCKET(interval, column) | ✅ Complete | 1.3.5 | SharpCoreDB.Analytics | Time bucketing |
| ROLLING_AVG(column, window) | ✅ Complete | 1.3.5 | SharpCoreDB.Analytics | Moving average |
| CUMULATIVE_SUM(column) | ✅ Complete | 1.3.5 | SharpCoreDB.Analytics | Running total |
| **OLAP** |
| PIVOT / UNPIVOT | ✅ Complete | 1.3.5 | SharpCoreDB.Analytics | Cross-tabulation |
| CUBE / ROLLUP | ✅ Complete | 1.3.5 | SharpCoreDB.Analytics | Multi-dimensional |
| **Performance** |
| 150-680x faster than SQLite | ✅ Complete | 1.3.5 | SharpCoreDB.Analytics | Benchmarked |
| SIMD-accelerated aggregates | ✅ Complete | 1.3.5 | SharpCoreDB.Analytics | Hardware dispatch |

---

### ✅ Distributed Features (Phase 7 & 10)

| Feature | Status | Version | Package | Notes |
|---------|--------|---------|---------|-------|
| **Replication** |
| Multi-Master Replication | ✅ Complete | 1.4.0 | SharpCoreDB.Distributed | Vector clocks |
| Vector Clock Causality | ✅ Complete | 1.4.0 | SharpCoreDB.Distributed | Conflict detection |
| Automatic Conflict Resolution | ✅ Complete | 1.4.0 | SharpCoreDB.Distributed | Last-write-wins, merge |
| Custom Conflict Resolvers | ✅ Complete | 1.4.0 | SharpCoreDB.Distributed | Pluggable |
| Real-Time Replication | ✅ Complete | 1.4.0 | SharpCoreDB.Distributed | Sub-second latency |
| Replication Monitoring | ✅ Complete | 1.4.0 | SharpCoreDB.Distributed | Health metrics |
| Failover & Recovery | ✅ Complete | 1.4.0 | SharpCoreDB.Distributed | Automatic |
| **Distributed Transactions** |
| Two-Phase Commit (2PC) | ✅ Complete | 1.4.0 | SharpCoreDB.Distributed | Cross-shard ACID |
| Transaction Recovery | ✅ Complete | 1.4.0 | SharpCoreDB.Distributed | Network failure recovery |
| Configurable Timeouts | ✅ Complete | 1.4.0 | SharpCoreDB.Distributed | Per-transaction |
| **Sharding** |
| Horizontal Sharding | ✅ Complete | 1.4.0 | SharpCoreDB.Distributed | Automatic distribution |
| Shard Key Management | ✅ Complete | 1.4.0 | SharpCoreDB.Distributed | Consistent hashing |
| Query Routing | ✅ Complete | 1.4.0 | SharpCoreDB.Distributed | Shard-aware |
| Shard Monitoring | ✅ Complete | 1.4.0 | SharpCoreDB.Distributed | Per-shard metrics |
| **WAL Streaming** |
| Streaming Replication | ✅ Complete | 1.4.0 | SharpCoreDB.Distributed | WAL-based |
| Buffer Pooling | ✅ Complete | 1.4.0 | SharpCoreDB.Distributed | Zero-copy |
| Compression | ✅ Complete | 1.4.0 | SharpCoreDB.Distributed | LZ4 |

---

### ✅ Sync Integration (Phase 10)

| Feature | Status | Version | Package | Notes |
|---------|--------|---------|---------|-------|
| **Dotmim.Sync Provider** | ✅ Complete | 1.4.0 | SharpCoreDB.Provider.Sync | Full provider |
| **Sync Targets** |
| SQL Server | ✅ Complete | 1.4.0 | SharpCoreDB.Provider.Sync | Bidirectional |
| PostgreSQL | ✅ Complete | 1.4.0 | SharpCoreDB.Provider.Sync | Bidirectional |
| MySQL | ✅ Complete | 1.4.0 | SharpCoreDB.Provider.Sync | Bidirectional |
| SQLite | ✅ Complete | 1.4.0 | SharpCoreDB.Provider.Sync | Bidirectional |
| **Change Tracking** |
| Shadow Tables | ✅ Complete | 1.4.0 | SharpCoreDB.Provider.Sync | _tracking tables |
| Tombstone Management | ✅ Complete | 1.4.0 | SharpCoreDB.Provider.Sync | Soft deletes |
| Incremental Sync | ✅ Complete | 1.4.0 | SharpCoreDB.Provider.Sync | Delta changes only |
| **Conflict Resolution** |
| Last-Write-Wins | ✅ Complete | 1.4.0 | SharpCoreDB.Provider.Sync | Default strategy |
| Client-Wins | ✅ Complete | 1.4.0 | SharpCoreDB.Provider.Sync | Client priority |
| Server-Wins | ✅ Complete | 1.4.0 | SharpCoreDB.Provider.Sync | Server priority |
| Custom Resolvers | ✅ Complete | 1.4.0 | SharpCoreDB.Provider.Sync | Pluggable |
| **Multi-Tenant Support** |
| Scope Filtering | ✅ Complete | 1.4.0 | SharpCoreDB.Provider.Sync | Tenant isolation |
| Parameter-Based Filters | ✅ Complete | 1.4.0 | SharpCoreDB.Provider.Sync | Row-level security |
| **Performance** |
| Bulk Operations | ✅ Complete | 1.4.0 | SharpCoreDB.Provider.Sync | Batch inserts |
| Compression | ✅ Complete | 1.4.0 | SharpCoreDB.Provider.Sync | LZ4 |
| Retry Logic | ✅ Complete | 1.4.0 | SharpCoreDB.Provider.Sync | Exponential backoff |
| **Testing** |
| 84/84 Tests Passing | ✅ Complete | 1.4.0 | SharpCoreDB.Provider.Sync | Full coverage |

---

### ✅ Integration Packages

| Feature | Status | Version | Package | Notes |
|---------|--------|---------|---------|-------|
| **Entity Framework Core** |
| EF Core Provider | ✅ Complete | 1.4.1 | SharpCoreDB.EntityFrameworkCore | Full LINQ support |
| Migrations | ✅ Complete | 1.4.1 | SharpCoreDB.EntityFrameworkCore | Code-first |
| Change Tracking | ✅ Complete | 1.4.1 | SharpCoreDB.EntityFrameworkCore | EF Core compatible |
| **Extensions** |
| Dependency Injection | ✅ Complete | 1.4.1 | SharpCoreDB.Extensions | AddSharpCoreDB() |
| Health Checks | ✅ Complete | 1.4.1 | SharpCoreDB.Extensions | ASP.NET Core |
| **Logging** |
| Serilog Sink | ✅ Complete | 1.4.1 | SharpCoreDB.Serilog.Sinks | Structured logging |

---

### 📅 Planned Features (v1.5.0+)

| Feature | Status | Target Version | Package | Notes |
|---------|--------|----------------|---------|-------|
| **Network Server** |
| TCP Binary Protocol | 📅 Planned | 1.5.0 | SharpCoreDB.Server | PostgreSQL-style |
| HTTP REST API | 📅 Planned | 1.5.0 | SharpCoreDB.Server | JSON over HTTPS |
| gRPC Protocol | 📅 Planned | 1.6.0 | SharpCoreDB.Server | Protobuf |
| WebSocket Streaming | 📅 Planned | 1.5.0 | SharpCoreDB.Server | Real-time queries |
| JWT Authentication | 📅 Planned | 1.5.0 | SharpCoreDB.Server | Token-based |
| Certificate Auth | 📅 Planned | 1.5.0 | SharpCoreDB.Server | Mutual TLS |
| Role-Based Access Control | 📅 Planned | 1.5.0 | SharpCoreDB.Server | Admin/Reader/Writer |
| Connection Pooling | 📅 Planned | 1.5.0 | SharpCoreDB.Server | 10,000+ connections |
| Windows Service | 📅 Planned | 1.5.0 | SharpCoreDB.Server | systemd/launchd |
| .NET Client Library | 📅 Planned | 1.5.0 | SharpCoreDB.Client | ADO.NET-like |
| Python Client | 📅 Planned | 1.5.0 | PySharpDB | pip package |
| JavaScript SDK | 📅 Planned | 1.5.0 | @sharpcoredb/client | npm package |
| **Advanced GraphRAG (v2.0)** |
| Community Detection | 📅 Planned | 2.0.0 | SharpCoreDB.Graph | Louvain algorithm |
| Centrality Algorithms | 📅 Planned | 2.0.0 | SharpCoreDB.Graph | PageRank, betweenness |
| GPU-Accelerated Traversal | 📅 Planned | 2.0.0 | SharpCoreDB.Graph | CUDA support |

---

## 📦 Package Versions

| Package | Latest Version | Status | Release Date |
|---------|---------------|--------|--------------|
| **SharpCoreDB** | 1.4.1 | ✅ Stable | Feb 20, 2026 |
| **SharpCoreDB.Analytics** | 1.4.1 | ✅ Stable | Feb 20, 2026 |
| **SharpCoreDB.VectorSearch** | 1.4.1 | ✅ Stable | Feb 20, 2026 |
| **SharpCoreDB.Graph** | 1.4.1 | ✅ Stable | Feb 20, 2026 |
| **SharpCoreDB.Distributed** | 1.4.1 | ✅ Stable | Feb 20, 2026 |
| **SharpCoreDB.Provider.Sync** | 1.4.1 | ✅ Stable | Feb 20, 2026 |
| **SharpCoreDB.EntityFrameworkCore** | 1.4.1 | ✅ Stable | Feb 20, 2026 |
| **SharpCoreDB.Extensions** | 1.4.1 | ✅ Stable | Feb 20, 2026 |
| **SharpCoreDB.Serilog.Sinks** | 1.4.1 | ✅ Stable | Feb 20, 2026 |
| **SharpCoreDB.Server** | - | 📅 Q2 2026 | TBD |
| **SharpCoreDB.Client** | - | 📅 Q2 2026 | TBD |

---

## 🎯 Legend

| Symbol | Meaning |
|--------|---------|
| ✅ Complete | Feature is production-ready and fully tested |
| 📅 Planned | Feature is designed and scheduled for implementation |
| ⚠️ Beta | Feature works but may have known issues |
| 🚧 In Progress | Feature is currently being implemented |
| ❌ Not Planned | Feature is not on the roadmap |

---

## 📚 Related Documentation

- **[Changelog](CHANGELOG.md)** - Version history
- **[Project Status](PROJECT_STATUS.md)** - Current status
- **[Documentation Index](INDEX.md)** - All documentation
- **[Server Implementation Plan](server/IMPLEMENTATION_PLAN.md)** - Network server design

---

**Last Updated:** January 28, 2026  
**Next Update:** v1.5.0 release (Q2 2026)
