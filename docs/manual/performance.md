# 7. Performance Guide

> **The headline:** SharpCoreDB v2.1.0 is the current *performance-first* release, on the .NET 11 /
> C# 15 line. The v1.x gap (point reads/updates/deletes ~16–52x behind SQLite, because every update
> appended a new row) is closed as an engine behaviour, and re-measured on 2026-10-11 (`92794084`)
> the **default posture is INSERT 1,17x (1,03–1,27) and READ 1,13x (1,12–1,15) ahead of SQLite** while
> **UPDATE 0,53x (0,42–0,53) and DELETE 0,47x (0,46–0,62)** stay behind — see §7.1.
>
> This chapter explains **when SharpCoreDB is fastest**, how to write code that gets there, and —
> honestly — where SQLite is still ahead (and why). Run the current numbers yourself with
> `tests/benchmarks/SharpCoreDB.Benchmarks.Comparative` (Release; `--pk-default` for the product
> posture, `--pk` for the record-layout A/B), or read `docs/BENCHMARK_RESULTS.md`.

---

## 7.1 The current v2.1.0 numbers

Harness: `tests/benchmarks/SharpCoreDB.Benchmarks.Comparative` — `--pk-default` (the product posture:
pure `DatabaseConfig` with the engine pinned to `AppendOnly`, encryption on, no harness flags) and
`--pk` (the tuned record-layout A/B), median of 3 runs, fixed-width Columnar fast path, ascending-PK
batches, measured 2026-10-11 on `92794084`. Every ratio is **same-run** against that run's own SQLite
arm, because SQLite's own reference drifts between sessions — ratios are the comparable part, never
lone absolutes.

**Default posture (`--pk-default`, encryption on) — ratio is SharpCoreDB ÷ SQLite, so > 1,00x means
SharpCoreDB is ahead; the parenthesised range is the paired per-rep range, and one that straddles
1,00x means the cell sits inside this box's noise and must be re-run, not rounded:**

| Operation (ops/s) | **SharpCoreDB v2.1.0 (default, encrypted)** | SQLite (same run) | ratio |
|---|---:|---:|---:|
| **INSERT** | **204.263** | 174.469 | **1,17x** (1,03–1,27) |
| **READ** | **134.634** | 116.916 | **1,13x** (1,12–1,15) |
| **UPDATE** | **398.709** | 773.323 | 0,53x (0,42–0,53) |
| **DELETE** | **509.941** | 1.085.140 | 0,47x (0,46–0,62) |

**Record-layout A/B (`--pk`, tuned config, plaintext, two runs) — the ratio column here is SQLite ÷
arm, exactly as the harness prints it, so > 1,00x means SQLite is faster:**

| Arm (ops/s, range of two runs) | INSERT | READ | UPDATE | DELETE |
|---|---:|---:|---:|---:|
| fixed-width, plaintext | 227–237K | 138–142K | **534–629K** | **687–716K** |
| fixed-width, at-rest | 191–192K | 107–113K | 292–329K | 455–488K |
| legacy variable-length, plaintext | 218–233K | 134–175K | 360–372K | 425–466K |
| SQLite (same runs) | 177–183K | 112–113K | 796–811K | 999K–1,04M |
| gap vs SQLite (fixed-width, plaintext) | 0,8x | 0,6–0,8x | 1,3–1,5x | 1,4–1,5x |

**Read the table honestly:**

- ✅ **INSERT and READ are ahead of SQLite in the default posture** (1,17x and 1,13x, neither range
  straddling 1,00x), and the fixed-width layout is **1,2–1,5x ahead of the legacy variable-length
  layout** on UPDATE/DELETE — the layout change paying off, measured with the layout as the only
  variable.
- ⚠️ **SQLite is still faster on point UPDATE and DELETE** — 1,3–1,5x / 1,4–1,5x on the tuned
  fixed-width arm and ~1,9x / ~2,1x in the default posture. Earlier published claims that the
  fair-PK layout was *ahead* on UPDATE/DELETE were measured while the harness left the SQLite arm
  allocating a command and two parameters per row; SQLite's own PK references jumped **3,05x** once
  the comparator held one prepared command (288.108 → 878.557 UPDATE, 385.116 → 1.172.704 DELETE),
  so those readings are superseded, not "better".
- ⚠️ **Per-record encryption costs 1,2–1,9x** on the otherwise-identical fixed-width arm (INSERT
  1,18–1,25x, READ 1,22–1,32x, UPDATE 1,83–1,91x, DELETE 1,41–1,57x) — a real, measured at-rest tax
  rather than a hidden one.
