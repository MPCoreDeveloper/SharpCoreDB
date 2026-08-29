# 2. Feature Overview

> Everything SharpCoreDB ships in v2.0, organized by area. Each item links to the deep-dive docs.

---

## 2.1 Core Engine (`SharpCoreDB`)

| Feature | Description | Docs |
|---------|-------------|------|
| Directory & single-file storage | `DirectoryStorageProvider` (default) and `.scdb` single-file (block-based, compressed metadata) | [`docs/storage/README.md`](../storage/README.md) |
| Append-only engine | Highest-throughput sequential writes for bulk loads | [`docs/storage/STORAGE_MODE_GUIDANCE.md`](../storage/STORAGE_MODE_GUIDANCE.md) |
| Page-based engine | In-place updates for OLTP workloads | same |
| Columnar storage | SIMD-accelerated analytics, per-table `STORAGE = COLUMNAR` | [`docs/analytics/README.md`](../analytics/README.md) |
| ACID + WAL | Transactions, crash recovery, group-commit WAL, batched durability | [`docs/storage/README.md`](../storage/README.md) |
| AES-256-GCM encryption | Encrypted metadata + data; encrypted single-file or per-record at rest | [`docs/storage/README.md`](../storage/README.md) |
| ULID primary keys | Auto-generated sortable IDs (`_rowid` support, SQLite-style) | [`docs/features/AUTO_ROWID.md`](../features/AUTO_ROWID.md) |
| Collation | `BINARY`, `NOCASE`, `RTRIM`, `UNICODE_CI`, `LOCALE("xx_XX")` | [`docs/collation/COLLATION_GUIDE.md`](../collation/COLLATION_GUIDE.md) |

## 2.2 Data Modeling

- **Data types:** `TEXT`, `INTEGER`, `LONG`, `REAL`, `DECIMAL`, `BOOLEAN`, `DATETIME`, `GUID`, `ULID`, `BLOB`, `ROWREF`
- **Constraints:** `PRIMARY KEY`, `FOREIGN KEY`, `UNIQUE`, `NOT NULL`, `CHECK`, `DEFAULT`
- **Auto-increment** counters persisted across restarts; **auto-ULID** and **auto-GUID** columns
- **Internal `_rowid`** (SQLite-compatible hidden column) for tables without an explicit PK

See [Data Modeling](data-modeling.md) and [`docs/serialization/README.md`](../serialization/README.md).

## 2.3 Querying (SQL)

- Full DML + DDL: `SELECT`, `INSERT` (multi-row, `INSERT … SELECT`, `ON CONFLICT`), `UPDATE`, `DELETE`, `CREATE/DROP TABLE/INDEX/VIEW/TRIGGER/PROCEDURE`
- `WHERE` with `=, <>, >, <, >=, <=, IN, NOT IN, BETWEEN, LIKE, GLOB, REGEXP`, `AND/OR`, `IS NULL`
- **ORDER BY, LIMIT, OFFSET, DISTINCT**, `COLLATE` clauses
- **100+ aggregate functions** (COUNT, SUM, AVG, STDDEV, VARIANCE, PERCENTILE, CORRELATION, …)
- **Window functions** (ROW_NUMBER, RANK, DENSE_RANK, LAG, LEAD, …)
- **Joins** (INNER/LEFT/RIGHT/FULL/CROSS), **subqueries** (derived tables, CTEs with `WITH RECURSIVE`)
- **Parameterized queries** via `@name`, `:name`, or positional `?` placeholders

See [Querying](query.md), [`docs/sql/SQL_DIALECT_EXTENSIONS_v1.7.2.md`](../sql/SQL_DIALECT_EXTENSIONS_v1.7.2.md), and
[`docs/internals/OPTIMIZER_GUIDE.md`](../internals/OPTIMIZER_GUIDE.md).

## 2.4 Indexing

- **Hash index** — O(1) point lookups; the v2.0 fast path uses it for zero-allocation reads
- **B-tree index** — range scans, `BETWEEN`, `ORDER BY` optimization
- **Expression index**, **partial index**, **unique index**
- **Adaptive index manager** — picks hash vs B-tree based on query shape

See [Indexing](indexing.md) and [`docs/internals/OPTIMIZER_ARCHITECTURE.md`](../internals/OPTIMIZER_ARCHITECTURE.md).

## 2.5 Analytics & Vector (`SharpCoreDB`, `SharpCoreDB.Analytics`, `SharpCoreDB.VectorSearch`)

- **Columnar SIMD analytics** — SUM/AVG/MIN/MAX on millions of rows in milliseconds
  (`Vector128/256/512`, AVX2/AVX-512/NEON with scalar fallback)
- **Vector search** — HNSW index, SIMD distance kernels, 10M+ vector workloads
- **GraphRAG** — community detection (Louvain, LPA), centrality (degree/betweenness/eigenvector),
  subgraph analysis, hybrid graph + vector retrieval
- **Graph traversal** — BFS, DFS, bidirectional, A* pathfinding

See [SIMD & Vector Engine](simd-vector.md), [`docs/Vectors/README.md`](../Vectors/README.md),
[`docs/graphrag/00_START_HERE.md`](../graphrag/00_START_HERE.md).

## 2.6 Time-Series

- Bucketed storage (`BucketManager`), hot/cold tiering + archival, retention policies
- `Gorilla`/`XOR`/`Delta-of-Delta` compression codecs, downsampling engine, time-range pushdown

See [`docs/features/README.md`](../features/README.md) and `src/SharpCoreDB/TimeSeries/`.

## 2.7 Server Mode (`SharpCoreDB.Server`)

- gRPC over HTTP/2 + HTTP/3, REST, binary TCP, WebSocket streaming
- JWT + optional mTLS, RBAC (Admin/Writer/Reader), rate limiting, connection pooling
- Multitenancy, system databases (`master`, `msdb`, `tempdb`), Prometheus metrics, health checks
- Deployable as Docker container, Windows service, systemd, launchd

See [Server Mode](server.md) and [`docs/server/README.md`](../server/README.md).

## 2.8 Providers & Ecosystem

- **ADO.NET** (`SharpCoreDB.Data.Provider`) — `DbConnection`/`DbCommand`/`DbDataReader`
- **EF Core** (`SharpCoreDB.EntityFrameworkCore`) — full provider with Guid/ULID key support
- **Dapper / EF Core / linq2db functional adapters** — `Option<T>`, `Fin<T>`, `Seq<T>`
- **YesSql / OrchardCore** provider
- **Dotmim.Sync** bidirectional sync provider
- **EventSourcing, Projections, CQRS, Distributed** architecture packages

See [Providers & Adapters](providers.md) and [Architecture Packages](ecosystem.md).

## 2.9 Performance (v2.0 headline)

| Workload | Result |
|----------|--------|
| Point reads (Direct API / `ExecuteQueryStruct`) | **~120K/s — beats SQLite (~90K/s)** |
| Batch inserts (`InsertBatch`) | **~100–130K/s** |
| Columnar SIMD aggregates | **~682x faster than SQLite** for `GROUP BY` SUM |
| Everything vs LiteDB | **reads ~5–8x, updates ~5x, deletes ~6–10x faster** |

Full details + how to get these numbers: **[Performance Guide](performance.md)**.
