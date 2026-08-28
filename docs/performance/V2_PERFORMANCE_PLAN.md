# SharpCoreDB v2.x — Performance-First Roadmap

**Status:** Active development
**Branch:** `release/v2.0.0.0`
**Target version:** 2.0.0.0
**Current toolchain:** .NET 10 / C# 14 (locked for v2.0.x)
**Next toolchain:** .NET 11 / C# 15 — mainstream November 2026 (planned for v2.1+)
**Last updated:** August 2026

---

## 1. Mandate

**SharpCoreDB v2.x is performance-first.** The v1.x series shipped an extensive feature set (SQL, encryption, vector search, time-series, columnar storage, distributed mode, providers), but the comparative benchmarks show that **point reads, updates, and deletes** — the operations embedded databases are measured on against SQLite and LiteDB — were dramatically slower than SQLite:

| Operation        | SharpCoreDB v1.9 | SQLite  | LiteDB | Gap vs SQLite |
|------------------|-----------------:|--------:|-------:|--------------:|
| INSERT (100K)    | **202,222/s**    | 167,363 | 91,845 | 1.2x faster   |
| READ (10K by idx)| 6,102/s          | 96,724  | 13,317 | **~16x slower** |
| UPDATE (10K)     | 8,411/s          | 252,482 | 9,218  | **~30x slower** |
| DELETE (10K)     | 7,203/s          | 378,961 | 13,907 | **~52x slower** |

*(Source: `docs/benchmarks/SHARPCOREDB_COMPARATIVE_BENCHMARKS.md` and `tests/benchmarks/SharpCoreDB.Benchmarks.Comparative`.)*

The v2 effort closes this gap while keeping the v1 API fully backwards compatible, and adds an **optional fast-path API** for users who want maximum throughput.

---

## 2. Root-cause analysis (v1.9.x)

### 2.1 Debug file logging left in hot paths — **FIXED in v2.0.0**
Unconditional `File.AppendAllText(...)` to hardcoded `D:\*.log` paths existed on:

- `Services/SqlParser.DML.cs` — **every SELECT returning rows** wrote `D:\core_debug.log` (StringBuilder + `DateTime.Now` + open/write/close per query). This single artifact dominates the ~6K/s READ result.
- `Database/Execution/Database.Execution.cs` — every parameterized `ExecuteSQL` wrote `D:\db_executesql.log`.
- `Database/Transactions/Database.BatchUpdateTransaction.cs` — every `BeginStorageTransactionOnly` wrote `D:\db_transaction.log`.
- `Services/SqlParser.DML.cs` + `DataStructures/Table.CRUD.cs` — INSERT paths wrote `D:\insert_debug.log`, `D:\insert_exception.log`, `D:\table_insert_debug.log`, `D:\auto_debug.log`.

**Resolution:** all of these blocks were removed in v2.0.0 (no API change; default behavior only stops writing stray log files).

### 2.2 String-based re-parsing instead of prepared/compiled execution
- `Database.Execution.cs` (`ExecuteQuery`) caches only a `CachedQueryPlan` (SQL + whitespace tokens). On every call the engine **re-binds parameters into a new string and re-splits**, then `ExecuteSelectQuery` re-runs regex checks (`HasActualParameters`, subquery detection), WHERE/ORDER/LIMIT parsing, and `TrackColumnUsage`.
- SQLite/LiteDB parse once and execute many; SharpCoreDB effectively re-parses on every execution even on a "cache hit".

### 2.3 Regex per statement in batch UPDATE/DELETE
`Database.Batch.cs` `TryParseUpdateForBatch`/`TryParseDeleteForBatch` run a non-compiled `Regex.Match` (with 1s timeout) for every statement. 10K updates + 10K deletes = 20K regex evaluations.

### 2.4 Per-call DI lookups and allocations
- `GetSharedSqlParser()` performs `_serviceProvider.GetService(IGraphRagProvider)` on every call.
- `ExecutePrepared` constructs a **new `SqlParser` per call**.
- Row materialization allocates a fresh `Dictionary<string, object>` per row; the UPDATE path copies rows with `new Dictionary<string, object>(row)`.

---

## 3. Work packages

