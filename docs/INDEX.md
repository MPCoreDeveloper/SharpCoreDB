# SharpCoreDB Documentation Index

**Status:** Active documentation set (`v2.0.0`)

## 0. Manual (start here)

The full feature manual with an index of every capability, code examples, and the performance
guide explaining when SharpCoreDB is fastest.

- `manual/README.md` — **Manual index** (table of contents)
- `manual/quickstart.md` — first CRUD in 30 seconds
- `manual/overview.md` — every feature at a glance
- `manual/database.md` — storage, transactions & WAL, encryption
- `manual/data-modeling.md` — types, ULID/rowid keys, constraints, collation
- `manual/indexing.md` — hash, B-tree, expression, partial indexes
- `manual/query.md` — SQL dialect, aggregates, window functions, joins, subqueries
- `manual/performance.md` — ⭐ **Performance Guide** (measured v2.0 numbers, API ladder, examples)
- `manual/simd-vector.md` — SIMD analytics, vector search, GraphRAG, time-series
- `manual/server.md` — gRPC/REST server, security, multitenancy, observability
- `manual/providers.md` — ADO.NET, EF Core, Dapper, linq2db, YesSql, Sync
- `manual/ecosystem.md` — EventSourcing, CQRS, Projections, Distributed, Functional
- `manual/migration.md` — migrate from SQLite / LiteDB / RavenDB

## 1. Project Entry Points

- `../README.md`
- `README.md`
- `FEATURE_MATRIX.md`
- `PROJECT_STATUS.md`
- `CHANGELOG.md`
- `CONTRIBUTING.md`
- `UseCases.md`
- `manual/README.md` — the v2.0 manual (see Section 0)


## 2. Package Documentation (src)

- `../src/SharpCoreDB/README.md`
- `../src/SharpCoreDB/NuGet.README.md`
- `../src/SharpCoreDB/PLATFORM_SUPPORT.md`
- `../src/SharpCoreDB.Server/README.md`
- `../src/SharpCoreDB.Server/NuGet.README.md`
- `../src/SharpCoreDB.Client/README.md`
- `../src/SharpCoreDB.Client/NuGet.README.md`
- `../src/SharpCoreDB.Data.Provider/README.md`
- `../src/SharpCoreDB.Data.Provider/NuGet.README.md`
- `../src/SharpCoreDB.EntityFrameworkCore/README.md`
- `../src/SharpCoreDB.EntityFrameworkCore/NuGet.README.md`
- `../src/SharpCoreDB.EntityFrameworkCore/USAGE.md`
- `../src/SharpCoreDB.Extensions/README.md`
- `../src/SharpCoreDB.Extensions/NuGet.README.md`
- `../src/SharpCoreDB.Analytics/README.md`
- `../src/SharpCoreDB.Analytics/NuGet.README.md`
- `../src/SharpCoreDB.VectorSearch/README.md`
- `../src/SharpCoreDB.VectorSearch/NuGet.README.md`
- `../src/SharpCoreDB.Graph/README.md`
- `../src/SharpCoreDB.Graph/NuGet.README.md`
- `../src/SharpCoreDB.Graph.Advanced/README.md`
- `../src/SharpCoreDB.Graph.Advanced/NuGet.README.md`
- `../src/SharpCoreDB.Distributed/README.md`
- `../src/SharpCoreDB.Distributed/NuGet.README.md`
- `../src/SharpCoreDB.Provider.Sync/README.md`
- `../src/SharpCoreDB.Provider.Sync/NuGet.README.md`
- `../src/SharpCoreDB.EventSourcing/README.md`
- `../src/SharpCoreDB.EventSourcing/NuGet.README.md`
- `../src/SharpCoreDB.Projections/README.md`
- `../src/SharpCoreDB.Projections/NuGet.README.md`
- `../src/SharpCoreDB.CQRS/README.md`
- `../src/SharpCoreDB.CQRS/NuGet.README.md`
- `../src/SharpCoreDB.Functional/README.md`
- `../src/SharpCoreDB.Functional.Dapper/README.md`
- `../src/SharpCoreDB.Functional.EntityFrameworkCore/README.md`
- `../src/SharpCoreDB.Functional.Linq2DB/README.md` (new production linq2db adapter with `Option`/`Fin`/`Seq` + BulkCopy)
- `../src/SharpCoreDB.Identity/README.md`
- `../src/SharpCoreDB.Serilog.Sinks/README.md`
- `../src/SharpCoreDB.Serilog.Sinks/NuGet.README.md`
- `../src/SharpCoreDB.Serilog.Sinks/CHANGELOG.md`

