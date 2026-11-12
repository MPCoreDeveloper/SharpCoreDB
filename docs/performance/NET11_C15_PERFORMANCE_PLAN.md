# .NET 11 GA / C# 15 — performance plan (only measured wins, zero regression)

> Status: **active** · Started 2026-11-12 (session 87) · Rule: *a change ships only when the path it touches is
> measurably faster and everything else stays green. "The same" is acceptable; a regression is not.*

## 1. What is already in place (do not redo)

| Lever | State | Evidence |
|---|---|---|
| **Runtime Async** (`Features=runtime-async=on`, one definition in `Directory.Build.targets`) | landed | awaited, synchronously-completing call **71,4 B/call → 0 B/call**; `SharpCoreDB.dll` **163 → 2** `IAsyncStateMachine` types (the 2 are async iterators). Canary: `RuntimeAsyncCodegenTests` |
| **Server GC** (`DOTNET_gcServer=1`) | landed as a documented host lever | INSERT **~200k → ~359k ops/sec (≈1,75×)**, READ **+20%**, gate INSERT **0,65×**. Costs a heap per core, so it is server/benchmark, not an embedded default. Documented in `docs/manual/performance.md` §7.7; the server image already sets it |
| **Union outcomes** (`SharpCoreDB.Unions`) | landed (API, not perf) | 3/3 tests, exhaustive switches |
| **Per-phase instrument** (`SHARPCOREDB_PHASE_DIAG=1`) | landed (harness) | wall vs process CPU vs GC counts/pause per phase |

## 2. Where the time actually goes (measured, GA runtime)

Per UPDATE statement on the gate's `docs` workload (≈2,5 µs total):

| Stage | time | allocation |
|---|---|---|
| parse (classification) | ~0,6–0,8 µs | ~432 B (5× `ToString` 128 B, `Dictionary` **216–240 B**, `ParseValue` 24 B, list growth ~40 B) |
| engine-write | ~0,3 µs | ~302 B |
| row-locate-index | ~0,5 µs | ~279 B |
| in-place-patch | ~0,2 µs | ~175 B |
| commit / commit-overwrites | ~0,2 µs | ~150 B |

**The consequence, and it changes the plan's order:** allocation is roughly **12 %** of the per-statement cost, so
allocation-only work has a ceiling of a few percent on UPDATE. The bigger lever by far is the mode in §3.

Refuted optimisations, so nobody spends a session on them again:
- `new Dictionary<string, object>(1)` — **no saving**: 216 B either way (measured, 200k iterations, GA runtime).
- `Table.UnquoteSqlLiteral` — the caller-instance early-out is **already in** (session 79); 32 B/statement is gone.
- `DOTNET_GCgen0size` smaller, `DOTNET_TieredCompilation=0`, deeper warm-up — none move the UPDATE cell.
- **The identity-remap fast path (skip the normalized `object[]` per row) — refuted for the default table shape.** An
  insert never names the hidden AUTO column `_rowid`, so the normalized array is *required* to carry it: aliasing the
  caller's row is impossible whenever any AUTO/hidden column exists, which is the default (`CREATE TABLE` without a
  PK). Implemented and measured 2026-11-12: **894 → 891 B/row**, i.e. 3 B of noise, so it was reverted rather than
  shipped as dead weight. The 112 B/row figure session 81 recorded belongs to a shape without hidden columns.

## 3. The open blocker: UPDATE picks a mode (~215k vs ~420k, 1,86–1,95×)

> **Resolved and implemented (2026-11-12, second half of the session).** The gate prints a `best` column (fastest of
> the three reps) and `--write-baseline` records `rawBest`/`defaultBest` alongside the medians. When a baseline carries
> them, the **capability comparison (best-to-best) decides the verdict**; the median spread still decides for
> median-only baselines and is reported as information otherwise. Recording is no longer gated on the spread (it is not
> a verdict) but refuses a capability more than 25 % below the one on disk. First run: medians spread 3,25× → no median
> verdict, capability 8/8 `ok`, `GATE PASSED`. **P0 done** — the UPDATE cell now yields a verdict a disturbance cannot
> flip. Details: `baselines/README.md` and worklog session 87 addendum 4/5.

Ruled out **by measurement**: machine load (INSERT/READ stay 1,03–1,14× in the same reps), warm-up depth (8/16/2
passes), GC frequency, GC mode, tiering, workload data, fixed-width layout, and **hash-index maintenance**
(`SHARPCOREDB_HASH_INDEXES=0` keeps the split).

