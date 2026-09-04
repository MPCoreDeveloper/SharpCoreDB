# Fair-PK benchmark with DEFAULT DatabaseConfig (P3 hardening)

Run: `dotnet run --project tests/benchmarks/SharpCoreDB.Benchmarks.Comparative -- -c Release -- --pk-default`
Date: 2026-09-04 · Machine: local dev box · median of 3 runs per phase unless noted.

The SharpCoreDB arm uses a **pure default** `DatabaseConfig` (only the engine type is pinned to
`AppendOnly` — `NoEncryptMode` stays at its default `false`, no page-cache/query-cache/durability
harness flags). The table is the fair-PK schema with `id INTEGER PRIMARY KEY`, so the default
record-layout rules apply: a new PK table becomes **Columnar + fixed-width** and the single-pass
contiguous UPDATE/DELETE paths do engage (verified separately by `DefaultEngineSelectionTests`).

## Results (ops/sec, median of 3)

| Database | INSERT | READ | UPDATE | DELETE |
|---|---:|---:|---:|---:|
| SharpCoreDB (default config) | 111,052 | 69,053 | **84,304** | **92,646** |
| SQLite | 188,247 | 107,122 | 291,414 | 389,120 |
| gap vs SQLite | 1.7x | 1.6x | **3.5x** | **4.2x** |

For comparison, the tuned benchmark config measured earlier in the day (UPDATE ~245K ops/s and
DELETE ~172K ops/s, gaps ~1.2x / ~2.1x).

## Knob isolation (diagnostic single-knob variants, single-shot and median)

The harness supports `SHARPCOREDB_PK_DEFAULT_VARIANT=<name>` for the default arm:
`async`, `bufferedio`, `novalidate`, `noadaptive`, `hsinsert`, `plain` (= full tuned knob set with
`NoEncryptMode=true`), `tuned` (= full tuned knob set, `NoEncryptMode=false`).

Observed (2026-09-04 afternoon):

| variant | UPDATE ops/s | notes |
|---|---:|---|
| pure default | ~84K (median) | reference |
| `async` (WalDurabilityMode) | ~74K (single) | **disproves the FullSync hypothesis** — the mode is only honored by GroupCommitWAL (default off) |
| `bufferedio` / `novalidate` | ~74-76K (single) | within noise |
| `tuned` (no NoEncryptMode) | ~109K (median) | knob set alone is not the gap |
| `plain` (= tuned + NoEncryptMode=true) | ~145K (median) | `NoEncryptMode` moves UPDATE ~1.3x; still below the morning's ~212-245K |

Machine drift is significant: SQLite's own UPDATE varied 275K-315K across these runs. Single-knob
deltas below ~1.3x are not reliably attributable outside a same-window interleaved A/B.

## Same-window interleaved A/B (--pk-ab)

Run: `dotnet run --project tests/benchmarks/SharpCoreDB.Benchmarks.Comparative -- -c Release -- --pk-ab`
Arm names: `SHARPCOREDB_PK_AB_ARM_A` / `SHARPCOREDB_PK_AB_ARM_B` (defaults: `""` pure default vs
`plain`); reps via `SHARPCOREDB_BENCH_REPS` (default 3). Each rep runs A then B back-to-back and
the per-rep **paired ratio** B/A per phase is reported (median), so slow-machine windows affect
both arms of a pair and cancel out — this is the reliable way to attribute default-vs-tuned deltas.

Preliminary smoke result (1 rep, 2026-09-04, default vs `plain`): UPDATE **1.41x**, DELETE **1.28x**,
INSERT 1.21x, READ 1.10x — direction consistent with the earlier medians, now measured inside a
single window. Re-run with the default 3 reps before quoting final numbers.

## Honest conclusion

1. The earlier claim that the default `WalDurabilityMode.FullSync` dominates the gap is **wrong**
   (disproven by the `async` variant). Do **not** implement a “FullSync commit-flush optimization”
   based on it.
2. The default path is correct and engages the fast paths; the `--pk-ab` smoke (same window) shows
   `plain` (NoEncryptMode=true) ahead by UPDATE 1.41x / DELETE 1.28x, so the remaining gap
   correlates with `NoEncryptMode`; the exact mechanism (record/at-rest toggles and file-format
   decisions) still needs a code-level explanation before any change.
3. The same-window A/B tooling now exists; re-run with 3 reps to quantify the paired ratio, then
   investigate the `NoEncryptMode` mechanism in code and only then consider a change.
