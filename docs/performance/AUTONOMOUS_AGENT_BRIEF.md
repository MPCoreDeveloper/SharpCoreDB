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

## 1. Mission and success criteria

Beat SQLite on the fair-PK CRUD benchmark (all four columns) and bring the PageBased engine's UPDATE
to parity, **before November 2026**.

Targets (median-of-3, tuned plaintext fixed-width, `--pk` arm; ratio = SharpCoreDB ÷ SQLite, so
> 1.0 = ahead):

| Operation | Current | Target |
|---|---:|---:|
| READ | 1.05× ahead | stay ahead (≥ 1.0×) |
| UPDATE | 1.29× ahead | stay ahead (≥ 1.0×) |
| DELETE | 2.19× ahead | stay ahead (≥ 1.0×) |
| INSERT | **0.54× behind** | **≥ 1.0× (faster than SQLite)** |
| PageBased UPDATE | **0.17× (4.9× gap)** | ≥ 0.5×, ideally parity |

Absolute acceptance targets (plan §8): UPDATE ≥ 120K, DELETE ≥ 150K, INSERT ≥ 150K ops/s.
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
# WARNING: tools/clean-benchmark.ps1 line 25 has a WRONG project path (it omits the
# `benchmarks\` folder). Do not rely on that script; call `dotnet run` directly:
dotnet run -c Release --project tests/benchmarks/SharpCoreDB.Benchmarks.Comparative -- --pk
dotnet run -c Release --project tests/benchmarks/SharpCoreDB.Benchmarks.Comparative -- --pk-default
dotnet run -c Release --project tests/benchmarks/SharpCoreDB.Benchmarks.Comparative -- --pk --engine=pagebased
dotnet run -c Release --project tests/benchmarks/SharpCoreDB.Benchmarks.Comparative -- --multirowinsert
dotnet run -c Release --project tests/benchmarks/SharpCoreDB.Benchmarks.Comparative -- --pk-profile --engine=pagebased
dotnet run -c Release --project tests/benchmarks/SharpCoreDB.Benchmarks.Comparative -- --gate
dotnet run -c Release --project tests/benchmarks/SharpCoreDB.Benchmarks.Comparative -- --write-baseline
```

Known flags: `--pk`, `--pk-default`, `--pk-ab`, `--dual-mode`, `--multirowinsert`, `--pk-profile`,
`--engine=pagebased`, `--gate`, `--gate-factor=X`, `--gate-baseline=<path>`, `--write-baseline`.

Harness-only env switches (used by the §9 experiments; set them explicitly and never let them leak
between runs): `SHARPCOREDB_MAIN_FIXEDWIDTH`, `SHARPCOREDB_INLINE_BYTES`, `SHARPCOREDB_HASH_INDEXES`,
`SHARPCOREDB_MAIN_PROFILE_UPDATE`, `SHARPCOREDB_WAL_DURABILITY`, `SHARPCOREDB_BUFFERED_APPENDS`,
`SHARPCOREDB_MULTIROW_ROWS`.

## 3. Measurement protocol (non-negotiable)

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
allocation and a **total-footprint wash** (the arena's per-block framing leaves as the record grows; 32 is past the knee
at +20 % disk for no speed). That is a policy choice about every new table's record size — pinned by
`Default_InlineCapacity_IsPinned_AndZeroKeepsTheHistoricalLayout`, and worth nothing on schemas whose values are shorter
than the capacity (a table of 5-character codes grows ~27 % in record size for no arena write saved) — so it is recorded
as an **owner decision, not landed**. The correctness blocker the CHANGELOG named is gone: the capacity is persisted and
restored on reopen in both storage modes (verified by reading both paths). **Even the flip leaves INSERT at ~0,84×, so
decision 4 still needs more than the capacity.** Tables: the plan's §4b extension under §9 priority 2 and the WORKLOG's
session-3 entry.


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

- The reconciliation is done; the 6.2× between the fair-PK arm and the PK-less arm is **real but not
  yet explained**. Four plausible readings have already failed their control runs.
- **Durable output:** priority 1 is now a **count-based** attribution of `UpdateMultiple`'s
  per-operation work (not time-based). Use `SHARPCOREDB_MAIN_FIXEDWIDTH`, `SHARPCOREDB_INLINE_BYTES`,
  `SHARPCOREDB_HASH_INDEXES`, `SHARPCOREDB_MAIN_PROFILE_UPDATE`; compare **call counts and allocation**
  across arms, not times.
- The eventual fix is a **new capability** (not a gate relax): extend in-place patching to
  non-PK-located updates — locate via the hash index, overwrite when the changed field's encoded width
  is unchanged, using `IStorageEngine.TryUpdateInPlaceSameLength` (already exists).
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
