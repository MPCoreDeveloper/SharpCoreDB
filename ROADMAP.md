# SharpCoreDB Roadmap

<div align="center">

**Last updated: v1.9.3 · Maintained by [@MPCoreDeveloper](https://github.com/MPCoreDeveloper)**

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
| 🗓️ | Planned near-term (target: v2.0 / v2.1) |
| 🔭 | Long-term research / high-complexity item |
| 💬 | Needs community input before scoping |

---

## ✅ Already Shipped (≤ v1.9.3)

### Core Engine
- ✅ **AES-256-GCM single-file encrypted database**
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
- ✅ **`SharpCoreDB.WebViewer`** — Razor Pages admin portal (table browser, query runner, live connection status)
- ✅ **.NET client SDK** (`SharpCoreDB.Client`, ADO.NET-style)
- ✅ **JavaScript/TypeScript SDK** (npm)
- ✅ **Python client** (`PySharpDB`)

---

## 🔶 In Progress

### Visual Query Execution Plan Explorer
- **Status:** Plan cache and optimizer internals exist; WebViewer UI not yet built
- **What's needed:** Tree/graph view in WebViewer showing join types, cost estimates, and row counts per node — similar to pgAdmin's EXPLAIN visualizer
- **Tracking:** [#issue](https://github.com/MPCoreDeveloper/SharpCoreDB/issues)

### Column-Level Security (CLS)
- **Status:** RLS is fully shipped. CLS (per-column GRANT masks and data redaction) is the next security layer
- **What's needed:** Column redaction policies, `MASKED WITH` syntax, integration with the RBAC engine

---

## 🗓️ Near-Term Roadmap (v2.0 / v2.1)

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

### Visual Query Execution Plan Explorer (WebViewer)
> **Why:** `QueryPlanCache` and `ExecutionPlan` already expose all node data. Only the UI is missing.

- Interactive tree/graph visualizer in WebViewer
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
> Beyond the near-term WebViewer plan explorer — advanced profiling tooling.

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

### Management Dashboard Expansion (WebViewer Pro)
> **Status:** WebViewer ships today. These are planned additions.

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
| **v2.0** | Let's Encrypt/ACME, Visual EXPLAIN in WebViewer, Column-Level Security |
| **v2.1** | Enterprise Backup Orchestrator, Backup retention + remote targets |
| **v2.2** | PITR (requires v2.1 backup foundation) |
| **v2.x** | Automated Data Tiering, full OTel distributed tracing, advanced plan profiling |

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
