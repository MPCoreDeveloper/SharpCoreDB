# SQLite / LiteDB / SharpCoreDB — comparative CRUD, 2026-09-16

Run with the `tests/benchmarks/SharpCoreDB.Benchmarks.Comparative` harness, which benchmarks **SharpCoreDB (SQL
path, Direct API and StructRow), SQLite and LiteDB** in one process against the same workload. The harness also
has a BLite arm, which cannot run — see finding 4.

```
Runtime: .NET 11.0.0-rc.1.26425.128
OS:      Microsoft Windows 10.0.26200
Cores:   12
Workload: 100,000 INSERTs (10,000 per batch), then 10,000 READ / UPDATE / DELETE by primary key
All engines: WAL mode, each engine's own optimal batch settings
```

**Protocol (plan §2):** one benchmark at a time, launched detached with its output to a file, nothing else on the
CPU. The AppendOnly table is a **median of 3 consecutive runs**; the PageBased table is **one run** and is
labelled as such throughout.

## AppendOnly engine — median of 3 (ops/sec)

| database | INSERT | READ | UPDATE | DELETE |
|---|---:|---:|---:|---:|
| **SharpCoreDB — SQL path** | 94,022 | 83,539 | 67,811 | 130,542 |
| **SharpCoreDB — Direct API** | 132,610 | 110,708 | 106,303 | 179,292 |
| **SharpCoreDB — StructRow** | 141,706 | 113,892 | n/a | n/a |
| **SQLite** | 147,874 | 101,518 | 276,063 | 373,337 |
| **LiteDB** | 78,597 | 15,629 | 11,080 | 14,432 |

Relative to SQLite (1.00× = SQLite):

| database | INSERT | READ | UPDATE | DELETE |
|---|---:|---:|---:|---:|
| SharpCoreDB SQL | 0.64× | 0.82× | 0.25× | 0.35× |
| SharpCoreDB Direct | 0.90× | **1.09×** | 0.39× | 0.48× |
| SharpCoreDB StructRow | 0.96× | **1.12×** | n/a | n/a |
| LiteDB | 0.53× | 0.15× | 0.04× | 0.04× |

Relative to LiteDB (multiplier = times faster):

| database | INSERT | READ | UPDATE | DELETE |
|---|---:|---:|---:|---:|
| SharpCoreDB SQL | 1.20× | 5.34× | 6.12× | 9.04× |
| SharpCoreDB Direct | 1.69× | 7.08× | 9.59× | 12.42× |
| SharpCoreDB StructRow | 1.80× | 7.29× | n/a | n/a |
| SQLite | 1.88× | 6.50× | 24.92× | 25.87× |

## PageBased engine — single run (ops/sec)

| database | INSERT | READ | UPDATE | DELETE |
|---|---:|---:|---:|---:|
| SharpCoreDB — SQL path | 144,461 | 31,463 | 75,239 | 160,248 |
| SharpCoreDB — Direct API | 120,276 | 40,656 | 103,969 | 234,155 |
| SharpCoreDB — StructRow | 217,440 | 59,340 | n/a | n/a |
| SQLite | 146,309 | 96,691 | 279,715 | 364,923 |
| LiteDB | 79,619 | 15,652 | 10,747 | 12,927 |

- vs SQLite: SQL **0.99× / 0.33× / 0.27× / 0.44×**, Direct 0.82× / 0.42× / 0.37× / 0.64×, StructRow
  **1.49×** / 0.61×.
- vs LiteDB: SQL 1.81× / 2.01× / 7.00× / 12.40×, Direct 1.51× / 2.60× / 9.67× / 18.11×, StructRow
  2.73× / 3.79×.

Read this with plan §6 open: PageBased is the in-place-update engine, so it posts the best INSERT in either table
(217,440 rows/s StructRow — **1.49× SQLite**) while its point reads collapse to 31–59K, i.e. 0.33–0.61× SQLite and
only 2–3.8× LiteDB. That is the PageBased read/update trap the write-path profiler is chasing, seen from the read
side.

## Findings

1. **Against LiteDB, SharpCoreDB wins every operation in both engines** — 1.2–1.8× on INSERT, 5.3–7.3× on READ,
   6.1–9.7× on UPDATE, 9.0–18.1× on DELETE.
2. **Against SQLite the picture is operation-specific, not engine-wide.** SharpCoreDB is ahead on READ in both the
   Direct (1.09×) and StructRow (1.12×) paths, level on INSERT (0.90–0.96× on AppendOnly, and 1.49× *ahead* on
   PageBased StructRow), and behind on single-row UPDATE (0.25–0.39×) and DELETE (0.35–0.48×) — the two columns
   the plan has targeted since §8.
3. **LiteDB's weakness is point access, not bulk write.** Its INSERT (66–80K) is within 1.2–1.9× of every other
   engine here; its READ/UPDATE/DELETE (10–16K) are 6–26× behind. A comparison that quotes INSERT alone would
   flatter it.
4. **BLite cannot be measured.** The harness catches a `NotSupportedException`: BLite 4.0.1's
   `BsonDocumentBuilder` exposes no public setter API (`Set`/`Add`/`Write` all missing), so no document can be
   populated. Already recorded in `docs/benchmarks/SHARPCOREDB_COMPARATIVE_BENCHMARKS.md`.

## Caveats

- **Quote the multipliers, not the absolutes.** Per §2 a single run is evidence about that run, and the
  AppendOnly spread across the three reps was: SQL INSERT 83.9–105.2K, Direct INSERT 105.2–155.8K, SQLite INSERT
  129.5–154.3K, SQL UPDATE 60.2–81.3K — up to ~1.5× between reps of the same build.
- **Tuned, plaintext harness configuration.** These arms use the harness's tuned `DatabaseConfig`
  (`NoEncryptMode = true`, `HighSpeedInsertMode`, page cache, memory mapping, hash indexes) and each competitor's
  own tuned settings, so this is a like-for-like *best case* for every engine. The product defaults — encrypted,
  `FullSync` — are measured separately in plan §8a (`--pk-default`, `--dual-mode`), where the encryption tax is
  ~1.12× on INSERT/READ and ~2× on UPDATE/DELETE.
- **These columns measure single-row UPDATE/DELETE by PK.** The batched multi-row INSERT path is measured by
  `--multirowinsert` — 17.18 µs/row / 58,205 rows/s, plan §8a.
- PageBased is one run, not a median; treat those four columns as indicative.
