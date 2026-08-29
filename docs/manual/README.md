# SharpCoreDB v2.0 Manual

> **SharpCoreDB v2.0 — performance-first.** An embedded, encrypted, SIMD-accelerated database
> engine and production gRPC server for .NET 10 / C# 14.
>
> Version 2.0.0.0 · MIT License · [GitHub](https://github.com/MPCoreDeveloper/SharpCoreDB)

---

## 1. Table of Contents

### Getting Started
- [1. Quick Start](quickstart.md) — install, open a database, first CRUD
- [2. Feature Overview](overview.md) — everything SharpCoreDB ships, at a glance

### Core Engine
- [3. Database Core](database.md) — storage modes, transactions & WAL, AES-256-GCM, metadata
- [4. Data Modeling](data-modeling.md) — data types, ULID, auto-rowid, collation, constraints
- [5. Indexing](indexing.md) — hash indexes, B-tree indexes, adaptive/expression/partial indexes
- [6. Querying](query.md) — SQL dialect, aggregates, window functions, joins, subqueries

### Performance
- [7. **Performance Guide**](performance.md) — **when SharpCoreDB is fastest and how to get there** ⭐
- [8. SIMD & Vector Engine](simd-vector.md) — analytics, vector search, GraphRAG, time-series

### Deployment & Ecosystem
- [9. Server Mode](server.md) — gRPC, REST, security, multitenancy, observability
- [10. Providers & Adapters](providers.md) — ADO.NET, EF Core, Dapper, linq2db, YesSql, Sync
- [11. Architecture Packages](ecosystem.md) — EventSourcing, CQRS, Projections, Distributed, Functional
- [12. Migration](migration.md) — from SQLite / LiteDB / RavenDB to SharpCoreDB

### Reference
- [13. Advanced Features, Integration & Troubleshooting](advanced.md) — stored procedures, views, triggers, ASP.NET Core patterns, unit testing, troubleshooting
- [14. Documentation Index](../INDEX.md) — the full docs hub
- [15. Performance Roadmap](../performance/V2_PERFORMANCE_PLAN.md) — v2.x plan + measured results
- [16. Changelog](../CHANGELOG.md)

---

## 2. How to use this manual

Each chapter is self-contained and includes runnable C# examples. Deep-dive documents live in
the [`docs/`](../INDEX.md) tree and are linked from the relevant chapters.

**Conventions**

| Mark | Meaning |
|------|---------|
| ✅ | Fully supported in v2.0 |
| ⚡ | Performance-relevant API — see the [Performance Guide](performance.md) |
| 🧩 | Optional package (separate NuGet) |

**Package quick reference**

| Package | Purpose |
|---------|---------|
| `SharpCoreDB` | The core engine (this manual covers it) |
| `SharpCoreDB.Data.Provider` | ADO.NET provider (`DbConnection`/`DbCommand`) |
| `SharpCoreDB.EntityFrameworkCore` | EF Core provider |
| `SharpCoreDB.Functional.Dapper` · `Functional.EntityFrameworkCore` · `Functional.Linq2DB` | functional adapters |
| `SharpCoreDB.Provider.YesSql` · `SharpCoreDB.Provider.Sync` | OrchardCore/YesSql + Dotmim.Sync |
| `SharpCoreDB.EventSourcing` · `Projections` · `CQRS` · `Distributed` | architecture packages |
| `SharpCoreDB.Server` · `Server.Core` · `Server.Protocol` | network server |
| `SharpCoreDB.VectorSearch` | vector index + similarity search |

---

## 3. Version history at a glance

| Version | Highlights |
|---------|-----------|
| **2.0.0** | **Performance-first release.** Zero-allocation `StructRow` reads, zero-reparse point-lookup fast path, SIMD numeric scan filtering, Native AOT readiness, allocation reduction, compiled regexes, cached DI. See the [Performance Guide](performance.md) for measured results (point reads **beat SQLite**; all operations **beat LiteDB**). |
| 1.9.x | WHERE IN regression fix, EF Core provider hardening, functional adapters, SonarCloud onboarding |
| ≤ 1.8 | Security, server, GraphRAG, vector, time-series feature growth |

See [`CHANGELOG.md`](../CHANGELOG.md) for the full history.
