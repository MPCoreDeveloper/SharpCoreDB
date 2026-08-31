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

### 3.4 #6 in-place UPDATE for columnar/append-only storage (2026-08-31, `3d4cee77` + `68cb5dab` on `release/v2.1.0.0`; `116fc30e` + `8a13ba2b` on `release/v2.0.0.0`)

UPDATE no longer appends a new version for fixed-width records. `Table.Update` first attempts an
in-place overwrite (`IStorageEngine.TryUpdateInPlace` → `Storage.OverwriteRecordAt`) that is only
taken when the new record fits the existing slot (same stored length); otherwise it falls back to
the append path unchanged. The PK index entry stays valid (no re-point), no stale version is left
for compaction, and hash-index entries move in place.

Measured (Windows, Release, directory storage, `StorageEngineType.AppendOnly`, 2,000 fixed-width
rows, per-statement autocommit):

| Workload | Pre-#6 (row-copy append) | #6 initial (broken) | #6 final |
|---|---:|---:|---:|
| fixed-width UPDATE | ~1,500 ops/s, **+90 KB growth** | 75 ops/s, 0 growth | **~3.5–5.3K ops/s, 0 growth** |
| variable-width UPDATE | ~1,600 ops/s, +128 KB growth | 133 ops/s | ~1.3–1.7K ops/s (append fallback) |

The initial #6 implementation opened a fresh read-write `FileStream` per statement for the
in-place overwrite; on Windows the read-write open measures ~5–8 ms (on-access filters), a ~20x
throughput regression. The fix reads the record's length prefix through the already-cached read
`SafeFileHandle` and writes through a per-call **write-only** `FileStream` (`FileMode.Open`,
`FileAccess.Write`) — as fast as the append path's open. Final result: fixed-width UPDATEs are
**2.3–3.5x faster than the pre-#6 append path AND the file does not grow**; variable-width
updates keep the append fallback (correct, unchanged semantics).

Regression coverage: `SqlInPlaceUpdateTests` (fixed-width overwrites in place, no file growth,
PK/hash indexes stay consistent; variable-width falls back to append) + `AppendOnlyEngine_TryUpdateInPlace`
unit tests; full suites green on both branches (1,635 tests).

### 3.5 #7/#8 single-pass DML — SQL DELETE/UPDATE no longer materialize twice (2026-08-31, `release/v2.1.0.0`)

The SQL DELETE path previously materialized every matching row **twice** per statement:
`ExecuteDelete` ran a full `Select` (for RETURNING + affected-count) and then `Table.Delete`
re-scanned/re-deserialized the same rows. The SQL UPDATE path was worse: a full `Select().Count`
for change-tracking, the update pass itself, and — for RETURNING — a second full `Select`.

Changes:

- **`ITable.DeleteAffectedRows(where)`** — default implementation keeps the historic two-pass
  behavior for third-party `ITable` implementers; `Table` and `SingleFileTable` override with a
  single pass (delete AND return the affected pre-delete rows). `ExecuteDelete` now uses it:
  one scan, RETURNING + count from the same rows.
- **`ITable.UpdateAffectedCount(where, updates)`** — same default/override pattern; applies the
  update and returns the affected count. `ExecuteUpdate` now uses it; the separate `Select().Count`
  pass is gone (RETURNING still re-selects, only when requested).
