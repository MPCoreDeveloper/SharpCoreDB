# SharpCoreDB — Autonomous Performance Work Brief

**Reader:** an autonomous coding agent (DeepSeek Flash) running unattended, long sessions.
**Mission:** close the remaining performance gap vs SQLite, working **fully independently**.
**Companion files:** `docs/performance/INSERT_UPDATE_PERFORMANCE_PLAN.md` (single source of truth),
`docs/performance/WORKLOG.md` (your only reporting channel), `docs/performance/V2_PERFORMANCE_PLAN.md`.

---

## How to start a session (paste this to the agent)

> Read `docs/performance/AUTONOMOUS_AGENT_BRIEF.md` and follow it exactly. Do not ask questions,
> do not pause for approval, do not stop to share status. Log everything to
> `docs/performance/WORKLOG.md` and commit locally to a feature branch. Resume from the last
> `NEXT:` item in the worklog.

---

## 0. Operating rules (these override everything else)

1. You work **alone and unattended**. The human is at work and **cannot answer questions**.
2. **NEVER ask a question.** NEVER stop for approval. NEVER pause to share status. A "should I…?"
   moment is a decision you make yourself, record, and move on.
3. The **only** reporting channel is the worklog (`docs/performance/WORKLOG.md`, append-only).
   The human reads it when they return. Do not print status essays into the console.
4. If something is ambiguous: pick the most reasonable option consistent with this brief, write the
   decision + reason into the worklog, and continue.
5. If a work item is blocked, or a measurement refutes your hypothesis: record the finding **with
   evidence** in the worklog, mark the item `BLOCKED` or `REJECTED`, and move to the next item.
   Do **not** spin on it. Do **not** ask.
6. Keep three things green at all times: **build**, **core test suite**, **`--gate`**. Any gate
   failure must be documented with a re-run, never ignored.
7. Section 11 (safety rails) is absolute. Breaking a rail is worse than making no progress.
8. Do not stop at the end of a work item — pick up the next one from section 5 until the session
   ends or the mission's acceptance criteria are met.
9. **Respect the timeboxes in §5.** No single work item may consume more than its timebox. When a
   timebox expires you stop, record what you learned, and move on — even mid-experiment.
10. **A bug you find, you fix — in the same session** (owner directive, 2026-09-26). A defect found
    while measuring is never parked as a note, a `TODO`, a worklog bullet or a hand-off: reproduce
    it, fix the cause (read the code that produces the wrong result first; if the same logic is
    duplicated, fix every copy), leave a test that fails before and passes after, run build + core
    suite, and record the evidence in the worklog. Hand it on only when §11 (safety rails) or the
    owner blocks the fix, or when it is outside this repository — then log it with evidence, mark it
    `BLOCKED`, and keep it in the `NEXT:` line. Never fix anything by weakening a guarantee
    (encryption, durability, or a test that then passes). Worked example: session 62 closed both of
    session 61's handed-on findings this way.

## 1. Mission and success criteria

Beat SQLite on the fair-PK CRUD benchmark (all four columns) and bring the PageBased engine's UPDATE
to parity, **before November 2026**.

Targets (median-of-3, tuned plaintext fixed-width, `--pk` arm; ratio = SharpCoreDB ÷ SQLite, so
> 1.0 = ahead):

| Operation | Current | Target |
|---|---:|---:|
| READ | 1.26× ahead | stay ahead (≥ 1.0×) |
| UPDATE | 1.29× ahead | stay ahead (≥ 1.0×) |
| DELETE | 1.62× ahead | stay ahead (≥ 1.0×) |
| INSERT | **0.87× behind** (was 0.63× before decision 8) | **≥ 1.0× (faster than SQLite)** |
| PageBased UPDATE | parity | ≥ 0.5×, ideally parity |

Absolute acceptance targets (plan §8): UPDATE ≥ 120K, DELETE ≥ 150K, INSERT ≥ 150K ops/s. **All three are now met**
on the shipped build: the first two were already, and INSERT crossed the floor when decision 8 raised the inline
capacity to 24 (**130–135K → 162–171K ops/s**, plan §8e). The INSERT *ratio* is 0.87× because this machine's same-run
SQLite reference reads 176–199K, against the ~155K it read when the 1.0× bar was set — so decision 4 is **"absolute
met, ratio short against a faster reference"**, and both halves are quoted together.
**Scope:** every ladder (SQL, Direct, StructRow) **and** the providers (ADO.NET, EF Core, Dapper,
Linq2DB, YesSql, Sync). Encrypted and plaintext are reported **separately**, never as one number.

## 2. Repository map and commands

- Root: `d:\repos\MPCoreDeveloper\SharpCoreDB`
- SDK (pinned in `global.json`): .NET 11 RC `11.0.100-rc.1.26425.128`; targets `net11.0` /
  C# 15 preview **only**. Do not change this.
- **Read these before acting:** `docs/performance/INSERT_UPDATE_PERFORMANCE_PLAN.md`
  (2710 lines — the single source of truth; §8 targets, §9 priorities, §11 instrumentation),
  `docs/performance/V2_PERFORMANCE_PLAN.md`, `docs/manual/performance.md`,
  `tests/benchmarks/SharpCoreDB.Benchmarks.Comparative/baselines/README.md`.
- **Key code to know:**
  - `src/SharpCoreDB/DataStructures/Table.CRUD.cs` — UPDATE/DELETE in-place paths, `UpdateMultiple`
  - `src/SharpCoreDB/Database/Execution/Database.Batch.cs` — batch dispatch, `TryParseUpdateForBatch`
  - `src/SharpCoreDB/DataStructures/Table.PerformanceOptimizations.cs`
  - `src/SharpCoreDB/Database/Execution/Database.PerformanceOptimizations.cs`
  - `src/SharpCoreDB/Services/SqlParser.DML.cs`, `src/SharpCoreDB/Services/SqlParser.DDL.cs`
  - `src/SharpCoreDB/Storage/*` — `IStorage`, `Storage`, `IStorageEngine`

