# SharpCoreDB Documentation Index

**Status:** Active documentation set (`2.1.0-RC.3`, branch `release/v2.1.0.0-RC.3` — net11.0 / C# 15 preview; the v2.0 stable line lives on `master`)

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

## 0b. Release notes

- `2.1.0-RC.3_WHAT_CHANGED.md` — **what's new in 2.1.0-RC.3** (net11.0 / C# 15 preview RC: the
  inline-capacity default, the overflow-arena write path, the fair-PK UPDATE/DELETE position, plus
  the full validation table and the honest remaining gaps)
- `2.0.0.2_WHAT_CHANGED.md` — **what's new in 2.0.0.2** (2.x-vs-1.9.x major steps + the hardening
  and data-integrity batch)
- `2.0.0.0_WHAT_CHANGED.md` — **what's new in v2.0 vs v1.9** (features + benchmarks vs SQLite/LiteDB)

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
- `Vectors/DISKANN_SQL_DDL.md` — `CREATE VECTOR INDEX … USING DISKANN` SQL DDL
- `Vectors/MUNARIUM_INSPIRATION_PLAN.md` — munarium-inspired implementation record + phase status
- `Vectors/MUNARIUM_GAP_ANALYSIS.md` — verified module-by-module gaps + borrowed-goodies backlog
- `Vectors/PERFORMANCE_NOTES.md` — measured vector-search numbers + open performance work
- `performance/INSERT_UPDATE_PERFORMANCE_PLAN.md` — ⭐ **Insert & Update Performance Plan** (write-path phased plan: measurement protocol, encryption-tax removal, in-place records, targets, owner decisions)

## 7c. Lexical & hybrid search (v2.1 RC)

- `../src/SharpCoreDB.Search/NuGet.README.md` — BM25 full-text index, classifying tokenizer, word-only stemmer
- `../src/SharpCoreDB.HybridSearch/NuGet.README.md` — lexical + vector fused by reciprocal-rank fusion
- `performance/graphrag-performance-tuning.md`

## 7b. .NET 11 / C# 15 (v2.1 RC)

- `net11/UPGRADE.md` — what .NET 11 consumers get (Zstd, Runtime Async, `SharpCoreDB.Net11`)
- `net11/UNION_TYPES_DESIGN.md` — union-types blueprint (deferred to .NET 11 GA)

## 8. Distributed, Sync, and Migration

- `distributed/README.md`
- `sync/README.md`
- `migration/README.md`
- `migration/MIGRATION_GUIDE.md`

## 9. Functional Programming & Null Safety

- `FUNCTIONAL_NULL_SAFETY.md`
- `NULLABLE_VS_OPTIONAL_REBUTTAL.md`

## 10. Benchmarks & Performance

- `2.1.0-RC.3_WHAT_CHANGED.md` — ⭐ **release notes (2.1.0-RC.3)**: the validation table (2,897 tests /
  16 suites), the comparative / fair-PK / pure-default / PageBased / multi-row arms measured
  2026-09-17, and the remaining gaps
- `2.0.0.2_WHAT_CHANGED.md` — ⭐ **release notes (2.0.0.2)**: the 2.x-vs-1.9.x major steps + the
  2.0.0.2 hardening/perf batch
- `CHANGELOG.md` — full per-version change log (`[2.0.0.2]`, new `[Unreleased]` on top)
- `manual/upgrade-and-downgrade.md` — compatibility matrix & downgrade policy
- `benchmarks/default-config-pk.md` — default-config fair-PK benchmark + NoEncryptMode root cause
- `manual/performance.md` — ⭐ **Performance Guide**: when SharpCoreDB is fastest + v2.0 results
- `performance/V2_PERFORMANCE_PLAN.md` — v2.x performance roadmap, root-cause analysis, .NET 11 plan
- `benchmarks/SHARPCOREDB_COMPARATIVE_BENCHMARKS.md` — comparative report vs SQLite/LiteDB/BLite
- `BENCHMARK_RESULTS.md` — micro-benchmark suite report (v1.x-era, with v2.0 update)
- `benchmarks/BENCHMARK_METHOD.md`
- `benchmarks/BENCHMARK_SCENARIOS_FINAL.md`
- `benchmarks/SHARPCOREDB_VS_ZVEC_COMPARISON.md`
- `benchmarks/ZVEC_BENCHMARKS_COMPLETE.md`
- `benchmarks/AVX512_2026-09-01.md` — **AVX-512 hardware results** (2026-09-01, real AVX-512 machine): SIMD kernels 2–26× vs scalar + CRUD vs SQLite/LiteDB (raw per-run `.md`/`.json` in `benchmarks/avx512-2026-09-01/`)
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

