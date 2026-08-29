# SharpCoreDB Feature Matrix (v2.0.0)

> Consolidated feature coverage by package, current for **v2.0.0**.
> Status: **2,412 tests / 0 failures** across all 15 test projects · Native AOT smoke verified (exit 0).
>
> ⚠️ **Single-file mode SQL limitations**: `.scdb` single-file mode does **not** support JOIN,
> GROUP BY, subqueries, aggregates, DELETE without WHERE, and other advanced SQL. Full matrix:
> [`docs/storage/SINGLE_FILE_SQL_LIMITATIONS.md`](storage/SINGLE_FILE_SQL_LIMITATIONS.md).

## Performance (v2.0 headline — ops/sec, two-run range)

| Operation | SharpCoreDB v2.0 | SQLite | LiteDB |
|-----------|-----------------:|-------:|-------:|
| READ — Direct / StructRow | **70–126K** | 87–97K | 14–16K |
| READ — SQL | 51–59K | 87–97K | 14–16K |
| INSERT (batch) | 91–133K | 145–150K | 66–77K |
| UPDATE (batch) | 41–59K | 241–296K | 10–11K |
| DELETE (batch) | 30–142K | 320–367K | 13–14K |
| Columnar `GROUP BY` SUM (10M rows) | **~2 ms** | ~1,300 ms | n/a |

> ✅ Point reads beat SQLite · every CRUD op beats LiteDB · analytics 100s of x faster than SQLite.
> Full analysis: [`docs/manual/performance.md`](manual/performance.md)

## v2.0 engine fast paths

| Capability | Detail |
|------------|--------|
| `ExecuteQueryStruct(sql, params)` | First-class zero-allocation struct-row SQL reads + cached `VariableLengthSchema` |
| `FindByPrimaryKey` / `FindByIndex` | No-SQL direct point reads (Direct API tier) |
| `SimpleSelectPlan` | Zero-reparse SELECT fast path for `SELECT … WHERE key = @p` plans |
| `NormalizeSql` (regex-free) | Allocation short-circuit for query-plan-cache key generation |
| Compiled regexes | Batch UPDATE/DELETE parsing, provider fast paths |
| SIMD numeric WHERE | `Vector<T>` batch filters for Integer/Long columns |
| Keyed `HashIndex.Add/Remove` | No full row copies on index maintenance |
| `LookupPositionsUnsafe` | No-copy position lookup with explicit write-lock contract |
| Cached `IGraphRagProvider` DI | No per-call `GetService` in `GetSharedSqlParser` |
| Removed hot-path debug I/O | No stray `D:\*.log` writes on SELECT/execute/transaction/INSERT |
| Native AOT readiness | AOT-safe `TypeConverter`, `Option<T>` reader, source-gen DTOs/JSON (`tools/SharpCoreDB.AotSmoke`) |

## Core platform

| Package | Purpose | Key capabilities in v2.0 |
|---------|---------|--------------------------|
| `SharpCoreDB` | Embedded core engine | AES-256-GCM encryption, SQL engine, ACID + WAL, hash/B-tree/expression/partial indexes, FTS, SIMD optimization, columnar storage, zero-allocation reads, time-series, GraphRAG SQL, `OPTIONALLY` + `IS SOME`/`IS NONE` optional SQL |
| `SharpCoreDB.Server` | Network server runtime | gRPC-first (HTTP/2 + HTTP/3), REST, WebSocket, JWT/RBAC, optional mTLS, multi-database hosting, health/metrics, RLS |
| `SharpCoreDB.Client` | .NET client | ADO.NET-style commands/readers, async access, parameterized execution, server connectivity |

## Data access and framework integrations