Commands (run from the repo root; PowerShell):

```powershell
# Build (validates the compile)
dotnet build tests/SharpCoreDB.Tests/SharpCoreDB.Tests.csproj -c Release -f net11.0

# Core test suite — MTP/xunit.v3 host. Run the EXE directly; `dotnet test` is NOT supported.
dotnet build tests/SharpCoreDB.Tests/SharpCoreDB.Tests.csproj -c Release -f net11.0
tests\SharpCoreDB.Tests\bin\Release\net11.0\SharpCoreDB.Tests.exe

# Benchmarks — use this direct form.
# NOTE: an earlier revision of this brief warned that tools/clean-benchmark.ps1 line 25 had a
# wrong project path. That is retired: re-verified 2026-09-24, line 25 resolves correctly to
# tests\benchmarks\SharpCoreDB.Benchmarks.Comparative (the script was fixed). The direct form is
# kept below because it is what the recorded measurements were taken with.
dotnet run -c Release --project tests/benchmarks/SharpCoreDB.Benchmarks.Comparative -- --pk
dotnet run -c Release --project tests/benchmarks/SharpCoreDB.Benchmarks.Comparative -- --pk-default
dotnet run -c Release --project tests/benchmarks/SharpCoreDB.Benchmarks.Comparative -- --pk --engine=pagebased
dotnet run -c Release --project tests/benchmarks/SharpCoreDB.Benchmarks.Comparative -- --multirowinsert
dotnet run -c Release --project tests/benchmarks/SharpCoreDB.Benchmarks.Comparative -- --pk-profile --engine=pagebased
dotnet run -c Release --project tests/benchmarks/SharpCoreDB.Benchmarks.Comparative -- --gate
dotnet run -c Release --project tests/benchmarks/SharpCoreDB.Benchmarks.Comparative -- --write-baseline
```

Known flags: `--pk`, `--pk-default`, `--pk-ab`, `--dual-mode`, `--multirowinsert`, `--pk-profile`,
`--scdb` (single-file INSERT arm, added 2026-09-22; **reports both caller shapes** — per-statement `ExecuteSQL` and a
single `ExecuteBatchSQL` call — since session 9), `--engine=pagebased`, `--gate`, `--gate-factor=X`,
`--gate-baseline=<path>`, `--write-baseline`.

Every write arm prints a `[diag]` line naming the shape it measured (2026-09-22): `--multirowinsert` and the fair-PK arm
both report allocation per row, gen0 collections and both file sizes — the fair-PK arm's allocation is **engine-scoped**
(read around the `db.InsertBatch` calls, so the harness's own row dictionaries are excluded) — and the fair-PK arm also
prints its resolved layout (`fixedWidth`, `noEncrypt`, `atRest`, `IsFixedWidthRecords`, `inline`), which is what makes an
inline-capacity run self-describing.

Harness-only env switches (used by the §9 experiments; set them explicitly and never let them leak
between runs): `SHARPCOREDB_MAIN_FIXEDWIDTH`, `SHARPCOREDB_INLINE_BYTES`, `SHARPCOREDB_HASH_INDEXES`,
`SHARPCOREDB_MAIN_PROFILE_UPDATE`, `SHARPCOREDB_WAL_DURABILITY`, `SHARPCOREDB_BUFFERED_APPENDS`,
`SHARPCOREDB_MULTIROW_ROWS`, `SHARPCOREDB_SCDB_ROWS`, `SHARPCOREDB_SCDB_SHAPES` (`both` default / `batched` — the
latter makes a 1.000.000-row `.scdb` run feasible because the statementwise shape is quadratic by construction),
`SHARPCOREDB_SCDB_MIN_EXTENSION` (the `.scdb` file-size floor, 2026-09-23/24 — see §9's growth item), and
`SHARPCOREDB_LADDER_REPS` (**1** default, **3 is the §5.4 protocol**: each SQL/Direct/StructRow/SQLite ladder arm is
then the median of N runs and prints its min–max spread, which is what keeps a single-shot reading out of the record).

## 3. Measurement protocol (non-negotiable)

0. **Ratios need their reference in the same run, and a single ladder run is not a reading (2026-09-24).** Four runs of
   the identical binary put the SQL ladder's INSERT ratio anywhere between 0,31× and 0,69× (our arm moved 35K → 84K ops/s
   while the same-run SQLite moved 9 %), so **the ladders are measured with `SHARPCOREDB_LADDER_REPS=3` and reported as
   medians**, with the printed min–max spread quoted beside them. Only the `--pk`/`--pk-default`/`--multirowinsert` arms
   already carry their own median-of-3/5 and their own same-run SQLite reference; §5.4's ladders now match that.
1. Clean shell with **no** `SHARPCOREDB_*` vars set, unless a specific experiment requires one —
   then record it in the worklog. The harness prints a `REGIME:` banner; check it on every run.
2. Release config only. Median-of-3 (median-of-5 for `--multirowinsert`).
3. One benchmark at a time; nothing else on the CPU; redirect output to a file; a detached process
   is preferred.
4. **Only ratios are comparable.** SQLite's absolute reference moved ~202K → 157K ops/s between
   sessions. Never quote absolute ops/s as a verdict; always quote the ratio vs the same-session
   SQLite arm.
5. Read section 8 before profiling — the profiler's *times* do not transfer across batch arms; its
   *call counts and allocation* do.
6. `--gate`: exit 0 = ok, 1 = regressed, 2 = too noisy. `ratio = baseline ÷ current`, so **> 1 means
   slower**. A `REGRESSED` verdict means "re-run on a quiet machine", **not** "revert".

## 4. Current state (the numbers you are moving)

| Shape | READ | UPDATE | DELETE | INSERT |
|---|---:|---:|---:|---:|
| fair-PK, tuned, plaintext, fixed-width | 1.21× ahead | 1.12× ahead | 1.72× ahead | **0.71× behind** |
| default document-CRUD job (no PK) | 0.76× | **0.24×** | **0.31×** | 0.67× |
| pure default (encrypted, `--pk-default`) | 0.65× | **0.42× (2.4× gap)** | **0.68× (1.5× gap)** | 0.58× |
| PageBased (fixed-width) | **2.1× ahead** | **1.2× ahead (parity)** | 0.8× | 0.83× |

**Re-measured 2026-09-21** (autonomous session; each number comes from a same-run SQLite arm, and its worklog entry
names the flag and the artifact): rows 1, 3 and 4 from the worklog's §5.4 and §5.2 entries. Row 2 (the no-PK
document-CRUD job) **was not re-measured against SQLite** in that session — its count-based attribution and its arms
are in the §5.3 entries instead, and it is the one row whose UPDATE/DELETE cells still describe the pre-session build.

