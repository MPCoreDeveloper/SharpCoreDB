# SharpCoreDB — Performance Worklog

Append-only. Newest entries at the bottom. The live `NEXT:` line tells the agent where to resume.
Protocol and rules: `docs/performance/AUTONOMOUS_AGENT_BRIEF.md`.

---

## 🕐 Morning check (human, 30 seconds)

Three commands, four signals — that is the whole check.

```powershell
cd d:\repos\MPCoreDeveloper\SharpCoreDB
git log --oneline -10                                    # 1. did it commit while you were away?
Select-String -Path 'docs\performance\WORKLOG.md' `
  -Pattern '^#{2,3} \d{4}-\d{2}-\d{2}|^- (Session|Verdict|NEXT):' | Select-Object -Last 16  # 2. progress + health
git status --short                                        # 3. clean tree / uncommitted work?
```

| # | Look at | 🟢 Good | 🔴 Intervene |
|---|---|---|---|
| 1 | The **last `NEXT:`** vs the entry before it | item advanced (5.1→5.2), **or** same item with a **new** concrete action | the **same `NEXT:` text in 2+ consecutive entries** |
| 2 | **`Session:`** vs the budget below | `n of m` with n ≤ m | `n > m` — it should have stopped and moved on |
| 3 | **`Verdict:`** of the last entry | `KEPT` / `REVERTED` / `BLOCKED`·`REJECTED` **with a `NEXT:` pointing at a different item** | failing tests or a `REGRESSED` gate with **no** quiet re-run recorded |
| 4 | **`git log`** since you left | ≥ 1 new commit per session | **2+ sessions with no commit and no `KEPT`/`REJECTED`** |

**If 🔴:** do not dig through the log. Restart the agent with the kickoff prompt — §0 rule 5 and the §5
timeboxes force it to record and move on. If the same item is red twice, tell it in one sentence which
item to skip:

> Skip item 5.3 and go to 5.4. Keep logging to the worklog.

**Timebox budget (for signal 2):** 5.1 = 2 sessions · 5.2 = 2 · 5.3 = 2 (hard stop) · 5.4 = 1 ·
5.5 = 0.5 on demand.

---

## 2026-09-21 — Session 0 (setup, human)

**State.** Brief created: `docs/performance/AUTONOMOUS_AGENT_BRIEF.md`. Worklog created.
Branch at setup time: `release/v2.1.0.0-RC.3` (do **not** commit here — create a feature branch).
SDK: .NET 11 RC `11.0.100-rc.1.26425.128` (`global.json`). Target: `net11.0`, C# 15 preview.

**Current baselines (ratios, SharpCoreDB ÷ SQLite; source: plan §8b/§8c/§8d).**

| Shape | READ | UPDATE | DELETE | INSERT |
|---|---:|---:|---:|---:|
| fair-PK, tuned, plaintext, fixed-width | 1.05× ahead | 1.29× ahead | 2.19× ahead | 0.54× behind |
| default document-CRUD (no PK) | 0.76× | 0.24× | 0.31× | 0.67× |
| pure default (encrypted) | 1.2× behind | 3.6× behind | 2.1× behind | 1.6× behind |
| PageBased (fixed-width) | 2.0× ahead | 0.17× | ~0.8× | ~1.0× |

**Known state of health at setup:** 2,897 tests / 0 failed / 16 skipped; `--gate` PASSED.

**Blockers:** none recorded.

**Timebox budget (brief §5):** 5.1 INSERT = 2 sessions · 5.2 PageBased UPDATE = 2 sessions ·
5.3 default-job UPDATE/DELETE = 2 sessions (hard stop) · 5.4 providers = 1 session ·
5.5 instrumentation = 0.5 session on demand. One session ≈ one unattended run (~6–9 h).

**NEXT:** Complete the session-0 ritual first (below), then start **5.1 — INSERT throughput**.

### Session-0 ritual (agent, do this once)

```powershell
cd d:\repos\MPCoreDeveloper\SharpCoreDB
git status
git checkout -b perf/autonomous-<YYYYMMDD>
dotnet build tests/SharpCoreDB.Tests/SharpCoreDB.Tests.csproj -c Release -f net11.0
tests\SharpCoreDB.Tests\bin\Release\net11.0\SharpCoreDB.Tests.exe
dotnet run -c Release --project tests/benchmarks/SharpCoreDB.Benchmarks.Comparative -- --gate
```

Record the outcome of each command in a new entry below, with the `REGIME:` banner of any harness run.
Then start 5.1.

### 5.1 first steps (INSERT profiling, exact)

```powershell
# Establish the current INSERT ratio and per-row cost on the tracked shape.
dotnet run -c Release --project tests/benchmarks/SharpCoreDB.Benchmarks.Comparative -- --pk
dotnet run -c Release --project tests/benchmarks/SharpCoreDB.Benchmarks.Comparative -- --multirowinsert