- **PK fast path (Issue #7) extended to `DeleteMultiple` and `UpdateMultiple`** — a simple
  `pk = value` WHERE on a columnar table resolves via the PK B-tree directly (single search + one
  read) instead of `SelectInternal` full-row materialization + a per-row PK re-search. Range /
  compound / non-indexed WHERE clauses bypass the fast path and keep their (correct) generic
  behavior — `TryParseSimpleWhereClause` only accepts a plain `col = value`.

Regression coverage: `DmlSinglePassTests` (affected counts, RETURNING pre-delete rows, range +
non-indexed fallbacks, batch PK deletes/updates) + the existing RETURNING / `CHANGES()` tests.
Full suite green: **1,644 tests, 0 failures** (16 skipped).

> **Benchmark note (2026-08-31):** the comparative harness (§3.1/§3.2) is not the right probe for
> this work — its UPDATE/DELETE run in **batch** (`ExecuteBatchSQL`) against a **hash-indexed
> non-PK column**, so neither the single-statement `ExecuteDelete`/`ExecuteUpdate` passes nor the
> PK fast paths are exercised (run-to-run variance on the dev machine was >60%: two HEAD runs of
> the same binary measured SQL DELETE at 28.5K and 51.1K ops/s). A controlled quiet-machine A/B
> with a PK single-statement DELETE/UPDATE workload is the correct validation (still pending, same
> caveat as §3.2).

### 3.6 Fixed-width layout step — field-level in-place patch on the columnar UPDATE path (2026-08-31, `release/v2.1.0.0`)

The columnar UPDATE write path previously **deserialized → mutated → re-serialized the whole row**
per statement (re-encoding every string), then either overwrote in place (Issue #6, same length) or
appended. This step removes the full re-serialize when the row's storage position is known:

- **`ComputeActualColumnOffsets(byte[])`** walks the length-prefixed record and resolves the **real**
  byte offset of every column — including columns **after a variable-length column**, which the
  schema-level cache (`GetColumnOffsetsCached`) marks as "unstable".
- **`TryOverwriteFieldsInPlaceActual(byte[], updates)`** patches only the updated fields at those
  actual offsets (same fit/safety rules as the existing WP11 `TryOverwriteFieldsInPlace`). Returns
  null when a field would change the record length → caller falls back to full serialization.
- **`UpdateAffectedCount`** now resolves rows as **(position, row)** pairs (`ResolveUpdateRows`:
  PK B-tree for `pk = value`, hash index for an indexed equality, `SelectInternal` otherwise),
  reads the existing record, patches the changed fields, and writes in place via `TryUpdateInPlace`.
  Same for `UpdateMultiple` (batch).
- **Stale-index regression fixed:** a write that creates a stale file record (append update /
  logical delete) must remove it from **every** registered hash index. The PK/hash fast paths
  bypassed the `EnsureIndexLoaded` that `SelectInternal` used to perform, so an unloaded index was
  later **rebuilt from the data file including the stale record** → SELECT returned the pre-update
  row for the same PK. All four WHERE-based DML entry points (`UpdateAffectedCount`, `UpdateMultiple`,
  `CollectDeleteRecords`, `DeleteMultiple`) now call `EnsureAllRegisteredIndexesLoaded()` first
  (cached — cheap after the first load).

Effect: `UPDATE t SET score = X WHERE name = 'User5'` on `(name TEXT, email TEXT, age INT,
score REAL, data TEXT)` — `score` sits **after two variable-length columns** — now patches the 8
`score` bytes in the existing record (no full string re-encoding) and overwrites in place (**no file
growth**). Variable-width fields that change size still fall back to append (correct, unchanged).

Regression coverage: `FixedWidthPatchTests` (5 cases: fixed field after variable columns → in-place
no-growth; PK fixed field → in-place; variable growth → append + correct read-back; compound WHERE →
correct; batch patch). Full suite green: **1,649 tests, 0 failures** (16 skipped).

> **Still open** for the full SQLite-style fixed-width record layout: a dedicated on-disk format
> with a fixed part + variable-length heap would make even variable-column updates in-place without
> the per-update record walk, and enable true per-field random access on reads. This step removes
> the full re-serialize and keeps the record length stable for fixed-size fields — the core of the
> SQLite update model.

### 3.7 Single-file (.scdb) — PK hash index + in-place block overwrite (2026-08-31)

- **PK hash index (A1):** `SingleFileTable` maintains a primary-key hash index (ordinal string key)
  on every mutation; `FindByPrimaryKey` / `UpdateByPrimaryKey` / `DeleteByPrimaryKey` and
  `SELECT … WHERE pk = value` resolve in **O(1)** instead of an O(N) cache scan. Rebuilt on cache
  load / rollback; numeric literals are normalized (`pk = 05` ≡ `pk = 5`).
- **In-place block overwrite (A2):** `WriteBlockAsync` already reuses a table's block offset when the
  row-cache JSON fits the allocated pages — a same-length update overwrites the block in place and
  the `.scdb` file does not grow (pinned by a regression test).
- **Still open:** delta/incremental flush (A3) and unifying single-file onto the columnar format (A4).

### 3.8 Out-of-line overflow — opt-in fixed-width record layout (B1, 2026-08-31)

`DatabaseConfig.FixedWidthRecordLayout` (opt-in, default off): records have a **constant size per
schema** — fixed-size columns inline at constant offsets, TEXT/BLOB values in a per-table overflow
arena (`.ovf`, `[len][payload]` blocks) referenced by a 4-byte offset in the record. Every UPDATE
(fixed **or** variable column) is therefore an **in-place overwrite**: the `.dat` does not grow, and
variable values are patched through the arena. Components: `OverflowArena` (append + in-memory cache
+ copy-on-compact), `FixedWidthRecordLayout`, and fixed-width serialize / deserialize / in-place
patch wired into the Table serializer/deserializer dispatch, PK index rebuild, full-scan
early-WHERE guards and the StructRow dictionary fallback. The flag is persisted in table metadata and
restored from config on reopen.

- **B3 · arena GC wired into auto-compaction (2026-08-31)** — `CompactStorage` now compacts the
  overflow arena together with the data file: it collects the live arena offsets from the current
  records, rewrites the `.ovf` (copy-on-compact), and re-points the active records' variable slots
  in place (fixed-width records, so re-pointing never changes their length). Dead blocks from
  variable updates / deletes are reclaimed.

- **B4 · constant-offset read-path wins (2026-08-31)** — early-WHERE is re-enabled for fixed-width
  tables using the constant slot offsets of `FixedWidthRecordLayout`:
  - numeric predicates (`col = value` on Integer/Long/Real) read the column directly at its constant
    slot offset (no layout walk, no boxing, no full-row deserialization) — also for columns that
    follow a variable-length column, which the variable-length walk previously rejected;
  - string predicates (`col = 'value'`, Binary collation) compare the arena payload byte-wise
    against the pre-encoded expected UTF-8 (one arena lookup per row, no row dictionary);
  - the StructRow numeric-SIMD batch filter (Integer/Long `Vector<T>`) now also serves fixed-width
    tables (matched records are materialized through the arena-aware dictionary deserialize).
  Arena block offset `0` is a valid offset (first arena block) — only the slot's null flag
  distinguishes NULL.

- **B5 · 1.x → 2.0 record-format migration path (2026-08-31)** — legacy databases (variable-length
  records) migrate to the fixed-width layout in place:
  - the fixed-width flag is now persisted per table in metadata (authoritative on reopen — config no
    longer overrides the on-disk format);
  - opening a legacy database with `DatabaseConfig.FixedWidthRecordLayout = true` **auto-migrates**
    every columnar table (current rows are re-read with the legacy codec, re-serialized as
    fixed-width records into a fresh overflow arena, the data file is swapped atomically and the
    PK / hash indexes are rebuilt);
  - explicit API `IDatabase.MigrateTableToFixedWidth(tableName)` for on-demand conversion (returns
    the migrated row count); read-only opens and page-based tables are never rewritten;
  - a format probe (constant record length + variable slots resolving in the arena) safely adopts
    tables that already store fixed-width records but predate flag persistence (B1–B4), and skips
    migration for byte-identical fixed-size-only legacy tables.

> **Still open (follow-up):** single-file (.scdb) fixed-width support; automatic PageBased →
> Columnar + fixed-width migration; cross-session persistence of the arena free-list.

- **B6 · arena free-list — in-place block reuse (2026-09-01)** — freed overflow blocks are tracked
  in an in-memory free-list (grouped by payload length) and reused via the storage layer's in-place
  overwrite (`IStorage.OverwriteRecordAt`) when a new payload has the exact same length. Same-length
  variable-column updates stop growing the `.ovf` within a session; the copy-on-compact pass still
  reclaims the remaining dead space. Also fixed a latent B1 leak where the first arena block
  (offset 0) was never freed on update (`oldOffset != 0` treated offset 0 as "no block").

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
