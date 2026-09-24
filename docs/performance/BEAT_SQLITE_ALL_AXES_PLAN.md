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

**All four are behind.** This is the single most important row in the plan: it is the shipped default,
encrypted, with no tuning switches.

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

**Acceptance.** A before/after on the arms that matter (B and C), with allocation and ops/s, *or* a
`REJECTED` verdict with the reason (e.g. AOT incompatible with a reflection path we need). Either is a
complete result. Note the deployment caveat: `net11` raised minimum hardware requirements `[9]`, and
any AOT recommendation must say so.

### S5 — A fair non-PK shape, so arm C is a comparison and not an artefact — **timebox 1 session**

**Why.** Arm C's 0,24×/0,31× is partly a *comparison* defect, not a product defect: the default job's
SharpCoreDB arm uses a **non-key** predicate while SQLite's arm resolves through its `INTEGER PRIMARY
KEY` rowid (brief §8 trap 4). With no index on our side, every statement scans. We cannot claim to beat
SQLite on a shape where the two engines are not doing the same work — but we also cannot fix the
product defect until we can see it clearly.

**What.** Add an indexed-non-PK arm: an explicit non-unique index on each side, the same predicate, both
engines resolving through an index. This isolates "row update cost" from "row location cost" — which is
exactly the separation §2.1's diagnosis depends on.

**Acceptance.** The new arm's numbers are stable enough (spread printed, median-of-3) that a claim is
possible, and the difference between arm C indexed and arm C unindexed is attributable to location, not
to the write path. This is a harness item and ships no `src/` change.

### S6 — Shape-matched runtime wins, only where the JIT's own conditions hold *(opportunistic)* — **timebox 1 session**

**Why.** The .NET JIT's measured wins are **shape-conditional**: assertions derived from `switch`
targets eliminate bounds checks only where code switches on a length and then indexes (`CORINFO_HELP_RNGCHKFAIL`
and six jumps to it disappear; 103 → 70 bytes) `[14]`. Our row codec indexes spans after such switches.

**What.** Audit `FixedWidthCodec`, `Table.Serialization` and the fixed-width patch paths for that exact
shape, and adopt whichever `System.Runtime.Intrinsics` lane APIs apply — noting they are permitted by
`SIMD_STANDARDS.md` (which forbids `System.Numerics.Vector<T>`), and that the .NET 11 SIMD
construction/composition APIs (`CreateGeometricSequence`, `Zip`, `Unzip`, `Concat`) exist precisely for
columnar codecs `[9]`.

**Acceptance.** Each change is measured on the harness or reverted. This item is scheduled **only after
S1–S3** and must never delay them. It is explicitly the lowest-priority item in the plan.

---

## 5. Acceptance targets

The plan is complete when **arm B and arm C each read ≥ 1,00× on all four operations**, or when each
behind-cell has a documented, evidence-backed reason it cannot.

| Arm | READ | UPDATE | DELETE | INSERT | Baseline → target |
|---|---:|---:|---:|---:|---|
| **A** fair-PK tuned plaintext | 1,26× | 1,29× | 1,62× | 0,87× | **hold ≥ 1,00×** where already met; A's INSERT ratio is closed as *not claimed* (decision 10) and is **not** re-opened by this plan |
| **B** pure default encrypted | 0,57× | 0,48× | 0,59× | 0,70× | **→ ≥ 1,00× on all four** |
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
9. **The benchmark's data must land on an excluded path, and `TEMP` is not a reliable carrier.**
   The harness reads **`SHARPCOREDB_BENCH_TEMP`** (printed as `REGIME (data dir):`) for every data file,
   falling back to the OS temp. Set the ambient `TEMP` instead and the exclusion can be **functionally
   inert** — measured: with a Defender exclusion on `D:\scdb-bench-tmp`, every database still landed in
   `C:\Users\<user>\AppData\Local\Temp`, because an agent's shell inherits VS Code's environment block
   captured at VS Code start-up, not the shell's own `$env:TEMP`. `quiet-machine.ps1 -Apply
   -BenchTempDir <dir>` sets the variable at **User** scope, which needs **one VS Code restart** to reach
   an already-running shell.
8. **A ruled-out explanation is worth recording.** Thermal throttling was the first hypothesis for the
   3,9× spread and it is **refuted by measurement** (see rule 7). Do not re-raise it without new data.

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
fails.

---

## 8. Execution order and timeboxes

| Order | Item | Timebox | Ships `src/`? | Why here |
|---|---|---:|---|---|
| 1 | **S1** two-sided regime banner | 1 | no | costs docs + harness only, and every later number depends on it |
| 2 | **S3** trap-3 control re-measured | 2 | no | answers the plan's highest-value question *before* any build |
| 3 | **S5** fair non-PK indexed arm | 1 | no | turns arm C from an artefact into a comparison; feeds the attribution S3 needs |
| 4 | **S2** HOT index gate | 2 | yes | narrow, reversible, and independent of the layout question |
| 5 | **S4** NativeAOT dispatch, measured | 1 | build cfg | helps every arm at once; measure-first |
| 6 | **S6** shape-matched JIT/SIMD | 1 | yes | opportunistic; must never delay 1–5 |

**Total timebox: 8 sessions.** S3 and S5 are ordered before S2 deliberately: if arm C's gap turns out
to be location-dominated, S2's value is smaller than it looks, and that ordering costs nothing while
inverting it could waste two sessions.

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
| 5 | **Does a Columnar table still auto-create a hash index on *every* column?** `SqlParser.DDL.cs:430-436` does, so the `docs` table carries **5** hash indexes while the workload uses **1** (`name`, via an explicit `CREATE INDEX`). Every UPDATE that changes any column therefore pays a hash remove+add (measured: 20.000 `index-maint` calls per 10.000 updates, 43 B/call) — and it is *correct* work, not waste, precisely because the index exists. | S2's verdict (worklog session 19); §2.1 | **Keep the current default** until the owner decides: narrowing it is a behaviour change for every equality query on a non-PK column, so it needs a measured comparison of READ cost against UPDATE cost, not a unilateral edit |

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




