# SharpCoreDB — Beating SQLite on Every CRUD Axis

**Status:** draft for owner review · **Date:** 2026-09-24 · **Branch:** `perf/autonomous-20260921`
**Mission it extends:** `INSERT_UPDATE_PERFORMANCE_PLAN.md` (single source of truth for the INSERT/UPDATE
campaign, now largely closed) and `AUTONOMOUS_AGENT_BRIEF.md` (session protocol).
**Evidence base:** `_bmad-output/planning-artifacts/research/technical-beating-sqlite-on-every-crud-axis-2026-09-24/research.md`
— a bmad-deep-recon *technical* run, 17 external sources, 12 claims in the ledger (10 verified). Cited as
`[n]` against that report's source appendix. **External sources support the *mechanisms* only; every
SharpCoreDB number in this plan comes from this repository's own harness and is cited to the file it
lives in.**
**Reporting channel:** `WORKLOG.md` only (append-only).
**Outcome so far (§5.2, session 53):** the §0 mandate is **not met**. The *fair* shape is won on all four
operations and the absolute floors hold, but the two mission arms — **B** (default posture, encrypted) and
**C** (default no-PK `docs` job) — are still short, and two of the blockers are owner decisions (§9 rows 5
and 7). §5.2 states every remaining cell with the evidence for it, so nothing here has to be re-derived from
the worklog.

---

## 0. Mandate, and what "all points" has to mean

The goal is to reach **≥ 1,00× on all four CRUD operations, in every arm we ship.** "Arm" is not
decoration — it is the unit of the claim, because the arms are genuinely different products:

| # | Arm | What it is | Why it counts |
|---|---|---|---|
| A | **Fair-PK, tuned plaintext, fixed-width** | `--pk`, `NoEncryptMode=true`, explicit PK | the like-for-like comparison (trap 4) |
| B | **Pure default, encrypted** | `--pk-default`, product defaults, no `SHARPCOREDB_*` | **what an ordinary user actually gets** |
| C | **Default document-CRUD (no PK)** | the `docs`-shaped job, no key in the predicate | the largest measured gap |
| D | **PageBased (explicit opt-in)** | `--engine=pagebased` | decision 1 promises it parity |
| E | **API ladder** — SQL / Direct / StructRow | the same work through three call paths | decision 5 puts all ladders in scope |

A statement like *"we beat SQLite on UPDATE"* is only meaningful with the arm attached. This plan
therefore treats **B and C as the real mission** — A is already won, and D and E are already scoped by
existing decisions. The honest one-line goal is:

> **Bring the *default posture* (B) and the *default schema shape* (C) to ≥ 1,00× on all four
> operations, without regressing A, D or E, and without trading encryption or durability.**

---

## 1. Current measured state

All ratios are SharpCoreDB ÷ SQLite against a **same-run** SQLite reference (the repo's standing rule:
ratios only, never absolutes). Sources are named per row so every cell is checkable.

### 1.1 Arm A — fair-PK, tuned plaintext, fixed-width *(the one already won)*

From `INSERT_UPDATE_PERFORMANCE_PLAN.md` §8e, median of three, product-default regime, capacity 24:

| READ | UPDATE | DELETE | INSERT |
|---:|---:|---:|---:|
| **1,26×** | **1,29×** | **1,62×** | **0,87×** |

Absolute INSERT 162–171K ops/s against the 150K floor (decision 8). The INSERT **ratio** is closed as
*not claimed* (decision 10) because the ≥ 1,0× bar was set against a ~155K SQLite reference that now
reads 175,6–198,5K in our own runs. **Arm A is the control, not the target.**

### 1.2 Arm B — pure default, encrypted *(the tracked default-posture cell)*

`--pk-default`, recorded 2026-09-24 in decision 10 — this is the arm decision 10 made the obligation:

| | READ | UPDATE | DELETE | INSERT |
|---|---:|---:|---:|---:|
| ratio | **0,57×** | **0,48×** | **0,59×** | **0,70×** |
| absolute | 55.497 | 127.883 | 218.695 | 128.410 |
| SQLite same run | 97.036 | 268.960 | 369.090 | 182.129 |

**All four were behind — but the correction below is *itself* superseded, so read it as history, not as the current
picture.** This second generation (0,82 / 1,12 / 1,05 / 0,98) was measured with discarded warm-ups and paired ranges,
but with **SQLite still handicapped**: its arm allocated a command and two parameters per row. Session 32 gave it the
same one-prepared-command correction our own arm had just received, SQLite's PK UPDATE/DELETE references rose **3,05×**
(288.108 → 878.557, 385.116 → 1.172.704), and the cells landed at **INSERT 0,83× / READ 1,10× / UPDATE 0,39× /
DELETE 0,41×** (see §4 below, and plan §9 item 7). **The parity this table shows on UPDATE (1,05×) does not exist** —
it was the comparator, not the engine. Re-measured again on 2026-09-27 (`a62ef168`) the same cells read 0,81–0,87× /
1,01–1,03× / 0,39× / 0,31–0,35×.

The values above were taken on the **old protocol** (all of one arm's reps, then all of the other's; no discarded
warm-up; no printed spread). Re-measured 2026-09-24 (worklog session 27) on the shared paired protocol —
three discarded warm-up reps, interleaved arms, paired ranges printed:

| cell | corrected median | corrected range | value above |
|---|---|---|---|
| INSERT | **0,82×** | 0,77–0,86× | 0,70× |
| READ | **1,12×** | 0,80–1,24× | 0,57× |
| UPDATE | **1,05×** | 0,94–1,14× | **0,48×** |
| DELETE | 0,98× | 0,38–1,09× | 0,59× |

**UPDATE moves from 0,48× to 1,05× and READ from 0,57× to 1,12×** — a factor of ~2,2 on UPDATE — from the
measurement fix alone. What survives as a **real** deficit is **INSERT: 0,82× on a tight 0,77–0,86× range**,
consistently behind in every rep; note that this arm's SQLite side still carries the trap-4 rowid asymmetry
(`id INTEGER PRIMARY KEY AUTOINCREMENT`, no secondary index) that S5 closed only for the fair arm.
READ, UPDATE and DELETE straddle 1,00× and are **unresolved**, because our per-rep values still vary while
SQLite's do not — the encrypted/PK posture needs more reps or stage attribution that three warm-ups
settled for the fair arm. See also the **cross-arm DELETE stall** named in worklog session 27.

### 1.3 Arm C — default document-CRUD, no PK

`WORKLOG.md` session 0 baseline, and the `docs`-shaped job the profile names as variable-length
(`IsFixedWidthRecords=False`, `AutoFixedWidthRecords=True`):

| READ | UPDATE | DELETE | INSERT |
|---:|---:|---:|---:|
| 0,76× | **0,24×** | **0,31×** | 0,67× |

### 1.4 Arm D — PageBased (opt-in)

| READ | UPDATE | DELETE | INSERT |
|---:|---:|---:|---:|
| 2,00× ahead | **0,17×** | ~0,80× | ~1,00× |

`docs/2.1.0-RC.3_WHAT_CHANGED.md` records the same shape: "UPDATE remains the open PageBased column
(legacy 11,0×, fixed-width 4,9× behind)". `StorageEngineType.Auto` deliberately never selects PageBased
(`DatabaseConfig.GetOptimalStorageEngine`) — a correct guard, and it means arm D never contaminates B.

### 1.5 Arm E — the API ladder, median of three

`WORKLOG.md` session 13 (2026-09-24), the first median-of-3 ladder run:

| arm | INSERT | READ | UPDATE | DELETE |
|---|---:|---:|---:|---:|
| SQL | **0,56×** | 0,91× | **0,26×** | **0,58×** |
| Direct | 0,65× | **1,49×** | 0,54× | 0,89× |
| StructRow | 0,68× | **1,32×** | — | — |

The spread column matters and is printed by the harness: session 12 measured SQL INSERT reading
0,31–0,69 across **four runs of the identical binary**, with the variance on *our* side of the ratio.
**Rule for this plan: a single run of any SQL-ladder cell cannot support a claim in either direction.**

---

## 2. Diagnosis — four distinct causes, and two of them are already half-built

The research run's central finding is that **this is a record-layout and row-location problem, not an
engine-speed problem**. Read against the code, four independent causes explain every behind-cell above.

### 2.1 Cause 1 — the constant-size record exists, and is gated off for exactly the shapes that need it

`DataStructures/FixedWidthCodec.cs` states the design in its own header:

> *"Every column occupies a constant slot in the record's fixed part: fixed-size columns store
> `[null-flag(1)][payload]` inline, variable-length columns (String / Blob) store a 5-byte slot
> `[null-flag(1)][arena-offset(4)]` referencing a block in the overflow arena."*

and the record is allocated as `new byte[layout.FixedSize]` — **one constant size per schema**. With
decision 8's inline capacity (24 bytes, shipped), short TEXT never touches the arena at all.

**That is precisely the artifact the research run derived independently** from PostgreSQL's
item-identifier indirection `[2]`, its out-of-line TOAST values `[4]`, and the AEAD API's whole-record
contract `[5]`. We built it. It is simply not granted to every table:

`SqlParser.DDL.cs:399-407` grants fixed-width **only** when
`primaryKeyIndex >= 0 && !hasInternalRowId && storageMode == StorageMode.Columnar && AutoFixedWidthRecords`.
A PK-less table — arm C, the 0,24×/0,31× arm — gets the legacy **variable-length** record, so a
growing UPDATE cannot be patched in place and falls through to `UpdateColumnarRow`'s append branch
(`engine.Insert` + index re-point).

### 2.2 Cause 2 — trap 3's prohibition was measured against a layout that no longer ships

`AUTONOMOUS_AGENT_BRIEF.md` §8 trap 3 and `AGENTS.md` both say: *never* flip the fixed-width default,
because forcing it on the PK-less shape measured **−24 % UPDATE and INSERT**.

But the plan's own §4b records *why* that measurement looked the way it did, and it is the pre-inline
capacity layout:

> *"i.e. every variable-length value, however short, goes to the arena. Measured on the multi-row pass
> that costs `arena-write` 2.26 + `arena-append` 1.79 = ~4.05 µs/row, ~24 % of the pass, and on the
> benchmark schema every TEXT value is short … so none of them needs the arena at all."*

**The −24 % is the arena tax, and decision 8 removed it** by shipping the inline capacity in the slot.
So the prohibition currently rests on a measurement of a layout that is no longer the layout being
switched on. That does **not** license flipping the default — the rail is absolute and this plan does
not propose breaking it. It means the control must be **re-measured at capacity 24**, and if the
regression is gone, that is an **owner decision** backed by fresh evidence (item S3).

