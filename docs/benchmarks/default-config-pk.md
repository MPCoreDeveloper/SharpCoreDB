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

### Definitive paired A/B (3 reps, tuned vs plain — isolates NoEncryptMode)

`--pk-ab` with `SHARPCOREDB_PK_AB_ARM_A=tuned` (full knob set, `NoEncryptMode=false`) vs
`SHARPCOREDB_PK_AB_ARM_B=plain` (same set, `NoEncryptMode=true`). Same-window per-rep paired medians
(2026-09-04):

| phase | A `tuned` ops/s | B `plain` ops/s | median B/A |
|---|---:|---:|---:|
| UPDATE | 100,647 | 163,083 | **1.62x** |
| DELETE | 87,251 | 105,932 | **1.59x** |
| INSERT | 95,958 | 125,400 | **1.31x** |
| READ | 58,742 | 71,768 | **1.45x** |

`NoEncryptMode` alone is worth a uniform ~1.3-1.6x across every phase.

## Root cause (code audit)

`NoEncryptMode` is NOT only the per-record at-rest gate. In `Storage` there are two layers:

1. **Per-record at-rest encryption** — `EnableAtRestRecordEncryption` (default false) gates
   `UseRecordEncryption` for record payloads.
2. **File-level encrypt/decrypt wrappers** — `Storage.noEncryption = !config.NoEncryptMode`
   (Storage.Core) is used unconditionally by the whole-file/point read paths that run through
   `Storage.ReadWrite` (`ReadBytes`/`WriteBytes`, `effectiveNoEncrypt = noEncrypt || noEncryption`)
   and by the page-cache loader `Storage.PageCache.LoadPageFromDisk`. With the default
   `NoEncryptMode=false`, every such read pays AES-GCM work even though per-record at-rest
   encryption is off.

That is why the fixed-width fast paths still "engage" (raw contiguous `ReadBytesRange` reads are
plaintext and the counters fire) yet default-config throughput is ~1.3-1.6x lower on ALL phases:
the DML resolution, point reads and page-cache reads around the fast paths still flow through the
encrypting/decrypting wrappers. The paired A/B isolates exactly this flag.

Follow-up candidates (only after a measured `--pk-ab` confirmation on each): route the hot
resolution/scan reads through the same raw range path that already bypasses the wrappers, or make
the file-level wrapper a no-op for the (default, at-rest-off) case without weakening real
encryption when `EnableAtRestRecordEncryption` is on.

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
