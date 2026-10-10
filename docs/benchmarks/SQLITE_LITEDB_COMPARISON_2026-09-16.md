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

> ⚠️ **Read this first: the DML columns in this job are not like-for-like.** The default job's SharpCoreDB arms
> declare **no primary key** (`Program.cs:738`, `:858`, `:977`), so their UPDATE/DELETE run
> `UPDATE docs SET score = … WHERE name = 'User…'` — a predicate on a **non-key** column — while the SQLite arm of
> the same job targets `id INTEGER PRIMARY KEY` (`:1073-1074`), an **INTEGER PRIMARY KEY = rowid**, i.e. an in-place
> page edit. The harness says so itself (`:906`: *"UpdateByPrimaryKey requires a PK column, so we use the SQL
> path"*). The resulting gap is not an engine property — on the fair-PK job, where **both** engines run
> `WHERE id = @pk`, the same UPDATE column measures SharpCoreDB **385,668** against SQLite **325,813**.
>
> The comparable view — both engines on `WHERE id = @pk`, tuned, plaintext, `--pk` job, median of 3, current build:
>
> | database | INSERT | READ | UPDATE | DELETE |
> |---|---:|---:|---:|---:|
> | SharpCoreDB (fixed-width, plaintext) | 97,316 | **120,166** | **385,668** | 213,727 |
> | SQLite | 196,404 | 101,695 | 325,813 | 418,093 |
> | ratio | 0.50× | **1.18× (ahead)** | **1.18× (ahead)** | 0.51× |
>
> On the fair shape SharpCoreDB **already wins READ and UPDATE** and is ~2× behind on INSERT and DELETE. The
> tables below measure a different, less favourable SharpCoreDB usage and are kept because they are what this
> harness has always published — but they must not be quoted as an engine-to-engine verdict. Note also that even
> the fair-PK INSERT is not route-symmetric: SharpCoreDB issues a batch of SQL *literals* (parsed per row) while the
> SQLite arm reuses one prepared statement inside a transaction.

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

## Refresh, 2026-09-16 16:00 — same regime, but set explicitly (ops/sec)

The figures above were taken in whatever environment the shell happened to hold. The diagnostic switches are
environment variables and that shell persists, so the 2026-09-16 session's runs were all buffered-appends +
FullSync WAL without that being stated anywhere (plan §11 carries the full correction). This refresh sets those
same two switches **deliberately** — `SHARPCOREDB_BUFFERED_APPENDS=1`, `SHARPCOREDB_WAL_DURABILITY=fullsync` — so
the deltas below measure code changes rather than a regime change. The competitor columns are the control:

| database | INSERT | READ | UPDATE | DELETE |
|---|---:|---:|---:|---:|
| SharpCoreDB — SQL path | 99,355 | 75,818 | 65,570 | 117,243 |
| SharpCoreDB — Direct API | 126,663 | 133,349 | 123,049 | 192,012 |
| SharpCoreDB — StructRow | 137,642 | 123,484 | n/a | n/a |
| SQLite (control) | 148,845 | 99,521 | 272,190 | 375,350 |
| LiteDB (control) | 75,491 | 14,048 | 10,977 | 15,055 |

- **Nothing moved outside the documented spread.** SQLite's own reference moved ≤2% and LiteDB ≤4%, so the machine
  is comparable; every SharpCoreDB column landed between −10% and +20%, inside the inter-rep spread this document
  already records (up to ~1.5×). Today's two fixes are invisible here for structural reasons rather than by luck:
  the query-cache capacity gate only affects workloads whose statement text differs *per row*, which the batched
  100K INSERT and the PK-bound UPDATE/DELETE never do, and the append-buffer fix only affects the **unbuffered**
  path, which this regime does not use.
- **Multi-row INSERT micro-benchmark, same regime:** **62,827 rows/s / 15.92 µs/row**, against 58,205 rows/s /
  17.18 µs/row recorded and 56,672 / 17.65 measured earlier in the day — inside ±8% rep noise, and the fastest of
  the three. Allocation 4,937 B/row, 14–16 gen0 collections per pass. The batched shape is unaffected by the
  buffer fix by construction: one 65,536-byte buffer per *statement*, not per row.
- **The `--pk` engine arms disagree by far more than any noise**, which is the useful finding. Same session, same
  regime, one variable:

| fair-PK arm, fixed-width plaintext | INSERT | READ | UPDATE | DELETE |
|---|---:|---:|---:|---:|
| AppendOnly | 102,833 | 110,366 | **391,668** | **878,487** |
| PageBased | 114,605 | 239,370 | 52,904 | 302,154 |
| SQLite | 190,333 | 105,491 | 302,923 | 400,589 |

  AppendOnly posts **7.4× PageBased's UPDATE** and **2.9× its DELETE** (and beats SQLite on both: UPDATE ×1.29,
  DELETE ×2.19), while PageBased is ahead on READ (2.2× AppendOnly, 2.27× SQLite) and INSERT (1.11×). That is the
  PageBased write trap this document's PageBased section describes, now confirmed within one regime and one
  session instead of across runs — and it is where the remaining SQL-path UPDATE work should be aimed.

## Refresh, 2026-09-16 18:21 — after the §4b inline-capacity default

