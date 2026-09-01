# SharpCoreDB v2.x — Performance-First Roadmap

**Status:** ✅ v2.0.0 shipped — WP1–WP7, WP9, WP9-B/C, WP9-E, WP14 complete (all committed on `release/v2.0.0.0` + `master`) · remaining items target v2.1
**Branch:** `master` / `release/v2.0.0.0`
**Target version:** 2.0.0.0 (shipped) → 2.1.0.0 (next)
**Current toolchain:** .NET 10 / C# 14 (locked for v2.0.x)
**Next toolchain:** .NET 11 / C# 15 — mainstream November 2026 (planned for v2.1+)
**Last updated:** September 2026

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
| **WP14** | Dedicated SQL batch-INSERT fast path (`object[]` rows) | `ExecuteBatchSQL` INSERTs now parse VALUES directly into column-ordered `object[]` rows (`PreparedInsertStatement.ParseValuesToArray`) and insert via `Table.InsertBatch(object[][], columnOrder)` — no per-row `Dictionary<string, object>` allocation, no column-name `TryGetValue` lookups; user-facing column order (excludes internal `_rowid`) is re-mapped to table positions with full dict-path parity (defaults, AUTO, explicit NULL, NOT NULL, PK, indexes). Also wires `UpdateMultiple` to the existing WP11 in-place field-overwrite fast path with runtime offsets (fixed-size fields after variable-length columns now patch in place) | ✅ **DONE in v2.0.0.1** (SQL INSERT 54.5K/s → 98.2K/s, **+80%**; verified by full 1,680-test suite + `TotalInPlacePatches` instrumentation) |

---

## 3.1 Measured results (comparative benchmark, 2026-08-28 — v2.0.0 final)

`SharpCoreDB.Benchmarks.Comparative` (100K inserts, 10K reads/updates/deletes), two runs, vs the v1.9 March baseline:

| Operation | v1.9.0 | **v2.0.0** | SQLite | LiteDB | vs SQLite |
|-----------|-------:|-----------:|-------:|-------:|----------:|
| INSERT | 202,222/s | **91–133K/s** | 145–150K/s | 77K/s | ~0.8–0.9x |
| READ (SQL) | 6,102/s | **51–66K/s** | 88–97K/s | 16K/s | ~0.7x |
| READ (Direct API) | — | **~120–125K/s** | 88–97K/s | 16K/s | **~1.3x — beats SQLite** |
| READ (StructRow zero-alloc) | — | **70–120K/s** | 88–97K/s | 16K/s | **≈ parity, beats LiteDB ~5–8x** |
| UPDATE (SQL) | 8,411/s | **40–45K/s** | 240–296K/s | 11K/s | ~0.16x |
| UPDATE (Direct API) | — | **56–59K/s** | 240–296K/s | 11K/s | **beats LiteDB ~5x** |
| DELETE (SQL) | 7,203/s | **30–67K/s** | 320–367K/s | 14K/s | ~0.1–0.2x |
| DELETE (Direct API) | — | **78–142K/s** | 320–367K/s | 14K/s | **beats LiteDB ~6–10x** |