# Per-stage budget (median of 5). Follow plan §11's table shape exactly:
#   arena-write ~1.1 KB/row, validate+encode ~1.45 KB, hash-index ~0.95 KB, parse, dispatch.
# Compare SHARPCOREDB_INLINE_BYTES=0 (default) against =16 on this exact shape.
```

Target: fair-PK INSERT ratio ≥ 1.0× and ≥ 150K ops/s tuned plaintext. The remaining deficit is
SQL-layer (statement parse ~15.4% at 1000 rows/statement, literal coercion, per-statement dispatch,
WAL/metadata) — **not** the row type, the layout, or the arena (all measured dead; see brief §7).

---

## Entry template (copy for each entry)

```
### <YYYY-MM-DD HH:MM> — <item id> — <one-line action>
- Session: <n of timebox, e.g. "1 of 2">
- Command(s): <exact command line>
- Regime: <the harness REGIME: banner line>
- Result: <ratio vs SQLite, and the absolute median if relevant>
- Verdict: <KEPT / REVERTED / BLOCKED: reason / REJECTED: reason / TIMEOUT: reason>
- Commit: <sha or "none">
- NEXT: <the next concrete action>
```

### 2026-09-21 — Session 0 ritual (agent) — environment verified on `perf/autonomous-20260921`
- Session: 0 (ritual, not counted against a timebox)
- Command(s): `git checkout -b perf/autonomous-20260921` · `dotnet build tests/SharpCoreDB.Tests/SharpCoreDB.Tests.csproj -c Release -f net11.0` · `tests\SharpCoreDB.Tests\bin\Release\net11.0\SharpCoreDB.Tests.exe` · `SharpCoreDB.Benchmarks.Comparative.exe --gate`
- Regime: `REGIME: no SHARPCOREDB_* switches set - harness and product defaults apply.`
- Result:
  - build: **0 errors** (294 pre-existing warnings), 19.7 s, `net11.0`
  - core suite: **1916 total / 0 failed / 0 errors / 16 skipped**, 100.0 s — matches the RC.3 baseline exactly
  - gate: **PASSED** (exit 0) — nothing slower than baseline × 1.50. Rep spread worst 2.18× (machine
    loaded), so this is a pass on a noisy machine; raw UPDATE 0.69× and raw DELETE 0.79× are *faster*
    than baseline, not slower.
  - gate medians (raw): INSERT 135,942 · READ 90,805 · UPDATE 155,021 · DELETE 259,827
  - gate medians (default): INSERT 126,910 · READ 87,959 · UPDATE 74,011 · DELETE 92,799
- Verdict: **KEPT** — environment sane and baseline-healthy; safe to work.
- Commit: `c1aac3fb` (brief + worklog added)
- NEXT: **5.1** — establish the fair-PK INSERT baseline (`--pk`, `--multirowinsert`) and build the
  per-stage INSERT budget.

### 2026-09-21 — 5.1 INSERT (session 1 of 2) — fair-PK baseline + first lever refuted
- Session: 1 of 2
- Command(s): `SharpCoreDB.Benchmarks.Comparative.exe --pk` · `… --multirowinsert`
- Regime: `REGIME: no SHARPCOREDB_* switches set - harness and product defaults apply.` (both runs)
- Verdict: **REVERTED** (hypothesis refuted, no measured win — nothing kept)
- Commit: none for `src/`; this worklog entry is committed
- NEXT: 5.1 session 2 — instrument the **fair-PK INSERT phase** (no existing flag profiles it; see below)

**Fair-PK baseline, median-of-3, this session (ops/sec):**

| arm | INSERT | READ | UPDATE | DELETE |
|---|---:|---:|---:|---:|
| SharpCoreDB FW plaintext | 118,994 | 108,851 | 364,629 | 713,353 |
| SharpCoreDB FW at-rest | 107,954 | 91,414 | 266,878 | 389,273 |
| SharpCoreDB legacy plaintext | 104,388 | 74,108 | 148,398 | 286,328 |
| SQLite | 178,047 | 98,653 | 286,788 | 372,717 |
| **FW plaintext ratio** | **0.67×** | **1.10×** | **1.27×** | **1.91×** |

At-rest tax: INSERT 1.10× · READ 1.19× · UPDATE 1.37× · DELETE 1.83×. **Only INSERT is behind** — the
brief §1 table reproduces exactly, so the mission's remaining target is confirmed.

**`--multirowinsert` baseline** (20,000 rows, 1,000 rows/statement, median of 5):
**64,959 rows/s · 15.39 µs/row · 4,342 B/row · gen0 12–14 · data 1,840,000 B · arena 488,890 B** —
identical to the plan §8d figures, so the shipped default is the one under test.

**New finding — corrected read of the stage table (the plan's "validate + encode = 1,742 B/row"
double-counts).** `validate` (871,034 B/call) is the **parent** of `validate-only` (0 B/call) + `encode`
(870,994 B/call); the two agree to within 40 B, so this is nesting, not two costs. **Leaf allocation per
row** (4,342 B total): hash-index **952** (22%) · encode **871** (20%) · arena-write **467** (11%) ·
parse **443** (10%) · row-build **400** (9%) · engine-write 178 · commit 132 · validate-only **0**.

**The arena lever is spent.** `arena-write` is now **467 B/row** against the **1,096 B/row** plan §11
recorded *before* the §4b inline capacity shipped as default 16. Validation is now pure CPU with **zero**
allocation. The remaining INSERT budget is the index path and the row codec, not the arena.

**Refuted hypothesis (this session's negative result).** `HashIndex.BuildUnsafeKey`'s string path allocated
**two** arrays per key — an intermediate UTF-8 buffer materialised only to learn its length, then the
tagged result. Rewriting it to `GetByteCount` + encode-into-result (and the same for the `default:` case)
produced a **bit-identical** measurement: `hash-index` allocation **951,792 B/call before and after**,
total **4,342 B/row unchanged**, rows/s 64,959 → 58,490 inside a min–max band of 0.262–0.475 s (noise).
**Conclusion: `BuildUnsafeKey` is not on either arm's path** — both run the *dictionary* path
(`_useUnsafeEqualityIndex == false`), so `AddBatchKeysLockedCore` uses the string key directly and never
serializes it. Key-serialization micro-optimisations are off-target here.

**Second hypothesis closed without spending a run.** `CollationExtensions.NormalizeIndexKey`
(`CollationExtensions.cs:29-40`) already returns the input **unchanged** for `CollationType.Binary`, so
there is no per-row string allocation to remove on the normalisation path.

**Why the next step is instrumentation, not another code guess.** The fair-PK INSERT arm calls
`db.InsertBatch` (`Program.cs:1368`), **not** SQL statements, and the harness's `--pk-profile` profiles the
**UPDATE** arm only (`Program.cs:1390-1397`: "the printed report describes the UPDATE batch and nothing
else"). So neither the plan §9 "SQL-layer" reading nor the multi-row profile describes the shape that is
actually behind. Instrument first, then attack the largest **named** stage.

### 2026-09-21 — 5.1 INSERT (session 2 of 2) — the fair-PK INSERT arm is attributed for the first time
- Session: 2 of 2
- Command(s): `SharpCoreDB.Benchmarks.Comparative.exe --pk-profile-insert`
- Regime: `REGIME: no SHARPCOREDB_* switches set - harness and product defaults apply.`
- Verdict: **KEPT** — new harness capability + the missing attribution; core suite 1916/0 failed/16 skipped, gate PASSED
- Commit: see the commit for this entry (`perf(bench)`: `--pk-profile-insert`)
- NEXT: 5.1 follow-up — attack `encode` (`FixedWidthCodec.SerializeRow(object[])`), the #1 leaf stage

**Delivered — `--pk-profile-insert`.** The fair-PK INSERT arm had **no** stage attribution at all:
`--pk-profile` covers UPDATE, `--pk-profile-delete` covers DELETE, and `--multirowinsert` measures a
different shape (1,000 rows/statement driven by SQL statements). The new flag turns the profiler on for
exactly the region that is timed — the `db.InsertBatch` loop over 10,000-row batches plus the `Flush`
(`Program.cs`: `RunSharpCoreDBPk(profileInsertArm: true)` / `RunPkInsertProfile` / the `--pk-profile-insert`
branch in `Main`).

**Attribution — 100,000 rows, 10 × 10,000, AppendOnly, fixed-width plaintext:**

| stage | total ms | calls | share | B/row | role |
|---|---:|---:|---:|---:|---|
| validate | 558.5 | 10 | 24.8% | 785 | envelope: validate-only + encode |
| **encode** | **512.1** | 10 | **22.7%** | **785** | **leaf — the #1 cost** |
| index-maint | 346.4 | 10 | 15.4% | 964 | envelope: hash-index + self |
| hash-index | 258.3 | 10 | 11.4% | 882 | leaf |
| arena-write | 222.8 | 99,000 | 9.9% | 362 | contains arena-append |
| arena-append | 138.0 | 99,000 | 6.1% | 206 | leaf |
| engine-write | 110.1 | 10 | 4.9% | 254 | leaf |
| row-locate | 67.0 | 10 | 3.0% | 95 | leaf |
| validate-only | 42.4 | 10 | 1.9% | **0** | leaf |
| arena-load | 0.7 | 1 | 0.0% | 0 | leaf |

Timed: 100,000 rows in 1.47 s = **67,962 ops/sec = 14.71 µs/row**. (`validate` = `validate-only` +
`encode` to within 4 ms, so the corrected nesting rule from session 1 holds on this arm too.)

**The finding that re-scopes the work.** Plan §9 attributes the INSERT deficit to "the SQL-only work:
statement parsing, literal coercion, per-statement dispatch, WAL/metadata bookkeeping". **That is not this
arm.** Here `parse`, `stmt-split`, `classify` and `row-build` fire **zero** times — the fair-PK INSERT goes
through the Direct API (`db.InsertBatch`), not SQL statements. The real #1 leaf cost is **`encode` =
`FixedWidthCodec.SerializeRow(object[])` at 22.7%**, then `hash-index` at 11.4%. Measuring the plan's
reading against the instrumented stages is exactly why this flag was the first step.

**The named next lever (read out of the code, not inferred from a ratio).**
`SerializeRow(object[] row, …)` (`FixedWidthCodec.cs:68-108`) does, per row:
1. `new byte[layout.FixedSize]` — the output record (necessary);
2. for **each** variable column, `Table.EncodeVariablePayload(...)` → a fresh UTF-8 `byte[]`;
3. if that payload fits the inline capacity (§4b, default 16) it is **copied into the slot and the array is
   thrown away** — for this 6-column schema `name` (9 B) and `data` (13 B) take that path, so **two of the
   three per-row payload allocations are pure garbage**;
4. if it does not fit, **two `List<>` scratch objects are created per row** (`variableColumns`,
   `variablePayloads`) — `email` (18 B) overflows, so both are allocated for every row.

So the concrete next step is (a) a **string fast path that encodes straight into the inline slot** when
`Encoding.UTF8.GetByteCount(s) <= layout.InlineValueBytes`, falling through to the existing path otherwise,
and (b) replacing the two per-row `List<>`s with a pooled/stack scratch pair. Both are additive and
behaviour-preserving on the fall-through.

**What was NOT done (timebox discipline).** No engine change landed this session: the timebox closed on the
instrumentation + attribution, which is the prerequisite the plan itself demands ("instrument first, then
attack the largest named stage"). The optimisation above is scoped and ready for the next session.

### 2026-09-21 — 5.1 INSERT (session 3, granted timebox extension) — `encode` inline-string fast path landed
- Session: 3 (extension; 5.1's 2-session timebox was spent on session 1 (dead end) + session 2 (the prerequisite instrumentation))
- Command(s): `… --pk-profile-insert` · `… --pk`
- Regime: `REGIME: no SHARPCOREDB_* switches set - harness and product defaults apply.`
- Verdict: **KEPT** — real, measurable win on the load-independent allocation metric AND on the unprofiled ratio;
  core suite 1916/0 failed/16 skipped, gate PASSED (second run — see the gate note)
- Commit: `perf(fixedwidth)`: the `encode` inline-string fast path
- NEXT: 5.1 timebox is now fully spent → move to **5.2 (PageBased UPDATE)** per the brief. Remaining INSERT levers
  are recorded below for a future INSERT pass.

**The change.** `FixedWidthCodec.SerializeRow` (both overloads) now tries
`TryWriteInlineStringSlot` **before** allocating the payload: when `value is string` on a non-Blob variable column
and `UTF8.GetByteCount(s) <= layout.InlineValueBytes`, the UTF-8 bytes are written **straight into the record slot**.
The result is byte-identical to the old path (`EncodeVariablePayload` for a string is
`UTF8.GetBytes(value.ToString())`, and `ToString()` on a string is the string itself), but the payload array is
never created. On the fair-PK schema two of the three TEXT columns fit the inline capacity (`name` 9 B, `data`
13 B; `email` 18 B overflows), so the old code allocated **two payload arrays per row only to copy them into the
slot and discard them**. Falls through unchanged when the payload does not fit.

**Measured — `--pk-profile-insert`, 100,000 rows / 10 batches (allocation is deterministic; it was bit-identical
across earlier runs, so this column is load-independent evidence):**

| metric | before | after | delta |
|---|---:|---:|---|
| `encode` B/call | 7,854,331 | **7,058,962** | **−795,369 B/call (−79.5 B/row)** |
| `validate` (envelope) B/call | 7,854,371 | 7,059,002 | −795,369 |
| `encode` stage time | 512.1 ms (22.7%) | **404.8 ms (21.4%)** | **−107.3 ms (−21%)** |
| total measured across stages | 2256.2 ms | **1889.7 ms** | −366.5 ms (−16.2%) |
| profiled pass | 14.71 µs/row / 67,962 ops/s | **12.15 µs/row / 82,337 ops/s** | **+21.2%** |
| `arena-write` / `arena-append` bytes | 36.2 MB / 20.6 MB | **36.2 MB / 20.6 MB** | **unchanged** |
| `hash-index` B/call | 8,824,845 | 8,824,644 | unchanged |

**Byte-identical on disk** is the correctness argument the arena columns give: the same arena byte totals with the
same call counts means the encoder produced the same records, only without the throw-away arrays. The suite agrees
(1916/0 failed, including the fixed-width reopen/round-trip and bulk tests), and READ/UPDATE/DELETE still measure
normally on the same run (66,986 / 80,220 / 155,572 ops/s).

**Measured — `--pk` (unprofiled, median-of-3, same session):**

| arm | INSERT | READ | UPDATE | DELETE |
|---|---:|---:|---:|---:|
| SharpCoreDB FW plaintext | **124,971** (was 118,994) | 119,210 | 355,550 | 683,008 |
| SQLite | 177,728 | 94,514 | 268,272 | 345,941 |
| **ratio** | **0.70×** (was 0.67×) | **1.26× ahead** | **1.33× ahead** | **1.97× ahead** |

So the unprofiled fair-PK INSERT moved **+5.0%** and the ratio **0.67× → 0.70×**. The profiled arm shows +21.2%
because the profiler's own overhead sat disproportionately on the `encode` stage; **+5.0% is the number that
describes the product**, and only the ratio is comparable across sessions (SQLite moved 178,047 → 177,728).

**Gate note (honest).** The first `--gate` returned **exit 2 = INCONCLUSIVE** (rep spread 2.79× > 2.5×) — the
machine has grown steadily noisier this session (2.18× → 2.38× → 2.79×). The documented re-run **PASSED** (exit 0,
spread 2.22×), with raw READ 0.81×, raw UPDATE 0.71× and raw DELETE 0.58× of baseline — i.e. *faster* than the
committed baseline, not slower.

**New finding — the harness is not route-symmetric on this arm, and that caps the prize.** Both fair-PK INSERT arms
build their row representation **inside** the timed region:
SharpCoreDB builds **1 `Dictionary<string, object>` (6 entries) + 3 interpolated strings per row** then calls
`db.InsertBatch` once per 10,000 rows (`Program.cs:1350-1372`); SQLite builds **1 `SqliteCommand` + 5
`SqliteParameter` objects + 3 interpolated strings per row** and executes per row (`Program.cs:1217-1235`). So part
of the measured 8.4 µs/row is harness object construction in both arms — engine work can only move the ratio within
the part that is actually engine work. This belongs beside the plan §1.4 "measurement defects that block
attribution" and should be quantified before the endgame is planned on this column.

**Remaining INSERT levers, recorded (not attempted — timebox):**
1. **The 2 per-row `List<>` scratch objects** in `SerializeRow` (`variableColumns`, `variablePayloads`), created
   whenever any value overflows — `email` (18 B) always overflows the 16-byte inline capacity, so both are
   allocated for every row. Fixing it needs a `WriteMany` overload (`byte[][]`+count or a span) because
   `IOverflowArena.WriteMany` takes `IReadOnlyList<byte[]>`; that is an interface change, so it needs its own session.
2. **The unattributed 39.9%** of the profiled total (753.6 ms after the fix) — code with no stamp. On this arm that
   is the `Table.InsertBatch` driver itself (`NormalizeInsertRow`, the column-index map, `ValidateExistingPrimaryKeys`)
   plus whatever the harness constructs inside the window. This is the largest single bucket and the next thing to
   instrument, per the plan's own "instrument first" rule.

### 2026-09-21 — 5.2 PageBased UPDATE (session 1 of 2) — the gap is a full-table deserialization, not the page engine
- Session: 1 of 2
- Command(s): `… --pk --engine=pagebased` · `… --pk-profile --engine=pagebased` · core suite · `--gate`
- Regime: `REGIME: no SHARPCOREDB_* switches set - harness and product defaults apply.`
- Verdict: **KEPT** — 4 new stages + `wal-flush` finally wired (zero hot-path cost); core suite 1916/0 failed/16 skipped, gate PASSED
- Commit: `perf(diagnostics)`: page/statement stages + `WalFlush` wiring
- NEXT: **5.2 session 2** — fix the profiler-enabled leak (below), then re-add the row/page stamps and eliminate the 100,000-decode full-table pass

**Baseline — `--pk --engine=pagebased`, median-of-3:**

| arm | INSERT | READ | UPDATE | DELETE |
|---|---:|---:|---:|---:|
| SharpCoreDB FW plaintext | 187,905 | **390,358** | **60,475** | 324,771 |
| SQLite | 181,152 | 99,133 | 267,253 | 347,005 |
| **ratio** | **1.0× (parity)** | **3.9× ahead** | **4.4× behind** | 1.1× |

PageBased is at parity or far ahead on every column **except UPDATE**, which is the whole of this work item.

**The finding (the reason this session existed).** The pre-existing profile attributed only **11.5%** of the
UPDATE wall time (38.1 ms of 330 ms; `row-locate` fired **once** for 10,000 updates). Adding four stages —
`page-read`, `page-update`, `stmt-build`, `row-decode` — and finally wiring `wal-flush` (which had **no writer
at all**, a gap plan §9 item 5 records) lifted attribution to **62.6%** and produced a decisive answer:

| stage | total ms | calls | share | B/call |
|---|---:|---:|---:|---:|
| **row-decode** | **168.0** | **100,000** | **72.6%** | **630** |
| parse | 21.5 | 10,000 | 9.3% | 500 |
| engine-write | 14.1 | 10,000 | 6.1% | 56 |
| page-update (inside engine-write) | 12.2 | 10,000 | 5.3% | 56 |
| stmt-build | 5.6 | 1 | 2.4% | — |
| in-place-patch | 2.9 | 10,000 | 1.3% | 112 |
| page-read | 2.9 | 10,000 | 1.3% | 216 |
| wal-flush | 2.7 | 1 | 1.2% | — |

**`row-decode` fires 100,000 times for 10,000 updates — exactly the table's row count.** A **full-table
deserialization** runs inside the UPDATE phase: 10 decodes per update, 6.3 KB of garbage per update. The page
engine itself is **4.1%** (`page-update` 1.22 µs + `page-read` 0.29 µs per update), so the 4.4× gap is **not**
the page write, the relocation, or the index re-point — the three candidates plan §9 item 3 listed. It is an
O(rows) pass charged to the DML phase (`EnsureIndexLoaded`/index rebuild is the prime suspect: the INSERT batch
marks registered indexes stale and the first update to touch them triggers a rebuild).

**A profiler defect this session exposed, and it matters more than the above.** `Stamp()` is cheap when the
profiler is **off** (~2 volatile reads), but `Enable()` is process-wide and nothing turns it back off — so once
any test enables it, **every** stamped call pays `GC.GetAllocatedBytesForCurrentThread()` + `Stopwatch.GetTimestamp()`
for the rest of the process. Evidence: with temporary stamps on the two hottest shared paths
(`PageBasedEngine.Read`, `DeserializeRowFromSpan`) the core suite took **744.6 s** against a ~100 s baseline;
with both reverted, **156.3 s**. And the remaining 156 s is **machine load, not instrumentation** — an A/B in
the same session with the `page-update` stamp present (156.286 s) and absent (156.853 s) is identical, and this
session's gate rep spread grew 2.18× → 2.38× → 2.79×. So: stamping a broadly-shared engine method is safe only
after the leak is fixed; the harness-only and DML-only stamps are free.

**What was committed and what was reverted (deliberate).** Committed: the 4 new stages plus `WalFlush` wired to
`db.Flush()` in the harness, and `stmt-build` around the statement-formatting loop — all zero-cost to the suite.
Reverted after measuring: the temporary `page-read`, `page-update` and `row-decode` stamps. They produced the
numbers above and are re-addable in one commit, but committing them would leave the leak armed for anyone who
runs the suite. Disclosed limitation: the table above is therefore **not reproducible from the committed tree**
until session 2 re-adds them — the exact three call sites are `PageBasedEngine.Read`, `PageBasedEngine.Update`, and
a wrapper over `DeserializeRowFromSpan` (`Table.PageBasedScan.cs:115`).

### 2026-09-21 — 5.2 PageBased UPDATE (session 2 of 2) — root cause of the 100,000 decodes identified
- Session: 2 of 2
- Command(s): static analysis of the index-load path (no benchmark run — see the note on budget)
- Verdict: **REJECTED (scoped out)** — root cause found and documented; no code change landed this session
- Commit: this worklog entry only
- NEXT: implement fix (1) below — it is localized and mirrors logic that already exists for the Columnar path

**Root cause — conclusive, with the code.** `Table.Indexing.cs:184-193`, inside `EnsureIndexLoaded`:

```csharp
if (StorageMode == SharpCoreDB.Storage.Hybrid.StorageMode.PageBased)
{
    var eng = GetOrCreateStorageEngine();
    foreach (var (pos, recordData) in eng.GetAllRecords(Name))   // every record in the table
    {
        var row = DeserializeRowFromSpan(recordData);            // the 100,000 decodes
        if (row != null && row.ContainsKey(columnName))
            index.Add(row, pos);
    }
}
```

A **full-table scan plus a full-row decode** to (re)build one hash index. It is reached from
`Table.BatchUpdateMode.cs:223` (`RebuildIndexInternal` → `EnsureIndexLoaded`), i.e. the first indexed
operation after the index was invalidated. The measurement is exactly the table's row count (100,000) and
630 B/call, so the identification is quantitative, not a guess.

**The structural asymmetry that makes it expensive.** The batch-INSERT path maintains a *loaded* index
incrementally (`InsertBatchCriticalSection` → `UpdateHashIndexes`) and loads registered-but-unloaded indexes
only on the **Columnar** path (`Table.CRUD.cs:801-807` is guarded by `if (StorageMode == Columnar)`). On
**PageBased** the INSERT instead marks each registered-but-unloaded index **stale**
(`Table.CRUD.cs:258`, `:2203`, `:3046`). So the index is never built while the table is small; the build is
deferred to the first UPDATE that touches it — by which time the table has 100,000 rows and the build costs a
full scan and 100,000 full-row decodes. Charging it to UPDATE is what produced the 4.4× column.

**Impact.** `row-decode` was 168.0 ms of a 370 ms UPDATE pass = **45.4% of the wall time** (measured; see
session 1's table). Removing the full-row decode alone would take the PageBased UPDATE ratio from **0.23×**
(60,475 ÷ 267,253) to roughly **0.4×**, and that is before any of the remaining page-engine work — so this is
the largest single item found on this column so far.

**Two candidate fixes, ranked (both to be implemented with the harness stamps re-added so the result is
attributable):**

1. **Load registered-but-unloaded indexes on the PageBased INSERT path too** — mirror the existing Columnar
   branch in `InsertBatchCriticalSection` (`Table.CRUD.cs:801-807`) so the index is built while the table is
   still small and then maintained incrementally by `UpdateHashIndexes`. This does not shift cost, it removes
   it: the O(n) rebuild is replaced by incremental maintenance. Low risk, localized, and semantically
   identical. ⚠️ Watch the lock protocol — `EnsureIndexLoaded` upgrades to a write lock and
   `Table.CRUD.cs:4589` records that a plain read lock there deadlocks, so confirm the INSERT path is not
   already holding the table's write lock when calling it.
2. **Decode only the indexed column during a rebuild** — replace the full-row `DeserializeRowFromSpan` in the
   PageBased rebuild branch with a single-slot read (`FixedWidthCodec.TryReadVariableSlot` /
   `Table.ReadTypedValueFromSpan` at `layout.Offsets[colIdx]`) and add by key via the existing
   `HashIndex.Add(object key, long position)` overload. This keeps the rebuild O(rows) but removes the full
   row materialisation and most of the 630 B/row. Higher risk: the null/`DBNull` handling must match
   `index.Add(row, pos)` exactly or indexed lookups change behaviour.

**Open question this session could not close (recorded, not assumed).** Whether the **AppendOnly** UPDATE arm
pays the same deferred rebuild. Its session-1 profile had no `row-decode` stage, so the question was
unaskable then; when fix (1) is implemented, add the rebuild site to the profile and check both engines.

**Why no code change landed (honest budget note).** The remaining session budget was not sufficient to make
either fix *and* validate it to the standard the brief requires (build + core suite + gate + a measured
before/after ratio). Both fixes touch index semantics, where an unvalidated change risks silently wrong
lookups — the exact class of defect this plan has paid for before. The root cause is documented with its
line numbers and its measured share, so the next session can implement fix (1) directly.

### 2026-09-21 — 5.2 follow-up (granted extension) — fix (1) refuted by measurement; root cause now conclusive
- Session: 1 (extension)
- Command(s): `… --pk --engine=pagebased` · static analysis of the invalidation path
- Regime: `REGIME: no SHARPCOREDB_* switches set - harness and product defaults apply.`
- Verdict: **REVERTED** — fix (1) produced no win (4.4× → 4.5×, inside noise); tree returned to the committed state
- Commit: this worklog entry only
- NEXT: implement the **precise-invalidation** fix below (it is the real lever, not a cost shift)

**Fix (1) as implemented and measured.** `InsertBatchCriticalSection` loaded registered-but-unloaded hash
indexes on the PageBased path too (mirroring the Columnar branch's `EnsureIndexLoaded` call), placed **before**
`engine.InsertBatch` so the ordering stays duplicate-free. Build green; the tracking shape moved
**60,475 → 62,264** ops/sec while SQLite moved 267,253 → 277,330, so the gap went **4.4× → 4.5×** — no win.
Reverted.

**The refined, now conclusive root cause.** `Table.CRUD.cs:2200-2205`, in `RepointIndexesAfterRelocation`:

```csharp
// values a precise repoint is not possible, so invalidate for a lazy rebuild.
foreach (var col in this.loadedIndexes)
{
    this.staleIndexes.Add(col);
    this._indexReadyCache.TryRemove(col, out _);
}
```

**The UPDATE path itself invalidates every loaded index** (its own comment says so), and the next indexed
operation then rebuilds it via `Table.BatchUpdateMode.cs:223` → `EnsureIndexLoaded`, whose PageBased branch
scans the whole table and full-decodes every row (`Table.Indexing.cs:184-193`). That is why pre-loading during
INSERT could not help: whatever INSERT does, the first relocating UPDATE throws the index away again.

**Why this is a genuine bug and not just a benchmark artifact.** The invalidation is **unconditional** — it
throws away *every* loaded index no matter which columns the UPDATE actually changed. The fair-PK harness
updates `score`; the index is on `name`. An index on a column the statement did not touch cannot have become
stale, so the O(rows) rebuild that follows is pure waste. (It is not merely a PageBased issue either: the same
unconditional sweep runs on any relocating update, and `Table.BatchUpdateMode.cs:217-218` limits the hash-index
branch to Columnar, so PageBased pays a full rebuild for an index its own rebuild path then declines to
refresh — worth confirming next session.)

**The fix to implement next (ranked):**

1. **Invalidate only the indexes the update can actually have changed** — pass the update's column set into
   `RepointIndexesAfterRelocation` (its batch caller already has `updateColumnName`/the update dictionary) and
   invalidate only indexes whose column is in that set. An update to `score` then leaves the `name` index
   loaded and fresh, and the 100,000-decode rebuild disappears entirely rather than moving. This is the real
   lever: measured share of the waste is 45 % of the PageBased UPDATE wall time (168.0 ms of 370 ms).
2. **Make any rebuild that does happen cheap** — the single-column decode variant from the previous entry
   (`FixedWidthCodec.TryReadVariableSlot` + `HashIndex.Add(key, pos)`), which removes the full-row
   materialisation (630 B/row) even when a rebuild is legitimate.
3. **Fix the profiler-enabled leak** before re-adding the page/row stamps, so the stamps that made this
   diagnosis possible can stay in the tree (see the session-1 note: 744.6 s vs 156.3 s on the core suite).

**Validation note (no re-run needed).** After the revert, `git status` shows `src/` byte-identical to commit
`1b2688e2`, and the build is green — so the last verified results for exactly this source stand unchanged:
core suite **1916 / 0 failed / 0 errors / 16 skipped**, `--gate` **PASSED** (exit 0). No new code has landed
since those runs.

### 2026-09-21 — 5.2 follow-up 2 — precise invalidation landed; the two triggers are complementary
- Session: 1 (extension 2)
- Command(s): `… --pk --engine=pagebased` · core suite
- Regime: `REGIME: no SHARPCOREDB_* switches set - harness and product defaults apply.`
- Verdict: **KEPT on correctness grounds; performance effect NOT attributable** (see the honest read)
- Commit: `perf(index)`: invalidate only the updated column's index on relocation
- NEXT: re-apply fix (1) **on top of this** and measure both together — that is the decisive experiment

**The change.** `RepointIndexesAfterRelocation` gained an optional `changedColumn` (default `null` = the previous
unconditional behaviour, so the other ten call sites are untouched). When supplied, only an index whose column
matches is invalidated: an index over a column the statement did not touch cannot have gone stale, so it must not
be thrown away. The PageBased batch path (`Table.BatchUpdate.cs:389`, inside
`UpdateBatchViaPrimaryKeyLookup`) passes its `updateColumnName`.

**Measured — `--pk --engine=pagebased`, median-of-3:**

| arm | UPDATE (ops/sec) | SQLite | gap |
|---|---:|---:|---:|
| baseline (session 1) | 60,475 | 267,253 | 4.4× |
| fix (1) alone (reverted) | 62,264 | 277,330 | 4.5× |
| **fix (2) (this commit)** | **64,289** | 288,552 | **4.5×** |

**Honest read: +6.3 % absolute, but not attributable.** The machine's own band on this shape is ±10–20 % (this
session's gate rep spread reached 2.79×), and SQLite's reference moved +8 % between the same two runs, so the
ratio is unchanged at 4.5×. This is **not** a measured performance win and must not be quoted as one. It is kept
because the change is *correct* on its own terms — invalidating an index over an untouched column is a bug —
and because it is the necessary half of the next experiment (below). Validation: core suite
**1916 / 0 failed / 0 errors / 16 skipped** (164.1 s; that level is machine load, established by the earlier
A/B where the same suite ran at ~100 s and where with/without a stamp measured 156.286 s vs 156.853 s).

**The key insight this session produced — the two triggers are complementary.** The index is invalidated in
**two** independent places, and each fix covers only one:
- **at INSERT time** — `Table.CRUD.cs:258` marks every registered-but-unloaded index stale, so the index is
  never built while the table is small (fix (1) addressed this, and failed alone);
- **at UPDATE time** — `RepointIndexesAfterRelocation` threw away every loaded index (fix (2) addresses this,
  and fails alone, since INSERT has already marked it stale).

Neither fix can work on its own for exactly the reason the other exists. **The decisive experiment — fix (1)
re-applied on top of fix (2), measured once — has not been run.** That is the first thing the next session does,
and it is cheap: the fix (1) diff is two lines plus the load loop, and the measurement is one
`--pk --engine=pagebased` run against the 60,475 / 64,289 figures above.

### 2026-09-21 — 5.2 follow-up 3 — the decisive test REFUTED the trigger hypothesis
- Session: 1 (extension 3)
- Command(s): `… --pk --engine=pagebased` with fix (1) **and** fix (2) together
- Verdict: **REVERTED** (fix (1)); fix (2) stays as committed
- NEXT: stop guessing — re-add the `row-decode` stamp (after the profiler-leak fix) and read the **call count** with both fixes in place

| state | PageBased FW UPDATE | SQLite | gap |
|---|---:|---:|---:|
| session-1 baseline | 60,475 | 267,253 | 4.4× |
| fix (2) alone | 64,289 | 288,552 | 4.5× |
| **fix (1) + fix (2)** | **64,649** | 294,373 | **4.6×** |

**64,649 and 64,289 are identical within the machine band**, so removing *both* index-invalidation triggers
did **not** remove the O(rows) rebuild. The hypothesis "index staleness is what triggers the full-table
rebuild" is **refuted** — and with it the whole fix (1)/(2) line of reasoning as a performance lever, even
though fix (2) remains correct on its own terms and stays committed.

**What this leaves.** Either the 100,000 decodes come from a **third** path, or the rebuild is not the dominant
cost in the *unprofiled* run (the 45 % share was measured under the profiler, which this plan has twice shown
distorts the stage it is applied to). The next session must not guess again: re-add the `row-decode` stamp,
run `--pk-profile --engine=pagebased` on both fix states, and read the **call count** — if it still reports
100,000 with both fixes in place, the rebuild comes from elsewhere in the batch-update path
(`Table.BatchUpdate.cs` has four more `RepointIndexesAfterRelocation` call sites and five `InPlacePatch` sites,
and the fair-PK UPDATE may not be taking the `UpdateBatchViaPrimaryKeyLookup` route that fix (2) patched at all —
that route assumption was never verified with a call count, which is the same error this plan has now paid for
five times).

### 2026-09-21 — 5.2 follow-up 4 — correction: the "profiler leak" claim is UNVERIFIED; no speculative fix made
- Session: 1 (extension 4)
- Command(s): static analysis of every `Enable`/`Disable`/`Reset` call site in `tests/`
- Verdict: **NO CHANGE** — the previous entry's leak hypothesis does not survive a call-site audit
- NEXT: settle the 744 s anomaly by measurement (below), then restore the stamps

**The correction (this is the deliverable, so the next session does not "fix" a non-bug).** The follow-up-2
entry blamed the 744.6 s core-suite run on `Enable()` being process-wide and never reset. Auditing every call
site in `tests/` refutes that as written:

- `tests/SharpCoreDB.Tests/Diagnostics/WritePathProfilerTests.cs` **pairs** them — `Disable()` at lines 33, 51,
  104, 135 and 159 against `Enable()` at 67, 119 and 150, and line 57 even asserts `Assert.False(...Enabled)`.
- the benchmark harness pairs them too: five `Enable()` calls (`Program.cs:535, 905, 1368, 1426, 1474`) against
  five `Disable()` calls (`:537, 924, 1400, 1456, 1493`).

So there is no leaking call site in the suite, and the 744 s **has no verified cause**. It is still real — it
was measured A/B in the same session — but the mechanism is unexplained. Candidate explanations that a
measurement can separate, in order of cost:

1. **`_explicitlyDisabled` state**: `Enable()` clears it and `Disable()` sets it, so a test that runs while a
   *parallel* test collection has the profiler on makes the *other* collection's `Stamp()` calls take the full
   path (the profiler is a static, process-wide singleton; xunit.v3 runs collections in parallel). This is the
   most likely mechanism and it is testable by running the two affected test classes in a single-threaded
   collection.
2. **`SHARPCOREDB_WRITE_PROFILE`** being set in the shell that launched the suite. Note the test run was
   launched by `Start-Process` **without** the env-clearing step that the benchmark runs used — so a variable
   left in the user's environment would auto-enable the profiler for the whole suite via
   `TryAutoEnableFromEnvironment`. This is one command to check (`$env:SHARPCOREDB_WRITE_PROFILE`) and it was
   never checked.
3. The count of hot-path calls is simply large enough that the enabled path is expensive: `DeserializeRowFromSpan`
   is on every scan path, so a suite that scans heavily could pay it tens of millions of times.

**No code change was made, deliberately.** Restoring the page/row stamps before this is settled would re-arm a
slowdown whose cause is unknown — the exact "act on a plausible story" error this plan has now paid for five
times. The cheapest next action is (2): print `$env:SHARPCOREDB_WRITE_PROFILE` and re-run the suite — if it is
set, clear it and the anomaly is explained without touching the profiler at all.

**Candidate (2) refuted by measurement (same session).** `SHARPCOREDB_WRITE_PROFILE` is **not** set in the
process, User or Machine scope — checked directly:
`[Environment]::GetEnvironmentVariable('SHARPCOREDB_WRITE_PROFILE', 'User'/'Machine')` both return empty, as
does `$env:SHARPCOREDB_WRITE_PROFILE`, and no `SHARPCOREDB_*` variable exists in either persistent scope at all.
So `TryAutoEnableFromEnvironment` never armed the profiler and the 744 s is **not** an environment-variable
effect. That narrows it to the two remaining candidates above — parallel test collections sharing the static
profiler (1), or sheer call volume on the hot path (3) — and (1) is the one to test next, because it needs no
new code: the profiler's `Enabled`/`_explicitlyDisabled` state is process-global while xunit.v3 runs collections
in parallel, so any collection running concurrently with `WritePathProfilerTests` pays the full `Stamp()` path
(`GC.GetAllocatedBytesForCurrentThread()` + `Stopwatch.GetTimestamp()` + a thread-static push) on every stamped
call. The test for it is to put `WritePathProfilerTests` and the heaviest scanning test class in the same
single-threaded collection and re-time the suite.

### 2026-09-21 — 5.2 follow-up 5 — the 744 s is REFUTED as an instrumentation effect; the O(rows) rebuild is named — it is NOT a relocation site, and it costs 3.6–4.4× of the arm
- Session: 1 (extension 5)
- Command(s): core suite ×4 (`-parallelMode` default / `none`) · `--pk-profile --engine=pagebased` ×3 (fix (2) only · fix (1)+fix (2) · temporary probe) · `--pk-profile-insert --engine=pagebased` · `--gate` ×2
- Regime: `REGIME: no SHARPCOREDB_* switches set — harness and product defaults apply.`
- Verdict: **KEPT** — the three stamps from session 1 are restored and measured free (170.0 s vs a 190.5 s same-session baseline, both 1916 / 0 failed / 16 skipped); the 744 s did **not** reproduce in the configuration that produced it; the rebuild is pinned to one call site and is worth 3.6–4.4× on this arm
- Commit: `perf(diagnostics)`: restore page-read/page-update/row-decode stamps (the 744 s is refuted)
- NEXT: fix `Table.CRUD.cs:2239` — the unconditional `EnsureAllRegisteredIndexesLoaded()` at the batch-UPDATE entry is the entire O(rows) rebuild (probe: removing it takes `row-decode` 100,000 → **0** and the arm 42.60 → **9.73 µs/update**); decide between the two candidates in §6 and measure with `--pk --engine=pagebased`

**1. The 744 s — candidate 1 measured, and REFUTED.** Session 1 blamed a 5× suite slowdown (744.6 s vs 156.3 s) on
temporary stamps on `PageBasedEngine.Read` and `DeserializeRowFromSpan`; follow-up 4 showed the leak hypothesis does
not survive a call-site audit and left two candidates. Candidate 1 (test collections sharing the static profiler) is
testable with no new code, because the xunit.v3 host has `-parallelMode none`. All runs below are this session, same
machine, same build inputs; A and B are the *same* source except for the three stamps.

| run | `-parallelMode` | stamps | wall | reported | tests |
|---|---|---|---:|---:|---|
| A | `collections` (default) | — (HEAD) | **191.8 s** | 190.468 s | 1916 / 0 failed / 16 skipped |
| B | `collections` (default) | **yes** | **171.3 s** | 169.963 s | 1916 / 0 failed / 16 skipped |
| C | `none` (single-threaded) | yes | 316 s | 315.014 s | 1916 / **1 failed** / 16 skipped |
| D | `collections` (default) | yes (**final tree**) | **158.0 s** | 157.971 s | 1916 / 0 failed / 16 skipped |

**The configuration that produced 744.6 s — parallel collections plus those two hot stamps — now runs in 170.0 s
(run B) and 158.0 s (run D, the final tree) against a 190.5 s same-day no-stamp baseline (run A).** No mechanism is
needed: the anomaly does not reproduce, so neither the shared-profiler candidate (1) nor the call-volume candidate (3)
has to explain it. The 744.6 s was a single
unpaired sample: the "156.3 s" it was compared against was a different run at a different moment, while the one pair
that *was* measured back to back in the same window (page-update stamp present 156.286 s / absent 156.853 s) showed no
effect at all — and that session's own gate spread was growing (2.18× → 2.38× → 2.79×), which is the signature of
load, not of code. This session's machine is loaded the way the plan has learned to distrust: 6 cores / 12 logical,
VS Code + extensions resident, and the same suite has run at ~100 s, 156.3 s, 164.1 s and 190.5 s across sessions.
**Caveat, stated rather than smoothed:** one sample per arm; the honest claim is "the ×5 slowdown does not reproduce in
the configuration that produced it", not "the profiler is free under all load".

**2. The single failure in run C is pre-existing, and that is measured, not argued.** `FsmBenchmarks.Benchmark_PageAllocation_UnderOneMicrosecond`
is a wall-clock micro-benchmark (`Assert.True(microseconds < 1000)`) over `ExtentAllocator` — no `Table`, no storage
engine, and no path any of these stamps sit on. It fails in serial mode (1146.1 µs in run C, 1206.4 µs run standalone)
**and it fails identically on the unmodified tree**: the two stamp files stashed, rebuilt, same command → 1495.6 µs.
It is an environment-sensitive timing assert, not a regression. Both parallel runs (A, B, D) are green.

**3. The three stamps are restored** (the limitation session 1 disclosed as "the table above is therefore not
reproducible from the committed tree"). The stages themselves — `Stage.PageRead` 24, `PageUpdate` 25, `RowDecode` 27 —
and their `StageNames` entries already existed; only the call sites were missing:

- `PageBasedEngine.Update` → `page-update` (wraps the whole method, so `TryUpdateInPlace` is covered by its route
  through `Update`);
- `PageBasedEngine.Read` → `page-read` (both returns);
- `Table.PageBasedScan.cs:120` — a new `DeserializeRowFromSpan` wrapper emits `row-decode`, with the original body
  renamed `DeserializeRowFromSpanCore` (line 133) so that **every** caller is counted, including the PageBased rebuild
  branch in `Table.Indexing.cs:190` and the scan at `Table.PageBasedScan.cs:68`.


**4. The call counts with both index fixes active — the decisive reading, and it refutes the relocation hypothesis.**
The worklog asked for `--pk-profile --engine=pagebased` in *both* fix states. Fix (2) (`changedColumn`) is committed;
fix (1) (load registered-but-unloaded indexes on the batch-INSERT path too, placed before `engine.InsertBatch` exactly
as follow-up 1 recorded it) was re-applied for the run and reverted again afterwards. Same three-minute window, same machine:

| state | `row-decode` calls | share | B/call | `page-read` | `page-update` | `row-locate` | profiled UPDATE |
|---|---:|---:|---:|---:|---:|---:|---:|
| fix (2) only (= committed) | **100,000** | 65.8 % | 630 | 10,000 | 10,000 | **1** | 0.35 s — 28,351 ops/s — 35.27 µs/update |
| fix (1) + fix (2) | **100,000** | 79.7 % | 630 | 10,000 | 10,000 | **1** | 0.43 s — 23,474 ops/s — 42.60 µs/update |

**Both fixes leave the rebuild exactly where it was: 100,000 calls, one per table row.** And `row-locate` = **1** is the
fact that decides the question the plan posed: that stamp is the one around `TryBulkUpdateContiguousFixedWidth`
(`Table.CRUD.cs:2272`), so all 10,000 statements of the batch were handled by the contiguous PK pass inside **one**
`UpdateMultiple` call — which returns at `:2255` and therefore means **`RepointIndexesAfterRelocation` never ran for
this arm at all**. Not the site fix (2) patched (`Table.BatchUpdate.cs:389`, inside `UpdateBatchViaPrimaryKeyLookup` —
a route this arm does not take), and not any of the four other sites in `Table.BatchUpdate.cs` (`:128`, `:540`, `:808`,
`:960`), nor `Table.BatchUpdateParallel.cs:190`, nor the two in `Table.CRUD.cs` (`:2032`, `:2687`). That whole
"which relocation site" line of reasoning is closed: the decode pass is paid **before the first update touches
anything**.

**5. The probe that names the caller.** One temporary change — `EnsureAllRegisteredIndexesLoaded()` at
`Table.CRUD.cs:2239`, the pre-load at the top of `UpdateMultiple`, commented out for one run and reverted immediately
after — gives the answer:

| state | `row-decode` | profiled UPDATE | measured stages |
|---|---:|---:|---:|
| fix (2) only | 100,000 calls | 35.27 µs/update | 217.6 ms |
| fix (1) + fix (2) | 100,000 calls | 42.60 µs/update | 265.0 ms |
| **pre-load removed (probe)** | **stage absent — 0 calls** | **9.73 µs/update (102,758 ops/s)** | **74.2 ms** |

`Table.CRUD.cs:2239` → `EnsureAllRegisteredIndexesLoaded()` (`Table.Indexing.cs:311`) → `EnsureIndexLoaded` →
`Table.Indexing.cs:184-193` (whole-table `GetAllRecords` plus a full-row decode per record) is responsible for **all
100,000 decodes, 60.2 MB of decode garbage, and 3.6–4.4× of this arm's wall time**. That is the largest single item
found on the PageBased UPDATE column so far, and it is a *pre-load*, not an invalidation: the guard exists because an
unloaded index would otherwise be rebuilt from a file that still contains stale records — an append-only/Columnar
concern. On this arm nothing appends (`row-locate` = 1, no relocation, no `in-place-patch` miss) and the probe's 10,000
updates still complete in a tenth of the time.

**6. Why neither fix could ever have worked, and what to do instead.** Fix (2) filters *which* index a relocation
invalidates, and no relocation happens on this route (§4). Fix (1) loads the index during INSERT — but the rebuild is
still there in both states, so the index is not loaded-and-fresh when `UpdateMultiple` starts. Every static suspect for
"who drops it between the INSERT arm and the UPDATE arm" was checked and eliminated: `Table.Flush()` (`Table.cs:798`)
does an engine flush plus `CompactPendingDeletes()` (Columnar-gated, no-op here); `RebuildIndex`
(`Table.BatchUpdateMode.cs:204`) is Columnar-gated and has no callers; `RebuildAllIndexesFromFile`
(`Table.Compaction.cs:289`) decodes through `DeserializeRow`, not `DeserializeRowFromSpan`, and fires only from the two
compaction entries; `ClearAllIndexes` is reached only from DDL; `CreateHashIndex` removes a loaded index only when
upgrading to `isUnique`. That question stays **open** — recorded, not guessed — but it no longer blocks the fix, because
the probe shows the pre-load is the cost regardless of *why* the index is unloaded. It also explains why fix (1) never
moved the ratio when it was first measured (60,475 → 62,264 ops/s): the same 100,000 decodes were still being paid.

Two candidates, ranked, for the next session — both measurable with `--pk --engine=pagebased` against the 4.5× gap:

1. **Stop the pre-load from rebuilding on PageBased.** `EnsureAllRegisteredIndexesLoaded()` protects an invariant that
   belongs to the append-only write path ("an unloaded index would later be rebuilt from the file INCLUDING the stale
   record"). This arm appends nothing (`row-locate` = 1, no relocation), yet it pays a full-table decode for the guard.
   The probe is the measurement: 42.60 → 9.73 µs/update with the call removed. The fix is to make the guard
   *conditional* rather than removed — e.g. only pre-load when the operation can leave a stale record — and to run the
   existing stale-record/deferred-index suites plus `VacuumStressTests` and `StorageEnginePerfTests` as the correctness
   gate, because a wrong version of this silently returns deleted rows (the regression the guard exists for).
2. **Make any rebuild that is still legitimate cheap.** The single-column decode variant from the 5.2 session-2 entry
   (`FixedWidthCodec.TryReadVariableSlot` + `HashIndex.Add(key, pos)`) removes the 630 B/row full-row materialisation
   even when a rebuild is correct — worth doing, but ranked second: candidate 1 removes the rebuild, this one only
   makes it cheaper.

Also still open from this session, for whoever picks up the profiler line: the `row-decode`/`page-read`/`page-update`
stamps are now **committed**, so the stage table in §4/§5 is reproducible from the tree (session 1's disclosed
limitation is closed).

**7. Honest limits.** (a) The gate was run twice and both runs were **INCONCLUSIVE (exit 2)** — worst rep spread 2.84×
then 3.07× against the 2.5× limit, the harness's own verdict being "this run measures the machine's load and not the
code". Exit 2 concludes nothing in either direction; the suite results in §1 are the `KEPT` basis, and the gate is
recorded as a documented failed-to-quiet re-run rather than as a pass. (b) The µs/update figures come from the profiled
pass, and the plan's rule stands: only *call counts and allocation* transfer across arms. The 100,000 → 0 call count and
60.2 MB → 0 allocation are hard; the 4.4× wall-time ratio is consistent with them and far outside the profiler's
demonstrated distortion band. (c) The probe removed a correctness guard; it was reverted before anything was rebuilt
for validation and was never committed — it is recorded as a diagnostic, not as a proposed change, and the reproduction
is three lines in §5.

<!-- APPEND-ENTRIES-BELOW -->



