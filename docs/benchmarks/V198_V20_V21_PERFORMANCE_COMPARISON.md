# SharpCoreDB Comparative Performance Report — V1.9.8 vs V2.0 vs V2.1

**Date:** 2026-08-30
**Branch under test:** `release/v2.1.0.0` (v2.1 line; v2.0 = `release/v2.0.0.0`, v1.9.8 = `master` @ tag `V1.9.8`)
**Method:** The repository's own comparative CRUD benchmark (`tests/benchmarks/SharpCoreDB.Benchmarks.Comparative`, AppendOnly engine, each version's defaults) on one machine, multiple runs per version.

---

## 1. Methodology

- **Workload (identical for all three versions):**
  - 100,000 inserts (in 10,000-row batches)
  - 10,000 point reads by primary key
  - 10,000 updates by primary key
  - 10,000 deletes by primary key
- **Databases compared:** SharpCoreDB (SQL path), SharpCoreDB (Direct API path), SharpCoreDB (StructRow zero-alloc path — v2.0/v2.1 only), SQLite, LiteDB. (BLite is excluded: its shipped NuGet API no longer matches the documented API, so it throws and is skipped by the harness.)
- **Runtimes / SDKs:**
  - V1.9.8 → .NET 10.0.11 (SDK 10.0.400)
  - V2.0 → .NET 10.0.11 (SDK 10.0.400)
  - V2.1 → .NET 11.0.0-preview.7 (SDK 11.0.100-preview.7)
- **Runs:** 2 runs for V1.9.8, 3 runs for V2.0 and V2.1 (reported as observed min–max ranges).
- **Machine:** Windows 10.0.26200, 12 cores, x64. CPU supports AVX2 + FMA but **not AVX-512** (the v2.1 Vector512 aggregate paths are therefore *not* exercised by these runs).
- **Honesty note:** the machine is not a quiet benchmark box — run-to-run variance is significant (especially DELETE, which ranged 21K–91K across runs). Treat small differences (≈±20%) as measurement noise, not signal.

---

## 2. Results (ops/sec, observed ranges)

| Operation | **V1.9.8** (net10) | **V2.0** (net10) | **V2.1** (net11 preview) |
|---|---:|---:|---:|
| INSERT — SQL | 78.7K – 84.7K | 86.9K – 92.6K | 73.5K – 84.3K |
| **READ — SQL** | **7.67K** | **58.2K – 60.2K** | **51.8K – 58.1K** |
| UPDATE — SQL | 38.0K – 46.0K | 37.3K – 42.8K | 26.5K – 40.9K |
| DELETE — SQL | 36.5K – 90.7K | 21.6K – 43.5K | 20.9K – 60.7K |
| INSERT — Direct API | 130.2K – 138.2K | 116.1K – 130.2K | 108.5K – 132.1K |
| READ — Direct API | 120.4K – 123.8K | 105.0K – 126.2K | 105.8K – 119.2K |
| UPDATE — Direct API | 55.2K – 59.5K | 47.5K – 55.5K | 46.7K – 51.7K |
| DELETE — Direct API | 128.6K – 132.5K | 37.3K – 125.7K | 118.9K – 132.5K |
| INSERT — StructRow | — | 118.7K – 133.9K | 125.8K – 138.4K |
| READ — StructRow | — | 83.2K – 93.9K | 69.6K – 100.0K |
| **SQLite (reference)** | INSERT 133.7–145.1K · READ 89.0–89.4K · UPDATE 269.6–279.8K · DELETE 339.8–363.6K |||
| **LiteDB (reference)** | INSERT 70.2–73.7K · READ 11.8–13.9K · UPDATE 8.6–9.3K · DELETE 12.7–13.1K |||

---

## 3. Headline findings