**What that changes for the next session.** Two of the four rows have moved where it matters: **PageBased UPDATE is at
parity (was 0.17×/4.9× behind)** — item **5.2 is CLOSED** — and the encrypted default row's UPDATE went 3.6× → 2.4×
behind, with the remaining ~1.1× of its encryption tax now named as the per-record AEAD open. So the open work is
**5.1 INSERT** (the fair-PK arm is still the only losing column there, and 5.4 measured **0.71×** against 0.54× before)
and the **locate's tail** on the encrypted UPDATE path. Everything else in §5 is closed, `BLOCKED` with a named
mechanism, or done — see the worklog's `SESSION CLOSE` entry for the per-commit table.

**Re-measured 2026-09-22** (two autonomous sessions — the worklog entries carry every table): both open items above got a
landed change, and both were accepted on the **allocation** column because this machine's gate cannot arbitrate (two
INCONCLUSIVE attempts that day, 2,91× and 2,76×, the second of them on a quiet machine). The fair-PK arm's serialization
is **706 → 547 B/row (−22.4 %)** — the two per-row `List<>` scratch collections are gone — and the encrypted UPDATE
locate's per-record allocation is **431 → 279 B (−35 %)** and now *identical* to the plaintext arm's, with the AEAD open
itself left in place.

**What the second session then measured, which is the part that matters for planning.** (a) **The fair-PK INSERT ratio
did not move**: a three-run ladder (each arm already median-of-3) gives **0,68×** median against 0,71× published, with
READ 1,08×, UPDATE 1,16× and DELETE 1,86× ahead — so the allocation win buys no ops/s on this shape and **5.1's
allocation budget is spent**; what is left there is the SQL-layer per-statement work 5.1 names. (b) **§5.4 is satisfied
for this build**: the SQL/Direct/StructRow ladders against same-run SQLite (0,63 / 0,88 / 0,96× on INSERT, and the
Direct ladder 1,34× on READ) plus 319 provider/EF/sync tests, 248 vector tests and the core suite all green by EXE, with
`SharpCoreDB.CI.slnf` building clean. (c) **`--pk-default`'s UPDATE cell looked better (0,42× → ~0,48×) and that is refuted**: a three-pair interleaved
A/B on the same machine puts the two builds at **0,53 / 0,50 / 0,50 (after) against 0,57 / 0,51 / 0,54 (before)** — what
moved is SQLite's own UPDATE reference (315K → 257–303K), while SharpCoreDB's UPDATE arm sits at 146–166K in both
builds. So **the allocation wins of this day bought no ops/s on either cell they could plausibly have touched** — the
honest form of the result, and the reason the plan's acceptance for these changes is the allocation column.
**Neither reaches the item's DoD**: INSERT is still below 1.0× and the locate's 12.6 MB snapshot is a design item.

