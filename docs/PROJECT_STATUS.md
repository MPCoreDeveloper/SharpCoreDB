# SharpCoreDB — Project Status

**Last updated:** 2026-02-05  
**Branch:** `master`  
**Framework:** .NET 10 / C# 14

---

## Build & Test

| Metric | Value |
|--------|-------|
| Build | **Pass** |
| Tests passed | **750** |
| Tests skipped | 70 |
| Tests failed | **0** |
| Production LOC | ~77,700 |

---

## Phase Completion

| Phase | Scope | Status |
|-------|-------|--------|
| 1 | Core engine — tables, CRUD, indexes, foreign keys | ✅ Complete |
| 2 | Storage — SCDB single-file format, block registry, WAL | ✅ Complete |
| 3 | Page management — slotted pages, free-space map, extent allocator | ✅ Complete |
| 4 | Transactions — group-commit WAL, checkpoint, recovery | ✅ Complete |
| 5 | Encryption — AES-256 column-level, key management | ✅ Complete |
| 6 | Query engine — enhanced parser, JOINs, subqueries, aggregates | ✅ Complete |
| 7 | Optimization — SIMD filters, cost-based optimizer, materialized views, plan cache | ✅ Complete |
| 8 | Time-series — compression codecs, buckets, bloom filters, downsampling, retention | ✅ Complete |
| 1.3 | DDL: Stored Procedures, Views | ✅ Complete |
| 1.4 | DDL: Triggers | ✅ Complete |

---

## Recently Shipped (Phase 1.3 / 1.4)

### Stored Procedures
`SqlParser.Procedures.cs` — `CREATE PROCEDURE`, `DROP PROCEDURE`, `EXEC`

- Parameter definitions with `@name TYPE [IN|OUT|INOUT]`
- `BEGIN...END` body with multi-statement execution
- Parameter substitution at exec time
- Static in-memory registry (survives across parser instances)

### Views
`SqlParser.Views.cs` — `CREATE VIEW`, `CREATE MATERIALIZED VIEW`, `DROP VIEW`

- Virtual tables backed by a SELECT definition
- Materialized views eagerly compute and cache results
- `TryGetView()` available for transparent query expansion

### Triggers
`SqlParser.Triggers.cs` — `CREATE TRIGGER`, `DROP TRIGGER`

- `BEFORE` / `AFTER` timing
- `INSERT` / `UPDATE` / `DELETE` events
- `NEW.col` and `OLD.col` substitution in trigger body
- `FireTriggers()` ready for integration into DML handlers

---

## Key Components

| Area | Files | Notes |
|------|-------|-------|
| SQL Parser | `SqlParser.Core/DML/DDL/Helpers/Procedures/Views/Triggers.cs` | Partial-class design, tuple-pattern dispatch |
| Enhanced Parser | `EnhancedSqlParser.cs`, `EnhancedSqlParser.DDL.cs` | AST-based, supports JOINs + subqueries |
| Storage | `SingleFileStorageProvider`, `BlockRegistry`, `WalManager` | SCDB single-file format |
| Time-Series | `DeltaOfDeltaCodec`, `XorFloatCodec`, `BucketManager`, `DownsamplingEngine` | Gorilla-style compression |
| Query | `CostBasedOptimizer`, `MaterializedView`, `ParallelQueryExecutor`, `SimdFilter` | Plan cache + SIMD |
| Indexing | `AdaptiveIndexManager`, `ExpressionIndex`, `PartialIndex` | Auto-index recommendations |

---

## Remaining Work

### Test Enablement
70 tests are currently skipped. Categories:

- **Infrastructure tests** — need storage provider setup or temp-file handling
- **Performance benchmarks** — gated behind `[Trait("Category", "Performance")]`
- **EF Core integration** — require EF Core provider wiring
- **Repair tool tests** — depend on corruption injection helpers

**Target:** Enable as many as feasible, push coverage above 90%.

### Integration
- Wire `FireTriggers()` into `ExecuteInsert`, `ExecuteUpdate`, `ExecuteDelete`
- Wire `TryGetView()` into `ExecuteSelectQuery` for transparent view expansion
- Persist procedure/view/trigger registries to SCDB storage on flush

### Documentation
- API reference for public surface
- Migration guide (SQLite → SharpCoreDB)
- Performance tuning guide

---

## Repository Layout

```
src/SharpCoreDB/          Production code (~77K LOC)
  Database/               Core + Execution + Caching partials
  DataStructures/         Table, Row, ColumnDefinition
  Execution/              QueryPlanCache, AggregationOptimizer
  Indexing/               Adaptive, Expression, Partial indexes
  Query/                  CostBasedOptimizer, MaterializedView, SIMD
  Services/               SqlParser (7 partials), TypeConverter, WAL
  Storage/                SCDB format, BlockRegistry, Columnar
  TimeSeries/             Codecs, Buckets, Downsampling, Retention
tests/SharpCoreDB.Tests/  820 tests (750 pass, 70 skip)
docs/                     Reference documentation
```

---

## How to Build & Test

```bash
dotnet build src/SharpCoreDB/SharpCoreDB.csproj -c Release
dotnet test tests/SharpCoreDB.Tests/SharpCoreDB.Tests.csproj -c Release
```