*(Two-run range reflects machine noise; the machine was under variable background load. Direct API READ is the stable headline: **~120–125K/s consistently beats SQLite's ~88–97K/s**.)*

Notes:
- **READ (SQL path)** gap vs SQLite closed from **~16x → ~1.5x**; the Direct API and StructRow READ paths **exceed or match SQLite**.
- **Every operation beats LiteDB** (reads ~5–8x, updates ~5x, deletes ~6–10x).
- **INSERT** is ~0.8–0.9x of SQLite (was 1.2x in the March run — the identical `InsertBatch` path varies with machine load; the StructRow section measured up to 132K/s).
- **UPDATE/DELETE** remain behind SQLite — its fixed-length C record format with direct field offsets and in-place writes is the strongest point; this is the remaining gap (targeted by WP3/WP6 follow-ups and the .NET 11 runtime improvements).

### 3.2 .NET 11 preview-7 measurements (branch `release/v2.1.0.0`, 2026-08-30)

Single-run numbers on the same machine (AppendOnly engine) after the net11.0 retarget
(Phase 0) and the Phase 5 SQL-verb allocation refactor (`FirstToken` span dispatch —
removes the `Trim().Split(' ')[0]` string[] + per-token allocations from every
`ExecuteSQL` / `ExecuteNonQuery` / `ExecuteSQLAsync` call):

| Operation | net10 baseline (§3.1) | net11 run A (pre-refactor) | net11 run B (post-refactor) |
|-----------|----------------------:|---------------------------:|----------------------------:|
| INSERT (SQL) | 91–133K | 73.1K | 75.8K |
| READ (SQL) | 51–66K | 64.0K | 59.2K |
| UPDATE (SQL) | 40–45K | 37.7K | 42.4K |
| **DELETE (SQL)** | 30–67K | 22.4K | **46.7K** |
| READ (Direct) | ~120–125K | 104.9K | 96.6K |
| DELETE (Direct) | 78–142K | 117.2K | 104.7K |
| READ (StructRow) | 70–120K | 87.7K | 91.9K |

Observations:
- **DELETE (SQL) ≈2× (22.4K → 46.7K ops/sec)** after the Phase 5 allocation refactor —
  the per-row `ExecuteNonQuery` DELETE path previously allocated a Trim substring +
  `string[]` + one string per token on every call; the span-based verb dispatch removed
  those allocations entirely (validated by the full 1,509-test suite, 0 failures).
- Single runs are noisy (SQLite/LiteDB also varied run-to-run on this machine); a
  controlled two-run before/after (net10 vs net11) on a quiet machine is still pending.
- The remaining UPDATE/DELETE gap vs SQLite is structural (row-copy based updates/deletes)
  and is targeted by the v2.1 in-place-update / fixed-width-record work.

### 3.3 #5 allocation cuts (2026-08-30, `74403f74` on `release/v2.1.0.0` + `release/v2.0.0.0`)

Per-operation allocations on the micro-bench harness (Release; pre-#5 → post-#5):

| Metric | Pre-#5 | Post-#5 |
|---|---:|---:|
| Directory READ (varying SQL) | 2,044 B/op | **1,684 B/op** |
| Directory READ (identical SQL) | 1,237 B/op | **911 B/op** |
| Directory `ExecuteQueryStruct` | 1,336 B/op | **976 B/op** |
| Single-file point lookup (varying) | 3,211 B/op | **2,293 B/op** |
| Single-file point lookup (identical SQL) | 2,447 B/op | **1,540 B/op** |

Changes: leaky `_dictPool` removed from the point-lookup materializer (fresh pre-sized
`Dictionary(Columns.Count)`); `TryParseSimpleWhereClause` zero-alloc span rewrite;
`ExecuteSelectQuery` drops `ToUpperInvariant` + `fromParts`/`keywords` + the
`SubqueryStartRegex` Match (span scan); `ExtractMainTableNameFromSql` span-based;
four `parameters ?? []` empty-dictionary allocations removed; single-file `ExecuteQuery`
hoists the per-query `PRAGMA table_info` regex to a compiled static field and replaces
`sql.Trim().ToUpperInvariant()` with span checks.

**#5 struct-enumerator refactor — DONE (2026-08-30):** full row-dictionary pooling is
structurally unsafe (callers retain the returned rows; a shared pool would corrupt data), so the
zero-allocation win was delivered via struct enumerators instead. `ExecuteSimpleSelectStruct` and
`ScanStructRowsWhere` are no longer yield iterators: `Table.ScanStructRowsWhere` returns a
`StructRowWhereEnumerable` struct whose enumerator handles the hash-index / primary-key point-lookup
fast paths allocation-free (the SIMD/full-scan fallback delegates to the yield-based core), and
`IDatabase.ExecuteQueryStruct` now returns a `StructRowQueryEnumerable` struct (foreach is
allocation-free; LINQ/boxing goes through a small class-based enumerator). A point lookup dropped
from **976 → 471 B/op (−52%)** on the StructRow path (911 B/op on the dictionary path), with +13%
throughput. Remaining bytes are the plan-cache key, WHERE-string build, `TryParseSimpleWhereClause`
strings, hash-index position list and `engine.Read`'s per-read byte[].

### 3.4 WP14 — dedicated batch-INSERT fast path (2026-09-01, `master` / v2.0.0.1 line)

Same machine, `SharpCoreDB.Benchmarks.Comparative` (AppendOnly, 100K inserts / 10K reads-updates-deletes),
**before → after** WP14 (identical harness invocation; SQLite numbers shift with machine load, so the
relative gap matters more than absolute ops/sec):

| Operation | before (SQL) | after (SQL) | SQLite (same run) | INSERT gap vs SQLite |
|-----------|-------------:|------------:|------------------:|---------------------:|
| **INSERT** | 54,515/s | **98,219/s (+80%)** | 144,603/s | 1.94× → **1.47×** |
| READ | 38,068/s | 75,569/s | 96,691/s | ~1.3× |
| UPDATE | 26,633/s | 41,648/s | 290,382/s | ~7× (structural) |
| DELETE | 35,294/s | 86,861/s | 367,711/s | ~4× (structural) |

What changed:
- **`PreparedInsertStatement.ParseValuesToArray`** — parses a VALUES clause directly into a
  column-ordered `object[]` (same `ParseValueFast` conversion rules; no dictionary, no column-name
  lookups). The batch INSERT path in `ExecuteBatchSQL` (`Database.Batch.cs`) now uses it via
  `ParseInsertStatementFastToArray`.
- **`Table.InsertBatch(object[][], List<string> columnOrder)`** (`Table.CRUD.cs`) — validates,
  defaults, auto-generates, NOT-NULL-checks and serializes column-ordered rows without per-row
  `Dictionary<string, object>` allocations. The user-facing column order (which excludes the
  internal `_rowid` column) is re-mapped onto table column positions; absent columns get defaults,
  explicit NULLs stay NULL, AUTO columns auto-generate — byte-for-byte parity with the dictionary
  path (verified by the full 1,680-test suite).
- **`UpdateMultiple` in-place wiring** — batch UPDATE now carries the storage position + raw bytes
  from the hash-index point lookup and tries the existing WP11 `TryOverwriteFieldsInPlace` fast path
  first. `TryOverwriteFieldsInPlace` gained **runtime offset resolution** (walks the encoded fields
  of the existing row when a variable-length column precedes the updated column), so fixed-size
  fields can now be patched in place even in schemas with a leading TEXT column. Instrumented via
  `Table.TotalInPlacePatches`. On AppendOnly the engine still appends the new version, so the
  UPDATE gain is modest (~1.5× vs pre-WP14 baseline, and still behind SQLite's in-place b-tree
  writes — the remaining structural gap targeted by the PageBased/in-place engine work).

> **Independent AVX-512 machine run (2026-09-01):** a 6-run benchmark on real AVX-512 hardware
> confirmed the same CRUD profile (INSERT 0.69–0.85× of SQLite, READ 0.58–0.72×, UPDATE 0.13–0.21×,
> DELETE 0.07–0.11×; beats LiteDB on every operation) and validated the AVX-512 SIMD tier
> (2–26× over scalar). See
> [`docs/benchmarks/AVX512_2026-09-01.md`](../benchmarks/AVX512_2026-09-01.md).



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