**Measured 2026-09-22 (session 3): 5.1's remaining lever is a *decision*, and its size is now known.** The fair-PK INSERT
arm is bounded by one per-row overflow-arena write, and the shipped inline capacity (16) is two bytes too small for its
18-byte `email` value: at `SHARPCOREDB_INLINE_BYTES=24` every TEXT column inlines, `arena-write`/`arena-append`/
`encode-scratch` fall from 99,000 calls to **zero** over 100,000 rows, and the **tracked ratio moves 0,63× → 0,84×**
(three interleaved `--pk` pairs, each with its own same-run SQLite arm), with +22 % on the multi-row shape, −13 %
allocation and a **footprint that is neutral within ±2 %** (−0,4 % on the multi-row schema, +2,0 % on the PK one: the
arena's per-block framing leaves as the record grows; 32 is past the knee at +20 % disk for no speed), and it also cuts
the tracked arm's **engine allocation by 25,3 %** (1.923 → 1.437 B/row, profiler-free and deterministic). That is a policy choice about every new table's record size — pinned by
`Default_InlineCapacity_IsPinned_AndZeroKeepsTheHistoricalLayout`, and worth nothing on schemas whose values are shorter
than the capacity (a table of 5-character codes grows ~27 % in record size for no arena write saved) — so it is recorded
as an **owner decision, not landed**. The correctness blocker the CHANGELOG named is gone: the capacity is persisted and
restored on reopen in both storage modes (verified by reading both paths). **Even the flip leaves INSERT at ~0,84×, so
decision 4 still needs more than the capacity.** Tables: the plan's §4b extension under §9 priority 2 and the WORKLOG's
session-3 entry.

**Decision 8 taken (2026-09-22) and the flip is shipped, measured and closed out — this supersedes the paragraph above.**
The owner raised `DatabaseConfig.FixedWidthInlineValueBytes` from 16 to 24, the pinning test with it, and nothing else
changed (existing databases keep the capacity their records were written with; only new tables take 24). Measured on the
**shipped** default, `REGIME: no SHARPCOREDB_* switches set`, with the harness's own `[diag]` line proving `inline=24`,
arena 0: three `--pk` runs give fixed-width plaintext INSERT **161.802 / 171.000 / 168.853 ops/s → 0,92 / 0,87 / 0,85×**
against each run's own SQLite arm, with READ 1,26×, UPDATE 1,29×, DELETE 1,62×; `--multirowinsert` **84.263 / 85.467
rows/s** at 3.555 B/row; `--pk-default` INSERT 0,68–0,75× (was 0,59–0,61×); core suite **1920 / 0 failed / 16 skipped**.
So **the absolute INSERT target (≥ 150K) is met for the first time** and the **ratio** target is 0,87× because SQLite's
own same-run reference on this quiet machine reads 176–199K against the ~155K it read when the bar was set. **5.1 is
closed on that basis** (see the WORKLOG's close entry): item 1 is *absolute met, ratio short against a reference that
moved*, item 2 (the per-stage budget) is met, and the residual is the drift of the reference rather than a named lever
in our code. The plan's §8e carries the full table.

**§8c landed too (2026-09-22, session 7 — the compatibility half of decision 8).** An existing table whose **stored**
inline capacity is below the configured one now rewrites itself once, on the first **writable** open, on **both** storage
modes (`Table.MigrateToInlineCapacity` for multi-file, `SingleFileTable.MigrateToInlineCapacity` for `.scdb`, with the new
capacity persisted in the metadata DTO / table-directory entry). It is one-way — a config asking for a lower capacity,
the historical `0` included, never re-layouts anything — and a read-only open never rewrites, so a skipped upgrade is
harmless. Two defects surfaced while landing it and both are fixed: the `.scdb` `CREATE TABLE` path had **ignored the
configured capacity entirely** (so that mode had never used the inline layout, nor earned the 20–33 % INSERT win), and a
**zero-byte block** threw on both halves of a rewrite (`AllocatePages(0)` and `FreePages(offset, 0)`) — which is the root
cause of the older "the 16-byte default broke the single-file reopen matrix" note, since at capacity 16 that schema still
overflowed and no test had ever produced an empty arena. Suite **1924 / 0 failed / 16 skipped**; the plan has **one**
technical item left (§5.3's snapshot) and **no** open INSERT lever.

**The §9 single-file item was corrected the same day (session 9), and the correction matters more than the item did.**
Session 8 measured that mode with one `ExecuteSQL` per statement and read 819–994 rows/s as a property of the *mode*
("not competitive on INSERT, needs an incremental block write"). Session 9 gave the `--scdb` arm the second shape a caller
has — **one `ExecuteBatchSQL` call** — and the numbers inverted: **703 rows/s → 368.535 rows/s**, **844.563 B/row →
1.444 B/row**, gen0 265 → 0, because the batch extension (`SingleFileDatabase.Batch.cs:47`) already disables auto-flush,
begins a block-registry batch and flushes **once per table** (`SingleFileTable.cs:465` is the suppressed flush). At that
shape the single-file mode is **4,4× the multi-file mode's batched arm** (84.263 rows/s at 3.555 B/row) — the fastest
INSERT path in the codebase — so the quadratic belongs to the *solo-statement shape* (guidance, now in
`docs/storage/SINGLE_FILE_SQL_LIMITATIONS.md`), not to the mode, and the "make the block write incremental" redesign is
**not justified by any measured INSERT cost**. What *did* survive is a **file-size** finding, and measuring it properly
turned it into a flat **floor** rather than growth: a `.scdb` database is **14,733,312 B whatever it holds** — measured at
1, 100, 500 and 2.000 rows, both shapes, both capacities — because the file starts at 1.037 pages and the first
extension adds `max(requiredPages, currentSize / 2, 10 MiB / pageSize)` = 2.560 pages, so 1.037 + 2.560 = 3.597 pages.
That is now a **knob** (`DatabaseConfig.SingleFileMinExtensionBytes`, default 0 = unchanged), which takes the same
100-row database to **6.369.280 B** at 64 KiB, with `SingleFileFileGrowthTests` pinning both ends. Both shapes in the
same process is what made the shape distinction visible; plan §9 and the session-9/session-10 worklog entries carry the
tables.

**The 2026-09-24 review closed the rest of the list; the decisions are rows 9–15 of the plan's §0.1.** In one line each:
(9) the `.scdb` growth minimum's **default went 10 MiB → 1 MiB**, taking every small single-file database from **14,7 MB
to 6,4 MB** (−56,8 %) at byte-identical allocation per row and no measurable throughput change, with the historical floor
one configuration line away and pinned by a test; (10) the **INSERT ≥ 1,0× ratio target is closed as *not claimed*** (the
absolute floor stays met) and is replaced by *publication* plus a no-regression rule on the **encrypted default arm**;
(11) overflow-arena appends **stay write-through**; (12)+(13) the encrypted commit's `commit-overwrites` half and
`index-maint` are **left as-is** (durability boundary and index freshness); (14) `.scdb` solo-statement coalescing is
**rejected** — batching is the answer and is documented; (15) the **unsafe hash-index backend is handed to its owner**
(0,07×, 75,6 µs per key, cost in the caller).
**Two items remain open and neither is a code decision:** the **gate baseline** is still dated 2026-09-15 and
`--write-baseline` still refuses on this machine (spread floor 2,76 / 2,91 / 3,31× against a 2,50× limit), so it needs a
quieter or self-hosted runner; and the **§5.4 ladders** now have a protocol instead of a caveat —
**`SHARPCOREDB_LADDER_REPS=3`**, median-of-3 with the per-cell spread printed, measured 2026-09-24 as **SQL 0,56× /
Direct 0,65× / StructRow 0,68× INSERT** against the same-run SQLite at 184.257 ops/s, with spreads of 1,05–2,72× quoted
beside them so nobody mistakes a median for a guarantee.


## 5. Work items, in priority order

Work them in this order. Finish (or timebox-and-record) 5.1 before starting 5.2, and so on.

**Timebox unit.** One *session* = one unattended run (~6–9 h wall clock, roughly one working day). A
timebox counts *sessions spent on the item*, not calendar days. Reset the counter only when the item's
DoD is met or the item is marked `BLOCKED`/`REJECTED`.

| Item | Timebox | Hard stop after |
|---|---|---:|
| 5.1 INSERT throughput | 2 sessions | 2 |
| 5.2 PageBased UPDATE | 2 sessions | 2 |
| 5.3 default-job UPDATE/DELETE | 2 sessions | 2 |
| 5.4 Providers re-validation | 1 session | 1 |
| 5.5 Instrumentation gaps | 0.5 session, on demand | 1 |

**Every item carries the same three-part obligation:** a `Timebox:` limit, a measurable
`Done when (DoD):`, and an `If the timebox expires:` instruction. The exit is always the same shape:
**commit or revert → write the worklog entry with the measured ratio + regime → set `NEXT:` → move to
the next item.** Timebox expiry is a normal outcome, not a failure — spinning past it is the only real
failure.

### 5.1 P2 — INSERT throughput (0.54× → ≥ 1.0×) — *do this first*

- **Already done (do not redo):** §4b inline capacity default = 16 (+19% multi-row INSERT);
  the `QueryCache.Count` gate fix; the `Storage.AppendBytes` buffer-size fix.
- **Remaining:** the ~2.4× lives in SQL-only work — statement parsing (~15.4% at 1000 rows/statement),
  literal coercion, per-statement dispatch, WAL/metadata bookkeeping.
- **Next step:** profile the `--multirowinsert` shape (1000 rows/statement, 20,000 rows) and build a
  per-stage budget exactly like plan §11's table (arena-write ~1.1 KB/row, validate+encode ~1.45 KB,
  hash-index ~0.95 KB, parse, dispatch). Then attack the largest SQL-layer stage.
- **Also pending:** two-region records (§4b Option B) — the only remaining format change. INSERT-only
  leverage; must ship with the magic-header versioned upgrade + migration path (decision 3).
- **Acceptance:** fair-PK INSERT ratio ≥ 1.0×, and ≥ 150K ops/s tuned plaintext.
- **Timebox:** 2 sessions.
- **Done when (DoD):** all four of —
  1. `--pk` fair-PK INSERT ratio ≥ 1.0× **and** ≥ 150K ops/s tuned plaintext, median-of-3, measured
     against the same-session SQLite arm;
  2. a per-stage INSERT budget for the `--multirowinsert` shape is in the worklog, in plan §11's table
     format (calls, B/row, µs/row, share);
  3. the change that produced the win is committed with its before/after ratio in the message;
  4. core suite green **and** `--gate` pass (or a documented quiet re-run).
- **If the timebox expires:** commit the best measured improvement if it is a real win (tests + gate
  green); otherwise revert to a clean tree. Record the remaining delta, the largest stage, and every
  refuted hypothesis. Then move to 5.2 — do **not** keep attacking INSERT.

### 5.2 P3 — PageBased UPDATE parity (4.9× → ≤ 2×)

- Profiled 2026-09-15: ~75% of the cost is in unstamped code; the largest *measured* cost is the
  overflow arena (6.81 µs/update, 2829 B/update = full re-serialization).
- **Next step is instrumentation, not another hypothesis:** stamp the PageBased update path
  (`UpdateColumnarRow` / `TryUpdateInPlace`, the relocation branch, its index re-point) the way the
  append-only batch path is already stamped. Reproduce with `--pk-profile --engine=pagebased`. Then
  determine whether the remaining ~34 µs is the double write, the index re-point, or full-row
  materialization.
- Then apply the same in-place fast paths the fixed-width Columnar path already uses (decision 1:
  bring PageBased to parity).
- **Timebox:** 2 sessions.
- **Done when (DoD):** all four of —
  1. `--pk-profile --engine=pagebased` shows every stage of the PageBased update path with call counts
     and allocation — no unstamped region left in that path;
  2. the dominant cost is named with evidence (a stage table, or a call-count/allocation diff), **not**
     inferred from a ratio;
  3. PageBased UPDATE ratio improved to ≥ 0.5× (gap ≤ 2×), or a documented `REJECTED` explaining why
     the remaining cost is structural;
  4. core suite green **and** `--gate` pass.
- **If the timebox expires:** land the instrumentation (it has standalone value), record the stage
  table, mark the item `BLOCKED` with the open question for the next session, and move to 5.3.

### 5.3 P1 — default-job UPDATE/DELETE (0.24× / 0.31×) — *measurement-first, lower confidence*

**STATUS 2026-09-26: CLOSED AGAIN — `REJECTED (documented)` for the lever, instrumentation KEPT; next item is 5.4.**
Superseding the 2026-09-23 close kept below: the item was re-opened on 2026-09-26 because `--docs-batch` makes the
SQL-free batch arm directly observable. Session 65 replaced the `fastPatch` branch's un-stamped whole-row decode with a
pre-patch single-column capture (**`row-decode` 623 → 144 B/call, −77 %**), and session 66 stamped the last three
un-stamped regions (`op-setup`, `batch-prep`, `db-flush`, plus plan §2's `wal-flush`, which had **no writer anywhere**)
and measured the remainder: the batch pass attributes **81,4 % cold (1 rep, no warm-up) against 92,7–100 % warm with one
discarded warm-up rep** — plan §2's acceptance is ≥ 90 % — so the "missing ~19 %" was **one-time JIT/first-touch inside
the timed window, not per-operation work**. The per-operation head is **0–24 B and 0,8–2,0 ms per 10.000 operations**,
the entry points' operation re-listing is **320.112 B per batch** (0,6–0,9 % of the pass, and removable only by also
reshaping the shared `TryBulkUpdateContiguousFixedWidth`), and the caller's explicit `db.Flush()` inside the window is
**0,3 ms** of which the WAL-batch-buffer half is **0,0 ms**. The 2026-09-23 table's per-operation ranking still holds
(`row-locate-index` 279 B/call, `engine-write`, `in-place-patch` 175 B, `index-maint` 43 B × 20.000 calls, the
10,4–13,2 MB one-call snapshot), and its two candidate levers still need the same owner decisions. Worklog: sessions
65 and 66.

**STATUS 2026-09-23: CLOSED — `REJECTED (documented)`, timebox rule applied; next item is 5.4.** The count-based
attribution table now exists for the two `--dual-mode` arms (six interleaved profiled passes, plan §9) and it says the
residual is **(a) AEAD-per-record**, which is the encryption contract and not a defect (`in-place-patch` 8,3 ms vs
1,2 ms for the *same* 175 B/call; `commit-overwrites` writes 1,60 MB vs 0,55 MB for the same 10.000 overwrites),
**(b) `parse` (14,2 %, 10.000 × 531 B)**, shape-inherent to 10.000 distinct literal statements, and **(c) two candidates
that need an owner decision, not a benchmark** (`commit-overwrites`' non-prep half on the durability boundary, and
`index-maint`). The 12,6 MB snapshot that dominated this item is 5,7 % of the pass, one call, and its per-record
fallback was already measured worse.

- The reconciliation is done; the 6.2× between the fair-PK arm and the PK-less arm is **real but not
  yet explained**. Four plausible readings have already failed their control runs.
- **Durable output:** priority 1 is now a **count-based** attribution of `UpdateMultiple`'s
  per-operation work (not time-based). Use `SHARPCOREDB_MAIN_FIXEDWIDTH`, `SHARPCOREDB_INLINE_BYTES`,
  `SHARPCOREDB_HASH_INDEXES`, `SHARPCOREDB_MAIN_PROFILE_UPDATE`; compare **call counts and allocation**
  across arms, not times.
- **The table now exists for the two `--dual-mode` arms (2026-09-23, session 11) and it re-prices the item:**
  the 12,6 MB whole-file snapshot that dominated the discussion is **one call, 12,6 MB, 5,7 % of the pass**;
  the sorted stages are `commit-overwrites` 16,4 %, `parse` 14,2 % (**shape-inherent**: 10.000 distinct
  literal statements, so no product fix), `row-locate-index` 13,9 %, `engine-write` 13,1 %, `index-maint`
  7,6 %, `in-place-patch` 7,0 %, `row-snapshot` 5,7 % — so the snapshot is **REJECTED as a lever** and the
  next candidates are `commit-overwrites` (a durability-boundary call, 1,5 MB) and `index-maint`. Plan §9
  carries the full table; the worklog entry is session 11.
- ~~The eventual fix is a **new capability** (not a gate relax): extend in-place patching to
  non-PK-located updates — locate via the hash index, overwrite when the changed field's encoded width
  is unchanged, using `IStorageEngine.TryUpdateInPlaceSameLength` (already exists).~~ **Stale — corrected
  2026-09-26 (session 65, §8(a)): the capability already exists and fires.** `in-place-patch` and `engine-write`
  each record **10.000 calls on a 10.000-row batch** on the default job's SQL-free batch arm, on both routes, so
  the premise that the machinery is missing was wrong and this paragraph now states what is actually left: the
  distributed per-operation cost, priced in the 2026-09-26 status line above and in the worklog's session-66 table.
- **Timebox:** 2 sessions — hard stop, no exceptions. This item must never block 5.1/5.2.
- **Done when (DoD):** **either** of the two acceptable exits —
  1. **a landed fix:** in-place patching extended to non-PK-located updates, and the default-job UPDATE
     ratio measurably improved (before/after in the worklog), tests + gate green; **or**
  2. **a documented `REJECTED`:** a count-based attribution table across the four arms (call counts +
     allocation per stage) that names what `UpdateMultiple`'s per-operation work actually is, plus the
     evidence for why no clean lever remains.
  A session that ends with hypothesis #5 refuted and no table is **not** DoD — the table **is** the
  deliverable, even when the fix does not land.
- **If the timebox expires:** stop immediately, record the table + every refuted hypothesis, mark
  `BLOCKED` or `REJECTED`, and move to 5.4.

### 5.4 Providers re-validation — *after every core change*

**STATUS 2026-09-27: RE-DONE on `a62ef168` — and this pass found a stale *reference*, not a regression.** All three
harness arms plus **six** provider suites (the five below plus `SharpCoreDB.Functional.Tests`, which the 09-24 wording
omitted) were re-run on the current build: EFCore **116**, Functional **38**, EFCore.Functional **3**, Dapper **3**,
Linq2DB **24**, Sync **135** = **319 tests, 0 failed**, every suite exit 0 — so **still no provider re-introduces
row-by-row overhead**. `--multirowinsert` is **3.555–3.563 B/row, data file 2.320.000 B, overflow arena 0 B** —
byte-identical to the record, so the deterministic half is unchanged. `--pk` fixed-width plaintext is **UPDATE 426.821
/ DELETE 629.453 / READ 157.161** against a same-run SQLite **768.220 / 1.145.160 / 117.880**; every one of our three
columns is *higher* than the 09-24 reading (318.799 / 416.411 / 114.004). `--pk-default` reads **INSERT 0,81–0,87× /
READ 1,01–1,03× / UPDATE 0,39× / DELETE 0,31–0,35×** over two full runs — and **those are the corrected numbers, not a
regression:** they reproduce session 32's symmetric-protocol cells (0,83 / 1,10 / 0,39 / 0,41) inside the box's own
spread. The older band (0,70 / 0,57 / 0,48 / 0,59) merely *reads* better because it was taken while the **SQLite** arm
was left allocating a command and two parameters per row; the moment that arm got one prepared command, SQLite's own PK
UPDATE/DELETE references jumped **3,05×** (288.108 → 878.557, 385.116 → 1.172.704) while ours moved +19 % / +11 %.
**Plan §0.1 decision 10 and the §8e close note still carried the superseded band as a no-regression obligation; both are
corrected in this pass** — and `BEAT_SQLITE_ALL_AXES_PLAN.md` §1.2, whose *second* generation (0,82 / 1,12 / 1,05 / 0,98)
had made the same cells look like near-parity, now warns against that reading too, because it was measured before the
comparator was corrected. All three documents agree again, on the symmetric-protocol cells. The SQL/Direct/StructRow
ladder, median of 3 with spreads quoted, reads **SQL 0,66× / Direct 0,81× /
StructRow 0,72× INSERT**, **Direct 2,14× / StructRow 0,98× READ**, **SQL UPDATE/DELETE 0,18× / 0,19×**, spreads
**1,17–2,53×**; StructRow still reports **0** for UPDATE/DELETE (it does not run those phases — a §5.5 gap, not a
result). **Consequence for anyone re-running this: quote the arm-B cells against the symmetric-protocol values, and
never against 0,70 / 0,57 / 0,48 / 0,59 — that inference is wrong by 20–40 % in the direction of a phantom regression.**

**STATUS 2026-09-24: DONE for this build — and the ladder half now has a protocol instead of a caveat.** The three
harness arms and the five provider suites were re-run on the current build (results archived in the project's
`results/`), the worklog's session-12 and session-13 entries carry the tables, and **no provider re-introduced row-by-row
overhead**: EFCore 116, EFCore.Functional 3, Dapper 3, Linq2DB 24, Sync 135 = **281 tests, 0 failures** (YesSql/
`Data.Provider` have no separate suite; they are exercised inside the core and functional projects). The ladder arm was
single-shot per invocation, and four runs of the identical binary put its SQL INSERT ratio anywhere between **0,31× and
0,69×** while our own SQL arm moved 35K → 84K ops/s and the same-run SQLite moved 9 % — so the harness now supports
**`SHARPCOREDB_LADDER_REPS=3`** (median per arm, with each cell's min–max spread printed, and `Reps` written into the
`comparative_*.json` archive). The median-of-3 run reads **SQL 0,56× / Direct 0,65× / StructRow 0,68× INSERT**, **Direct
1,49× and StructRow 1,32× READ**, SQL UPDATE/DELETE 0,26× / 0,58×, **with spreads of 1,05–2,72× quoted beside every
cell** — which is the honest form of the §5.4 answer: Direct and StructRow reproduce the recorded picture, the SQL ladder
is the noisy arm, and no cell can be compared without its spread. The deterministic columns are unchanged
(`--multirowinsert` 3.556 B/row, data file 2.320.000 B, arena 0 B; `--pk` fixed-width UPDATE/DELETE/READ 1,18× / 1,14× /
1,13× ahead of SQLite).

Re-run the comparative harness (`--pk`, `--pk-default`, `--multirowinsert`) plus the provider test
projects (EF Core, Dapper, Linq2DB, ADO.NET/`Data.Provider`, YesSql, Sync) and report the
SQL/Direct/StructRow ladders **separately**, never as one number.
- **Timebox:** 1 session.
- **Done when (DoD):** the three harness arms (`--pk`, `--pk-default`, `--multirowinsert`) and the
  provider test projects have been re-run on the current build, and the worklog carries one table per
  ladder (SQL, Direct, StructRow) with the ratio vs the same-session SQLite arm. Any provider that
  re-introduces row-by-row overhead is named explicitly.
- **If the timebox expires:** report the ladders you did complete, name the providers not yet re-run,
  and set `NEXT:` to finish them at the start of the next insert/update change.

### 5.5 Instrumentation coverage gaps — *only as needed*

`WalAppend`/`WalFlush` have no writer stamp; the second batch-dispatcher path and parser internals
below the dispatcher are uncovered. Add stamps only where needed to answer 5.1/5.3.
- **Timebox:** 0.5 session, **on demand only** — skip this item entirely if 5.1/5.3 produced a clean
  answer without new stamps.
- **Done when (DoD):** the new stamp(s) answer the specific question that motivated them (stated in the
  worklog), the profiler still compiles and runs, and no unrelated stage or path was changed.
- **If the timebox expires:** remove any half-finished stamp that does not yet answer its question so the
  profiler stays trustworthy, and move on. A profiler carrying a misleading half-stamp is worse than no
  stamp.

## 6. Decisions already locked (do NOT revisit)

1. PageBased → parity with the in-place paths.
2. Two-region record layout (Option B), overflow arena.
3. Format change allowed, via the magic-header versioned upgrade
   (`PersistenceConstants.EncryptedTableMagic`, `PageHeader.MagicNumber`/`Version`). Any format change
   must ship **with** a migration/upgrade path, and existing files must still open.
4. INSERT must **beat** SQLite, not merely match it.
5. Every API ladder and provider is in scope.
6. Security stays the default **and must be real**; `NoEncryptMode=true` is the documented raw-speed
   opt-out; **both modes benchmarked side by side**.

## 7. Already tried and eliminated (do NOT redo these)

- "The PK route skips the parser / has a dedicated batched updater" — **refuted**: `parse` fires
  10,000× on **both** arms.
- WAL durability, hash-index maintenance, record layout, inline capacity, `in-place-patch` +
  `engine-write`, and record locate as the default-job UPDATE lever — **all eliminated by measurement**
  (UPDATE flat to ±1.5% across configs).
- The `TryParseUpdateForBatch` regex fallback as the 6.2× — **refuted** (parse differs by only
  ~0.73 µs/statement).
- The `ToUpperInvariant` copies in `SqlParser.DML.cs` — **refuted** (0.03 µs/stmt when stamped).
- The dispatcher's post-call block (`IsSchemaChangingCommand` + metadata flags) — **excluded**
  (0.03 µs/stmt, zero allocation).
- Fixed-width inline capacity recovering the PK-less penalty — **refuted** (still ~20% worse there).
- The `QueryCache.Count` gate and the 64 KiB `AppendBytes` buffer — **already fixed**; the remaining
  single-row-statement floor is a *durability* decision, not a defect.
- The **pooled unsafe hash-index backend** (`EnableUnsafeEqualityIndex`) as an INSERT lever — **measured and
  eliminated 2026-09-22**: on the fair-PK arm at capacity 24 it is **0,07× (12× slower)** in three interleaved pairs
  while *cutting* allocation (1.437 → 924 B/row), with the profiler attributing it to `hash-index` at **75,6 µs per
  key** against the managed path's 2,45 µs. The unproven hypothesis for the size is degraded probe chains (an
  accidental per-key O(n)); the pointers for its owner are in the session-5 worklog entry. ⚠️ Note also that its
  documented environment variable and AppContext switch **could not reach it** before that session — the config
  property is a non-nullable `false`, so `_config?.EnableUnsafeEqualityIndex ?? Resolve…` never consulted them; a
  "safe-backend" measurement taken that way would have been the same configuration twice.

## 8. Traps that have already fooled the team (read before profiling)

1. **The profiler's times do not transfer across batch arms.** It is `Interlocked`-summed with
   `[ThreadStatic]` allocation checkpoints, and is biased under `Parallel.For` serialization
   (`WritePathProfiler.cs:212-215, 311-324`). Use its **call counts and allocation**, not its times,
   for cross-arm comparisons. Reproduce repros with `--pk-profile`.
2. **The default job's arms declare no PRIMARY KEY**, so they run the legacy variable-length layout
   and cannot use the in-place fixed-width fast paths. The fair-PK arm declares a PK. That is why the
   same build shows 0.24× vs 1.29×.
3. **Never "fix" the default-job gap by flipping the fixed-width layout default.** Forcing fixed-width
   on the PK-less shape is a *measured regression* (−24% UPDATE and INSERT). `SqlParser.DDL.cs:392-407`
   grants fixed-width only to tables with an explicit PRIMARY KEY — that is intentional.
4. **SQLite's `INTEGER PRIMARY KEY` = rowid = in-place page edit.** The default job's SharpCoreDB arm
   uses a *non-key* predicate, so those columns are **not like-for-like**. Only `--pk` (both engines on
   `WHERE id = @pk`) is a fair comparison.
5. **Ratios only, never absolutes** — SQLite's own reference drifts between sessions and even runs.
6. **The gate refuses to write a baseline on a loaded machine.** Re-record `--write-baseline` only on a
   quiet machine, and put the reason in the commit message.
7. **Every plausible reading must survive its own control run** — this plan has paid four times for
   acting on a reading inferred from a ratio instead of read out of the code.

## 9. Commit and verification rules

- **Before every commit:** build clean + core suite green + `--gate` pass (or a documented, quiet
  re-run showing the verdict).
- Commit **locally** to a feature branch (e.g. `perf/autonomous-YYYYMMDD`). **Do not push.** **Do not
  commit to `release/*` or `master`.**
- Commit message: imperative, cite the plan section (e.g. `perf(insert): … (plan §5.1)`), state the
  change and the measured before/after **ratio** + regime.
- Re-recording the baseline is explicit and reviewable — always state why in the commit message.

## 10. Worklog protocol (this is your only "report")

- File: `docs/performance/WORKLOG.md` (create if absent). Append-only, newest at the bottom.
- **Session start ritual:** read this brief + the worklog; find the last `NEXT:` line; resume there.
  Never restart from memory.
- **Every entry:** timestamp → item → **session count for that item** → action taken → measured result
  (ratio + `REGIME:` banner) → decision → `NEXT:` line. The session count is what enforces the §5
  timeboxes; state it every time, so a timebox expiry is visible in the log.
- Keep the three tokens `Session:`, `Verdict:` and `NEXT:` exactly as written and on their own lines —
  the human's 30-second morning check greps for them (see the top of `docs/performance/WORKLOG.md`).
  Never rename them or bury them inside prose.
- A finding that kills a hypothesis is a valid result — record it and move on.
- Keep entries short and evidence-first: numbers, file paths, command lines. No narrative essays.

## 11. Safety rails (absolute — breaking one is worse than making no progress)

- **NEVER:** force-push, rewrite history, delete the worklog, modify release/version metadata or
  package versions, pack/publish, change `global.json` or the branch structure, or disable encryption
  to "win".
- **NEVER** trade durability: WAL semantics, crash-consistency, and the reopen round-trip matrix
  (`ReopenRoundTripMatrixTests`, `FormatCompatPolicyTests`) must stay green.
- **NEVER** flip the fixed-width layout default (measured regression, section 8 trap 3).
- **Regressions to watch (the canaries):** `ReopenRoundTripMatrixTests`, `FormatCompatPolicyTests`,
  `FixedWidthBulkUpdateTests`, `FixedWidthBulkDeleteTests`, `FixedWidthPatchTests`,
  `WritePathProfilerTests`, `FixedWidthInlineValueTests.Reopen_KeepsInlineAndOverflowValues`,
  `SingleFileDirectoryParityTests`.

---

**End of brief.** If you have reached this line and have not yet opened the worklog, do that now — it
contains the live `NEXT:` pointer. Then begin. Do not ask for confirmation.