| WP | Area | Scope | Status |
|----|------|-------|--------|
| **WP1** | Remove hot-path debug logging | SELECT, parameterized `ExecuteSQL`, batch transactions, INSERT | ✅ **DONE in v2.0.0** |
| **WP2** | Prepared/compiled query execution | Parse once → execute many; wire existing `QueryCompiler`/`CompiledQueryExecutor` into `ExecuteQuery`; add optional `PreparedStatement` reuse API | ✅ **DONE in v2.0.0** (simple point-lookup fast path) |
| **WP3** | Allocation reduction | Reuse shared `SqlParser`; pool row dictionaries; remove redundant row copies; `StructRow`-based read option | ✅ **DONE in v2.0.0** (shared parser reuse, key-based hash-index Add/Remove, no full row copies in `UpdateMultiple`, `DeduplicateByPrimaryKey` early-exit) |
| **WP4** | Batch UPDATE/DELETE regex → `[GeneratedRegex]` | Precompile `TryParseUpdateForBatch`/`TryParseDeleteForBatch`/`HasActualParameters`/subquery detection | ✅ **DONE in v2.0.0** (static compiled regexes + regex-free `NormalizeSql`) |
| **WP5** | Cache DI lookups | Cache `IGraphRagProvider` resolution in `GetSharedSqlParser` | ✅ **DONE in v2.0.0** |
| **WP6** | Storage/index tuning | AppendOnly/PageBased read path, page cache, hash/B-tree index maintenance batching | ✅ **DONE in v2.0.0** (no-copy hash-index lookup for write-locked batch paths, `ExecuteQueryFast` precompiled regexes, `NormalizeSql` allocation short-circuit; storage read path already uses cached `SafeFileHandle` + `RandomAccess`) |
| **WP7** | Provider fast paths | ADO.NET `SharpCoreDBCommand`/`DataReader`, YesSql, Sync provider materialization | ✅ **DONE in v2.0.0** (per-`ExecuteReader` full SQL parse eliminated via `OPTIONALLY` keyword fast path; span-based write/sqlite_master detection removes per-call `ToUpperInvariant`; YesSql delegates to the ADO.NET provider so it inherits the wins) |
| **WP8** | **.NET 11 / C# 15 migration** | Target `net11.0` + `LangVersion 15`; adopt runtime async, intrinsics, SIMD lane APIs | Planned (v2.1, after Nov 2026 GA) |
| **WP9** | Zero-allocation `StructRow` read path | Promote the dormant zero-copy `StructRow` machinery into a first-class parameterized/WHERE-capable API; cache the variable-length schema; benchmark vs SQLite | ✅ **DONE in v2.0.0** (`ExecuteQueryStruct` READ = 112K/s — **beats SQLite 84K/s**) |
| **WP9-B/C** | SIMD in the row scan path | Fixed-offset numeric WHERE fast path: direct binary reads (no boxing/string) + portable `Vector<T>` SIMD batch equality filter for Integer/Long in `ScanStructRowsWhere`; numeric early-WHERE in the columnar full scan | ✅ **DONE in v2.0.0** (point-lookup read unaffected; numeric full-scan WHERE now SIMD-filtered, verified by tests) |
| **WP9-E** | Native AOT readiness | `[RequiresDynamicCode]` on `QueryCompiler.Compile` + LINQ translator; AOT-safe `TypeConverter` (no `Convert.ChangeType`); AOT-safe `Option<T>` reader (no reflection); source-generated metadata JSON via `TableMetadataDto` + `SharpCoreDBJsonContext` with a JIT/AOT conditional resolver | ✅ **DONE in v2.0.0** (`tools/SharpCoreDB.AotSmoke` publishes with `PublishAot=true` and **runs: 1000 inserts, point lookup, StructRow point + full scan, reopen — exit 0**) |

---

## 3.1 Measured results (comparative benchmark, 2026-08-28)

Single-run `SharpCoreDB.Benchmarks.Comparative` (100K inserts, 10K reads/updates/deletes), vs the v1.9 March baseline:

| Operation | v1.9.0 | **v2.0.0** | SQLite | LiteDB | Delta vs v1.9 |
|-----------|-------:|-----------:|-------:|-------:|--------------:|
| INSERT (SQL) | 202,222/s | 94,927/s | 148,151/s | 75,501/s | env noise (identical InsertBatch code path) |
| READ (SQL) | 6,102/s | **62,631/s** | 98,622/s | 16,352/s | **10.3x faster** |
| UPDATE (SQL) | 8,411/s | **45,218/s** | 269,468/s | 11,356/s | **5.4x faster** |
| DELETE (SQL) | 7,203/s | **33,527/s** | 363,480/s | 14,616/s | **4.7x faster** |
| READ (Direct API) | — | **141,100/s** | 98,622/s | 16,352/s | **beats SQLite** |
| READ (StructRow zero-alloc) | — | **112,472/s** | 83,986/s | 14,679/s | **beats SQLite by ~34%, LiteDB by ~7.7x** |
| UPDATE (Direct API) | — | 47,584/s | 269,468/s | 11,356/s | — |
| DELETE (Direct API) | — | **136,133/s** | 363,480/s | 14,616/s | **beats LiteDB 9x** |

