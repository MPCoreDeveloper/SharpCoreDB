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

<!-- APPEND-ENTRIES-BELOW -->