Same explicit regime as the 16:00 refresh (`SHARPCOREDB_BUFFERED_APPENDS=1`, `SHARPCOREDB_WAL_DURABILITY=fullsync`),
but every fixed-width table now uses the **shipped default inline capacity of 16**. This is therefore not like-for-like
with the tables above in the sense that matters: **the record layout itself changed**, and the deltas below are the
layout's, not a machine's. SQLite's control columns stayed within 3 % on the first run (its DELETE swings ±12 %
run-to-run, which is worth remembering when reading the fourth column).

**AppendOnly, fair PK, fixed-width plaintext** (median of 3 per run; three runs shown because the DELETE column moves):

| run | INSERT | READ | UPDATE | DELETE |
|---|---:|---:|---:|---:|
| 16:00 (capacity 0) | 102,833 | 110,366 | 391,668 | 878,487 |
| 18:21 (capacity 16) | 125,152 | 112,580 | 359,262 | 691,037 |
| 18:2x rep 1 | 131,345 | 129,038 | 358,537 | 684,106 |
| 18:2x rep 2 | 135,233 | 123,891 | 373,371 | 516,819 |

**PageBased, fair PK, fixed-width plaintext:**

| run | INSERT | READ | UPDATE | DELETE |
|---|---:|---:|---:|---:|
| 16:00 (capacity 0) | 114,605 | 239,370 | 52,904 | 302,154 |
| 18:21 (capacity 16) | **193,413** | **300,864** | **61,860** | 307,762 |

**The layout change is a trade, and it should be read as one.** Inlining short values removes an overflow-arena write
per short variable-length value, which is why INSERT improves sharply on every arm — AppendOnly +22 % to +32 % on the
fixed-width arm, +35 % legacy, +46 % at-rest, and **PageBased INSERT reaches parity with SQLite (1.0×, from 1.7×)**,
with its READ up 26 % and UPDATE up 17 % too. The cost is space (every variable-length column reserves `2 + 16` bytes
per record — 760,000 → 1,840,000 B on the benchmark schema) and, now measurably, **the AppendOnly DELETE column**:
878,487 (capacity 0) against a 516,819–691,037 cluster at capacity 16, i.e. roughly **−20 % to −40 %**, with UPDATE
about 7 % down. Bigger records mean more bytes per delete, so the direction is explicable; whether it is worth the
INSERT gain is a trade the owner should see rather than have decided by a default. **Follow-up: re-examine the
AppendOnly delete path against the larger record before treating this as settled.**

## Findings

1. **Against LiteDB, SharpCoreDB wins every operation in both engines** — 1.2–1.8× on INSERT, 5.3–7.3× on READ,
   6.1–9.7× on UPDATE, 9.0–18.1× on DELETE.
2. **Against SQLite the picture is operation-specific, not engine-wide.** SharpCoreDB is ahead on READ in both the
   Direct (1.09×) and StructRow (1.12×) paths, level on INSERT (0.90–0.96× on AppendOnly, and 1.49× *ahead* on
   PageBased StructRow), and behind on single-row UPDATE (0.25–0.39×) and DELETE (0.35–0.48×) — the two columns
   the plan has targeted since §8.
   ⚠️ **The fair-PK harness disagrees with this row by ~5×, and that is the most useful thing in this document.**
   Same build, same session, same regime, `--pk` fixed-width plaintext: **UPDATE 1.29× *ahead* of SQLite (391,668)
   and DELETE 2.19× ahead (878,487)** against this table's 0.24× and 0.31× (65,570 / 117,243). Both are by-PK runs,
   so the deficit here is route- or layout-dependent rather than mechanical — a default-job table that resolves to the
   legacy variable-length layout cannot use the in-place fast paths that the fixed-width arm uses. Reconciling the two
   is **priority 1** of the plan (§9) and is instrumentation work first, not a change.
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
  `--multirowinsert` — 1,000 rows/statement: 17.65 µs/row / 56,672 rows/s; and one statement per row
  (`SHARPCOREDB_MULTIROW_ROWS=1`), which is the shape that exposed the query-cache capacity-gate defect:
  **25.47 µs/row / 39,269 rows/s, up from 57.70 µs/row / 17,332 rows/s** (2.27×, plan §11).
- **Regime caveat on those two `--multirowinsert` figures, and on the four columns above.** The shell that runs
  the harness persists between commands and the diagnostic switches are environment variables, so the figures in
  this document taken during the 2026-09-16 session were measured with `SHARPCOREDB_BUFFERED_APPENDS=1` and
  `SHARPCOREDB_WAL_DURABILITY=fullsync` still set from earlier turns — **buffered appends with a FullSync WAL**,
  not the harness's tuned `Async` default and not the product default. The **ratios** are unaffected: both halves
  of every comparison shared the regime, and the arms here still match the previously recorded table within noise,
  which was taken in that same regime. The **absolutes** describe buffered appends. For scale, the same shape
  measured minutes apart: **write-through appends 961 rows/s / 1,037.51 µs/row / 76,467 B allocated per row / 244
  gen0 per pass, against buffered 32,725 rows/s / 30.56 µs/row / 7,925 B per row / 25 gen0** — a 34× cliff, and
  the reason `Storage.AppendBytes` documents its write-through branch at 0.4597 ms per value. Plan §11 carries the
  full correction table and the note that the per-call open is a reader-sharing decision, not a mechanical fix.
- PageBased is one run, not a median; treat those four columns as indicative.
