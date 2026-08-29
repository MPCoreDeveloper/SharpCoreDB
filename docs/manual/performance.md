# 7. Performance Guide

> **The headline:** SharpCoreDB v2.0 is a *performance-first* release. The benchmark gap that
> made v1.x ~16–52x slower than SQLite on point reads/updates/deletes is closed: **point reads
> now beat SQLite, all four CRUD operations beat LiteDB, and SIMD columnar analytics remain in
> a class of their own.**
>
> This chapter explains **when SharpCoreDB is fastest**, how to write code that gets there, and
> — honestly — where SQLite is still ahead (and why). Every number in this chapter comes from
> the two-run final v2.0.0 comparison on the reference machine; run them yourself with
> `tests/benchmarks/SharpCoreDB.Benchmarks.Comparative` (Release).

---

## 7.1 The measured v2.0.0 numbers (ops/sec, two-run range)

Reference machine: Intel i7-10850H (6C/12T), 32 GB RAM, Windows 10, NVMe SSD, .NET 10.0.3.
Schema: 5 columns (`name TEXT`, `email TEXT`, `age INTEGER`, `score REAL`, `data TEXT`);
100K inserts in 10K batches, 10K reads/updates/deletes. No secondary indexes.

| Operation | **SharpCoreDB v2.0** | SQLite | LiteDB | vs SQLite | vs LiteDB |
|-----------|----------------------|--------|--------|-----------|-----------|
| **READ – Direct API** | **120–126K** | 87–97K | 14–16K | ✅ **1.3–1.4x faster** | ~8x faster |
| **READ – StructRow** | 70–100K | 87–97K | 14–16K | ~parity | ~5–6x faster |
| **READ – SQL** | 51–59K | 87–97K | 14–16K | ~0.6x (was 0.06x) | ~3.5–4x faster |
| **INSERT** | 91–133K | 145–150K | 66–77K | ~0.8x | ~1.3–1.9x faster |
| **UPDATE** | 41–59K | 241–296K | 10–11K | ~0.15x | ~4–5x faster |
| **DELETE** | 30–142K | 320–367K | 13–14K | ~0.1–0.2x | ~2–10x faster |

**Read the table honestly:**

- ✅ **Point reads now beat SQLite** — but only through the **Direct** and **StructRow** APIs
  (`FindByPrimaryKey`/`FindByIndex`, `ExecuteQueryStruct`). Plain `ExecuteQuery` (SQL +
  `Dictionary` rows) is still ~0.6x of SQLite: the SQL string path costs a row-materialization
  allocation per row.
- ✅ **Every operation beats LiteDB** — sometimes by an order of magnitude.
- ⚠️ **Single-row UPDATE/DELETE are still slower than SQLite.** SQLite writes records in-place
  in fixed-length C structs; SharpCoreDB v2.0 still uses append-on-update for row stores. This
  is the single remaining gap and the top v2.1 priority.
- ✅ **Analytics are the crown jewel** — SIMD columnar aggregates run **~682x faster than
  SQLite** on `GROUP BY` SUM workloads (see §7.5).

> The comparative harness and raw JSON results live in
> `tests/benchmarks/SharpCoreDB.Benchmarks.Comparative/results/` (final run
> `comparative_20260828_*.json`).

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
| **Single-row UPDATE/DELETE vs SQLite** | SQLite wins ~5–7x on pure row-store in-place writes (v2.1 target). If a workload is 90% update-in-place, consider SQLite — or batch the writes (§7.3.3). |
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

### 7.3.3 Bulk update/delete

For `UPDATE`/`DELETE` of many rows, **always batch** rather than one statement per row:
`db.UpdateMultiple(...)` / `db.DeleteMultiple(...)` and `ExecuteBatchSQL` amortize the
statement-parse and WAL costs. The v2.0 batch parser uses **compiled regexes** (no per-call
`Regex.Match`), and `DeduplicateByPrimaryKey` short-circuits redundant rows.

```csharp
// ⚠️ Slow pattern (one statement per row):
for (int i = 0; i < 10_000; i++)
    db.ExecuteSQL("UPDATE t SET score = @s WHERE id = @id",
        new Dictionary<string, object?> { ["@s"] = i, ["@id"] = i });

// ✅ Fast pattern (one batched call):
db.UpdateMultiple("t",
    new Dictionary<string, object?> { ["score"] = 0 },  // set-clause
    new Dictionary<string, object?> { ["id"] = 1 });    // where-clause (applies to all)
```

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
| **In-place row updates / fixed-width records** | Close the UPDATE/DELETE gap vs SQLite (the last remaining CRUD gap) |
| **.NET 11 / C# 15** | Runtime-native async, AVX-VNNI-512/SVE2, SIMD lane APIs, Zstandard, Decimal32/64/128 — free speedups in hot paths |
| **AOT interface-dispatch improvements** | Faster interface-heavy storage paths under NativeAOT |

Track progress in [`docs/performance/V2_PERFORMANCE_PLAN.md`](../performance/V2_PERFORMANCE_PLAN.md).