- ✅ **Analytics remain the crown jewel** — SIMD columnar aggregates run far faster than SQLite
  on `GROUP BY` SUM workloads (see §7.5).

### 7.1.1 Historical reference — v2.0.0 launch numbers (2026-08-29)

Superseded by §7.1 (the fixed-width layout is now the default for new PK tables); kept for the
AppendOnly-vs-PageBased roadmap archive. Runs: 2026-08-29, 12 cores, .NET 10.0.11, Windows 11,
NVMe SSD; 100K inserts in 10K batches, 10K reads/updates/deletes.

| Operation | **v2.0 — AppendOnly** | **v2.0 — PageBased** | SQLite | LiteDB |
|-----------|-----------------------|----------------------|--------|--------|
| **READ – Direct** | **114K** | 34K | 99K | 16K |
| **READ – StructRow** | 93K | 36K | 99K | 16K |
| **READ – SQL** | 64K | 30K | 99K | 16K |
| **INSERT – batch (Direct/StructRow)** | 100–103K | **194–206K** | 109–144K | 75K |
| **UPDATE** | 42–50K | **58–65K** | 239–279K | 10–11K |
| **DELETE** | 29–128K | **107–129K** | 317–376K | 13–14K |

> Raw JSON: `tests/benchmarks/SharpCoreDB.Benchmarks.Comparative/results/`
> (`comparative_20260829_135914.json` AppendOnly, `comparative_20260829_140203.json` PageBased).

---

## 7.2 When SharpCoreDB has the **best** performance

Use SharpCoreDB as your *first choice* for these workloads:

| Workload | Why SharpCoreDB wins | Section |
|----------|----------------------|---------|
| **PK/unique point reads in a hot loop** | zero-allocation `StructRow`/Direct path, hash-index keyed lookup, zero-reparse plan fast path | §7.3.1 |
| **Bulk ingestion** (`InsertBatch`) | SQL-free insert path, batched WAL fsync, keyed index maintenance | §7.3.2 |
| **SIMD columnar analytics** | `Vector<T>` aggregates over contiguous column segments | §7.5 |
| **Numeric range filters over wide tables** | fixed-offset predicate fast path + SIMD batch filtering (integer/long) | §7.5.2 |
| **Vector similarity search** | SIMD distance kernels + HNSW, sub-millisecond p50 at 10M+ vectors | §7.6 |
| **Encrypted-at-rest embedded storage** | AES-256-GCM built in — no external FDE needed | — |
| **Everything compared with LiteDB** | faster on every CRUD axis, plus SQL, vector, analytics | — |

**Be aware of where it is not yet fastest:**

| Workload | Honest guidance |
|----------|-----------------|
| **Single-row UPDATE vs SQLite** | now competitive on the keyed path (~0.8–1.7x, §7.1) with the fixed-width default; the tuned-harness numbers assume ascending-PK batches |
| **Pure DELETE vs SQLite** | SQLite still wins ~2.1–3.5x on pure row-store deletes (v2.1 target). If a workload is ~90% delete-in-place, consider SQLite or batch/compact (§7.3.3). |
| **Pure default config** | runs ~1.3–1.6x slower than the tuned harness (`NoEncryptMode` file-level wrapper overhead — documented root cause, see `docs/benchmarks/default-config-pk.md`) |
| **Very large complex multi-way joins** | SQLite's mature query engine can still win on pathological joins; profile first. |
| **Maximum raw INSERT** | SQLite edges ahead by ~10–20% using its fastest C bulk path. |

---

## 7.3 The API tiers — pick the right tool per hot path

SharpCoreDB exposes read/write APIs on a deliberate **performance ladder**. The further down
the ladder, the faster the loop. Use the top tiers for *correctness and convenience*, and the
bottom tiers for *hot paths*:

### 7.3.1 Reads