The refined signature (per-pass profiler + phase diag, 10 passes): in a *slow* pass exactly **one stage blows up**
— `engine-write` 54,1 ms (normal 1,9–6,3), `row-locate-index` 38,4/27,7 ms (normal 4–8), `in-place-patch` 24,1 ms
(normal 1,1–5,4) — and which stage it is **varies per pass**, while process CPU rises with it (`cpu/wall` ≥ 1).
That is the shape of a **one-off event landing inside whichever stage happens to be running**, not of a different
code path for the whole phase.

Next discriminating experiments, in order:
1. `SHARPCOREDB_WAL_DURABILITY` (benchmark-only) — if the event is a WAL/fsync landing in the window, this removes it.
2. A settle step between phases (wait for background work: index drain, flush loop, maintenance timer) — if the mode
   disappears when the harness waits, the cause is deferred work from the previous phase, and the *engine* question
   becomes whether `InsertBatch` should hand back a settled database.
3. Only then: attribute the extra CPU with `SHARPCOREDB_PHASE_DIAG=1` + `SHARPCOREDB_MAIN_PROFILE_UPDATE=1` in one pass.

## 4. Phases and acceptance criteria

- **P0 — make UPDATE certifiable** (in progress). Either fix the mode, or give the gate an honest UPDATE statistic
  (repeat count + explicit mode reporting) so a verdict is a verdict. *Blocks every UPDATE claim.*
- **P1 — allocation, on a certifiable metric first.** `INSERT` is stable (1,03–1,14×), so its allocation profile is
  the first target: profile `InsertBatch` (`SHARPCOREDB_WRITE_PROFILE`), cut the top item, prove it with a
  red→green allocation canary in the style of `BlockCompressor_Zstd_Compress_AllocatesOnlyTheReturnedFrame`.
- **P2 — structural UPDATE cut (the only change that can move parse materially):** the classifier currently hands
  the core `Dictionary<string, object>` + 5 strings per statement. Replace with a deferred-span representation
  (statement + offsets, materialised only when the core needs a string) or a specialised single-assignment struct.
  Higher risk, so: default-off behind a config flag, full statement/parameter/type matrix, and ship only on a
  measured timing win (allocation alone is not enough here — §2).
- **P3 — host levers** (server GC documented; nothing to code).

## 5. Rails for every change

1. Red→green test on the touched path (allocation tests use `GC.GetAllocatedBytesForCurrentThread` around a warmed loop).
2. The wide suite (today 2123 / 0 / 0 / 0).
3. The standing canaries: `ReopenRoundTripMatrixTests`, `FormatCompatPolicyTests`, `FixedWidthBulkUpdateTests`,
   `FixedWidthBulkDeleteTests`, `FixedWidthPatchTests`, `WritePathProfilerTests`,
   `FixedWidthInlineValueTests.Reopen_KeepsInlineAndOverflowValues`, `SingleFileDirectoryParityTests`.
4. The write-path gate on the same machine/regime as the baseline.
5. Worklog entry with `Session:` / `Verdict:` / `NEXT:`; an item that measures "the same" is recorded as **not
   shipped**, never as a silent micro-change.

## 6. Non-goals

No storage-format, encryption, durability or WAL-semantics change; no `System.Numerics.Vector<T>` (intrinsics only,
per `.github/SIMD_STANDARDS.md`); no version/pack/publish metadata (owner action — a merge to `master` publishes).

## 7. Status log

- 2026-11-12: plan written; P0 has six refutations and a refined signature; P1's first assumption refuted by
  measurement (dictionary capacity saves nothing), so P1 moves to the INSERT path and P2 becomes the UPDATE route.
- 2026-11-12 (later): **P0 done** — capability recording + capability verdict, first `GATE PASSED` a disturbance
  cannot flip. **No regression against the previous version**: gate against the `2026-09-27` baseline → `PASSED`,
  worst cell 1,08×, UPDATE faster on both arms (0,85×/0,94×). **P1 has its number**: `InsertBatch` allocates
  **919 B/row** (1.838.776 B / 2.000 rows), pinned by `InsertBatchAllocationTests` at a 950 B/row ceiling.
- 2026-11-12 (P1, first cut): the per-row presence mask was a statement property, not a row property → hoisted per
  batch. `InsertBatch` **919 → 894 B/row (−25 B, −2,7 %)**, canary ceiling 900 B/row, suite **2124/0**, gate
  **PASSED** (capability INSERT 0,99×). Next: the identity-remap fast path (skip the normalized `object[]` when the
  column order already matches — 112 B/row, session 81's queue head).
- 2026-11-12 (P1, second attempt **reverted**): the identity-remap fast path was implemented and measured — **894 →
  891 B/row**, 3 B of noise — because an insert never names the hidden AUTO column `_rowid`, so the normalized array
  is required. Reverted (dead code is not a win) and recorded on the refuted list above. Remaining INSERT/UPDATE cuts
  are structural (P2), not per-row scratch.
