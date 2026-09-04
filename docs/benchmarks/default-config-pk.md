# Fair-PK benchmark with DEFAULT DatabaseConfig (P3 hardening)

Run: `dotnet run --project tests/benchmarks/SharpCoreDB.Benchmarks.Comparative -- -c Release -- --pk-default`
Date: 2026-09-04 · Machine: local dev box · median of 3 runs per phase.

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

For comparison, the same schema with the tuned benchmark config (NoEncryptMode + Async WAL + larger
batches) measures UPDATE ~245K ops/s and DELETE ~172K ops/s (gaps ~1.2x / ~2.1x).

## Honest conclusion

The out-of-the-box default is **correct and engages the fast paths, but is not yet at the tuned
throughput**: UPDATE/DELETE land ~3.5-4.2x behind SQLite (vs ~1.2-2.1x tuned). The dominant
difference is the **default `WalDurabilityMode.FullSync`** (per-commit flush) versus the benchmark
arm's `Async`; the per-batch commit cost explains most of the gap. Fast-path counters prove the
contiguous code is running — the throughput is limited by durability flushing, not by resolution.

## Recommendation / follow-up

1. Do **not** silently weaken the durability default (`FullSync` is the safe choice for
   production). Instead, optimize the **FullSync commit flush** path (fewer/single flush per
   commit, group-commit of the commit markers + overwrites that already batch per page) so a
   synchronous commit costs a few ms instead of tens of ms.
2. Re-run this `--pk-default` harness after that change and require the default-config UPDATE/DELETE
   gap to move from ~3.5-4.2x toward ~2x before the release-cut.
3. Keep this file updated with the latest median-of-3 numbers.