| Package | Purpose | Key capabilities in v2.0 |
|---------|---------|--------------------------|
| `SharpCoreDB.Data.Provider` | ADO.NET provider | `DbConnection`/`DbCommand`/`DbDataReader`, transactions, parameterized queries; v2.0 provider fast paths (span-based detection, `OPTIONALLY` check) |
| `SharpCoreDB.EntityFrameworkCore` | EF Core provider | Provider services, query translation, Guid/ULID keyed entities, reliable two-query relationship materialization |
| `SharpCoreDB.Extensions` | Productivity extensions | Dapper helpers, health checks, optional FluentMigrator integration |
| `SharpCoreDB.Provider.YesSql` | YesSql integration | Storage provider components for YesSql/Orchard-style patterns |
| `SharpCoreDB.Provider.Sync` | Dotmim.Sync provider | Sync adapter, tracking/tombstones, metadata/scope builders, cloud/offline sync |
| `SharpCoreDB.Serilog.Sinks` | Logging sink | Batch-oriented structured logging into SharpCoreDB |

## Analytics, vector, graph, and GraphRAG

| Package | Purpose | Key capabilities in v2.0 |
|---------|---------|--------------------------|
| `SharpCoreDB.Analytics` | Analytical SQL extension | 100+ aggregates, window functions, statistical analysis (`STDDEV`, `VARIANCE`, `PERCENTILE`, `CORRELATION`) |
| `SharpCoreDB.VectorSearch` | Vector retrieval | HNSW indexing, SIMD distance computations, quantization, sub-ms p50 at 10M+ vectors |
| `SharpCoreDB.Graph` | Graph traversal engine | BFS/DFS/bidirectional traversal, A* pathfinding, graph query helpers |
| `SharpCoreDB.Graph.Advanced` | Advanced graph analytics + GraphRAG | Community detection, centrality metrics, subgraph analysis, graph-aware ranking, DI-cached provider |

## Distributed and synchronization

| Package | Purpose | Key capabilities in v2.0 |
|---------|---------|--------------------------|
| `SharpCoreDB.Distributed` | Distributed runtime components | Multi-master replication, vector clocks, streaming replication, distributed transactions (2PC) |

## Event-driven optional architecture packages

| Package | Purpose | Key capabilities in v2.0 |
|---------|---------|--------------------------|
| `SharpCoreDB.EventSourcing` | Event persistence | Append-only streams, global ordered feed, snapshots, snapshot-aware loading, upcasting hooks |
| `SharpCoreDB.Projections` | Read model projection scaffold | Projection registration/runners, durable checkpoints, background hosted execution, OTel metrics |
| `SharpCoreDB.CQRS` | Command/outbox scaffold | Command handlers/dispatchers, aggregate root base, in-memory/persistent outbox, retry/dead-letter workflows |

## Functional package family

| Package | Purpose | Key capabilities in v2.0 |
|---------|---------|--------------------------|
| `SharpCoreDB.Functional` | Functional façade | `Option<T>`, `Fin<T>`, `Seq<T>`, functional-first database APIs |
| `SharpCoreDB.Functional.Dapper` | Functional Dapper adapter | Functional wrappers over Dapper read/write/query patterns |
| `SharpCoreDB.Functional.EntityFrameworkCore` | Functional EF Core adapter | Functional wrappers over `DbContext` workflows |
| `SharpCoreDB.Functional.Linq2DB` | linq2db adapter | Compile-time safe LINQ + `Option<T>`/`Fin<T>`/`Seq<T>` railway patterns, `BulkCopyAsync`, ULID/GUID/DateTime mappings, low-overhead queries |

## Quality and compatibility summary

- **2,412 tests / 0 failures** across all 15 test projects (+ JS/Python suites)
- **Native AOT smoke test** publishes and runs successfully (CREATE/INSERT/query/StructRow/reopen, exit 0)
- v2.0 is **drop-in backward compatible** with v1.9.x — no public API breaking changes
- .NET 10 / C# 14 toolchain (locked for v2.0.x); .NET 11 / C# 15 planned for v2.1

## Related docs

- `INDEX.md`
- `manual/README.md` — full manual
- `manual/performance.md` — performance guide
- `../README.md`

