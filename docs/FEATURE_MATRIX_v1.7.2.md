# SharpCoreDB Feature Matrix (v1.7.2)

This page consolidates the major SharpCoreDB capabilities by package for quick discovery.

> ⚠️ **Single-File mode SQL limitations**
>
> Features marked below apply to **Directory mode** (`Database` class, `.db` folder) unless otherwise noted.
> `.scdb` single-file mode (`SingleFileDatabase`) uses a **regex-based SQL parser** and does **not** support
> JOIN, GROUP BY, subqueries, aggregates, DELETE without WHERE, and other advanced SQL.
>
> → Full matrix: [`docs/storage/SINGLE_FILE_SQL_LIMITATIONS.md`](storage/SINGLE_FILE_SQL_LIMITATIONS.md)

## Core platform

| Package | Purpose | Key capabilities in v1.7.2 |
|---|---|---|
| `SharpCoreDB` | Embedded core engine | AES-256-GCM encryption, SQL engine, ACID + WAL, indexing, FTS, SIMD optimizations, metadata durability fixes, compiled-query parser fixes, `GRAPH_RAG` SQL clause, `OPTIONALLY` + `IS SOME`/`IS NONE` optional SQL semantics |
| `SharpCoreDB.Server` | Network server runtime | gRPC-first (HTTP/2 + HTTP/3), REST, WebSocket, JWT/RBAC, optional mTLS, multi-database hosting, health/metrics |
| `SharpCoreDB.Client` | .NET client | ADO.NET-style commands/readers, async access, parameterized execution, server connectivity |

## Data access and framework integrations

| Package | Purpose | Key capabilities in v1.7.2 |
|---|---|---|
| `SharpCoreDB.Data.Provider` | ADO.NET provider | `DbConnection`/`DbCommand`/`DbDataReader`, transactions, parameterized queries |
| `SharpCoreDB.EntityFrameworkCore` | EF Core provider | Provider services, query translation components, migration/update SQL generation |
| `SharpCoreDB.Extensions` | Productivity extensions | Dapper helpers, health checks, optional FluentMigrator integration |
| `SharpCoreDB.Provider.YesSql` | YesSql integration | Storage provider components for YesSql/Orchard-style patterns |
| `SharpCoreDB.Serilog.Sinks` | Logging sink | Batch-oriented structured logging into SharpCoreDB |

## Analytics, vector, graph, and GraphRAG

| Package | Purpose | Key capabilities in v1.7.2 |
|---|---|---|
| `SharpCoreDB.Analytics` | Analytical SQL extension | 100+ aggregates, window functions, statistical analysis (`STDDEV`, `VARIANCE`, `PERCENTILE`, `CORRELATION`) |
| `SharpCoreDB.VectorSearch` | Vector retrieval | HNSW indexing, SIMD distance computations, quantization, semantic search workflows |
| `SharpCoreDB.Graph` | Graph traversal engine | BFS/DFS/bidirectional traversal, A* pathfinding, graph query helpers |
| `SharpCoreDB.Graph.Advanced` | Advanced graph analytics + GraphRAG | Community detection, centrality metrics, subgraph analysis, graph-aware ranking, profiling and caching helpers |

## Distributed and synchronization

| Package | Purpose | Key capabilities in v1.7.2 |
|---|---|---|
| `SharpCoreDB.Distributed` | Distributed runtime components | Multi-master replication, vector clocks, streaming replication components, distributed transaction primitives |
| `SharpCoreDB.Provider.Sync` | Dotmim.Sync provider | Sync adapter, tracking/tombstones, metadata/scope builders, provider integration for cloud/offline sync |

## Event-driven optional architecture packages

| Package | Purpose | Key capabilities in v1.7.2 |
|---|---|---|
| `SharpCoreDB.EventSourcing` | Event persistence | Append-only streams, global ordered feed, snapshots, snapshot-aware loading, upcasting hooks |
| `SharpCoreDB.Projections` | Read model projection scaffold | Projection registration/runners, durable checkpoints, background hosted execution, OTel metrics |
| `SharpCoreDB.CQRS` | Command/outbox scaffold | Command handlers/dispatchers, aggregate root base, in-memory/persistent outbox, retry/dead-letter workflows |

## Functional package family (new in v1.7.2 line)

| Package | Purpose | Key capabilities in v1.7.2 |
|---|---|---|
| `SharpCoreDB.Functional` | Functional façade | `Option<T>`, `Fin<T>`, `Seq<T>`, functional-first database APIs |
| `SharpCoreDB.Functional.Dapper` | Functional Dapper adapter | Functional wrappers over Dapper read/write/query patterns |
| `SharpCoreDB.Functional.EntityFrameworkCore` | Functional EF Core adapter | Functional wrappers over `DbContext` workflows |

## Quality and compatibility summary

- `1,490+` tests passing across workspace and targeted suites
- Ecosystem package synchronization on `v1.7.2`
- No intended breaking changes from `v1.5.0` to `v1.7.2`

## Related docs

- `INDEX.md`
- `README.md`
- `release/PHASE12_RELEASE_NOTES.md`
- `../README.md`
