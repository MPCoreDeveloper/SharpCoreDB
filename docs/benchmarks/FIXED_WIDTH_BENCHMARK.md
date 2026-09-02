# Fixed-Width vs Legacy — benchmark results

Run: 2026-09-01, .NET 11.0.0-preview.7, Windows, Release.
Command: `dotnet run --project tests/benchmarks/SharpCoreDB.Benchmarks.Comparative -- --fixedwidth`

The same workloads run against a legacy (variable-length records) database and a fixed-width
database (directory-mode Columnar, `DatabaseConfig.FixedWidthRecordLayout = true`). Both databases
use identical settings (no encryption, memory mapping, page cache).

## Results

| Workload | Metric | Legacy | Fixed-width | Win |
|---|---|---|---|---|
| A · 10,000 growing variable-column updates | elapsed | 17.99 s | **2.12 s** | **~8.5× faster** |
| A · 10,000 growing variable-column updates | storage growth (post-auto-compact) | 0.0 KB | 20.4 KB | ≈ |
| B · 1,000 variable updates + arena compaction | elapsed | 0.59 s | **0.14 s** | **~4× faster** |
| B · 1,000 variable updates + arena compaction | storage growth | 23 B | 17 B | ≈ |
| C · 30 full scans, non-indexed `WHERE category = -1` over 100,000 rows | time per query | 6.31 ms | **2.63 ms** | **~2.4× faster** |
| D · 100,000 batch inserts | throughput | 242,487 rows/s | 208,692 rows/s | ~14% slower |

## Interpretation

- **Updates (growing variable values) — ~8.5× faster.** Legacy appends a new record per growing
  update and pays for full `.dat` compactions (1000-update threshold); fixed-width keeps the `.dat`
  constant (in-place overwrite), grows only the overflow arena, and compacts only the arena (B1/B3).
- **Non-indexed full-scan WHERE — ~2.4× faster.** Fixed-width reads the predicate column at its
  constant slot offset (numeric early-WHERE) or compares the arena payload (string early-WHERE) and
  skips full-row deserialization for non-matches (B4).
- **Variable updates + compaction — ~4× faster.** The arena copy-on-compact is cheaper than a
  `.dat` rewrite (B3).
- **Inserts — ~14% slower.** Fixed-width writes each variable value into the overflow arena
  (payload encoding + free-list bookkeeping); for insert-heavy workloads the legacy format is
  slightly faster. This is the expected trade-off: fixed-width targets update-heavy / point-read
  workloads.

## Comparative CRUD vs SQLite/LiteDB (post B7 update-path work)

AppendOnly engine (`--engine=appendonly`), 100K inserts / 10K reads / 10K updates / 10K deletes:

| Database | INSERT ops/s | READ ops/s | UPDATE ops/s | DELETE ops/s |
|---|---|---|---|---|
| SharpCoreDB (SQL) | 91,873 | 65,858 | 37,203 | 60,211 |
| SharpCoreDB (Direct) | 116,146 | 141,293 | 46,189 | 61,554 |
| SharpCoreDB (StructRow) | 135,322 | 120,224 | – | – |
| SQLite | 148,654 | 95,143 | 281,072 | 351,863 |
| LiteDB | 78,569 | 13,721 | 9,641 | 14,710 |

PageBased engine (`--engine=pagebased`):

| Database | INSERT ops/s | READ ops/s | UPDATE ops/s | DELETE ops/s |
|---|---|---|---|---|
| SharpCoreDB (SQL) | 123,995 | 30,807 | 69,574 | 124,176 |
| SharpCoreDB (Direct) | 124,917 | 39,669 | 102,160 | 175,887 |
| SharpCoreDB (StructRow) | 209,068 | 51,438 | – | – |
| SQLite | 146,301 | 94,523 | 266,673 | 372,029 |
| LiteDB | 71,677 | 13,616 | 10,337 | 14,061 |

- **vs LiteDB: SharpCoreDB wins every workload** (1.5–8×).
- **vs SQLite:** SharpCoreDB wins on INSERT (StructRow) and on READ (AppendOnly Direct + SQL after
  B9); SQLite remains ~6–8× faster on UPDATE/DELETE. The batch UPDATE path is now in-place and
  **deserialize-free on the hot path** (only the changed fields are patched at their slot
  offsets) — the remaining gap is the per-statement parser + write-behind bookkeeping vs SQLite's
  specialized b-tree writes.

### Point-read micro-benchmark (`--readtest`, median of 7 × 10K reads on 100K rows)

| Path | ops/s | notes |
|---|---|---|
| SQL `SELECT * FROM docs WHERE name = @name` | ~120,000–166,000 | B9 direct hash-index lookup (was ~65,000) |
| Direct `FindByIndex("docs", "name", …)` | ~160,000–188,000 | reference |
| SQL/Direct overhead | 1.1–1.5× | was ~2× |
| SQLite (same workload) | ~95,000 | — |