## 3. NuGet Publishing

- `../.github/NUGET_PUBLISHING_GUIDE.md`
- `../.github/CI_CD_BEST_PRACTICES.md`

## 4. Server Documentation

- `server/README.md`
- `server/QUICKSTART.md`
- `server/INSTALLATION.md`
- `server/CLIENT_GUIDE.md`
- `server/ADMIN_TOOLING_GUIDE.md`
- `server/REST_API.md`
- `server/BINARY_PROTOCOL_SPEC.md`
- `server/CONFIGURATION_SCHEMA.md`
- `server/SECURITY.md`
- `server/SYSTEM_DATABASES_SECURITY.md`

## 5. Storage and Engine Internals

- `scdb/README.md`
- `scdb/README_INDEX.md`
- `scdb/ENCRYPTION.md` — full at-rest encryption, key model, password/key rotation, worked example
- `scdb/PRODUCTION_GUIDE.md`
- `serialization/README.md`
- `serialization/SERIALIZATION_AND_STORAGE_GUIDE.md`
- `storage/QUICK_REFERENCE_v1.7.0.md`
- `storage/METADATA_IMPROVEMENTS_v1.7.0.md`
- `storage/SINGLE_FILE_SQL_LIMITATIONS.md`

## 6. Engine Implementation Notes

- `internals/JOIN_IMPLEMENTATION.md`
- `internals/SUBQUERY_IMPLEMENTATION.md`
- `internals/SUBQUERY_INTEGRATION_GUIDE.md`
- `internals/OPTIMIZER_ARCHITECTURE.md`
- `internals/OPTIMIZER_GUIDE.md`
- `internals/PROJECTIONS_OPEN_TELEMETRY_METRICS.md`

## 7. GraphRAG, Vector, and Analytics

- `graphrag/00_START_HERE.md`
- `graphrag/README.md`
- `graphrag/GRAPH_RAG_SINGLE_SQL.md`
- `graphrag/METRICS_AND_OBSERVABILITY_GUIDE.md`
- `analytics/README.md`
- `Vectors/README.md`
- `performance/graphrag-performance-tuning.md`

## 8. Distributed, Sync, and Migration

- `distributed/README.md`
- `sync/README.md`
- `migration/README.md`
- `migration/MIGRATION_GUIDE.md`
- `migration/FLUENTMIGRATOR_EMBEDDED_MODE_v1.7.0.md`
- `migration/FLUENTMIGRATOR_SERVER_MODE_v1.7.0.md`

## 9. Functional Programming & Null Safety

- `FUNCTIONAL_NULL_SAFETY.md`
- `NULLABLE_VS_OPTIONAL_REBUTTAL.md`

## 10. Benchmarks & Performance

- `manual/performance.md` — ⭐ **Performance Guide**: when SharpCoreDB is fastest + v2.0 results
- `performance/V2_PERFORMANCE_PLAN.md` — v2.x performance roadmap, root-cause analysis, .NET 11 plan
- `benchmarks/SHARPCOREDB_COMPARATIVE_BENCHMARKS.md` — comparative report vs SQLite/LiteDB/BLite
- `BENCHMARK_RESULTS.md` — micro-benchmark suite report (v1.x-era, with v2.0 update)
- `benchmarks/BENCHMARK_METHOD.md`
- `benchmarks/BENCHMARK_SCENARIOS_FINAL.md`
- `benchmarks/SHARPCOREDB_VS_ZVEC_COMPARISON.md`
- `benchmarks/ZVEC_BENCHMARKS_COMPLETE.md`
- `performance/graphrag-performance-tuning.md`
- `QUERY_PLAN_CACHE.md`
- `../tests/benchmarks/SharpCoreDB.Benchmarks.Comparative/` — runnable harness + `results/*.json`


## 11. Developer Standards

- `../.github/CODING_STANDARDS_CSHARP14.md`
- `../.github/SIMD_STANDARDS.md`

## Documentation Governance

- Files listed in this index are the maintained product documentation set.
- Historical phase-design notes remain in `internals/` for background context.
- Superseded duplicates are removed instead of linked alongside canonical files.
- All documentation is in English only.