```csharp
// TIER 1 — ad-hoc, dynamic SQL, max flexibility. Allocates one Dictionary per row.
foreach (var row in db.ExecuteQuery("SELECT * FROM t WHERE age > @a",
    new Dictionary<string, object?> { ["@a"] = 30 }))
{
    Console.WriteLine(row["name"]);
}

// TIER 2 — zero-allocation struct rows. The v2.0 fast path. (⭐ fastest SQL reads)
foreach (var row in db.ExecuteQueryStruct(
    "SELECT * FROM t WHERE name = @n",
    new Dictionary<string, object?> { ["@n"] = "Ada" }))
{
    int age = row.GetValue<int>("age");          // by column name
    string name = row.GetValue<string>("name");
}

// TIER 3 — Direct API: no SQL parsing at all. (⭐ fastest point reads)
var row = db.FindByPrimaryKey("t", key: 42);      // by primary key → Dictionary or null
var byName = db.FindByIndex("t", "name", "Ada");  // by any indexed column → List<Dictionary>
```

**Rules of thumb for hot read loops:**

1. Prefer `FindByPrimaryKey(table, key)` / `FindByIndex(table, column, value)` over
   `SELECT ... WHERE pk = @p` for pure point lookups — the Direct API bypasses SQL parsing.
2. Prefer `ExecuteQueryStruct` over `ExecuteQuery` for SQL reads inside loops.
3. **Reuse the parameter dictionary** (mutate in place) — avoid allocating a fresh dictionary
   per iteration.
4. Read `GetColumnNames()` once outside the loop and cache your column indexes.
5. Keep the working set of SQL statements small enough to stay in the `QueryPlanCache`
   (one cached plan per normalized SQL).

### 7.3.2 Writes

```csharp
// TIER 1 — single-row convenience.
db.Insert("t", new Dictionary<string, object> { ["name"] = "Ada" });

// TIER 2 — parameterized SQL.
db.ExecuteSQL("INSERT INTO t (name) VALUES (@n)",
    new Dictionary<string, object?> { ["@n"] = "Ada" });

// TIER 3 — multi-row statement.
db.ExecuteSQL("INSERT INTO t (name) VALUES ('A'),('B'),('C')");

// TIER 4 — batch API. (⭐ fastest ingestion: no SQL, one fsync per batch)
var batch = new List<Dictionary<string, object?>>();
for (int i = 0; i < 100_000; i++)
    batch.Add(new Dictionary<string, object?> { ["name"] = $"C{i}", ["age"] = i });
db.InsertBatch("t", batch);
db.Flush();   // single durability point for the whole batch
```

**Why `InsertBatch` is fast:** the SQL parser is bypassed, column order is resolved once from
the cached schema, index maintenance happens in keyed batches, and the WAL groups the whole
batch into a single fsync. This is why SharpCoreDB inserts ~1.3–1.9x faster than LiteDB and
within ~15% of SQLite.

**There are two `InsertBatch` overloads and the column-ordered one is measurably faster.** The
dictionary overload allocates one `Dictionary<string, object>` per row and makes the engine do one
name-based lookup per column per row; the array overload hands over column-ordered values aligned with
a column list, so neither happens:

```csharp
// ✅ Dictionary overload (convenient, self-describing):
db.InsertBatch("t", new List<Dictionary<string, object>>
{
    new() { ["name"] = "u1", ["score"] = 1.5 },
});

// ✅ Column-ordered overload (dictionary-free; 1,10–1,25× the row rate of the one above,
//    measured on the encrypted default posture and on a no-PK document table):
string[] columns = ["name", "score"];
db.InsertBatch("t", new List<object[]> { ["u1", 1.5] }, columns);
db.Flush();
```

Both apply the same defaults, AUTO values, NOT NULL checks and index maintenance; a column omitted
from `columns` gets the engine default (for a TEXT column that is the empty string, not NULL), and the
internal `_rowid` column must not be listed. Engine-side allocation is identical between the two
(a measured 2.304 vs 2.313–2.342 B/row, same file size) — the difference is the per-column name
lookups and the caller's much smaller row objects.

> **One statement per row is the slowest ladder.** Measured on one-row writes, a single
> `ExecuteSQL("INSERT …")` costs **~25 µs/row more** than the equivalent `Table.Insert(dictionary)`
> call — the SQL layer classifies, tokenizes, plan-caches and re-parses every statement. For per-row
> writes prefer the Direct API or a batch; reserve one-statement-per-row SQL for the shapes that need
> it. `DatabaseConfig.EnableBufferedAppends = true` additionally lets single-row INSERTs share one
> append buffer (measured **25×** on 2,000 inserts), and `EnableDeferredDeleteIndexes` (default on)
> covers the delete side.

### 7.3.3 Bulk update/delete

For `UPDATE`/`DELETE` of many rows, **always batch** rather than one statement per row. Two batch
routes exist and both amortize the statement-parse and WAL costs:

* **`ExecuteBatchSQL(List<string>)`** — the SQL route. The v2.0 batch parser uses **compiled regexes**
  (no per-call `Regex.Match`), recognises the canonical single-row DML shape with a quotes-aware span
  scan, and `DeduplicateByPrimaryKey` short-circuits redundant rows. Best when you already have SQL text.
* **`db.UpdateBatch` / `db.DeleteBatch`** — the SQL-free route. You hand the engine the key column with
  an already-typed value plus typed SET values, so no statement is classified, no `WHERE` string is
  rebuilt and no literal is parsed back into a value. Best for bulk mutation from data you already hold
  in CLR types.

```csharp
// ⚠️ Slow pattern (one statement per row, ~25 µs/row of SQL-layer work):
for (int i = 0; i < 10_000; i++)
    db.ExecuteSQL("UPDATE t SET score = @s WHERE id = @id",
        new Dictionary<string, object?> { ["@s"] = i, ["@id"] = i });

// ✅ SQL batch (statement list, one transaction):
db.ExecuteBatchSQL(updateStatements);
db.Flush();

// ✅ SQL-free batch (typed keys and values; no SQL text at all):
db.UpdateBatch("t", operations);   // List<(string KeyColumn, object? KeyValue, Dictionary<string, object> Values)>
db.DeleteBatch("t", keys);         // List<(string KeyColumn, object? KeyValue)>
db.Flush();
```

Both `UpdateBatch` and `DeleteBatch` return the number of rows changed. The key column must be the
primary key or carry an index for the batch to resolve through the engine's direct locate paths — an
unindexed key column still works, through a predicate fallback. On a fixed-width table with a primary
key and ascending keys they reach the same contiguous, single-range-write fast path the SQL route uses.

**Measured (same-engine attribution, three-arm paired protocol, `docs/performance/WORKLOG.md`
sessions 45–48): the SQL-free route is 1,5–3,5× the SQL batch route on the fair no-PK shape, 1,53× on
UPDATE and 1,91× on DELETE in the encrypted default posture, and 1,35× / 1,16× on the no-PK `docs`
job. It is the same engine doing the same work; what differs is the per-statement text layer. Ranges
straddle on isolated slow reps, so treat those as medians rather than floors.**

**`DELETE` index maintenance is deferred by default (v2.1).** A DELETE writes only the durable
tombstone and skips its per-key index removal; the primary-key B-tree is rebuilt from the data file
(dropping tombstones) on reopen — or, if `DeferredDeleteIndexThreshold` is crossed (default 100,000,
deliberately high), at the next non-transactional delete. Readers are unaffected — a point lookup
treats a tombstoned position as absent and a re-INSERT of a deleted key still succeeds. Measured as a
same-session interleaved A/B on the random-key delete workload: DELETE **1.92×** faster plaintext and
**1.33×** in the encrypted default. The observable difference is that `GetHashIndexStatistics` counts
tombstoned entries until the index is rebuilt; set `DatabaseConfig.EnableDeferredDeleteIndexes = false`
for the eager behaviour.

---

## 7.4 The v2.0 fast-path machinery (what changed)

Understanding this lets you write code that *hits* the fast paths:

| Mechanism | What it does | Commit |
|-----------|--------------|--------|
| **No debug file I/O** | Removed unconditional `File.AppendAllText` to `D:\*.log` on every SELECT/execute/transaction/INSERT — the single biggest v1.x read bottleneck | `e877375a` |
| **`SimpleSelectPlan` zero-reparse fast path** | Simple `SELECT … WHERE key = @p` plans resolve from cache without re-lexing | `78a418ba` |
| **Compiled regexes** | All hot-path regexes precompiled (batch update/delete parsing, provider detection) | `78a418ba`, `0fc34a95` |
| **Cached DI (`IGraphRagProvider`)** | No per-call `GetService` lookup in `GetSharedSqlParser` | `78a418ba` |
| **Regex-free `NormalizeSql`** | Plan-cache key generation no longer uses regex | `78a418ba`, `0fc34a95` |
| **Keyed `HashIndex.Add/Remove`** | Index maintenance operates on the key only — no full row copy | `09a5b865` |
| **No row copies in `UpdateMultiple`** | Removed `new Dictionary(row)` copying on the batch-update path | `09a5b865` |
| **`LookupPositionsUnsafe`** | No-copy position lookup under an explicit write-lock contract | `0fc34a95` |
| **`ExecuteQueryFast` + allocation short-circuit** | Precompiled regexes + `NormalizeSql` that bails out when no substitution is needed | `0fc34a95` |
| **Provider fast paths** | `OPTIONALLY` keyword check avoids full parse per `ExecuteReader`; span-based single-file/sqlite_master detection | `18c3c1d3` |
| **`ExecuteQueryStruct` + `VariableLengthSchema` cache** | First-class zero-alloc SQL reads; column layout cached, not re-parsed per row | `74756521` |
| **Fixed-offset numeric WHERE fast path** | Numeric predicates skip the generic comparator; `Vector<T>` SIMD batch filters for Integer/Long columns | `68e0f38f` |
| **Native AOT readiness** | AOT-safe type conversion, `[RequiresDynamicCode]` annotations, source-gen DTOs/JSON | `aa9738cd` |