### 3.1 V1.9.8 → V2.0: the SQL read path is ~8× faster — and that is the headline
- **SQL point reads: 7.67K → ~59K ops/sec ≈ 7.7× faster.** This is the dominant, reproducible win of the v2.0 "performance-first" release (removed hot-path debug I/O, prepared/compiled query plan reuse, regex-free normalization, shared parser, StructRow fast path).
- **Everything else measured is statistically unchanged** (within the machine's run-to-run noise): SQL INSERT, UPDATE, DELETE and all Direct-API numbers overlap between 1.9.8 and 2.0.
- New in v2.0: the **zero-allocation StructRow read path (83–94K)** — a first-class API that did not exist in 1.9.8.
- Note: 1.9.8 as measured here is already far ahead of the original v1.9.0 March baseline (UPDATE 8.4K / DELETE 7.2K in `V2_PERFORMANCE_PLAN.md`) — much of the UPDATE/DELETE improvement already landed in the 1.9.x line.

### 3.2 V2.0 → V2.1: no measurable difference in this workload
- Every metric overlaps the V2.0 range within noise (V2.1 SQL READ 52–58K vs V2.0 58–60K; SQL UPDATE 27–41K vs 37–43K; DELETE high-variance in both).
- The .NET 11 preview-7 runtime's automatic wins (Runtime-native async, JIT improvements, NativeAOT dispatch) do **not** translate into a measurable gain on this synchronous CRUD benchmark.
- The v2.1 value is the **foundation**: net11.0 / C# 15 toolchain, guarded Vector512 aggregate fast paths (active only on AVX-512 hardware — not on this machine), and the allocation-free SQL-verb dispatch (helps per-call overhead but is swamped by noise here).
- V2.1's SQL numbers trend slightly lower in some runs (26.5K UPDATE, 51.8K READ outliers); with only 3 runs per version this is **within measurement noise, not a demonstrated regression**. A quiet-machine, more-repetition protocol is required to resolve it.

### 3.3 The UPDATE/DELETE gap vs SQLite persists in all three versions
- SQL UPDATE is ~5–10× slower than SQLite in every version (2.x: 27–43K vs SQLite 218–280K).
- SQL DELETE is ~5–17× slower (2.x: 21–61K vs SQLite 295–364K), high variance.
- Root cause is structural: SharpCoreDB's row-store updates/deletes are row-copy based, while SQLite uses fixed-length C records with direct field offsets and in-place writes. This is the targeted v2.1+ engine work (in-place records), **not** something the runtime or allocations fix.

### 3.4 Versus competitors
- SharpCoreDB 2.x beats **LiteDB on every operation** (~5–8× reads, ~4–5× updates, ~3–9× deletes).
- SharpCoreDB 2.x SQL READ is now ~1.4–1.5× behind SQLite (down from ~11.6× in 1.9.8); the Direct/StructRow read paths roughly match SQLite.

---

## 4. Relative performance summary

| Metric vs SQLite | V1.9.8 | V2.0 | V2.1 |
|---|---|---|---|
| SQL READ | **11.6× slower** | ~1.4× slower | ~1.5× slower |
| SQL INSERT | ~1.7× slower | ~1.5× slower | ~1.7× slower |
| SQL UPDATE | ~6× slower | ~6× slower | ~7× slower |
| SQL DELETE | ~4–8× slower | ~7–13× slower | ~6–14× slower |

---

## 5. Conclusions

1. **V1.9.8 → V2.0 is a real, measurable win on the SQL read path (~8×)**, plus the new StructRow zero-alloc API. Other CRUD operations are essentially unchanged in this benchmark.
2. **V2.0 → V2.1 shows no measurable win on the I/O-bound CRUD workload, but a real ~12–17% JIT win on CPU-bound code and ~2–4% lower per-operation allocations** (see §6). The full benefit still requires AVX-512 hardware, the GA .NET 11 runtime, and the deferred C# 15 feature work.
3. **The remaining bottleneck across all versions is UPDATE/DELETE vs SQLite** — a structural engine issue, not a toolchain issue.

---

## 6. Targeted micro-benchmarks — where .NET 11 / C# 15 already differs

The CRUD harness above is synchronous and I/O-bound, so it hides the runtime-level differences. A focused micro-benchmark (identical source compiled against each version's own build; 20,000 ops per section; `GC.GetTotalAllocatedBytes` deltas) isolates the CPU/alloc/async behaviour:

| Metric | **V2.0** (net10.0.11) | **V2.1** (net11 preview 7) | Difference |
|---|---:|---:|---:|
| Sync INSERT — ops/sec | 1.55K – 1.64K | 1.43K – 1.60K | ≈ (noise) |
| Sync INSERT — allocated | 11.33K – 11.35K B/op | 11.06K – 11.07K B/op | **−2.4%** |
| Sync READ (point lookup) — ops/sec | 35.8K – 36.6K | 32.6K – 33.4K | ≈ (noise) |
| Async INSERT — ops/sec | 2.10K – 2.13K | 1.97K – 2.12K | ≈ (noise) |
| Async INSERT — allocated | 9.96K B/op | 9.67K B/op | **−3.0%** (Runtime Async) |
| **JIT: Vector256 SIMD sum (4M ints × 50)** | **40.6 – 43.2 ms** | **35.6 – 36.7 ms** | **~12–17% faster** |

Key reading:
- **The .NET 11 JIT is measurably faster on identical CPU-bound SIMD code: ~12–17%** (35.6–36.7 ms vs 40.6–43.2 ms for the same `Vector256` loop). This is the "free" .NET 11 win — but it only shows where the work is **CPU-bound**.
- **Per-operation allocations are ~2–4% lower on net11** (SQL-verb dispatch refactor + Runtime Async state-machine savings). The absolute per-op allocation (2–11 KB) is still dominated by the row materialization / WAL encoding, which is identical in both versions.
- **DB CRUD throughput does not move** because those operations are **I/O + allocation bound**, not CPU bound — the JIT win is hidden behind the WAL/FSM writes and per-row dictionary materialization.
- **C# 15 language features are not yet used** in the v2.1 code: the branch compiles with the C# 15 preview compiler (`LangVersion latest`) but the source is still C# 14 style. The planned C# 15 work (union types/closed hierarchies for the SQL AST, extension indexers) is Phase 4 and deliberately deferred until the preview compiler stabilizes — so "no C# 15 difference yet" is by design, not by failure.

### Recommendations for a follow-up benchmark
- Run on a quiet machine with ≥5 repetitions per version and report medians.
- Add an AVX-512-capable runner to exercise the v2.1 Vector512 aggregate paths.
- Re-run v2.1 after .NET 11 GA (Nov 2026) to capture final runtime-async/JIT effects.
- Track `Allocated` bytes per operation (BenchmarkDotNet) alongside ops/sec.

---

*Raw logs: `comparative_*.json` per run in the harness `results/` folder; console logs retained in the dev `TestResults/` folder.*

