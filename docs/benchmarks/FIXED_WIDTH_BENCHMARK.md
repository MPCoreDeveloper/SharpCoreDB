# Fixed-Width vs Legacy — benchmark results

Run: 2026-09-01, .NET 11.0.0-preview.7, Windows, Release.
Command: `dotnet run --project tests/benchmarks/SharpCoreDB.Benchmarks.Comparative -- --fixedwidth`

The same workloads run against a legacy (variable-length records) database and a fixed-width
database (directory-mode Columnar, `DatabaseConfig.FixedWidthRecordLayout = true`). Both databases
use identical settings (no encryption, memory mapping, page cache).

## Results

| Workload | Metric | Legacy | Fixed-width | Win |
|---|---|---|---|---|
| A · 10,000 growing variable-column updates | elapsed | 17.77 s | **3.18 s** | **~5.6× faster** |
| A · 10,000 growing variable-column updates | storage growth (post-auto-compact) | 0.0 KB | 20.4 KB | ≈ |
| B · 1,000 variable updates + arena compaction | elapsed | 0.45 s | **0.23 s** | **~2× faster** |
| B · 1,000 variable updates + arena compaction | storage growth | 23 B | 17 B | ≈ |
| C · 30 full scans, non-indexed `WHERE category = -1` over 100,000 rows | time per query | 6.22 ms | **2.12 ms** | **~2.9× faster** |
| D · 100,000 batch inserts | throughput | 242,487 rows/s | 208,692 rows/s | ~14% slower |

## Interpretation

- **Updates (growing variable values) — ~5.6× faster.** Legacy appends a new record per growing
  update and pays for full `.dat` compactions (1000-update threshold); fixed-width keeps the `.dat`
  constant (in-place overwrite), grows only the overflow arena, and compacts only the arena (B1/B3).
- **Non-indexed full-scan WHERE — ~2.9× faster.** Fixed-width reads the predicate column at its
  constant slot offset (numeric early-WHERE) or compares the arena payload (string early-WHERE) and
  skips full-row deserialization for non-matches (B4).
- **Variable updates + compaction — ~2× faster.** The arena copy-on-compact is cheaper than a
  `.dat` rewrite (B3).
- **Inserts — ~14% slower.** Fixed-width writes each variable value into the overflow arena
  (payload encoding + free-list bookkeeping); for insert-heavy workloads the legacy format is
  slightly faster. This is the expected trade-off: fixed-width targets update-heavy / point-read
  workloads.