## 7.5 SIMD analytics — where SharpCoreDB is in a class of its own

The columnar engine stores each column as a contiguous array of scalars and evaluates
aggregates with `Vector<T>` (auto-vectorized `Sum`/`Min`/`Max`, AVX2/AVX-512/NEON where
available, scalar fallback otherwise). Measured results on the reference machine:

| Workload | SharpCoreDB | SQLite | Speedup |
|----------|------------|--------|---------|
| `GROUP BY` SUM over 10M rows | ~2 ms | ~1,300 ms | **~682x** |
| `COUNT(*)` / `MIN` / `MAX` / `AVG` over 10M rows | ms-scale | seconds-scale | 100s of x |

### 7.5.1 Use columnar storage for analytic tables

```sql
-- Declare the table columnar:
CREATE TABLE telemetry (
  ts     DATETIME,
  metric INTEGER,
  value  REAL,
  site   TEXT
) STORAGE = COLUMNAR;

-- Hash index over the grouping key for fast group resolution:
CREATE INDEX idx_telemetry_metric ON telemetry (metric);
```

### 7.5.2 Numeric predicates use SIMD batch filtering

For wide tables, a `WHERE score > @min` predicate is evaluated with SIMD compares over whole
column vectors before any row is materialized:

```csharp
// ⭐ This pattern benefits from the v2.0 SIMD numeric filter fast path:
foreach (var row in db.ExecuteQueryStruct(
    "SELECT * FROM telemetry WHERE score > @min AND site = @site",
    new Dictionary<string, object?> { ["@min"] = 99.9, ["@site"] = "us-east" }))
{
    // ...
}
```

**Guidance:**

- Prefer **numeric** predicates on the hot path (Integer/Long/Real) — they use the SIMD filter.
- Put the most selective predicate first in the `WHERE` clause; the early-WHERE in the columnar
  scan prunes rows before column materialization.
- Keep the scan column narrow (`SELECT score` rather than `SELECT *`) when you only need a
  handful of columns — columnar storage makes this nearly free.

## 7.6 Vector search

HNSW index + SIMD distance kernels (cosine, euclidean, dot). On the reference machine:
~9,200 QPS at 8 threads with p50 ≈ 0.53 ms over 10M+ vectors. The engine adapts its SIMD width
to the CPU (`Vector256`/`Vector512`/NEON) instead of crashing when AVX-512 is absent.

```csharp
// Query vectors with SQL:
var rows = db.ExecuteQuery(
    "SELECT id, distance FROM vectors ORDER BY embedding <-> @q LIMIT 10",
    new Dictionary<string, object?> { ["@q"] = queryEmbedding });

// Or via the VectorSearch package API with explicit top-K.
```

See [`docs/Vectors/PERFORMANCE_TUNING.md`](../Vectors/PERFORMANCE_TUNING.md) for index
parameter tuning (`M`, `efConstruction`, `efSearch`).

## 7.7 Configuration knobs that affect performance

