# SharpCoreDB Roadmap

<div align="center">

**Last updated: v2.0.0-preview.3 · Maintained by [@MPCoreDeveloper](https://github.com/MPCoreDeveloper)**

</div>

> This roadmap reflects what is **actually in the codebase today** and what is planned next.
> Features are derived from real source files, not marketing copy.
> Community votes on [GitHub Issues](https://github.com/MPCoreDeveloper/SharpCoreDB/issues) directly influence priority order.

---

## Legend

| Symbol | Meaning |
|--------|---------|
| ✅ | Shipped and production-ready |
| 🔶 | Partially implemented — foundation exists, full feature in progress |
| 🗓️ | Planned near-term (target: v2.1 / v2.2) |
| 🔭 | Long-term research / high-complexity item |
| 💬 | Needs community input before scoping |

---

## ✅ Already Shipped (≤ v1.9.6)


### Core Engine
- ✅ **AES-256-GCM single-file encrypted database** — full at-rest encryption: block data AND
  the block registry, free-space map and WAL are ciphertext (no metadata leakage); envelope
  key model (password → PBKDF2 → wrapped DEK) with password/key rotation APIs
  (`ChangeEncryptionPasswordAsync` / `RotateEncryptionKeyAsync`)
- ✅ **ACID transactions + WAL** (`RecoveryManager`, crash recovery tests passing)
- ✅ **B-tree and hash indexing**
- ✅ **Full-text search**
- ✅ **SIMD acceleration** — `Vector256.LoadUnsafe` in columnar aggregate hot paths
- ✅ **Memory pooling + JIT-oriented performance optimizations**
- ✅ **100+ aggregate functions** (COUNT, SUM, AVG, STDDEV, PERCENTILE, CORRELATION, …)
- ✅ **Window functions** (ROW_NUMBER, RANK, DENSE_RANK, LAG, LEAD)
- ✅ **Query plan cache** (`QueryPlanCache`, `ExecutionPlan`, `QueryOptimizer`)

### Network Server
- ✅ **gRPC over HTTPS** (HTTP/2 + HTTP/3) — primary protocol
- ✅ **Binary TCP handler, REST API, WebSocket streaming** — secondary protocols
- ✅ **Multi-database hosting + system databases** (`master`, `msdb`, `tempdb`)
- ✅ **TLS 1.2+ enforced** — no plain HTTP endpoints
- ✅ **JWT authentication + optional mTLS**
- ✅ **RBAC** (Admin / Writer / Reader roles)
- ✅ **Rate limiting** (fixed-window, per-IP, configurable)
- ✅ **Connection pooling** (1,000+ concurrent connections)
- ✅ **Health checks + Prometheus-compatible metrics endpoint**
- ✅ **Graceful shutdown + production deployment** (Docker, Windows Service, Linux systemd, macOS launchd)

### Security
- ✅ **Row-Level Security (RLS)** — `RowLevelPolicyEngine` with `Enforced`/`Audit` modes and per-tenant discriminator-column filtering (`src/SharpCoreDB.Server.Core/Security/`)

### Analytics & Search
- ✅ **Vector search** — HNSW indexing with SIMD acceleration, 10M+ vector workloads validated
- ✅ **Graph traversal** — BFS, DFS, bidirectional, A* pathfinding
- ✅ **GraphRAG** — community detection (Louvain, LPA), centrality metrics (degree, betweenness, eigenvector), subgraph analysis

### Optional Packages
- ✅ **`SharpCoreDB.EventSourcing`** — append-only per-stream storage, global ordered feed, in-memory + persistent stores, snapshot policy
- ✅ **`SharpCoreDB.Projections`** — checkpoint persistence, OpenTelemetry-ready projection metrics
- ✅ **`SharpCoreDB.CQRS`** — command/handler abstractions, aggregate root, outbox with dead-letter workflow
- ✅ **`SharpCoreDB.EntityFrameworkCore`** — full Guid-keyed entity CRUD, relationship materialization, 22/22 integration tests passing
- ✅ **`SharpCoreDB.Distributed`** — multi-master replication with vector clocks, distributed transactions (2PC)
- ✅ **`SharpCoreDB.Provider.Sync`** — Dotmim.Sync provider for bidirectional cloud/edge data sync
- ✅ **Time-series cold tiering** — `BucketTier.Hot/Cold`, archival manager, retention policies (`src/SharpCoreDB/TimeSeries/`)

### Tooling
> **Note:** the graphical UI (formerly `SharpCoreDB.Viewer` / `SharpCoreDB.WebViewer`) has moved to
> the standalone repo **[MPCoreDeveloper/SCDMS](https://github.com/MPCoreDeveloper/SCDMS)**.
- ✅ **SCDMS** — management UI (Razor Pages web admin portal: table browser, query runner, live connection status)
- ✅ **.NET client SDK** (`SharpCoreDB.Client`, ADO.NET-style)
- ✅ **JavaScript/TypeScript SDK** (npm)
- ✅ **Python client** (`PySharpDB`)

---

## ✅ Shipped in v2.0 (performance-first release)

> v2.0 closes the v1.x benchmark gap (was **16–52x slower than SQLite** on point reads/updates/deletes).
> Measured results: **point reads beat SQLite**, every CRUD op beats LiteDB, SIMD analytics ~682x vs SQLite.

- ✅ **Zero-allocation reads** — first-class `ExecuteQueryStruct(sql, params)` + cached `VariableLengthSchema`; Direct API (`FindByPrimaryKey`/`FindByIndex`) point reads
- ✅ **Zero-reparse SELECT fast path** — `SimpleSelectPlan` resolves `SELECT … WHERE key = @p` from the plan cache without re-lexing
- ✅ **SIMD numeric WHERE filters** — `Vector<T>` batch predicates for Integer/Long columns
- ✅ **Compiled regexes** on all hot paths; regex-free `NormalizeSql` with allocation short-circuit
- ✅ **Keyed index maintenance** — `HashIndex.Add/Remove` without full row copies; no `new Dictionary(row)` in `UpdateMultiple`
- ✅ **Cross-page record relocation on UPDATE (WP10)** — the engine returns the actual post-write `(page, slot)` reference so the PK index and dirty-tracking re-point to the real record after a growing update moves it
- ✅ **In-place fixed-size UPDATE patches (WP11)** — single-row UPDATE and PK-lookup batch paths overwrite only the changed fields at cached fixed column offsets instead of a deserialize → re-serialize round trip (safe fallback to full serialization for growing variable-length fields)
- ✅ **Unified DELETE core (WP12)** — shared `DeleteRecordsCore` + key-only hash index cleanup; `DeleteByPrimaryKey` skips row reads when no hash indexes are loaded
- ✅ **Exact-size row serialization (WP13)** — one allocation per insert/update instead of `ArrayPool.Rent` + `Span.ToArray()` (double allocation + extra copy); per-update index maintenance snapshots only the PK + indexed keys instead of a full row dictionary copy
- ✅ **Schema-aware delta codec (WP13)** — `DeltaCodec.EncodeDelta/ApplyDelta` operate on the real field layout (no more fixed 8-byte blocks); `EnableDeltaUpdates` wiring records per-update delta byte savings (`TotalDeltaUpdates` / `DeltaBytesSaved`) for monitoring
- ✅ **`LookupPositionsUnsafe`** — no-copy position lookup with explicit write-lock contract
- ✅ **Cached DI** — `IGraphRagProvider` resolved once, not per call
- ✅ **Removed hot-path debug I/O** — no more stray `D:\*.log` writes per SELECT/execute/transaction/INSERT
- ✅ **Provider fast paths** — `OPTIONALLY` keyword check, span-based single-file/sqlite_master detection
- ✅ **Native AOT readiness** — AOT-safe `TypeConverter`, `Option<T>` reader, source-gen DTOs/JSON; `tools/SharpCoreDB.AotSmoke` publishes + runs (exit 0)
- ✅ **2,412 tests / 0 failures** across all 15 test projects
- ✅ **Fixed regression** — positional `?` placeholders now fall back to the legacy binder (were treated as literals)
- ✅ **Envelope encryption + full at-rest metadata encryption** — password-based per-file DEK (PBKDF2-HMAC-SHA256), encrypted block registry / FSM / WAL; `ChangeEncryptionPasswordAsync` / `RotateEncryptionKeyAsync` rotation APIs (#341 follow-on)
- ✅ **Block-level Brotli/GZip compression** — single-file (`.scdb`) storage, per-block, before-encryption / after-decryption (#344)
- ✅ **Configurable metadata region sizes** — `FsmSizePages` / `BlockRegistrySizePages` / `TableDirectorySizePages` + byte-based file extension (~10 MB regardless of PageSize) (#345)
- ✅ **Unicode & large-blob regression coverage** — CJK/emoji/RTL/combining characters, 16 MB blob block chaining (#346)

> 📊 Full details + honest guidance: [`docs/manual/performance.md`](docs/manual/performance.md) · plan: [`docs/performance/V2_PERFORMANCE_PLAN.md`](docs/performance/V2_PERFORMANCE_PLAN.md)

---

## 🔶 In Progress


### Visual Query Execution Plan Explorer
- **Status:** Plan cache and optimizer internals exist; SCDMS UI not built yet
- **What's needed:** Tree/graph view in SCDMS showing join types, cost estimates, and row counts per node — similar to pgAdmin's EXPLAIN visualizer
- **Tracking:** [#issue](https://github.com/MPCoreDeveloper/SharpCoreDB/issues)

### Column-Level Security (CLS)
- **Status:** RLS is fully shipped. CLS (per-column GRANT masks and data redaction) is the next security layer
- **What's needed:** Column redaction policies, `MASKED WITH` syntax, integration with the RBAC engine

---

## 🗓️ Near-Term Roadmap (v2.1 / v2.2)

### Performance: Close the UPDATE/DELETE gap vs SQLite (v2.1)
>> **Why:** v2.0 closed the read/insert gap; single-row UPDATE/DELETE is still ~5–7x behind SQLite
>> because row-store writes are append-on-update instead of in-place.

- ✅ **In-place record updates for columnar/append-only (#6)** — fixed-width/unchanged-length
  records overwrite their slot; no file growth
- ✅ **Single-pass SQL DELETE/UPDATE (#7/#8)** — no more double materialization for RETURNING /
  `CHANGES()`; PK fast path in `Delete`/`DeleteMultiple`/`UpdateMultiple`
- ✅ **Field-level in-place patch on columnar UPDATE (fixed-width layout step)** — only the changed
  fields are patched at their actual record offsets; no full re-serialize; fixed-size fields keep
  the record length stable (in-place, no file growth), even after variable-length TEXT columns
- ✅ **Out-of-line overflow (B1, opt-in)** — `DatabaseConfig.FixedWidthRecordLayout`: constant-size
  records + per-table overflow arena for TEXT/BLOB; every UPDATE is in-place (fixed or variable col)
- ✅ **Arena GC (B3)** — overflow arena compacts together with the data file (copy-on-compact)
- ✅ **Constant-offset read wins (B4)** — early-WHERE on constant slot offsets for fixed-width
  (numeric direct reads, string arena-payload compare, StructRow numeric-SIMD batch filter)
- ⬜ Migration (B5)
- ⬜ Storage-level DELETE reuse (free-slot reuse / compaction on PageBased)

### Single-file `.scdb` (A-track)
- ✅ **PK hash index (A1)** — O(1) point reads (`FindByPrimaryKey`, `SELECT … WHERE pk = value`)
- ✅ **In-place block overwrite (A2)** — same-length updates do not grow the `.scdb` (pinned)
- ⬜ Delta/incremental flush (A3); unify onto the columnar format (A4)
- Track in [`docs/performance/V2_PERFORMANCE_PLAN.md`](docs/performance/V2_PERFORMANCE_PLAN.md)

---

### .NET 11 / C# 15 migration (v2.1, after Nov 2026 GA)
>> **Why:** Runtime Async, AVX-VNNI-512/SVE2, SIMD lane APIs, Zstandard, Decimal32/64/128 are
>> automatic wins on hot paths. v2.0.x stays on .NET 10 / C# 14 (locked).

- `LangVersion` 14 → 15; keep `net10.0` via `TargetFrameworks` during transition
- Adopt Runtime Async in async hot paths; AVX-VNNI-512/SVE2 behind `SIMD_ENABLED` guards
- Optional Zstandard WAL/page compression flag (default off)

---

### Clean remaining Native AOT warnings (v2.1)
>> **Why:** AOT smoke passes; remaining non-blocking warnings are B-tree reflection
>> (`TryBTreeRangeScan`/`BTreeIndexManager` via `GetMethod`/`Activator.CreateInstance`),
>> `.scdb` single-file JSON reflection, and `ParseVectorValue`.

- Refactor B-tree index activation from reflection to an interface-based factory
- Move `.scdb` / `ParseVectorValue` JSON to source-generated context

---

### Let's Encrypt / ACME Auto-Renewal
> **Why:** Today the server requires manual PFX/PEM cert paths. Self-hosted deployments must manage certificate rotation by hand.

- Integrate ACME protocol (via [Certes](https://github.com/fszlin/certes) or `LettuceEncrypt`) into Kestrel startup
- Zero-touch certificate provisioning and auto-renewal for self-hosted server
- `appsettings.json` switch: `"AcmeEnabled": true` alongside existing `TlsCertificatePath`
- Target: Linux/Docker-first, then Windows Service

```json
"Security": {
  "AcmeEnabled": true,
  "AcmeDomain": "mydb.example.com",
  "AcmeEmail": "admin@example.com"
}
```

---

### Enterprise Backup Orchestrator
> **Why:** The WAL and `RecoveryManager` are the foundation, but there is no scheduled/streaming backup engine yet.

- Scheduled full, incremental, and differential backups
- Remote target support: **Azure Blob Storage**, **AWS S3**, **SFTP**
- Backup retention policies (keep last N, time-window based)
- Backup catalog with integrity verification (checksum + test-restore)
- `SharpCoreDB.Backup` optional NuGet package
- REST + gRPC management endpoints (`/backup/start`, `/backup/list`, `/backup/restore`)

---

### Visual Query Execution Plan Explorer (SCDMS)
> **Why:** `QueryPlanCache` and `ExecutionPlan` already expose all node data. Only the UI is missing.

- Interactive tree/graph visualizer in SCDMS
- Show operator type, estimated/actual row count, cost, index used
- Highlight bottleneck nodes (slowest % of total cost)
- Export plan as JSON or SVG

---

### Column-Level Security (CLS / Data Masking)
> **Why:** Completes the security story started with RLS in v1.9.3.

- `MASKED WITH (FUNCTION = ...)` DDL syntax
- Built-in masking functions: `default()`, `email()`, `partial()`, `random()`
- Policy enforcement inside the SQL execution engine (not at API proxy layer)
- `GRANT UNMASK` privilege to bypass masking for privileged roles

---

## 🔭 Long-Term Roadmap (v2.x+)

### Point-in-Time Recovery (PITR)
> **Why:** Application bugs or human errors in high-throughput clusters carry a high risk of data loss without microsecond-level rollback.

- **Prerequisite:** Enterprise Backup Orchestrator (above) must ship first
- Continuous WAL/transaction log shipping to remote storage
- LSN-stamped log stream with sub-second granularity
- `RESTORE DATABASE mydb TO TIMESTAMP '2025-06-01 14:32:00.000'`
- Distributed-node replay coordination (builds on existing `TransactionLog` in `SharpCoreDB.Distributed`)
- Target: no data loss window under 1 second in single-node, <5 seconds in distributed cluster

---

### Automated Data Tiering (Hierarchical Storage)
> **Why:** At huge scale, keeping all data on local NVMe is cost-prohibitive. Cold historical blocks should transparently migrate to object storage.

- **Status:** Time-series `ArchivalManager` already implements `BucketTier.Hot/Cold` for time-series data. Needs to extend to general page-based storage.
- Transparent hot (NVMe) → cold (Azure Blob / AWS S3) block migration
- Query router continues to serve cold data without schema or SQL changes
- Policy-driven tiering rules (age threshold, access frequency, size)
- Read-back warming cache for frequently accessed cold blocks
- `SharpCoreDB.Tiering` optional NuGet package

---

### Deep Query Execution Plan Visualizer (Advanced)
> Beyond the near-term SCDMS plan explorer — advanced profiling tooling.

- Runtime query profiling with actual vs estimated row counts
- Per-operator memory and CPU time breakdown
- Historical plan regression detection (alert when plan changes cause slowdowns)
- Exportable plan traces compatible with external tools

---

### Telemetry & Observability Expansion
> **Status:** Prometheus metrics and OpenTelemetry projection metrics already ship. This expands coverage.

- Full distributed tracing (OTel spans across gRPC + embedded operations)
- Query-level trace context propagation
- Built-in Grafana dashboard template (`SharpCoreDB.Grafana.json`)
- Structured log enrichment (query ID, database, user, latency histogram)

---

### Management Dashboard Expansion (SCDMS Pro)
> **Status:** SCDMS (the management UI) has moved to github.com/MPCoreDeveloper/SCDMS. These additions are planned in SCDMS.

- 📊 Live connection monitor (active queries, blocked sessions, lock waits)
- 📈 Performance dashboard (QPS, latency P50/P95/P99, cache hit rate)
- 🗄️ Backup / restore management UI
- 🔒 Security audit log viewer (RLS policy hits, failed auth attempts)
- 📋 Query history + plan comparison
- 🧩 Schema designer with type picker (including ULID and GUID)

---

## 💬 Community Input Needed

These are ideas raised by the community that need more design work or votes before committing to a release target:

| Feature | Discussion |
|---------|-----------|
| **Zero-Knowledge Sync** | E2E encrypted sync where the server never sees plaintext |
| **Offline Queue** | Queue writes while disconnected, replay on reconnect |
| **Vector Sync** | Sync embeddings between edge and server for local-first AI |
| **Graph Sync** | Sync graph edges/nodes in the Provider.Sync pipeline |
| **WebSocket Push** | Real-time data push instead of poll-based sync |
| **Selective Column Sync** | Sync only specific columns per table |

> 👉 **Vote or add ideas at:** [GitHub Issues](https://github.com/MPCoreDeveloper/SharpCoreDB/issues)

---

## How Priority Is Determined

1. **Community votes** — Issues with the most 👍 reactions get prioritized
2. **Security and reliability** — Security features and data safety items move up automatically
3. **Ecosystem completeness** — Features that unlock new use cases (PITR, CLS, ACME) over pure performance work
4. **Effort/impact ratio** — Let's Encrypt (medium effort, huge DevEx win) ships before PITR (very high effort)

---

## Version Targets (Tentative)

| Version | Focus |
|---------|-------|
| **v2.0** ✅ | **Performance-first release** — closed the 16–52x benchmark gap (point reads beat SQLite, all ops beat LiteDB), zero-allocation reads, SIMD filters, Native AOT readiness |
| **v2.1** | Close UPDATE/DELETE gap vs SQLite (in-place writes, fixed-width records), **.NET 11 / C# 15 migration (🔶 in progress on `release/v2.1.0.0` — Phase 0 toolchain done)**, AOT warning cleanup, Let's Encrypt/ACME |
| **v2.2** | Enterprise Backup Orchestrator, backup retention + remote targets, Column-Level Security |
| **v2.x** | PITR (requires backup foundation), Automated Data Tiering, full OTel distributed tracing, advanced plan profiling |

> Targets are indicative. Issues and PRs from the community can accelerate any item.

---

## Contributing

Have a feature idea? Found a bug? Want to work on a roadmap item?

- 🐛 [Open a bug report](https://github.com/MPCoreDeveloper/SharpCoreDB/issues/new)
- 💡 [Propose a feature](https://github.com/MPCoreDeveloper/SharpCoreDB/issues/new)
- 🔀 [Submit a PR](https://github.com/MPCoreDeveloper/SharpCoreDB/pulls)

All contributions follow the standards in `.github/CODING_STANDARDS_CSHARP14.md`.

---

**Made with ❤️ for the .NET community**
