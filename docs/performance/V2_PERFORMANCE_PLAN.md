# SharpCoreDB v2.x — Performance-First Roadmap

**Status:** ✅ v2.0.0 shipped — WP1–WP7, WP9, WP9-B/C, WP9-E complete (all committed on `release/v2.0.0.0`) · **WP8 Phase 0 (toolchain baseline) complete on `release/v2.1.0.0`** · remaining items target v2.1
**Branch:** `release/v2.0.0.0` (v2.0.x line, .NET 10 / C# 14) · `release/v2.1.0.0` (v2.1 line, .NET 11 / C# 15)
**Target version:** 2.0.0.0 (shipped) → 2.1.0.0 (next)
**Current toolchain (v2.1 branch):** .NET 11 preview 7 / C# 15 preview (`LangVersion latest` — numeric `15.0` is only valid at GA)
**Next milestone:** .NET 11 GA (mainstream November 2026) — switch to `LangVersion 15.0`, adopt Zstandard + IEEE 754 decimal when they land in the runtime
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
| **WP8** | **.NET 11 / C# 15 migration** | Target `net11.0` + C# 15; adopt runtime async, intrinsics, SIMD lane APIs | 🔶 **IN PROGRESS on `release/v2.1.0.0`** — Phase 0 toolchain baseline done: `net11.0` + `LangVersion latest`, SDK `11.0.100-preview.7`, CI on 11.0.x; build (0 errors), **1,790 tests**, Native AOT smoke exit 0, pack → 24 nupkgs |
| **WP9** | Zero-allocation `StructRow` read path | Promote the dormant zero-copy `StructRow` machinery into a first-class parameterized/WHERE-capable API; cache the variable-length schema; benchmark vs SQLite | ✅ **DONE in v2.0.0** (`ExecuteQueryStruct` READ = 112K/s — **beats SQLite 84K/s**) |
| **WP9-B/C** | SIMD in the row scan path | Fixed-offset numeric WHERE fast path: direct binary reads (no boxing/string) + portable `Vector<T>` SIMD batch equality filter for Integer/Long in `ScanStructRowsWhere`; numeric early-WHERE in the columnar full scan | ✅ **DONE in v2.0.0** (point-lookup read unaffected; numeric full-scan WHERE now SIMD-filtered, verified by tests) |
| **WP9-E** | Native AOT readiness | `[RequiresDynamicCode]` on `QueryCompiler.Compile` + LINQ translator; AOT-safe `TypeConverter` (no `Convert.ChangeType`); AOT-safe `Option<T>` reader (no reflection); source-generated metadata JSON via `TableMetadataDto` + `SharpCoreDBJsonContext` with a JIT/AOT conditional resolver | ✅ **DONE in v2.0.0** (`tools/SharpCoreDB.AotSmoke` publishes with `PublishAot=true` and **runs: 1000 inserts, point lookup, StructRow point + full scan, reopen — exit 0**) |
| **WP10** | .NET 11 SQL-verb allocation refactor | Replace the hot-path `sql.Trim().Split(' ')[0]` verb dispatch (Trim substring + `string[]` + one string/token per `ExecuteSQL`/`ExecuteNonQuery`/`ExecuteSQLAsync`) with an allocation-free `FirstToken(ReadOnlySpan<char>)` span dispatch | ✅ **DONE on `release/v2.1.0.0`** — 1,509 tests green; **DELETE (SQL) ≈2×** (22.4K → 46.7K ops/sec) in a single-run comparison |
| **WP11** | Columnar aggregates → Vector512 | Add a guarded `Vector512.IsHardwareAccelerated` fast path ahead of every `Vector256` branch in the 18 column-store SUM/MIN/MAX aggregate methods (2× SIMD width on AVX-512 hardware) | ✅ **DONE on `release/v2.1.0.0`** — 1,509 tests green; fallback path verified on this AVX2-only machine; the Vector512 path activates automatically on AVX-512 hardware |

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


---

## 4. C# 15 / .NET 11 readiness (mainstream November 2026)

**Decision:** v2.0.x ships on **.NET 10 / C# 14**. We are already *preparing* the codebase for .NET 11 / C# 15 so the migration after November 2026 GA is a low-risk, mechanical step.

### 4.0 Verified preview-7 availability (measured on SDK/runtime `11.0.100-preview.7`, 2026-08-30)

| Feature (§4) | In preview 7? | Evidence / note |
|---|---|---|
| Runtime-native async | ✅ Yes (default for `net11.0`, no `EnablePreviewFeatures` needed) | runtime docs; automatic |
| JIT improvements (bounds-check elim, devirt, switch folding) | ✅ Yes | automatic |
| NativeAOT faster interface dispatch | ✅ Yes | automatic |
| AVX-512 / FMA intrinsics | ✅ Yes | already used in `DistanceMetrics` / `SimdWhereFilter` on net10 too |
| **SIMD lane composition APIs** (`CreateGeometricSequence`, `Zip`→`(Lower,Upper)`, `Unzip`, `Concat*`) on `Vector128/256/512` | ✅ **Yes** | compile + run verified; target for columnar codecs / row scanning (Phase 2) |
| **`INumberBase<TSelf>.TryParsePartial`** | ⚠️ Present but signature in flux | compiles with changed parameter order; re-verify per preview before adopting (Phase 3) |
| **Arm SVE2** (`Sve`/`Sve2`) | ⚠️ Evaluation-only (`SYSLIB5003`) | usable behind `#if NET11_0_OR_GREATER` + `[RequiresPreviewFeatures]`; defer until GA (Phase 2) |
| **Zstandard in `System.IO.Compression`** | ❌ **Not present** | `ZstdCompressor` not in preview 7; **deferred to a later preview / GA** (Phase 3) |
| **IEEE 754 decimal (`Decimal32/64/128`)** | ❌ **Not present** | not in preview 7; **deferred to GA** (Phase 3) |
| **C# 15 union types / closed hierarchies** | ⚠️ Not yet stabilized | validate against preview compiler before AST refactor (Phase 4) |

**Toolchain note:** numeric `LangVersion 15.0` is rejected by the preview compiler (`CS1617`); the v2.1 branch uses `LangVersion latest` (maps to C# 15 preview). Switch to `15.0` at GA.

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
1. ✅ **DONE (Phase 0, on `release/v2.1.0.0`)** — toolchain centralized: `TargetFramework` net11.0 + `LangVersion latest` in `Directory.Build.props` (root + nested `src/SharpCoreDB`); SDK `11.0.100-preview.7` via `global.json`; CI/workflows on `11.0.x` (preview quality); net10 stays on `release/v2.0.0.0`.
2. Re-run benchmarks on .NET 11; measure runtime-async + JIT wins (was already planned; the v2.0.0 baseline numbers are in §3.1).
3. ✅ **Partial (WP11 done)** — guarded `Vector512` fast paths added to all 18 column-store SUM/MIN/MAX aggregates; `Table.StructScanning` already uses portable `Vector<T>` (auto-scales to 512 on AVX-512). **Deferred:** SIMD lane composition APIs (`Zip`/`Unzip`/`CreateGeometricSequence`/`Concat`) — verified available in preview 7, but the current bit-level time-series/columnar codecs (Gorilla/XorFloat/DeltaOfDelta, RLE, bit-packing) are inherently sequential; a clean integration needs a columnar-layout refactor, not a point edit. SVE2 stays deferred until it leaves evaluation-only status.
4. Add optional Zstandard page compression behind a new config flag (default off) **once `ZstdCompressor` lands in the runtime** (not in preview 7).
5. C# 15 union types / closed hierarchies for the SQL AST — after the preview compiler stabilizes (Phase 4).

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