> ⚠️ **This paragraph's hypothesis was tested by S3 and REFUTED — read S3's verdict with it.** Forcing
> the constant-size layout at capacity 24 leaves `index-maint` at **20.000 calls per 10.000 updates
> with identical 43 B/call**, and moves `row-locate-index` from a constant **279 B/call (~13 % of the
> pass)** to **320–568 B/call (47–72 %)`. So the −24 % is at least partly a **row-location** cost that
> capacity 24 does *not* remove — not the arena tax this paragraph guessed. **Trap 3's rail is
> confirmed**, and the layout is not the lever for arm C. See `WORKLOG.md` session 15.

### 2.3 Cause 3 — the encryption tax is an API contract, so only the layout can pay it

`WORKLOG.md` §5.3's profile (2026-09-23/24) measured the encrypted arm against its plaintext twin:

| stage | default (encrypted) | raw | delta |
|---|---:|---:|---|
| `commit-overwrites` | 19,5 ms / 1,60 MB | 3,4 ms / 0,55 MB | +16,1 ms, **3× the bytes** |
| `in-place-patch` | 8,3 ms | 1,2 ms (**same 175 B/call**) | +7,1 ms |
| `engine-write` | 15,6 ms | 6,0 ms | +9,6 ms |
| `row-locate-index` | 16,6 ms | 10,6 ms | +6,0 ms |

`AesGcm` exposes only whole-buffer `Encrypt`/`Decrypt`, with a separate tag and a per-message nonce,
and **no incremental or field-level update** `[5]`. Patching one field therefore re-encrypts the whole
record — at *identical* allocation, 7× the time. **A constant-size record makes that cost constant and
small; a variable-length record makes it a re-encryption of an arbitrarily large payload.** Causes 1
and 3 are the same cause seen from two sides — the research run's strongest cross-dimension signal.

### 2.4 Cause 4 — per-row interface dispatch costs every arm, and the runtime fix is not in the storage layer

`Storage/Engines/AppendOnlyEngine.cs` shows the shape: `Insert`, `Update`, `TryUpdateInPlace`,
`TryUpdateInPlaceSameLength`, `Read`, `Delete` are each **one interface call per row**, on
`IStorageEngine` → `IStorage`. .NET 11 ships "faster interface dispatch using a shared dispatch helper
… improving throughput for **interface-heavy workloads**" in NativeAOT `[9]`. This is the only lever in
the plan that helps **all four operations in all five arms at once**, and it costs a build
configuration rather than a format change.

### 2.5 What is *not* a cause (the two things this plan will not touch)

- **The LSM/compaction design is not a defect.** SQLite itself defers physical reclamation into a
  freelist and requires an explicit `VACUUM`; its FAQ answers *"I deleted a lot of data but the database
  file did not get any smaller"* as expected behaviour `[11]`. RocksDB makes update/delete first-class
  operators with deferred compaction `[13]`. Our append-plus-compaction is the same family of design,
  deliberately chosen.
- **The durability default is deliberate.** Decision 7 records a **34× cliff** behind buffered appends
  on per-row statements (961–1.037 µs/row against ~30 µs/row) and keeps write-through anyway, with
  `BulkImport` as the explicit opt-in. Nothing in this plan reopens it.

---

## 3. Non-goals — what must not be spent to win this

The research run found no evidence that any of these need to trade for row-store parity, so they are
explicit constraints, not afterthoughts:

1. **Encryption stays the default and stays real** (decision 6). No `NoEncryptMode` short-circuit may
   leak into a default path.
2. **Durability is untouchable**: WAL semantics, crash-consistency, the reopen round-trip matrix.
3. **The `StorageEngineType.Auto` guard stays**: PageBased is never auto-selected until its UPDATE
   reaches parity.
4. **The −24 % rail stays in force until an owner decision replaces it with a fresh measurement.**
5. **The "different league" wins are protected, not spent**: columnar/SIMD aggregates, vector search,
   GraphRAG, encrypted-at-rest-by-default, P/Invoke-free embedding.
6. **No release/version metadata, `global.json`, or packaging changes.** Local commits only.

---

## 4. Work items

Ordered by risk-adjusted value: **cheapest and most certain first**, format-affecting last. Each item
names its evidence, its gate, and a timebox in **sessions** (one unattended run ≈ 6–9 h, per the brief).
Every item inherits the brief's §0 operating rules, §11 rails, and §10 worklog protocol unchanged.

### S1 — Two-sided regime banner *(docs + harness only; no `src/` change)* — **timebox 1 session**

**Why.** Every ratio this repo publishes names our own `SHARPCOREDB_*` regime and leaves SQLite's
tuning implicit. SQLite's own documented surface (`journal_mode`, `synchronous`, `cache_size`,
`mmap_size`, `page_size`, `locking_mode`, `temp_store`, `wal_autocheckpoint`) can move its write
throughput by more than 2× `[7][12]`, and the vendor publishes **no** CRUD benchmark `[11][12]`. Until
both sides are named, no ratio is falsifiable.

**What.** Extend the harness's `REGIME:` line to print the SQLite arm's pragma set alongside the
`SHARPCOREDB_*` line, and record the chosen pragma set in every `comparative_*.json`.

**Acceptance.** Any number produced after this item carries both banners in its own log. A reader can
reproduce the SQLite reference without reading the harness source.

**Deliberately not in scope:** re-tuning SQLite to its maximum. The point is a *named* comparator, not
a slower one — and choosing the reference regime is an owner decision (see §9).

**Landed 2026-09-24 (worklog session 14) — `KEPT`.** The SQLite arm's pragma set is now one resolved
list with a `SHARPCOREDB_SQLITE_PRAGMAS` override, printed in a second `REGIME (SQLite reference):`
banner line beside our own switches, **read back** after being applied so the archive records what
SQLite actually accepted, and written into every archive that serializes a SQLite arm
(`BenchmarkResult.SqlitePragmas`, null on our own arms). Built-in default unchanged at WAL + NORMAL, so
no previously recorded number is invalidated. Validation: two `--pk-default` runs printed the correct
banners (`[built-in reference set]` and `[from SHARPCOREDB_SQLITE_PRAGMAS]`), the override read back
`journal_mode=delete, synchronous=2`, and the archive carried
`"SqlitePragmas": "journal_mode=wal, synchronous=1"` on the SQLite entry only.

### S2 — The HOT gate: skip index maintenance when nothing indexed changed — **timebox 2 sessions**

**Why.** PostgreSQL's HOT qualifies when "the update does not modify any columns referenced by the
table's indexes" and, when it does, **new index entries are not needed at all** `[3]`. We already run
this idea on DELETE — `DeleteByPrimaryKey` (`Table.CRUD.cs:4886`) is key-only and skips row reads when
no hash index needs the row — but the UPDATE side has no equivalent gate: `UpdateColumnarRow` walks
`MoveHashIndexesInPlace` / `RepointPrimaryKeyIfChanged` unconditionally
(`Table.CRUD.cs:1923-1931`).

**What.** Add a per-statement predicate: *does any key of `updates` participate in a loaded index
(PK B-tree or any `hashIndexes` entry)?* When it does not, take the no-index-maintenance branch.

**Why this is not decision 13 re-opened.** Decision 13 rejected *deferring* index maintenance to
`Flush()`, because the O(n) reconcile there took random-key DELETE from 294.185 → 70.248 ops/s. This
item **skips** work that is provably unnecessary for that statement — no deferred state, no reconcile,
no freshness window. The distinction must be stated in the commit message so the two are not conflated.

**Closed 2026-09-24 (worklog session 19) — `REJECTED` as specified, cause named and empirically
confirmed.** PostgreSQL's HOT precondition (a) is **structurally unreachable on a Columnar table**
here: `SqlParser.DDL.cs:430-436` auto-creates a hash index on **every** column
(`for (int i = 0; i < columns.Count; i++) … CreateHashIndex(columns[i])`), so the benchmark's own
`UPDATE docs SET score = …` names an **indexed** column — the index on `score` genuinely must be
updated. The measured **20.000 `index-maint` calls per 10.000 updates are therefore correct work, not
waste** (10.000 removes + 10.000 adds on the `score` index). Proof, same workload and same profiler:
the Columnar arm reports `index-maint 20.000 / 43 B/call`, while the **PageBased arm reports no
`index-maint` at all** — because the hash-index block sits inside
`if (storageMode == StorageMode.Columnar)` and PageBased gets none. The first attempt also proved a
process point: the gate was initially added to `UpdateColumnarRow`, and the profile showed **byte-identical
counts** (20.000 / 43 / 279 / 175), which is what exposed that the `docs` job routes through
`UpdateMultiple` (`Table.CRUD.cs:2287`) instead. The gate is **kept** because it is provably equivalent
where it fires and it removes genuinely dead work on the no-hash-index shape; it simply does not deliver
S2's target. **Promoted to an owner decision: see §9 row 5.** Bonus finding for arm D: PageBased's
`row-locate-index` costs **1.007 B/call against the Columnar arm's 279** — 3,6× — which is recorded
against arm D's 0,17×, not pursued here.


### S3 — Re-measure the trap-3 control at capacity 24, then decide *(measurement first)* — **timebox 2 sessions**

**Why.** This is the plan's highest-value question and it is **not yet answered**: does forcing the
constant-size layout onto the PK-less shape still cost 24 % now that short TEXT is stored inline in
its slot (decision 8) instead of in the arena? §2.2 argues the −24 % was the arena tax; if that is
right, the regression is gone and arm C can have in-place UPDATE/DELETE. If it is wrong, the rail
stands and arm C needs a different answer.

**What.** Reproduce the control exactly as trap 3 states it — the PK-less shape, `--pk` and the default
job, both layout settings, same-run SQLite reference, medians of three, interleaved arms — then
attribute the delta with `--pk-profile` (INSERT: `arena-write`/`encode-scratch`; UPDATE:
`row-locate`/`in-place-patch`/`engine-write`; file growth).

**Explicitly a measurement, not a build.** No `src/` change ships from this item. Its deliverable is a
§-style results table in the plan plus a worklog entry with a `Verdict:` of `KEPT` (hypothesis
supported), `REJECTED` (rail confirmed with fresh evidence), or `BLOCKED`.

**Acceptance.** Either outcome is a success, provided the attribution table is produced. **A reading
inferred from a ratio without the profile will not be accepted** — this plan has paid for that five
times.

**Closed 2026-09-24 (worklog session 15) — `REJECTED`, with a new mechanism named.** The control needed
no new code: `SHARPCOREDB_MAIN_FIXEDWIDTH=1` already forces the layout on the PK-less job (the
harness's own comment says it exists to "separate the two candidate gates"), and
`SHARPCOREDB_MAIN_PROFILE_UPDATE=1` supplies the attribution. Result: **`index-maint` is 20.000 calls
per 10.000 updates in *both* arms at identical 43 B/call** — the layout buys nothing there — while
**`row-locate-index` goes from a constant 279 B/call and ~13 % of the pass to 320–568 B/call and
47–72 %**, i.e. the forced layout makes row *location* the dominant cost. §2.2's arena-tax hypothesis
is therefore **refuted as the explanation**, trap 3's rail is **confirmed**, and §9 row 2 is answered
by evidence. The ratio half of the control was **recorded and not used** (arm A's own reps spanned
UPDATE 62.184–245.350, a 3,95× spread, on a machine that had just failed `--gate` twice at 2,92–2,99×).
Consequence: **S2 is promoted to the plan's main lever** (§8 order unchanged, since S2 was already
next). `row-locate-index`'s exact composition under a fixed-width decode is an open follow-up, not a
guess.


### S4 — NativeAOT interface dispatch, measured *(build config + measurement)* — **timebox 1 session**

**Why.** Per-row `IStorageEngine`/`IStorage` dispatch is the one cost every arm pays on every operation
(§2.4). .NET 11's NativeAOT "shared dispatch helper" is documented to improve "throughput for
interface-heavy workloads" `[9]` — the closest documented match to our shape in the whole research run,
and the only lever that is not arm-specific.

**What.** Publish an AOT profile and/or a documented recommendation, then measure it on the harness.
**Measure-first:** the vendor statement is about dispatch throughput in the abstract; the magnitude on
our workload is unknown and must not be assumed.

**Closed 2026-09-24 (worklog session 24) — `BLOCKED`, because the mechanism is untestable on this build
machine.** `dotnet publish -r win-x64 -p:PublishAot=true` fails at
`Microsoft.NETCore.Native.Windows.targets:152` with `vswhere.exe failed to locate Visual Studio with
Microsoft.VisualStudio.Component.VC.Tools.x86.x64`. That message is correct but indirect: vswhere *does*
find `...\18\Community`, but the query `-requires …VC.Tools.x86.x64` returns nothing — the component is not
registered. Reading the targets file gives the supported override (`IlcUseEnvironmentalTools=true` gates
all vswhere discovery on `!= 'true'`), which then exposed the real cause: `vcvarsall.bat` is **absent**, and
`...\VC\Tools\MSVC\14.51.36231\` contains only `Auxiliary`, `bin` and `lib\onecore` — **no `include`, no
`lib\x64`, no `msvcrt.lib`**. Compiler and linker *binaries* without the *libraries*: a partial C++
toolset, so `link.exe` cannot link. **This is not a refutation** — the vendor's dispatch claim is neither
confirmed nor denied, only untestable here — so the honest label is `BLOCKED`, not `REJECTED`. The remedy
is one checkbox (VS Installer → Modify → **Desktop development for C++**), recorded as §9 row 6 rather
than worked around: a helper that hand-builds the VC environment cannot work without those libraries and
would be dead code the moment the workload is installed. **The deliverable caveat survives and is the
point:** NativeAOT requires the VS C++ workload on the build machine, which belongs beside `net11`'s
raised minimum hardware requirements in any future AOT recommendation.

### S5 — A fair non-PK shape, so arm C is a comparison and not an artefact — **timebox 1 session**

**Why.** Arm C's 0,24×/0,31× is partly a *comparison* defect, not a product defect: the default job's
SharpCoreDB arm uses a **non-key** predicate while SQLite's arm resolves through its `INTEGER PRIMARY
KEY` rowid (brief §8 trap 4). With no index on our side, every statement scans. We cannot claim to beat
SQLite on a shape where the two engines are not doing the same work — but we also cannot fix the
product defect until we can see it clearly.

**What.** Add an indexed-non-PK arm: an explicit non-unique index on each side, the same predicate, both
engines resolving through an index. This isolates "row update cost" from "row location cost" — which is
exactly the separation §2.1's diagnosis depends on.

**Landed 2026-09-24 (worklog session 20) — `KEPT`, and it inverts arm C.** `--fair-ni` removes both
asymmetries: no primary key on either side, the same predicate through a secondary index on `name` on
both sides, and SQLite given an explicit index on **every** column to match SharpCoreDB's implicit five.

**Re-measured 2026-09-24 (session 21) with the arms interleaved and the spread printed** (§6.4), five
paired reps:

| | INSERT | READ | UPDATE | DELETE |
|---|---:|---:|---:|---:|
| SharpCoreDB (median) | 148.640 | 176.607 | 142.527 | 281.640 |
| SQLite (median) | 102.088 | 95.490 | 136.300 | 52.159 |
| **median paired ratio** | **1,45×** | **1,85×** | **1,04×** | **5,33×** |
| **range** | 1,28–1,59× | 1,07–2,26× | **0,47–1,75×** | 2,62–8,41× |
| *default job, no PK (arm C)* | *0,67×* | *0,76×* | *0,24×* | *0,31×* |

**INSERT, READ and DELETE clear 1,00× and stand; the UPDATE cell straddles it and does not.** The
session-20 `0,60×` UPDATE is **retracted as unsupported** — the honest statement is "somewhere between
0,47× and 1,75×, unresolved at five reps". The DELETE inversion (SQLite 52.159 against its own 136.300
UPDATE) is the index set: with five secondary indexes and no rowid predicate SQLite removes the row from
every index, while our hash entries are cheap to remove. See **S7** — this run also showed that the
per-rep variance is ours, not the machine's.

> ⚠️ **SUPERSEDED FOR UPDATE (2026-09-24, session 29).** The UPDATE values in the table below were measured
> on a SQLite arm that allocated a command and two parameters **per row**. Session 29 gave that arm one
> prepared command with re-bound parameters — the same correction our own arm had just received, applied
> symmetrically on purpose — and SQLite's UPDATE jumped **2,5×**, turning the cell from **1,78–1,87× ahead
> into 0,80× (0,68–0,91×) behind**. **The UPDATE half of this table, and the sentence below it, is
> withdrawn.** INSERT, READ and DELETE were reproduced on the corrected protocol (1,56× / 1,74× / 6,33×).
> Full reasoning: worklog session 29; the rule it produced is §6 rule 12.

**UPDATE resolved 2026-09-24 (session 25) — then reversed in session 29; read the warning above.** With `SHARPCOREDB_WARMUP_REPS=3` (see §6
rule 10), two independent runs gave all four cells clear of 1,00× — **but the UPDATE column does not survive
the corrected protocol; see both warnings in this entry**:

| run | INSERT | READ | UPDATE | DELETE |
|---|---|---|---|---|
| `WARMUP_REPS=3`, run 1 | **1,48× (1,47–1,50)** | **2,09× (1,92–2,16)** | ~~1,87× (1,66–2,02)~~ ⚠️ | **7,59× (3,13–7,79)** |
| `WARMUP_REPS=3`, run 2 | **1,56× (1,52–1,57)** | **2,06× (1,43–2,08)** | ~~1,78× (1,43–1,81)~~ ⚠️ | **7,75× (3,00–7,96)** |

**Three of four ahead** on the corrected protocol, against the default job's no-PK arm of 0,67 / 0,76 / 0,24 / 0,31. The UPDATE cell
moved **~7×** with no engine optimisation at all. Both archives are committed (`Reps: 4`, pragma set
recorded). **Residual wrinkle:** DELETE's range is wide because one rep per run is slow on DELETE
(150.623 / 161.641 against 326k–401k) while SQLite stays flat — the cell stands regardless, and
`SHARPCOREDB_WARMUP_REPS` is the dial if the *range* ever needs tightening.

> **Correction (2026-09-24, session 29):** the sentence immediately above overstates the UPDATE cell — that
> cell did **not** hold. A large part of the apparent gain was the SQLite arm's own **per-row command
> allocation**; removing it symmetrically (one prepared command with re-bound parameters, on both arms) left
> UPDATE at **0,80× (0,68–0,91×)** — behind, and the tightest cell in the table after DELETE. INSERT, READ
> and DELETE reproduced on the corrected protocol. Same warning as at the top of this entry.


### S6 — Shape-matched runtime wins, only where the JIT's own conditions hold *(opportunistic)* — **timebox 1 session**

**Why.** The .NET JIT's measured wins are **shape-conditional**: assertions derived from `switch`
targets eliminate bounds checks only where code switches on a length and then indexes (`CORINFO_HELP_RNGCHKFAIL`
and six jumps to it disappear; 103 → 70 bytes) `[14]`. Our row codec indexes spans after such switches.

**What.** Audit `FixedWidthCodec`, `Table.Serialization` and the fixed-width patch paths for that exact
shape, and adopt whichever `System.Runtime.Intrinsics` lane APIs apply — noting they are permitted by
`SIMD_STANDARDS.md` (which forbids `System.Numerics.Vector<T>`), and that the .NET 11 SIMD
construction/composition APIs (`CreateGeometricSequence`, `Zip`, `Unzip`, `Concat`) exist precisely for
columnar codecs `[9]`.

**Closed 2026-09-24 (worklog session 26) — `REJECTED` on attribution, before a line was written.** The
audit found the JIT's shape **already present**: `WriteTypedValueToSpan` (`Table.Serialization.cs:1114`)
guards each fixed-size write with `if (buffer.Length < 9) throw …` before
`BinaryPrimitives.WriteInt64LittleEndian(buffer.Slice(bytesWritten), …)`, and that guard *is* the fact the
JIT needs to discharge the bounds check — there is nothing to restructure. Then the attribution settled
it: on `--pk-profile-insert` (100.000 rows, fixed-width plaintext) the per-column/per-row write path
**`encode-layout` is 0,3 % of the pass across 100.000 calls at 0 B/call**, while **index maintenance is
≥ 31,1 %** and batch payload encoding 12,3 %. A perfect elimination of the targeted code therefore buys
**0,3 %** against a per-rep spread of 2–3 %, i.e. it is **unmeasurable** — and a change that cannot be told
from noise cannot satisfy this item's own "measured or reverted" rule. **What it hands over instead:**
the INSERT budget is *index maintenance first, payload encoding second, per-field writing last*, which is
the **second independent measurement pointing at §9 row 5** (S2 found `index-maint` at 20.000 calls per
10.000 updates; this finds index work at ≥ 31,1 % of INSERT) — and row 5 now has evidence on both sides,
a measured cost and a measured benefit (S5's 4,07× DELETE).

### S7 — Explain the engine-side variance *(new 2026-09-24; measurement + at most one diagnostic switch)* — **timebox 2 sessions**

**Why, and why it outranks S4.** The S5 spread run (worklog session 21) measured both engines in the same
process, on the same box, in alternating order, five times. **SQLite was nearly deterministic — UPDATE
135.099–137.338, i.e. ±1 % — while our own arms moved 3,72× on UPDATE and 3,14× on DELETE.** Machine load
cannot produce an asymmetry in which one engine is stable and the other is wild for five consecutive
pairs, so **the campaign's recurring 3–4× spreads and `--gate` `INCONCLUSIVE` verdicts are at least
partly engine-side** — which is a different problem from the one session 16 solved (the Defender/IO
filter is a real per-I/O cost, but it is not what makes *our* numbers move 3,7× and SQLite's 1 %). This
is **upstream of every measure-first item**, including S4: measuring an AOT build with a 3,7× noise floor
would repeat session 15's mistake at larger cost.

**Prime suspect, named and falsifiable.** `DatabaseConfig.ColumnarAutoCompactionThreshold` defaults to
**1000** (`DatabaseConfig.cs:827`) and each arm performs 10.000 updates + 10.000 deletes — ~20 threshold
crossings per arm — while `Table.TryAutoCompact()` (`Table.Compaction.cs:36`) exposes **no counter**, so
"did a compaction overlap this phase?" cannot be answered from the report today. A background compaction
landing inside a measured phase is exactly the shape that yields a bimodal 3–4× spread.

**What.** (1) Add a diagnostic `SHARPCOREDB_COMPACTION_THRESHOLD` override (precedent:
`SHARPCOREDB_INLINE_BYTES`) and, if cheap, a compaction counter in the profiler. (2) Run `--fair-ni` three
ways — default threshold, `0` (off), and a value above the pass total — at 5 paired reps, and compare the
**per-rep variance** rather than the medians. (3) If the variance collapses, that is the campaign's noise
cause with a name, and the follow-up is a measurement-protocol change plus, if warranted, an owner
decision on the default.

**Acceptance.** A verdict on whether compaction explains the variance: `KEPT` (variance collapses with it
disabled), `REJECTED` (it does not, with the variance table as evidence), or `BLOCKED`. **Either outcome
is a success** — a refuted cause still removes it from the list, and the variance table itself is a
permanent asset for reading every later result. No `src/` change beyond a diagnostic switch may ship
without an owner decision.

**Closed 2026-09-24 (worklog session 22) — split: compaction `REJECTED`, JIT tiering `KEPT`.** New
counters `Table.AutoCompactionLaunches` / `AutoCompactionCompletions` (pure instrumentation, incremented
at launch and in a `finally` around `CompactStorage`) read **`launches=0 completions=0` in every phase of
every rep of both runs**, so compaction cannot be the cause — and it *correctly* cannot: on an in-place
workload the counters `NeedsCompaction()` reads stay at 0, because `_updatedRowCount` only counts
*appended* versions and `_deletedRowCount` has no increment site at all. That second fact is logged as a
separate latent finding (`ColumnarAutoCompactionThreshold` is documented as a "sum of UPDATEs and
DELETEs" but behaves as a stale-version counter). The real cause is **managed warm-up**: with
`DOTNET_TieredCompilation=0` the per-rep spreads collapse — INSERT **1,70× → 1,10×**, DELETE
**3,20× → 1,54×** — and the verdicts move with them: READ goes from straddling (**0,93–2,04×**) to
standing (**1,64×, 1,30–1,77×**) and UPDATE's median goes from 0,84× to **1,79×** (its range still
straddles on rep 1). See §6 rule 10 for the protocol consequence, which is the item's real output.

---

## 5. Acceptance targets

> ⚠️ **Engine comparison complete (2026-09-24, session 35).** PageBased and AppendOnly were measured on both
> shapes on the identical harness. PageBased **fixes arm B's INSERT and READ outright** (0,83× → **1,72×**
> and 1,10× → **4,58×**) and is worse on DELETE (0,41× → 0,31×); on the fair shape it **trades READ for
> INSERT** (READ 1,74× → **0,82×**, INSERT 1,56× → **2,30×**), because a secondary-index predicate is the
> access a page-based engine is not built for while a PK lookup is the one it is. **UPDATE and DELETE are
> behind in both engines** (PK UPDATE 0,39×/0,47×, PK DELETE 0,41×/0,31×), so those two deficits are **not
> engine properties** — they live in the layer both engines share, and the engine swap is the evidence that
> rules the storage engine out. **PageBased's READ passed its correctness check (session 39)** — a 200-key
> sample on both shapes and both engines, 0 empty · 0 unexpected row count · 0 wrong value, run after the timed
> loop so the cell is untouched — so the 4,58× is a real engine result and is publishable. The check also
> established that the harness asymmetry runs *against* us (our arm materialises a dictionary per row, SQLite's
> only calls `reader.Read()`), which is a second, independent reason to believe the number. It proves the
> **path**, not every row, and covers the SharpCoreDB arms only.

> ⚠️ **UPDATE's deficit is attributed and needs an owner decision (worklog session 38).** Its three parts are
> the batch dispatcher's per-statement classification (~11–33 % of the phase, and **time** — the 531 B is worth
> only ~1,6 %, measured in session 37), the row locate (~16–24 %), and **hash-index maintenance on the written
> column (~7,6–20 %)**, which exists because *every Columnar column auto-creates a hash index*
> (`SqlParser.DDL.cs:430-436`) — so `SET score = …` pays a remove + add per row for a column nothing filters on,
> work that is **correct and therefore not removable by an optimisation** (S2 removed only the branch that had
> no effect). ~~Three answers: narrow the auto-index default, exempt non-filtered columns, or accept the cost and
> document it.~~ **⚠️ REFUTED (session 42):** `EnableHashIndexes` was a **dead property** (fixed — it now gates
> auto-creation, `src/` change, suite green), and with auto-indexes actually off, arm B's UPDATE reads
> **0,41×** and DELETE **0,35×** against 0,39× and 0,38× — **both inside the cell's noise**, while READ moves
> (0,10×), so the gate fired. **The auto-index default is not the lever**, and neither is index maintenance in
> general: a stage's *share* of a phase is not a prediction of what deleting the work is worth. What remains is
> the dispatcher's per-statement classification (~11–33 %, ~1,5 µs of it in the scanner + `SqlParser.ParseValue`)
> and the row locate (~16–24 %). Closing 0,39× → 1,00× needs the phase to shrink **~62 %**, which those two
> parts roughly cover — **reachable at the edge, not demonstrated.**
> The "give UPDATE the B3 structured predicate" item (session 36) is **cancelled by measurement**; do not
> re-open it.

> ✅ **The SQL-free batch path landed and is measured (2026-09-24, session 45).** `Database.UpdateBatch` /
> `Database.DeleteBatch` (new, optional API: `DataStructures/Table.StructuredDml.cs`,
> `Table.UpdateMultipleStructured` + the shared `UpdateMultipleCore`, `Database.Batch.cs`) are the
> UPDATE/DELETE siblings of `InsertBatch(object[][], columnOrder)`: typed keys, no statement text, no
> per-statement classification. Measured by `--fair-ni-batch` — three arms in **one process** (SQL batch,
> SQL-free batch, SQLite), rotated every rep, paired ranges printed — on the same fair shape, index and values:
>
> | cell | batch/SQLite | SQL/SQLite (control) | batch/SQL (attribution) |
> |---|---:|---:|---:|
> | 5 reps · UPDATE | **2,24×** (0,95–2,36) | 0,64× (0,52–1,19) | **3,47×** (0,80–4,12) |
> | 5 reps · DELETE | **12,01×** (2,55–13,19) | 7,81× (2,76–8,11) | **1,50×** (0,33–1,80) |
> | 9 reps · UPDATE | **2,05×** (0,83–2,44) | 1,04× (0,28–1,47) | **1,82×** (0,84–7,14) |
> | 9 reps · DELETE | **10,42×** (4,82–13,03) | 6,05× (3,27–7,08) | **1,84×** (0,69–2,94) |
> | both runs · INSERT | 2,05–2,20× | 2,04–2,23× | **0,98–1,01×** ← identical code in A and B |
> | both runs · READ | 1,96–1,99× | 1,55–1,97× | **1,01–1,02×** ← identical code in A and B |
>
> **The fair UPDATE cell moves from 0,90× (0,68–0,97) on the SQL path to 2,05–2,24× ahead**, reproduced in two
> independent runs, and the per-rep count agrees with the median (run 2: UPDATE 7 of 9 paired reps ≥ 1,00×,
> 6 of 9 ≥ 1,66×; DELETE 8 of 9 ≥ 1,70×). What reads as the protocol's own resolution is the INSERT/READ
> column — 0,98–1,02× where the two arms are the *same code* — so ±20 % per-rep noise is the bar the
> UPDATE/DELETE medians clear. **The ranges still straddle on single-rep stalls of the batched arm, so the
> medians stand and the floors do not**: neither UPDATE nor DELETE may be quoted as a single number. Arm B
> (`--pk-default`) and arm C (the no-PK `docs` job) are **not** re-measured — the `SHARPCOREDB_FAIR_BATCH_DML`
> dial is wired to the fair arm only, and no default-posture cell moves. Archives:
> `results/fair_ni_batch_20260924_190931.json` (5 reps), `…_191233.json` (9 reps); raw logs in
> `D:\scdb-bench-tmp\fair-ni-batch-20260924-1900.txt` and `…-run2-reps9.txt`. Suite 1836/0/0; `--gate`
> 🟡 **INSERT parity is now a configuration question, and the auto-index default is what stands between the
> two (2026-09-25, sessions 49–50).** With the dictionary-free INSERT overload *and* the per-column auto-index
> set off (`SHARPCOREDB_HASH_INDEXES=0`), the INSERT cell reads **1,60× (1,38–1,78)** ahead of SQLite on the
> **default-posture arm B** (session 48, same arm and binary with the indexes on: 0,95×) and **1,38× / 1,41×**
> on the no-PK `docs` arm C (0,90× with them on). In both A/Bs *both* of our arms gain while SQLite's reference
> stays flat, which is what identifies the dial rather than the machine. **So decision 4's ≥1,00× obligation is
> met at that configuration and not at the shipped default** — arm B's shipped INSERT is 0,95× (0,88–1,02).
> READ does not degrade without the auto indexes in either workload (the predicate columns carry explicit
> indexes; the PK point reads keep resolving), but the *benefit* side for arbitrary user equality queries is
> unmeasured, so §9 row 5 remains an owner decision — now the single blocking item for INSERT parity, with the
> cost measured and the benefit open. Session 42's contradicting reading (arm B INSERT 0,82× → 0,80× for the
> same dial) is recorded as unexplained rather than dropped.
>
> ⚠️ Also fixed here: the manual's "fast pattern" for bulk writes documented `db.UpdateMultiple(...)` /
> `db.DeleteMultiple(...)`, which do not exist on `Database` — the pages now document the measured
> `UpdateBatch`/`DeleteBatch` and the column-ordered `InsertBatch(table, rows, columns)` overload.

> 💡 **The auto-index cost, session 49's reading (the arm-C half; arm B's is in the row above).** With
> `SHARPCOREDB_HASH_INDEXES=0` (which gates auto-creation since session 42) the default no-PK `docs` arm's
> INSERT goes **0,90× → 1,38× / 1,41×** against SQLite in two independent runs — *both* our arms move (the SQL
> arm's own control 0,71× → 0,99× / 1,00×) while the SQLite reference does not, so the dial and not the machine
> is the cause. Arm B showed **nothing** for the same dial in session 42 (INSERT 0,82× → 0,80×, UPDATE/DELETE
> unmoved). So §9 row 5's question is answered *per shape* and the answer is asymmetric: on the legacy
> variable-length no-PK shape the auto-index set costs ~29–46 % of INSERT; on the fixed-width PK default shape
> it costs nothing measurable. What is **not** measured is the benefit side (this workload reads only `name`,
> through its explicit index): the row still needs the owner's call, now with a cost number attached.
> `HashIndexAutoCreationGateTests` pins the gate so the question stays askable.

> ✅ **The dictionary-free INSERT overload lands and is measured (2026-09-25, session 48).** `Database.InsertBatch(table, IReadOnlyList<object[]> rows, IReadOnlyList<string> columns)`
> exposes the engine path the SQL batch parser already used (`Table.InsertBatch(object[][], columnOrder)`,
> "explicitly dictionary-free") — until now the fastest INSERT the engine had was unreachable from the public
> API. Two arms, three arms per rep, 5 reps × 8 warm-ups; the two SharpCoreDB arms differ in the INSERT phase
> **only** in the input shape, so the attribution column is a clean measurement of the lever:
>
> | arm | INSERT batch/SQL | INSERT vs SQLite (dictionaries → arrays) | UPDATE batch/SQL | DELETE batch/SQL |
> |---|---:|---:|---:|---:|
> | B — pure default (encrypted, PK) | **1,10×** (1,05–1,18) | 0,87× (0,82–0,91) → **0,95×** (0,88–1,02) | 1,68× (1,50–2,03) | 2,02× (1,63–2,71) |
> | C — default no-PK `docs` job | **1,25×** (1,19–1,34) | 0,71× (0,69–0,75) → **0,90×** (0,87–0,96) | 1,43× (0,69–2,68) | 1,16× (1,10–1,26) |
>
> Both INSERT attributions clear 1,00× with tight ranges; both **mission** cells move toward parity but do not
> reach it (arm B 0,95×, arm C 0,90×). The engine's own allocation is **unchanged** (2.304 B/row vs
> 2.313–2.342 B/row, data file and arena byte-identical), so the win is the per-column name lookup plus the
> caller's smaller row objects — work, not memory. Decision 4's obligation (INSERT must *beat* SQLite) is now
> ~5–10 % short on arm B and ~10 % short on arm C, with the remaining residue attributed to index maintenance
> (§9 row 5) and payload encoding.


> (1) **"make the op shape dictionary-free"** — measured with `--batch-dml-shape-cost` as a **ceiling** first:
> the dictionary read pattern costs 47,5 ns/op against 7,7 ns/op for a shared column list + `object[]`, i.e.
> **39,8 ns/op = 1,1–3,0 %** of the five cells it would touch, inside a protocol whose own resolution is ±20 %;
> the op-list build (2,8 MB vs 1,0 MB) happens outside the window and produced **0 gen0** either way. Not
> built — the B3 pattern, applied before writing the code instead of after.
> (2) **the no-PK delete's per-key row decode** — `DeleteRecordsCore` reads its row payloads only for the
> eager hash cleanup and the PK cleanup, and the product default defers the former, so the decode is dead work
> on a no-PK table; removing it moved arm C's DELETE batch/SQL **1,11× → 1,16×** and left the mission cell at
> **0,14×**, i.e. inside the run's own spread, so it was **reverted** rather than kept on faith (S6's rule).
> What survived: one diagnostic property (`Table.DeferredDeleteIndexesEnabled`), two tests pinning both
> index-maintenance modes, and the `--batch-dml-shape-cost` mode itself.
>
> ✅ **The fair-arm attribution now has three independent samples** (5/9/5 reps): UPDATE batch/SQL **3,47× /
> 1,82× / 2,01×**, DELETE batch/SQL **1,50× / 1,84× / 1,61×**, with the INSERT/READ control column at
> 0,98–1,03× in all three (same code in both arms). The **attribution** is quotable; the **mission** cell
> (fair UPDATE vs SQLite: 2,24× / 2,05× / 1,42×, ranges straddling) is not — it inherits the SQL arm's own
> unresolved spread, whose control has read 0,64× / 1,04× / 0,74× across those runs.

> ✅ **The same lever, measured on arm B and arm C (2026-09-25, session 46).** The dial is general now
> (`SHARPCOREDB_BATCH_DML`), arm B and the default no-PK `docs` job both accept a `batchDml` override, and
> `--pk-default-batch` / `--docs-batch` drive the same three-arm protocol on them. Attribution (batch/SQL,
> same engine, same values, three arms rotated in one process):
>
> | arm | UPDATE batch/SQL | DELETE batch/SQL | UPDATE vs SQLite (SQL → batch) | DELETE vs SQLite (SQL → batch) |
> |---|---:|---:|---:|---:|
> | B — pure default (encrypted, PK) | **1,53×** (1,20–2,56) | **1,91×** (1,57–2,30) | 0,36× → 0,56× | 0,35× → 0,68× |
> | C — no-PK `docs` job | **1,35×** (1,13–1,50) | 1,11× (0,67–1,15) | 0,25× → 0,35× | 0,13× → 0,14× |
>
> **Arm B's first run read DELETE 0,38× (0,19–0,46) — a regression introduced by the new entry point, not by
> the engine** — because the typed loop never attempted the contiguous fixed-width DELETE resolver (B9) that
> the SQL path reaches. Fixed in `Table.StructuredDml.cs` and pinned by
> `DeleteBatch_FixedWidthPkTable_UsesTheContiguousFastPath`; the re-run reads **1,91×** with a range that
> clears 1,00×. **A new entry point inherits nothing.**
> ⚠️ **Arm C's protocol changed with this session:** the default job formatted its 10.000 UPDATE/DELETE
> statements *inside* the timed window while SQLite's side has used one prepared command since session 31, so
> both lists are now cached and **arm C's posted cells are not comparable with these new ones** — on the
> corrected harness its SQL columns read 0,25× (UPDATE) and 0,13× (DELETE) against SQLite, where the recorded
> cells said 0,24× and 0,31×. Arm B and arm C both remain behind SQLite on UPDATE/DELETE largely for trap 4's
> reason (the reference resolves through `id INTEGER PRIMARY KEY`; arm C matches on `name`), which is what
> `--fair-ni` exists to separate.

### 5.2 Outcome against the mandate (session 53) — not met, and every remaining cell has its reason

§0's mandate is "**bring the default posture (B) and the default schema shape (C) to ≥ 1,00× on all four
operations**". Read against §5.1's paired medians — sessions 52 and 53 added no arm measurements, because
they fixed the gate's method and landed the branch — the answer is **not met**:

| arm | INSERT | READ | UPDATE | DELETE | cells met |
|---|---|---|---|---|---|
| **B** — shipped default (encrypted, PK) | 0,95× (0,88–1,02) | **1,13×** (1,02–1,20) | 0,62× (0,61–0,65) SQL-free batch · 0,36× SQL path | 0,83× (0,58–0,88) batch · 0,35× SQL path | 1 of 4 |
| **C** — default no-PK `docs` job | 0,90× (0,87–0,96) | **1,07–1,13×** | 0,35–0,55× | 0,14–0,17× | 1 of 4 |
| **B/C with `SHARPCOREDB_HASH_INDEXES=0`** (dial only, not a shipped posture) | **1,60× (1,38–1,78)** / **1,38–1,41×** | unchanged (1,08–1,13×) | §5.1 row 4 | §5.1 row 4 | INSERT crosses, at that configuration |
| **fair shape** (tuned, no PK, indexed predicate) | **1,45–1,56×** | **1,74–2,07×** | **1,42–2,24×** (batch) | **10,4–12,0×** (batch) | 4 of 4 |
| **A** — fair-PK tuned plaintext (control) | 0,87× — *closed as not claimed*, decision 10 | **1,26×** | **1,29×** | **1,62×** | held by decision |

**The same cells in percent** (ratio − 1, so **+** means ahead of the same run's SQLite; a presentation of the
recorded ratios, not a new statistic). The config column is the point: the INSERT "we win" cells are
**not** the shipped default.

| configuration | INSERT | READ | UPDATE | DELETE |
|---|---|---|---|---|
| **shipped default** (arm B: encrypted, PK, no settings) | **−5 %** (−12…+2) | **+13 %** (+2…+20) | −38 % (−39…−35) | −17 % (−42…−12) |
| shipped default (arm C: no-PK `docs` job) | **−10 %** (−13…−4) | +9 % (−9…+19) *unresolved* | −64 % (−83…−62) | −84 % (−86…−83) |
| arm B with `EnableHashIndexes=false` | **+60 %** (+38…+78) | +13 % (+2…+20) | −8 % (−24…−2) | −36 % (−39…−27) |
| arm C with the same dial | **+38…+41 %** (+29…+48 / +37…+43) | +1…+8 % | −47…−45 % | −83 % |
| **fair shape** (tuned, no PK, indexed predicate) | **+45…+56 %** | **+74…+107 %** | −20 % SQL path · **+42…+124 %** batch | **+533 %** SQL · **+940…+1100 %** batch |
| **PageBased** on the arm-B shape (opt-in) | **+72 %** | **+358 %** | — | −69 % |
| **arm A** fair-PK tuned plaintext (control) | −13 % — *closed as not claimed* | **+26 %** | **+29 %** | **+62 %** |

The fair-shape medians above are the session-51 spread (INSERT 1,45–1,56×, READ 1,74–2,07×, UPDATE/DELETE
batch). Session 29's single-valued medians on the corrected protocol carried *wider* ranges (INSERT 1,56× at
1,36–1,71, READ 1,74× at 1,18–2,05), which is exactly why the campaign quotes a range beside every median and
treats a percentage here as a presentation of a ratio, never as a claim on its own.

So, stated as percentages: **faster than SQLite out of the box** on **READ in the default posture (+13 %)**,
and on **all four operations on the fair shape**; **INSERT is only a win if `EnableHashIndexes=false` is set**
(+60 % arm B, +38–41 % arm C) — which is a supported configuration but not the default, and it is a *trade*,
not free: see §9 row 5 for what that dial costs on queries (the explicitly indexed control is unaffected at
1,07×, while `email` / `age` / `score` equality queries degrade 940× / 32× / 7.298× — expressed as multipliers,
because "−99,9 %" would hide the three orders of magnitude between them).

The dial behind those two "+" cells is **not** a private harness knob: `SHARPCOREDB_HASH_INDEXES=0` sets
`DatabaseConfig.EnableHashIndexes = false` (`Program.cs:1313`/`1404`), a public `init` property defaulting to
`true` (`DatabaseConfig.cs:368`) whose auto-creation gate is `SqlParser.DDL.cs:434` and applies to **Columnar**
storage — the two shapes measured here. PageBased never auto-creates them (`SqlParser.DDL.cs:421`), which is
also why the PageBased row above reaches INSERT/READ without the dial. No default was changed in any session
named here.


1. **The fair shape is won on all four operations**, UPDATE included: S5 had to withdraw its UPDATE cell at
   0,80× behind, and the SQL-free batch path (`Database.UpdateBatch` / `DeleteBatch`) moved it to **1,42–2,24×
   ahead**, reproduced in three independent samples (5/9/5 reps) with a same-code control column at 0,98–1,03×.
2. **A large part of the recorded deficit was the measurement, not the engine.** The paired, interleaved
   protocol with discarded warm-up reps moved arm B's UPDATE **0,48× → 1,05×** and READ **0,57× → 1,12×** on
   its own, and session 32's symmetric-harness correction tripled SQLite's own PK UPDATE/DELETE reference. No
   ratio published before session 27 may be quoted.
3. **PageBased fixes arm B's INSERT and READ outright** (0,83× → **1,72×**; 1,10× → **4,58×**, correctness
   checked on a 200-key sample) and is worse on DELETE — so UPDATE/DELETE being behind in *both* engines is
   **not an engine property**. The engine swap is the A/B that rules the storage engine out.
4. **All seven work items are closed** (§8): S1 ✅, S5 ✅, S7 ⚖️, S3/S2/S6 ⛔ — and three of the four rejections
   refuted their own hypothesis *before* a speculative change shipped. S4 is ⛔ `BLOCKED`, so its hypothesis is
   **untested, not refuted**.
5. **The absolute floors stay met**: UPDATE ≥ 120K, DELETE ≥ 150K, INSERT ≥ 150K ops/s.

**Why the two mission arms are still short — per cell, with the evidence that decides it:**

- **INSERT — B 0,95×, C 0,90×: a configuration decision, not missing code.** The same build crosses 1,00× with
  the per-column auto-index set off (**1,60×** arm B, **1,38–1,41×** arm C, reproduced in two runs each), and
  the dial is identified because both of our arms move while SQLite's reference stays flat. It is blocked on
  **§9 row 5**, which now carries both halves of the trade: the set costs ~30–45 % of INSERT and buys
  **32×–7.298×** on equality queries over columns the user did not explicitly index. Narrowing the default is
  therefore the *wrong* lever; lazy/on-demand creation or a column-class rule are the live options.
- **UPDATE and DELETE — B 0,62×/0,83×, C 0,35–0,55×/0,14–0,17×: the comparison's shape and the encryption
  tax.** The same engine on a *matched fair* reference (no rowid, same index set) reads UPDATE **1,43×** and
  DELETE **1,16×** — so the row write is not the deficit. The reference resolves `WHERE id = …` through
  `INTEGER PRIMARY KEY` while these arms match a non-key column (trap 4), and arm B additionally pays the
  default encryption tax. That is the documented, evidence-backed reason of this plan's own completion clause
  — **but a reason is not the mandate met**, and it is stated as such.
- **No no-regression claim is verifiable yet: six `--gate` runs, 6 of 6 `INCONCLUSIVE`.** Two mechanisms are
  named and now separated in the output — machine load, and the cold first rep the gate includes in the
  statistic that decides it (**§9 row 7**). `D:\scdb-bench-tmp\quiet-gate-run.ps1` is the decisive experiment
  the moment the owner clears the box (one elevated command; the agent shell has no elevation).

**Not levers — measured, and not to be re-opened:** NativeAOT dispatch (S4 *untested*, blocked on the VS C++
workload, §9 row 6) · compaction · shape-matched JIT/SIMD (0,3 % available) · the dictionary-free op shape
(measured *ceiling* 1,1–3,0 %) · the no-PK delete's dead per-key row decode (moved 0,14× by nothing) · the B3
structured predicate for UPDATE (cancelled by measurement, session 36) · "narrow the auto-index default"
(§9 row 5, refuted in the direction it was proposed).

The plan is complete when **arm B and arm C each read ≥ 1,00× on all four operations**, or when each
behind-cell has a documented, evidence-backed reason it cannot.

### 5.1 Where each cell stands now (session 51, after sessions 45–51)

Two batch paths were built in this stretch — `Database.InsertBatch(table, rows, columns)` (dictionary-free,
column-ordered) and `Database.UpdateBatch` / `DeleteBatch` (SQL-free, typed keys) — plus the three-arm paired
harness that measures them (`--fair-ni-batch`, `--pk-default-batch`, `--docs-batch`). Cells below are the
**paired, per-rep median** against the same run's SQLite reference; the range in brackets is the observed
spread, and a range that straddles 1,00× is marked *unresolved* rather than rounded.

| arm | INSERT | READ | UPDATE | DELETE |
|---|---|---|---|---|
| **B — pure default, encrypted** (shipped posture) | **0,95×** (0,88–1,02) — *unresolved at the top of the range* | 1,13× (1,02–1,20) | 0,62× (0,61–0,65) SQL-free batch · 0,36× (0,29–0,40) SQL path | 0,83× (0,58–0,88) batch · 0,35× (0,30–0,38) SQL path |
| **C — default no-PK `docs` job** | **0,90×** (0,87–0,96) | 1,07–1,13× | 0,35–0,55× | 0,14–0,17× |
| **Fair (tuned, no PK, indexed predicate)** | 1,45–1,56× (SQL path) · **2,96×** (array path) | 1,74–2,07× | **2,01–3,47×** batch/SQL, 1,42–2,24× vs SQLite | 5–8× (SQL) · **10,4–12,0×** (batch) |
| **Arm B / C with the auto-index set off** | **1,60×** (1,38–1,78) / **1,38–1,41×** | unchanged (1,08–1,13×) | 0,92× (0,76–0,98) | 0,64× (0,61–0,73) |

**The three behind-cells, and the evidence for each:**

1. **Arm B INSERT 0,95× and arm C INSERT 0,90×** — *configuration-limited, not code-limited.* With the
   per-column auto-index set off, the same build reads **1,60×** (arm B) and **1,38–1,41×** (arm C), both
   reproduced, both with the dial as the only variable and both of our arms moving while SQLite's reference
   stays flat. The blocker is the owner decision §9 row 5, which now has **both** numbers: the set costs
   ~30–45 % of INSERT and buys **32×–7.298×** on equality queries for columns the user did not explicitly
   index. Narrowing the default is therefore *not* the lever; lazy/on-demand creation or a column-class rule
   are the live options.
2. **Arm B UPDATE 0,62× / DELETE 0,83×** — *partially closed by this stretch* (they were 0,39× / 0,38× on the
   SQL path; the SQL-free route is 1,53–1,68× / 1,91–2,02× the same engine's SQL path). What remains is
   dominated by the reference's shape: SQLite resolves `WHERE id = …` through its rowid with one prepared
   command, which trap 4 has documented since session 0, plus the encrypted default posture's per-row cost.
   Not attributable to a missing fast path: the contiguous single-range UPDATE/DELETE both arms use is
   reached by both entry points (session 46's regression was exactly this, and it is fixed and canary-pinned).
3. **Arm C UPDATE 0,35–0,55× / DELETE 0,14–0,17×** — *trap-4-dominated, with an independent measurement to
   prove it.* The same schema shape measured against a **fair** SQLite reference (no rowid, matched index set)
   reads **1,43×** (UPDATE) and **1,16×** (DELETE) on the SQL-free path and 5–8× on DELETE via the SQL path.
   So the deficit is the comparison, not the row write: this arm matches on `name` while the reference
   matches on `id`.


> ⚠️ **Arm B re-measured on the symmetric protocol (2026-09-24, session 32): INSERT 0,83× (0,79–0,88) ·
> READ 1,10× (0,88–1,20) · UPDATE 0,39× (0,38–0,43) · DELETE 0,41× (0,33–0,42).** Arm B was still running
> the old harness shape — statements formatted inside the timed window, no `StmtBuild` stamp on DELETE —
> while its SQLite comparator allocated a command and its parameters per row. Correcting **both** sides
> tripled SQLite's PK UPDATE and DELETE (288.108 → 878.557 and 385.116 → 1.172.704) and left arm B a
> **three-cell** gap, not the one-cell gap the old numbers implied. The INSERT cell is untouched code on
> both sides and reproduced at 0,82× → 0,83×, which is the control that makes the 3× shift on the other two
> credible. Where the engine work has to land: the batch dispatcher's per-statement classification
> (session 31) and the SQL-free UPDATE/DELETE batch path that INSERT already has. Arm C's fair-shape
> numbers are also superseded for UPDATE — see the warning in §4.

| Arm | READ | UPDATE | DELETE | INSERT | Baseline → target |
|---|---:|---:|---:|---:|---|
| **A** fair-PK tuned plaintext | 1,26× | 1,29× | 1,62× | 0,87× | **hold ≥ 1,00×** where already met; A's INSERT ratio is closed as *not claimed* (decision 10) and is **not** re-opened by this plan |
| **B** pure default encrypted | ~~0,57×~~ | ~~0,48×~~ | ~~0,59×~~ | ~~0,70×~~ | **→ ≥ 1,00× on all four; as of session 32 it reads 0,83 / 1,10 / 0,39 / 0,41** |
| **C** default document-CRUD (no PK) | 0,76× | 0,24× | 0,31× | 0,67× | **→ ≥ 1,00× on all four** |
| **D** PageBased | 2,00× | 0,17× | ~0,80× | ~1,00× | → UPDATE ≥ 1,00× (decision 1's "parity", restated as a ratio floor) |
| **E** ladder (SQL/Direct/StructRow) | — | — | — | — | **no cell may regress**, and SQL's spread must be printed so a claim is possible |

**Regression floors that outrank everything above** (from decision 10 and the brief): the `--pk-default`
arm carries a **no-regression rule against its own recorded values**; the absolute floors (UPDATE ≥ 120K,
DELETE ≥ 150K, INSERT ≥ 150K ops/s) stay met.

---

## 6. Measurement protocol

This plan adds **one** obligation to the existing protocol (`INSERT_UPDATE_PERFORMANCE_PLAN.md` §2); it
does not replace it.

1. **Two-sided regime banner.** Every published number names *both* regimes (item S1). A number without
   the SQLite pragma set is unpublished.
2. **Ratios only, never absolutes** — SQLite's reference drifts between runs. Existing rule, unchanged.
3. **Every ratio carries its same-run absolute pair**, per decision 6's both-columns rule.
4. **Arm order alternates per rep**, medians of three, spread printed. Existing rule, and now mandatory
   for the SQL ladder cell, which measured 0,31–0,69 across four runs of the identical binary.
5. **A verdict may not rest on a ratio alone.** The profile must name the stage. This is the plan's
   response to the five recorded instances of acting on an inferred reading.
6. **Re-record the gate baseline only on a quiet machine, and say why in the commit message.** Existing
   rule, unchanged.
7. **Check the machine before you measure, and never record a baseline on a `NOISY` verdict.**
   `pwsh scripts/quiet-machine.ps1` reports the environment and exits `0` (QUIET) or `1` (NOISY) so it
   can gate a session. Measured on this project's own dev laptop, a `--dual-mode` run showed a **3,9×**
   within-run UPDATE spread while the box was **not** thermally throttled (`% of Maximum Frequency` held
   at 100 %), **not** CPU-bound (total CPU 2–22 %) and **not** disk-bound (physical-disk queue 0,00,
   ~1 MB/s). The load was **Defender's real-time filter**: `MsMpEng` measured **0–17 % during the run**,
   tracking the benchmark's own phases, and an *idle* reading of 4,6 % rose to 9,3 % merely as files were
   written — on a workload that does thousands of tiny `FileOptions.WriteThrough` opens, every one of
   which is a filter-driver callback. **So "quiet" here means low per-I/O interference, not low CPU.**
   `-Apply` fixes what it can (Defender path/process exclusions for the repo and the benchmark exe, plus
   `dotnet build-server shutdown`) and **requires an elevated shell**; `-StopServices` additionally stops
   Windows Search, SysMain and DiagTrack for the session.
   *What this machine is not:* it is a 6-core/12-thread `i7-10850H` — **no hybrid P/E cores**, so core
   pinning is not an available lever; the power plan is already High performance and it runs on AC.
   **Verified 2026-09-24 (worklog session 17), and the emphasis corrected.** The check no longer reads
   `MsMpEng` at idle (it read 1,5 % and nearly returned `QUIET` about a filter-bound machine); it times
   real file opens — **buffered and write-through**, in the excluded dir and in an **unexcluded sibling
   on the same volume**. Measured: **excluded 176,5 µs/open buffered vs unexcluded 257,5 µs — the
   exclusion is confirmed at 1,46×**, i.e. the filter costs ~**81 µs per open**. But on the
   **write-through** path the engine actually uses, both directories cost ~**350 µs** and the filter's
   marginal share is only **1,08×**. So **flush (~350 µs) > filter (~81 µs)**: the durability floor is the
   dominant per-open cost, and the filter is a *noise* source rather than the largest *cost*.
   **Re-verified live 2026-09-26 (session 69).** The persistent half is still in force: the interleaved I/O
   probe reads the exclusion at **1,26×** control ÷ data (4/5 rounds ≥ 1,10×), the power plan is High
   performance on AC, MaxFreq 100 %, total CPU 10,7 %, disk queue 0, and `SHARPCOREDB_BENCH_TEMP` is
   `D:\scdb-bench-tmp` at User scope. **`WSearch` is the only finding**, and stopping it needs an elevated
   shell — a privilege limit. **A gate verdict does not wait for that command:** this box has passed
   `--gate` with the `WSearch` finding present (attempts 9/10, then sessions 64, 65, 66-rerun and 67).
   Check the machine with the script; never wait for a coincidentally idle one.

9. **The benchmark's data must land on an excluded path, and `TEMP` is not a reliable carrier.**
   The harness reads **`SHARPCOREDB_BENCH_TEMP`** (printed as `REGIME (data dir):`) for every data file,
   falling back to the OS temp. Set the ambient `TEMP` instead and the exclusion can be **functionally
   inert** — measured: with a Defender exclusion on `D:\scdb-bench-tmp`, every database still landed in
   `C:\Users\<user>\AppData\Local\Temp`, because an agent's shell inherits VS Code's environment block
   captured at VS Code start-up, not the shell's own `$env:TEMP`. `quiet-machine.ps1 -Apply
   -BenchTempDir <dir>` sets the variable at **User** scope, which needs **one VS Code restart** to reach
   an already-running shell.
10. **Run a discarded warm-up rep before the measured ones — the first rep is cold and only we pay for
   it.** Measured (worklog session 22): our per-rep numbers improve **monotonically** from rep 1 to rep 5
   while SQLite's stay flat, in two independent 5-rep runs — and since every rep builds a fresh database,
   only *process* state can carry across them. The cause is JIT tiering on a 100 %-managed hot path:
   `DOTNET_TieredCompilation=0` collapses INSERT's spread **1,70× → 1,10×** and DELETE's **3,20× → 1,54×**
   and moves READ out of "straddling" into "standing" (1,50× [0,93–2,04] → **1,64× [1,30–1,77]**).
   **The published setting stays the shipped configuration** — a discarded warm-up rep measures the
   *shipped* engine, whereas `DOTNET_TieredCompilation=0` measures a configuration no user runs, so that
   switch is a **diagnostic for attributing variance only**. Consequence: any ratio this campaign
   published from a cold single rep is re-read with that in mind, and re-taken if a cold rep decided it.
   **Implemented and confirmed 2026-09-24 (session 23):** `SHARPCOREDB_WARMUP_REPS` runs the real arm pair
   and discards it. It reproduces the diagnostic switch's effect **without changing the configuration**:
   INSERT's spread fell **1,70× → 1,16×** (tiering off: 1,10×) and DELETE's **3,20× → 1,54×** (tiering off:
   1,54×); rep 1 stopped being the outlier and the variability became scattered instead of ordered.
   **Three warm-ups, not one (session 25).** One warm-up rep removed the *monotone* ramp but not the
   spread — the measured UPDATE cell still moved 2,05× and its range straddled 1,00× (0,86–1,71×). Three
   warm-up reps collapsed that spread to **1,18×**, reproduced at **1,26×** in a second independent run,
   with **all four cells' ranges clearing 1,00× in both** (UPDATE **1,87× [1,66–2,02]** and **1,78×
   [1,43–1,81]**).
   **⚠️ The default is 8, not 3 (session 40).** Three was validated on the *fair* arm. Arm B still ramps
   across **five** measured reps at three warm-ups — UPDATE **92.338 → 182.719 → 254.114 → 150.922 →
   350.654 → 355.664**, monotone through measured rep 5 (= **pass 8**), while SQLite was flat from pass 1 —
   which is what put a **0,11×** low end on its UPDATE range. At eight warm-ups the ramp is gone (measured
   reps 1–3 read **353.713 / 350.089 / 357.393**) and arm B's UPDATE range tightens **0,11–0,43 →
   0,36–0,41**. Three had *looked* sufficient only because five measured reps sample passes 4–8, a segment
   of the ramp: **a protocol validated on a segment of a curve is not validated.** If a range ever
   straddles, raise this count before adding reps — and note that single anomalous reps survive the fix, so
   read the **median**, not the extremes.
11. **Do not time the harness's own work inside a measured phase, and print the split when you do.**
   Measured (worklog session 28): the fair arm's DELETE phase spent **0–17 ms of its 21–65 ms window
   building 10.000 interpolated SQL strings** — harness work, variable per rep, and about a quarter of the
   phase — while `flush` was **0 ms in every rep**. The phase is now split into `build` / `exec` / `flush`
   and all three are printed, so the engine's share is isolable; the same split is owed to the UPDATE
   phase, which has the identical shape. Also from that run: **cold JIT costs ~9× on `exec` here**
   (269 ms on the first warm-up against 21–48 ms warm), which is the largest cold penalty this campaign
   has measured on any single stage and a mechanical explanation for how bad the pre-warm-up DELETE numbers
   looked.

12. **Give the statement cache to both arms, or to neither.** Measured (worklog session 29): removing our
   in-window statement building fixed the DELETE stall *and*, done symmetrically, **reversed UPDATE from
   1,71× ahead to 0,80× behind** — because the old SQLite arm allocated a command and two parameters per row
   while our new one reads a cache. A one-sided correction is not a correction, it is a bias, and this one
   would have been invisible in the ratio it produced. **Any change to timing boundaries or per-rep
   allocations must be applied to both arms in the same commit, and both arms' cells re-measured.**
   Corollary: the pre-`Prepare()` fair-arm UPDATE, arm B and the PageBased/dual-mode columns are all
   provisional until re-run on session 29's harness.

8. **A ruled-out explanation is worth recording.** Thermal throttling was the first hypothesis for the
   3,9× spread and it is **refuted by measurement** (see rule 7). Do not re-raise it without new data.

9. **Read the gate's own noise, not its conclusion.** The §2.4 gate discards **no warm-up rep** (unlike
   every other mode in the harness, which starts with `SHARPCOREDB_WARMUP_REPS` rep(s), default 3), so a
   cold first rep lands inside the max-rep-spread statistic that decides its verdict (measured: a
   monotone 58.653 → 142.819 → 174.354 ops/sec default-arm UPDATE ramp *was* the printed 2,97× worst
   spread — worklog session 52). `INCONCLUSIVE` therefore means "this run cannot arbitrate", never
   "the machine is loaded": the gate now prints each arm's per-rep sequence and classifies it
   `cold-start` / `load` / `mixed`, and that shape — not the verdict word — is what a session records.
   **The protocol changed on 2026-09-26 (`97fb2651`): the gate now discards `ResolveWarmupReps()` reps of
   both arms like every other mode in the harness**, verified at exit 0 with 8/8 metrics `ok` and a 2,23×
   worst spread. What remains of this rule's caveat is the *baseline*: re-recording it is an owner decision
   (§9 row 7) and wants a `pwsh scripts/quiet-machine.ps1`-verified `QUIET` box — a command, not a wait.

10. **Capture the suite's full output, and do not read `Time:` as a duration.** A suite run piped through
   a summary filter destroys the only evidence a failure produces — session 52 lost one red run's test
   name and message that way, which is why the standing form is now
   `… -filterVSTest '<filters>' -result-trx <path> *> <file>`. Four clean runs on that same tree report
   `Time` **61,6–64,4 s against 62–65 s of wall**, so `Time` tracks wall normally and not always: the one
   red run reported **3866,074 s** while its captured log spanned ~5 minutes, and no test-level retry
   policy exists to explain it (`maxParallelThreads: 0`, `parallelizeTestCollections: true`). One
   un-named red run is a flake to be re-run and named, never explained away.

---

## 7. Safety rails and canaries

**Rails — absolute, unchanged from the brief §11** (they are not restated loosely here; the brief is
authoritative):

- Never force-push, rewrite history, delete the worklog, modify release/version metadata or package
  versions, pack/publish, change `global.json`, or disable encryption to win.
- Never trade durability: WAL semantics, crash-consistency and the reopen round-trip matrix stay green.
- Never flip the fixed-width layout default (S3 changes the *evidence*, not the default).
- Any format-affecting change **must ship with a migration/upgrade path** and existing files must still
  open (decision 3). The magic already reserves version bytes.

**Canaries that must stay green on every commit** (the brief's list, plus one addition):

`ReopenRoundTripMatrixTests`, `FormatCompatPolicyTests`, `FixedWidthBulkUpdateTests`,
`FixedWidthBulkDeleteTests`, `FixedWidthPatchTests`, `WritePathProfilerTests`,
`FixedWidthInlineValueTests.Reopen_KeepsInlineAndOverflowValues`, `SingleFileDirectoryParityTests`,
and — added by this plan — **`FixedWidthInlineValueTests` in full** and **`SingleFileFileGrowthTests`**,
because S3 probes exactly the layout whose correctness those two classes pin (the inline/overflow
distinction and the file-growth floor).

**Every commit:** build clean + core suite green + `--gate` pass, with a documented re-run if the gate
fails. The gate's verdicts on this machine are frequently `INCONCLUSIVE` (exit 2), which is **not a
failure and not a pass**: it means the run's own reps disagreed by more than 2,50×, so the run cannot
arbitrate. Record it as `INCONCLUSIVE` with the measured spread, re-run once, and never read it as green
(§9 row 7 names the mechanism now known to contribute to it).

---

## 8. Execution order and timeboxes

| Order | Item | Timebox | Ships `src/`? | Status | Why here |
|---|---|---:|---|---|---|
| 1 | **S1** two-sided regime banner | 1 | no | ✅ `KEPT` (`b11903e3`) | every later number depends on it |
| 2 | **S3** trap-3 control re-measured | 2 | no | ⛔ `REJECTED` (`5494b594`) | answered before any build, and the plan §2.2 note was corrected |
| 3 | **S5** fair non-PK indexed arm | 1 | no | ✅ `KEPT` (`d8c7da1b`, `e686727d`); INSERT/READ/DELETE confirmed on the corrected protocol, **UPDATE withdrawn — 0,80× behind (session 29)** | turns arm C from an artefact into a comparison — and it did |
| 4 | **S2** HOT index gate | 2 | yes | ⛔ `REJECTED` as specified (`2a93e5cf`) | narrow and reversible; the gate is kept, the target was unreachable |
| 5 | **S7** engine-side variance | 2 | no (diagnostic switch only) | ⚖️ **split**: compaction ⛔ `REJECTED`, JIT tiering ✅ `KEPT` (`6c44ff3e`); warm-up rep landed session 23 | found the campaign's dominant measurement error |
| 6 | **S4** NativeAOT dispatch, measured | 1 | build cfg | ⛔ **`BLOCKED`** on the missing VS C++ workload (§9 row 6) | hypothesis **untested, not refuted** — see the entry |
| 7 | **S6** shape-matched JIT/SIMD | 1 | yes | ⛔ `REJECTED` on attribution (session 26) | the target is already taken and worth 0,3 %; index work is ≥ 31,1 % |

**All seven items are closed.** Final tally: **S1 ✅ · S3 ⛔ · S5 ✅ · S2 ⛔ · S7 ⚖️ · S4 ⛔ `BLOCKED` · S6 ⛔** —
two `KEPT`, four `REJECTED`, one `BLOCKED`, and **not one of the four rejections was a failure**: each
refuted its own hypothesis with a measurement, and three of them (S2, S5, S6) did so *before* a speculative
change shipped. The campaign's result is **three of four operations ahead on the fair shape** — INSERT **1,56× (1,36–1,71)** ·
READ **1,74× (1,18–2,05)** · DELETE **6,33× (4,99–6,77)** on the session-29 corrected protocol — against the
default job's no-PK arm of 0,67 / 0,76 / 0,24 / 0,31. **UPDATE is behind at 0,80× (0,68–0,91×)**, because the
1,78–1,87× reported in session 25 was substantially the SQLite arm's own client-side overhead. The three wins
were still achieved with **no engine optimisation at all**, purely by fixing the comparison (S5) and the
measurement (S7) — and the UPDATE finding is the campaign's most useful output, because it names a real
engine target rather than a measurement artefact. What remains is the §9 owner review **plus the UPDATE
deficit**.

*Closing note on the timebox: **11 sessions were budgeted and ~10 were used** — one or two per item, plus
the extra sessions that S7's two verdicts and the warm-up count required. **No item overran its own
timebox**, which is the §5 rule the campaign was built around.*

S3, S5 and S7 all sit before a *build* deliberately: each is a measurement that decides whether the build
is worth making, and each has now paid for itself — S3 refuted a hypothesis, S2 refuted its own target
before a line of it shipped beyond a correct no-op gate, and S5 refuted one of its own cells. **S7 is the
clearest case: it refuted its own hypothesis (compaction, by a counter that read 0), found the real cause
(JIT warm-up), and that finding invalidated the measurement basis of every ratio the campaign had
published — including S5's own first result.**

**Decision points.** If S3 returns `REJECTED` (the −24 % reproduces at capacity 24), arm C's target
moves to a §9 owner decision rather than a build, and S2 becomes the plan's main lever. If S3 returns
`KEPT`, arm C's fix becomes a format change whose shape and migration are §9 decisions.

---

## 9. Owner decisions required

| # | Decision | Evidence in hand | Cheapest safe default |
|---|---|---|---|
| 1 | **The SQLite reference regime.** Which pragma set do we compare against — SQLite's own defaults, or a tuned set? | `[7][12]`; SQLite's per-run drift is already a documented problem | Print both sides (S1) and keep SQLite at its defaults until the owner picks; never mix |
| 2 | **The −24 % rail, if S3 reproduces it in the other direction.** If forcing the constant-size layout on the PK-less shape is ≤ 1,0× cost at capacity 24, may it be enabled for new tables (not migrated) as a *conditional* default? | S3's table + `[2][3][4][5]` | Keep the rail; report and wait |
| 3 | **A numeric floor for arm B.** Decision 10 lists this as its one open sub-decision. | §1.2's recorded band | Keep the no-regression-against-its-own-values rule until a floor is chosen |
| 4 | **PageBased UPDATE (arm D).** Decision 1 says parity; the research adds no PageBased-specific mechanism. | §1.4; decision 1 | Keep PageBased opt-in and out of Auto; fix as a separate campaign |
| 5 | **Does a Columnar table still auto-create a hash index on *every* column?** `SqlParser.DDL.cs:430-436` does, so the `docs` table carries **5** hash indexes while the workload uses **1** (`name`, via an explicit `CREATE INDEX`). Every UPDATE that changes any column therefore pays a hash remove+add (measured: 20.000 `index-maint` calls per 10.000 updates, 43 B/call) — and it is *correct* work, not waste, precisely because the index exists. | S2's verdict (worklog session 19); §2.1 | **Keep the current default** until the owner decides: narrowing it is a behaviour change for every equality query on a non-PK column, so it needs a measured comparison of READ cost against UPDATE cost, not a unilateral edit. **⚠️ S5 (session 20) now argues *against* narrowing it:** on a matched index set (`--fair-ni`) our hash indexes beat SQLite's B-trees on DELETE **4,07×**, so the per-column indexes may be an asset on the fair shape rather than the cost they looked like on the unfair one. Measure before acting on this row.<br>**✅ MEASURED BOTH SIDES (sessions 42/49/50/51).** *Cost:* the set is ~**30–45 % of INSERT** — with `SHARPCOREDB_HASH_INDEXES=0` the INSERT cell reads **0,95× → 1,60× (1,38–1,78)** on arm B and **0,90× → 1,38× / 1,41×** on arm C, reproduced in two runs per arm, with the dial as the only variable (both of our arms move, SQLite's reference does not). *Benefit:* without it, equality queries on columns the user did not explicitly index degrade **32×–7.298×** (`--auto-index-benefit`, 20.000 rows: `email` **940×**, `age` **32×**, `score` **7.298×**; the explicitly indexed control reads **1,07×** and the row counts agree exactly). **So "narrow the default" is the wrong lever** — it trades a bounded per-row cost for up to three orders of magnitude on a class of user queries. Lazy/on-demand creation, or a rule by column class, are the live options and both now have their numbers. `HashIndexAutoCreationGateTests` pins the gate so the experiment stays askable. |
| 6 | **Install the Visual Studio C++ workload to unblock NativeAOT (S4).** `...\VC\Tools\MSVC\14.51.36231\` has `bin` and `lib\onecore` only — no `include`, no `lib\x64`, no `msvcrt.lib` — and `vcvarsall.bat` is absent, so `link.exe` cannot link. | S4's verdict (worklog session 24), with the directory listing and the `vswhere -requires` result | **VS Installer → Modify → Desktop development for C++** (`Microsoft.VisualStudio.Component.VC.Tools.x86.x64`). After that, plain `dotnet publish -r win-x64 -p:PublishAot=true` needs no override. Until then S4 stays `BLOCKED` and its hypothesis is *untested*, not refuted. |
| 7 | **May the gate discard a warm-up rep?** `RunRegressionGate` runs its 3 measured reps and nothing else, while every other mode in the same harness discards `SHARPCOREDB_WARMUP_REPS` rep(s) first (default 3); the verdict is the max rep spread against 2,50×, so the cold first rep sits inside the statistic that decides it. Measured (session 52): the default arm's UPDATE ran **58.653 → 142.819 → 174.354 ops/sec** across the three reps — a monotone 2,97× rise that *was* the printed worst spread, and the shape of a cold process rather than of load. This is one non-load mechanism for the ≥ 6 `INCONCLUSIVE` verdicts recorded in sessions 19–32, four of them straddling the limit with docs-only commits behind them. | Session 52 (worklog): four measured runs (3,43× / 2,97× / 3,24× / 3,11×), the code path at `Program.cs:4741-4846`, and the warm-up call sites at `:2388`, `:2511`, `:2664` | **The protocol half is DONE (2026-09-26) and the box is not the blocker for the rest.** `97fb2651` gave the gate the same discarded warm-ups every other mode has (`ResolveWarmupReps()`, default **8**, `Program.cs:4801-4818`), and that commit's own run was **exit 0, eight of eight metrics `ok`, worst rep spread 2,23×** — three sessions of exit-2 runs carried a rep-1 ramp inside the statistic that no longer sits there. Its message states **no baseline re-recorded**, so the comparison's two sides came from *different* protocols; whether that *loosens* the guard is **not** established by reasoning (the compared statistic is a median, `MedianOf(raw)` vs the recorded median, and the cold rep is the minimum of three) — what is established is the provenance mismatch, which is why re-recording is still the clean end state. That step wants `--write-baseline` on a script-verified `QUIET` box: `pwsh scripts/quiet-machine.ps1 -Apply -StopServices -BenchTempDir D:\scdb-bench-tmp` — **one elevated command**, i.e. a privilege limit, not a machine limit (session 18 already returned `QUIET`; and gate *verdicts* pass here with `WSearch` running: attempts 9/10, sessions 64/65/66-rerun/67). It stays an **owner call**, because it moves the reference the guard measures against. Meanwhile the gate prints each arm's per-rep sequence and classifies it (`cold-start` / `load` / `mixed`), so every verdict states the shape it can see instead of asserting load; thresholds, medians and exit codes are unchanged. |
| 8 | **Is `release/v2.1.0.0-RC.3` the landing target for campaign work, rather than a `perf/*` branch?** The owner, 2026-09-25 (session 53): **yes** — RC.3 becomes the future main line and carries all the .NET 11 / C# 15 optimizations, so the campaign's work is fast-forwarded onto it. This **overrides brief §9's "do not commit to `release/*`" for this branch only**; every other rail in §11 stands, and `Never push` was *not* part of the override. | Session 53 (worklog): `84b79dbf` (= `origin/release/v2.1.0.0-RC.3`) is **114 behind / 0 ahead** of the campaign head, `git merge-base --is-ancestor` exits 0, `git merge-tree --write-tree` reports no conflict, and the range is 394 files / +40.924, with **28 commits touching `src/`** | **Fast-forward only** (`git merge --ff-only`), never a merge commit and never a hand-merged file; the agent does not push, and the released **`v2.1.0-RC.3` tag is untouched** because it sits one commit behind the branch head (`d5924420`) and a fast-forward cannot move it. |
| 9 | **How do the two lines coexist once RC.3 is the main line?** The owner, 2026-09-25 (session 53): `master` (net10.0 / C# 14, `ci.yml`, `DOTNET_VERSION: 10.0.x`) stays the **maintenance line for the currently released framework**, and `release/v2.1.0.0-RC.3` (net11.0 / C# 15 preview, `ci-net11.yml`, .NET 11 SDK pin) is the **forward line** that becomes the default. **Fixes flow `master` → net11 line, never the reverse.** | `ci.yml` triggers `branches: [master, develop]` and `tags: ['v*', '!v2.1*']` with pack/publish master-only; `ci-net11.yml` triggers `release/v2.1.0.0-*`; the lines diverge in `global.json`, `Directory.Build.props` and `Directory.Packages.props` (69 changed lines); csproj says `net11.0`/`preview` on the forward line vs `net10.0`/`14.0` on master; `ls-remote --symref` shows GitHub's default is still `master` | **Promote the net11 branch by default-branch *pointer*, not by rename** — a rename stops `ci-net11.yml` matching *and* hands the net11 commits to the net10 pipeline in one move. Forward-port with `git cherry-pick -x`, resolving conflicts in `global.json` / `Directory.Build.props` / `Directory.Packages.props` in favour of the **net11 build config** (take the fix, never the config). Never `git merge` the net11 line into `master`: net11-only API and C# 15 syntax would land in a net10/C#14 line that cannot compile it. Agent sessions never rename branches, change the default branch, edit `global.json`, or move version metadata. |



---

## 10. Provenance

- **Research artifact:** `_bmad-output/planning-artifacts/research/technical-beating-sqlite-on-every-crud-axis-2026-09-24/`
  — `research.md` (report, citation-checked clean), `digests/` (8 digests), `.memlog.md` (31 entries:
  16 sources, 12 claims — 10 verified, 2 unverified), `imports/` (empty: a native run, nothing imported).
- **Method:** bmad-deep-recon, `technical` type, 2 rounds, inline (no subagent harness), `validation=normal`
  (load-bearing claims spot-checked; 10 of 12 verified by a second source or by the publisher's own
  primary enumeration). Red-team pass not run (`red_team=off`); contrary evidence is quoted from the
  purchased sources themselves.
- **External sources support mechanisms only.** Every SharpCoreDB figure in this plan is cited to this
  repository (`INSERT_UPDATE_PERFORMANCE_PLAN.md` §0.1/§8e, `WORKLOG.md`, `AGENTS.md`, or a source file
  and line). The research firewall was honoured: project material shaped the questions, never the
  findings.
- **Staleness:** the report's fastest-ageing claims are the .NET 11 ones (RC at access time; GA expected
  November 2026) — re-read `.NET 11` after GA before any net11-based claim is published.