| Knob | Effect |
|------|--------|
| `DatabaseConfig.NoEncryptMode = true` | Removes AES-256-GCM per-record cost (use on already-encrypted volumes / benchmarks) |
| `DatabaseConfig.EnableDeferredDeleteIndexes` (default `true`) | Defers DELETE index maintenance to the reopen rebuild — **1.92×** on random-key deletes (same-session A/B). Set `false` for eager maintenance (keeps `GetHashIndexStatistics` exact between deletes) |
| `DatabaseConfig.DeferredDeleteIndexThreshold` (default 100,000) | Deferred DELETE keys that may accumulate before the PK B-tree is rebuilt. ⚠️ the rebuild is a **full O(n) index pass**, so keep it high relative to your delete rate — a low value cancels the deferral win (measured: **294K → 70K** ops/s when a 10,000-delete batch crossed a 10,000 threshold) |
| `DatabaseConfig.EnableBufferedAppends` (default `false`) | Single-row INSERTs share the append buffer — one open/write/close per flush boundary instead of per row (**~512 µs → ~4.5 µs** per 64-byte record; **25×** end-to-end). Bounds via `AppendBufferFlushThresholdBytes` / `AppendBufferFlushIntervalMs` |
| WAL durability batching (`Flush()` / `Commit(force: false)`) | Groups fsyncs; dramatically raises write throughput at the cost of a tiny durability window |
| Append-only vs page-based storage engine | Choose per workload (append-heavy vs update-heavy) |
| `STORAGE = COLUMNAR` | For analytic tables |
| `QueryPlanCache` capacity | A larger working-set capacity avoids cache eviction for hot statements |
| `PreparedStatements` (`db.Prepare(sql)` → `ExecutePrepared`/`ExecuteCompiledQuery`) | Compile once, execute many — removes parse + plan costs from the loop |
| Collation `BINARY` | The only collation eligible for SIMD byte-compare paths |
| Hash index on the lookup key | O(1) point access; enables the Direct/StructRow fast path |

## 7.8 Query-writing rules of thumb

1. **Parameterize everything.** Parameterized statements hit the plan cache; string-built SQL
   does not. Positional `?` placeholders fall back to the legacy binder in v2.0 — prefer
   `@name` for the fast path.
2. **Reuse `IDatabase` instances** and prepared statements across calls.
3. **Batch writes** — never one statement per row in a loop when `InsertBatch` /
   `UpdateMultiple` / `ExecuteBatchSQL` fit.
4. **Keep hot SELECTs simple.** The zero-reparse fast path covers `SELECT … FROM t WHERE
   key = @p` shapes. Complex joins/subqueries parse normally — fine, but don't put them in
   per-request loops without a prepared statement.
5. **Don't `SELECT *` in a hot loop** unless you need every column; fetch only what you consume.
6. **Use `ExecuteQueryStruct` / `FindByPrimaryKey`** for read loops — that is the v2.0 headline path.
7. **Turn off encryption during development** when the volume is already encrypted — it is real,
   measurable write-path cost.
8. **Measure on the target machine.** Numbers vary by CPU/SIMD width, RAM, and storage. Use
   `tests/benchmarks/SharpCoreDB.Benchmarks.Comparative` (Release) and the result JSONs.

## 7.9 Reproducing the benchmarks

```bash
cd tests/benchmarks/SharpCoreDB.Benchmarks.Comparative
dotnet run -c Release
```

The harness prints ops/sec for SharpCoreDB (SQL / Direct / StructRow), SQLite, and LiteDB and
writes a timestamped JSON to `results/`. Every v2.0 work package was validated against this
harness, and the two-run final ranges are recorded in §7.1.

## 7.10 What's next (v2.1)

| Item | Expected win |
|------|--------------|
| **DELETE fast path — landed (v2.1)** | Deferred index maintenance (default on) cut random-key DELETE to **2.19×**/1.19× (plaintext/encrypted); the remaining gap is the tombstone write plus per-statement overhead |
| **Single-statement SQL ladder** | The SQL layer still adds ~25 µs/row over the direct API on one-row statements; the remaining cost is spread across per-statement parsing and plan-cache work |
| **.NET 11 / C# 15** | Runtime-native async, AVX-VNNI-512/SVE2, SIMD lane APIs, Zstandard, Decimal32/64/128 — free speedups in hot paths |
| **AOT interface-dispatch improvements** — **measured 2026-10-09: no win** | The .NET 11 dispatch helper is *documented* to speed interface-heavy paths, but on this harness native AOT is **at or behind the JIT on 12 of 16 cells** (SQLite's own cells on the same rig read 0,98–1,01), so no AOT profile is recommended — publish AOT for deployment size and startup, not throughput. Per-cell table and both harness defects the measurement exposed: [`../BENCHMARK_RESULTS.md`](../BENCHMARK_RESULTS.md) §NativeAOT · [BEAT plan §S4](../performance/BEAT_SQLITE_ALL_AXES_PLAN.md) |

Track progress in [`docs/performance/V2_PERFORMANCE_PLAN.md`](../performance/V2_PERFORMANCE_PLAN.md).