*(StructRow READ measured 2026-08-28 via the new `ExecuteQueryStruct` fast path: hash-index point lookup → zero-alloc `StructRow` over the raw record buffer, no `Dictionary<string,object>` materialization.)*

Notes:
- The v1.9 vs v2.0 INSERT delta is **environmental**, not a code regression: the SQL and Direct benchmark sections execute the identical `db.InsertBatch(...)` code path and differ by ~50% within the same run (cold-JIT warmup). SQLite and LiteDB also showed 10–17% lower throughput than the March run.
- READ (SQL) gap vs SQLite closed from **~16x → ~1.6x**; the Direct API READ path now **exceeds SQLite**.


---

## 4. C# 15 / .NET 11 readiness (mainstream November 2026)

**Decision:** v2.0.x ships on **.NET 10 / C# 14**. We are already *preparing* the codebase for .NET 11 / C# 15 so the migration after November 2026 GA is a low-risk, mechanical step.

### 4.1 Runtime & JIT (automatic wins on `net11.0`)
- **Runtime-native async (Runtime Async):** lower-overhead async, tail-merged suspension points, reduced code size, ExecutionContext-capture opt-out when no ambient state. Directly benefits `Execute*Async`, `InsertBatchAsync`, `ExecuteBatchSQLAsync`, and server paths.
- **JIT:** bounds-check elimination, redundant checked-context removal, devirtualization, switch-expression folding, constant-folding of `SequenceEqual`, redundant branch elimination → free speedups in parser/materializer loops and index lookups.
- **NativeAOT:** faster interface dispatch (shared dispatch helper) → better throughput for interface-heavy code paths (`IStorage`, `IStorageEngine`, `ITable`).

### 4.2 Hardware intrinsics
- **AVX-VNNI-512** (x64) and **Arm SVE2** → vector search (HNSW) distance kernels and `SimdHelper` fallback/optimized paths.
- **SIMD lane construction/composition APIs** (`CreateGeometricSequence`, `Zip`, `Unzip`, `Concat` family on `Vector128/256/512/64`/`Vector<T>`) → columnar codecs (Delta, Gorilla, XorFloat) and SIMD row scanning.

### 4.3 Libraries
- **Zstandard in `System.IO.Compression`** → optional WAL/page-level compression mode.
- **IEEE 754 decimal floating-point (`Decimal32/64/128`)** + `INumberBase<TSelf>.TryParsePartial` → faster decimal column parsing/serialization.
- **Improved Base64 APIs** → faster binary row codec paths.

### 4.4 C# 15 language
- **Union types / closed hierarchies** → leaner AST/SQL-node design with fewer allocations and exhaustive pattern matching.
- **Collection-expression arguments** → cheaper list/spread constructions in parser and planner.
- **Extension indexers / memory safety** → cleaner, allocation-free public API surface.

### 4.5 Migration plan (for v2.1)
1. `Directory.Build.props`: `LangVersion` 14 → 15; `TargetFramework` net10.0 → net11.0 (net10 remains supported via `TargetFrameworks` if needed).
2. Re-run benchmarks on .NET 11; measure runtime-async + JIT + intrinsics wins.
3. Adopt Runtime Async in async hot paths; enable AVX-VNNI-512/SVE2 intrinsics behind existing `SIMD_ENABLED` guards.
4. Add optional Zstandard page compression behind a new config flag (default off).

---

## 5. Backwards compatibility strategy

- **WP1–WP7 are drop-in:** no public API changes; default behavior for existing callers is preserved (debug logging removal only stops stray `D:\*.log` writes).
- **New fast-path APIs are additive:** e.g., reusable `PreparedStatement`/`CompiledCommand` objects, `StructRow` readers, `InsertBatchTyped` — old APIs remain untouched.
- **.NET 11 / C# 15:** net10.0 target is kept until the ecosystem moves; `#if NET11_0_OR_GREATER` guards isolate runtime-specific code (Runtime Async, intrinsics, Zstandard).

---

## 6. Validation

1. Full test suite: `tests/SharpCoreDB.Tests` (and provider/EF/sync test projects) after each WP.
2. Comparative benchmark: `tests/benchmarks/SharpCoreDB.Benchmarks.Comparative` (Release) — track READ/UPDATE/DELETE ops/sec vs SQLite and LiteDB per WP.
3. Allocation/GC validation: BenchmarkDotNet `Allocated` column for the SELECT/UPDATE/DELETE paths.
4. Profiling: dotnet-trace / dotnet-counters on the benchmark to confirm no remaining per-call disk I/O or hidden allocations.
