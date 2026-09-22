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

### 2026-09-21 — 5.2 follow-up 6 — PageBased no longer pre-loads indexes before a write: FW UPDATE 4.6× behind → 0.8–1.0× (parity), reproduced in both directions in one session
- Session: 1 (extension 6)
- Command(s): `… --pk --engine=pagebased` ×4 (before · after · after · before) · `… --pk-profile --engine=pagebased` · core suite ×2 · `-class PageBasedIndexRebuildTests` ×2 (fix applied and reverted) · `--gate`
- Regime: `REGIME: no SHARPCOREDB_* switches set — harness and product defaults apply.`
- Verdict: **KEPT** — the pre-load is skipped on PageBased (follow-up 5's candidate 1); the fair-PK PageBased UPDATE gap closes from 4.6× behind to parity, with the profiler's `row-decode` call count going 100,000 → **0**, and the suite is green at 1919 / 0 failed / 16 skipped
- Commit: `perf(index)`: PageBased does not pre-load hash indexes before a write (plan §9 priority 3)
- NEXT: the PageBased UPDATE pass is no longer dominated by the index rebuild — re-read `--pk-profile` (§3) and attack `engine-write` + `page-update` (23 % + 21 % of the stages now); the untested shapes in §5 are the follow-up

**1. The change — one guard, and the invariant that justifies it.** Follow-up 5 pinned the whole O(rows) rebuild to
`Table.CRUD.cs:2239` (`UpdateMultiple`) → `EnsureAllRegisteredIndexesLoaded()` → `EnsureIndexLoaded`. The fix is to stop
that pre-load from rebuilding on PageBased, and it is placed in the guard itself (`Table.Indexing.cs:326`) so that all
five call sites (two UPDATE entry points, three DELETE entry points) inherit it rather than five call sites being
patched independently — the invariant is mode-dependent, not call-site-dependent.

*Why PageBased is exempt, from the code:* the pre-load exists because a rebuild re-reads a **version-bearing** data
file — an append/Columnar UPDATE leaves the superseded record in the file, where a later rebuild still enumerates it.
That is the "stale row returned for the same PK" regression `54b0a5b8` fixed (its message says so, and its regression
test is `FixedWidthPatchTests`). PageBased has no such version to leak: `PageManager.UpdateRecord`
(`PageManager.cs:516-578`) either rewrites the slot in place or moves the slot pointer inside the page — the old bytes
are never enumerated — and when the page is full it marks the old slot `RecordFlags.Deleted` (line 568) and inserts the
record elsewhere; `PageManager.GetAllRecordsInPage` (line 723) yields only slots that are **not** flagged deleted, and
`PageManager.TryReadRecord` (line 668) returns false for them. A PageBased rebuild therefore reads exactly the live
rows, so deferring it is correct rather than merely cheaper. The append/Columnar paths keep the pre-load unchanged.

**2. The measurement — four runs, same session, ratios only.** `--pk --engine=pagebased` twice with the fix and twice
without, so the effect is bracketed in *both* directions on the same machine in one window:

| run | state | FW plaintext UPDATE | FW at-rest UPDATE | SQLite UPDATE | FW gap |
|---|---|---:|---:|---:|---:|
| R0 | before (HEAD) | 47,592 | 51,159 | 282,045 | **5.9×** |
| R1 | **after** | 240,032 | 410,598 | 238,874 | **1.0×** |
| R2 | **after** (2nd sample) | 333,407 | 400,761 | 267,871 | **0.8×** |
| R3 | before (fix reverted) | 60,857 | 58,410 | 278,453 | **4.6×** |

The same-session control is tight: SQLite's UPDATE median never left 238.9k–282.0k (1.18× spread) while the FW arm moved
47.6k/60.9k → 240k/333k. R3 reverted to **60,857**, i.e. this shape's historical baseline almost exactly (60,475 /
62,264 / 64,289 / 64,649 → 4.4–4.6× across the earlier sessions), so the win is the code, not a quiet machine. The
other two PageBased arms moved with it (legacy 32,709 → 119,951 / 130,900; FW at-rest 51,159 → ~400k), which is the
expected signature of an engine-wide guard rather than a per-arm tweak.

**3. The call count, which is load-independent.** `--pk-profile --engine=pagebased` after the fix: the `row-decode`
stage is **absent — 0 calls**, against 100,000 in both fix states measured in follow-up 5. The stage totals corroborate
it: 265.0 ms with the rebuild and 217.6 ms without it in follow-up 5, and **88.3 ms** now — 217.6 − 143.2 (the measured
`row-decode` time) ≈ 74, the remaining stages in the same band. Profiled UPDATE: 42.60 / 35.27 µs/update before →
**12.17 µs/update** now. `page-read` (10,000), `page-update` (10,000), `in-place-patch` (10,000) and `row-locate` (1) are
unchanged, so the write path itself did not change shape — only the decode pass disappeared.

**4. Guard tests added.** `tests/SharpCoreDB.Tests/PageBasedIndexRebuildTests.cs` (3 tests) pins the PageBased half of
the invariant, which nothing covered: the existing PageBased tests always call `EnsureIndexLoaded` explicitly first
(`DeleteIndexCleanupTests:93,118`), so the *unloaded* case — exactly the state this no-op leaves behind — was untested.
Each test writes with the index unloaded and then asserts the indexed lookups: an UPDATE of the indexed column
(`UpdateAffectedCount`), a batch UPDATE through `UpdateMultiple` (the gated route), and a DELETE, each checking that the
new value is found and the superseded/deleted one is not. **They pass on the reverted tree as well** (measured, same
session) — they are pins, not a bug finder: the honest claim is that the change is *covered*, not that it fixed a live
defect. `--pk --engine=pagebased` is not a correctness test (it checks no values), so these are what stands behind it.

**5. Honest limits.** (a) Rep spreads inside R1/R2 reach 3.1×, so the *ratio* carries roughly ±50 % error — quoted as
"4.6× behind → parity", not as a precise multiplier; the direction is bracketed by R3 and the call count is exact.
(b) This is a **cost removal only where the index is not read afterwards**: if the same workload later reads by the
indexed column, the rebuild still happens once, lazily, at that read. That is at worst a shift and often a saving (the
lazy path rebuilds one index; the pre-load rebuilt every registered one). (c) Shapes not re-measured with the fix:
`--pk-default`, `--dual-mode`, the at-rest non-PK arms, and the DELETE arms whose entry points also inherit the skip
(`CollectDeleteRecords:3352`, `DeleteMultiple:3501`, `DeleteMultipleKeys:3632`). The DELETE column did move with it in
R1/R2, but that was not an isolated measurement. (d) Still open from follow-up 5, and now the next thing on this
column: `Table.CRUD.cs:2687` calls `RepointIndexesAfterRelocation` without a `changedColumn`, so a **relocating**
PageBased per-row update still invalidates every loaded index — the contiguous fast path this arm takes never reaches
it, so it remains unmeasured; the code path is still there. (e) `--gate` ran green on the re-run but **failed once**:
the first attempt reported `default DELETE 98.725 → 58.681 = 1,68×`, with five of the other seven metrics parked at
~1.25× — the whole-run offset signature of a loaded machine, not of one arm (today's earlier gate attempts were
INCONCLUSIVE at 2.84× and 3.07× rep spread). The immediate re-run passed all eight metrics (worst 1.06×, `default
DELETE` 1.03×). The static argument settles it independently: the gate prints `engine=AppendOnly`, and this change is
PageBased-only, so the gate's arms never execute the new branch — and the PageBased DELETE measurement moved the other
way (183,864 → 269,757 / 297,160 ops/s in R1/R2).

### 2026-09-21 — 5.2 PageBased UPDATE — CLOSED (DoD check; the measurement is follow-up 6's)
- Session: closure record (the item's 2-session budget was spent long ago; follow-up 6 landed the lever)
- Command(s): none new — this entry checks the brief's §5.2 DoD against follow-up 6's measurements
- Verdict: **CLOSED** — 3 of the 4 DoD points met, #1 partially (two per-row regions in that path remain unstamped and are named below with line numbers)
- Commit: `2bc3e947` (the fix), `cad4715c` (the stamps it is measured with)
- NEXT: 5.3 (the entry below)

**DoD check, point by point (brief §5.2):**

1. *"`--pk-profile --engine=pagebased` shows every stage of the PageBased update path with call counts and
   allocation — no unstamped region left in that path"* — **partially met.** The pass now attributes **88.3 ms of a
   120 ms** profiled pass (**73.6 %**), against ~24 % when the item opened. Two regions in that path are still
   unstamped, and they are named with line numbers rather than left as a suspicion: the **per-row locate**
   (`Table.CRUD.cs:2344-2390` — the hash-index lookup plus `engine.Read`, which fires per operation) and the
   **contiguous patch's internals** (`Table.CRUD.cs:2759+`, whose outer `row-locate` stamp fires once for the whole
   batch and therefore cannot see it). Instrumenting them is §5.5 work, not §5.2 work.
2. *"the dominant cost is named with evidence, not inferred from a ratio"* — **met.** `row-decode` = **100,000 calls
   = one per table row**, 630 B/call, 79.7 % of the stamped pass, pinned by a one-line probe to
   `Table.CRUD.cs:2239` → `EnsureAllRegisteredIndexesLoaded()` → `EnsureIndexLoaded`; the fix removes it and the
   count now reads **0**.
3. *"PageBased UPDATE ratio improved to ≥ 0.5× (gap ≤ 2×), or a documented REJECTED"* — **met, exceeded.** Same-session,
   same-machine: **47,592 / 60,857 ops/s (5.9× / 4.6× behind SQLite) → 240,032 / 333,407 (1.0× / 0.8×)**.
4. *"core suite green and `--gate` pass"* — **met.** Core suite **1919 / 0 failed / 16 skipped**; `--gate` failed
   once (`default DELETE 1.68×` inside a ~1.25× whole-run offset) and **PASSED on the immediate re-run** (worst
   1.06×). The gate's arms are `engine=AppendOnly` and this change is PageBased-only, so they never execute the new
   branch — and the PageBased DELETE arm moved the other way (183,864 → 269,757 ops/s) in the same session.

**Remaining delta, recorded rather than chased:** the two unstamped regions in point 1. The item's target is met and
its hard stop is 2 sessions, so they are left as named work for 5.5 (on demand) instead of being instrumented here —
the brief is explicit that an item must not keep being attacked past its timebox.

### 2026-09-21 — 5.3 default-job UPDATE/DELETE (session 1 of 2) — the count-based attribution table exists; three switchable axes are refuted by counts, not times
- Session: 1 of 2
- Command(s): `--dual-mode` ×3 with `SHARPCOREDB_MAIN_PROFILE_UPDATE=1` — baseline, `+SHARPCOREDB_MAIN_FIXEDWIDTH=1`, `+SHARPCOREDB_HASH_INDEXES=0` (the inline-capacity arm was not run: see §3)
- Regime: only the switch named per run; the harness's `[diag]` layout line is quoted per arm
- Verdict: **BLOCKED on instrumentation** — the durable output exists (counts + allocation per stage, per switch), every stamped stage is now accounted for with code evidence, and the remaining 39–61 % is named by line number; no switchable lever survived, and no *fix* is proposed until that remainder is stamped
- Commit: this worklog entry only
- NEXT: 5.5 (on demand) — stamp exactly the two named regions (the per-row locate at `Table.CRUD.cs:2344-2390` and the batch driver's per-statement work around `parse`), re-read the counts, and only then decide between a fix and a documented `REJECTED`

**1. The shape (stated once, because §2 rule 7 applies).** `--dual-mode`'s **default** arm, `engine=AppendOnly`,
`reps=3`: `docs(name TEXT NOT NULL, email TEXT, age INTEGER, score REAL, data TEXT)`, **no PK**, 100,000 rows inserted
via `InsertBatch`, then **10,000 × `UPDATE docs SET score = <literal> WHERE name = 'User<i>'`** in ONE
`ExecuteBatchSQL`, 1 row per statement. `[diag] docs layout: IsFixedWidthRecords=False` on the baseline run.

**2. The count-based attribution (the deliverable).** Calls and allocation only. These runs re-prove the plan's rule
as a side effect: two of them are the *same configuration* (the hash-index switch does not bite — §3) and their
profiled times differ by 1.4× (41.02 vs 28.83 µs/update; 172.2 vs 92.7 ms staged) **while their call counts are
identical**.

| stage | calls | share of staged | alloc MB | B/call | what it is, in the code |
|---|---:|---:|---:|---:|---|
| `commit` | **1** | 41.5 % | 0.5 | 556,568 | the single `db.Flush()`; not per-operation work |
| `index-maint` | **20,000** | 17.4 % | 0.8 | 43 | **two per update**: the hash remove + add pair for the *updated* column (`Table.CRUD.cs:2473-2484`) |
| `engine-write` | 10,000 | 17.3 % | 2.7 | 287 | `engine.TryUpdateInPlaceSameLength` per row (`:2450`) |
| `parse` | 10,000 | 15.1 % | 5.1 | 531 | one parse per statement — the driver does **not** skip the parser |
| `in-place-patch` | 10,000 | 7.4 % | 1.7 | 175 | `TryOverwriteFieldsInPlaceActual` per row (`:2445`) |
| `row-locate` | **1** | 0.7 % | 0.0 | 32 | the single batch contiguous attempt (`:2253`), inapplicable without a PK |
| `classify` | 10,000 | 0.5 % | 0.0 | 0 | per-statement classification |

`arena-write`, `arena-append`, `hash-index`, `validate`, `encode`, `row-build`, `row-decode`, `wal-*` **never fire** on
this route. The default arm's staged total is 172.2 ms against a 410 ms pass (**42 % attributed**); the raw
(plaintext) arm's is 43.1 ms against 110 ms (**39 %**).

**3. What the counts refute — three axes, by counts rather than by times.**

- **Record layout (`SHARPCOREDB_MAIN_FIXEDWIDTH=1`, `[diag] IsFixedWidthRecords=True`): the count structure is
  identical.** `parse` 10,000, `index-maint` 20,000, `engine-write` 10,000, `in-place-patch` 10,000, `classify`
  10,000, `commit` 1, `row-locate` 1 — the same seven rows in the same proportions, per-call allocation in the same
  band (`in-place-patch` 175 → 144 B, `engine-write` 287 → 318 B). The layout moves *bytes*, not *work*.
- **Inline capacity (`SHARPCOREDB_INLINE_BYTES`): provably inert on this shape, so the arm was not run.** The
  fixed-width arm reports **no `arena-write` and no `arena-append` call at all** — with the product default of 16
  inline bytes every value in this schema already fits, so there is no arena half to remove. The switch would have
  produced the same table with a different number in a column that is already empty.
- **Hash index (`SHARPCOREDB_HASH_INDEXES=0`): the switch does not bite.** `index-maint` still fires **20,000** times
  and allocates the *identical* 0.8 MB, because the job registers its index through DDL
  (`CREATE INDEX idx_docs_name ON docs(name)`, and `CREATE TABLE` in Columnar auto-registers a hash index for **every**
  column — `SqlParser.DDL.cs:430-436`), which a config flag cannot undo. That is the trap the plan recorded for the
  multi-row arm, now confirmed here — and it *explains the 2-per-update count without a guess*: `score` is
  hash-indexed (auto-registered), so the update touches a hash-indexed column and pays the remove+add pair per row.

**4. The one structural finding worth carrying forward.** `CREATE TABLE` in Columnar mode registers a hash index on
**every** column (`SqlParser.DDL.cs:421-437`), so a five-column default table carries **five** registered hash
indexes; `EnsureAllRegisteredIndexesLoaded()` loads all five at the first write, and an UPDATE of any single column
then pays two hash operations per row for that column. On PageBased, that same auto-registration is what follow-up 6
stopped paying *up front* — and it is why the count here is 20,000 rather than 10,000. Whether per-column
auto-registration is worth its cost on a PK-less, append-only table is a **separate** question this table raises and
does not answer; it is recorded in NEXT rather than acted on.

**5. Honest limits.** (a) All three runs are profiled passes, so the *times* in them are not comparable to timed
numbers (the plan's rule) — the table's claim is about calls and allocation. (b) The allocation column is stable in
structure and magnitude but **not bit-identical** across runs: `engine-write` measured 287 B/call on the baseline and
302 B/call on the hash-index arm for the same configuration — a 5 % spread. (c) `row-locate` = 1 means the per-row
locate is *not* instrumented at all: the one stamp on this route covers only the batch-level contiguous attempt, so
the 39–61 % unattributed share is exactly where the per-operation work sits — which is why the verdict is `BLOCKED`
rather than `REJECTED`, which the DoD allows only with that work named. (d) `--dual-mode` does not honour
`SHARPCOREDB_BENCH_REPS` (it is read on the `--pk` path only), so each arm above is a 3-rep run. (e) Artifacts, so the
table is traceable: the three runs are `results/dual-mode-20260921_202354.json` (baseline),
`…_202934.json` (fixed-width), `…_202958.json` (hash-index arm) — all committed alongside this entry. The follow-up 6
`--pk` runs wrote to the repo-root `results/` (a git-ignored scratch path the harness uses for that flag), where the
four files are `pk_comparative_20260921_180319.json` (before/R0), `…_180429.json` (after/R1), `…_181028.json`
(after/R2) and `…_181156.json` (reverted/R3).

### 2026-09-21 — 5.5 Instrumentation coverage gaps — the two named regions stamped, and they name the lever 5.3 needed
- Session: 1 (0.5-session item, used on demand exactly as the brief allows)
- Command(s): `--dual-mode` with `SHARPCOREDB_MAIN_PROFILE_UPDATE=1` (after the stamps) · build · core suite · `-class WritePathProfilerTests` · `--gate`
- Regime: `SHARPCOREDB_MAIN_PROFILE_UPDATE=1` only, named here because it is the vehicle; the harness's `REGIME:` banner and the `[diag]` layout line are quoted in §2
- Verdict: **KEPT** — both stamps answer the question they were added for (§5.3's "what is `UpdateMultiple`'s per-operation work"), the profiler compiles and runs, no unrelated stage or path changed, core suite 1919 / 0 failed / 16 skipped, and the two new stages cost nothing when the profiler is off
- Commit: `perf(diagnostics)`: stamp the UPDATE route's per-row locate and whole-file snapshot (plan §5.5)
- NEXT: 5.3 session 2 — the named lever is the *locate*, and its mechanism is `TryLoadWholeFileForRowAccess`'s encryption guard (`Table.CRUD.cs:4132-4134`); two candidates in §4

**1. What was added, and nothing else.** Two stages — `Stage.RowSnapshot = 28` / `"row-snapshot"` and
`Stage.RowLocateIndex = 29` / `"row-locate-index"` (`WritePathProfiler.cs`, `StageCount` 28 → 30) — with exactly two
stamp sites, both in `UpdateMultiple`:

- `row-snapshot` around `TryLoadWholeFileForRowAccess()` (`Table.CRUD.cs:2248-2250`) — one call per batch UPDATE on
  this route, and it allocates the entire data file, so a per-operation cost was carrying batch-level invisibility;
- `row-locate-index` around the per-operation hash-locate block (`Table.CRUD.cs:2348-2402`) — the registered-index
  lookup plus the record read/slice. It is a **new** stage rather than an extra `row-locate` stamp on purpose: the
  existing `row-locate` reading is the *batch-level* contiguous attempt (one call), and folding 10,000 per-row calls
  into it would have destroyed the count that §5.2 follow-ups used as evidence.

The region that looked like the second gap — "the batch driver's per-statement work" — turned out to be **already
stamped**: `Database.Batch.cs:1028-1073` wraps the whole per-statement classification/parse loop
(`IsInsertStatement`, `TryParseUpdateForBatch`, the `updates` dictionary build) in one `parse` stamp per statement, and
`classify` comes from `SqlParser.DML.cs:116,141`. Reading that before stamping is why this entry adds two stages and
not three.

**2. What the new stages say.** Same shape as §5.3 (default arm of `--dual-mode`, `engine=AppendOnly`, PK-less
`docs`, 10,000 × `UPDATE docs SET score = … WHERE name = 'User<i>'`, `[diag] IsFixedWidthRecords=False`), calls and
allocation only:

| stage | default (encrypted) arm | raw (plaintext) arm |
|---|---|---|
| **`row-locate-index`** | **10,000 calls · 52.4 % of staged (rep 1) / 72.7 % (rep 3) · 487 / 431 B/call** | 10,000 calls · 14.2 % / 17.6 % · 279 B/call |
| **`row-snapshot`** | **1 call · 0.1 % · 0 B** | **1 call · 6.3 % / 12.7 % · 9.9 MB (10,366,792 B in ONE call)** |
| `commit` | 1 call · 14.2 % · 0.5 MB | 1 call · 16.6 % · 0.7 MB |
| `engine-write` | 10,000 · 9.4 % · 302 B/call | 10,000 · 9.9 % · 94 B/call |
| `parse` | 10,000 · 9.3 % · 531 B/call | 10,000 · 19.8 % · 531 B/call |
| `index-maint` | 20,000 · 8.2 % · 43 B/call | 20,000 · 16.9 % · 43 B/call |
| `in-place-patch` | 10,000 · 5.5 % · 175 B/call | 10,000 · 5.3 % · 175 B/call |
| `row-locate` (batch attempt) | 1 · 0.5 % | 1 · 0.0 % |

Attribution of the profiled UPDATE pass rises from **39–42 %** (§5.3) to **73–77 %** on the encrypted arm and
**29–58 %** on the plaintext arm.

**3. The finding, and it is a mechanism rather than a share.** `TryLoadWholeFileForRowAccess`
(`Table.CRUD.cs:4132-4149`) returns `null` when the records are **encrypted**
(`this.storage.AreRecordsEncrypted(DataFile)`), so on the *default* (encrypted) arm there is no snapshot at all
(0 B, one no-op call) and every update takes the `engine.Read(Name, pos)` fallback — which is exactly the
`row-locate-index` column: **487 B/call and 52–73 % of the staged time on the arm that is 1.67× behind its own
plaintext twin** (dual-mode summary: raw UPDATE 106,339 vs default 63,636 ops/s). On the plaintext arm the snapshot
*is* taken, and it is a visible one-call cost of **9.9 MB** (6.3–12.7 %). So the same 10,000 locates are served two
different ways, and the slower way is the one the product's default configuration uses.

**4. What this gives 5.3 session 2 — three candidates, ranked, all from the counts.** §5.3's durable output now
exists: `UpdateMultiple`'s per-operation work on this route is **the locate**, then the record write, then the parse,
then index maintenance — not the record layout, the inline capacity, the hash-index config or the re-serialisation the
plan's first reading suspected.

1. **Serve the encrypted arm's per-row locate from ONE range read with in-memory decryption**, mirroring what
   `TryBulkUpdateContiguousFixedWidth` already does for encrypted fixed-width records ("an encrypted ... record keeps a
   constant physical stride ..., so the span is still read in ONE range and each payload is decrypted in memory",
   `Table.CRUD.cs:2752-2755`). This is a **capability**, not a gate relax, and it targets 52–73 % of the arm that is
   1.67× behind its plaintext twin. ⚠️ It cannot be done by simply relaxing the snapshot's guard: the guard exists
   because `TryOverwriteFieldsInPlaceActual` needs **plaintext** field offsets, so feeding it raw ciphertext would
   corrupt records — the decryption has to happen per record *inside* the new path, exactly as the contiguous path
   does it.
2. **Cut the locate's per-call allocation** — 487 B/call (encrypted) / 279 B/call (plaintext) over 10,000 updates is
   4.6 MB / 2.7 MB of garbage per pass, and it is a fresh `byte[]` per row for a payload that is only ever patched in
   place and written back. A reused/pooled buffer is self-contained and does not need the range-read rework.
3. **Skip the plaintext arm's 9.9 MB snapshot when the caller only ever needs a few bytes per row** — the raw arm's
   `row-snapshot` is 6.3–12.7 % of its staged time in ONE call. Lower priority: that arm is not the product default.

**5. Honest limits and validation.** (a) Every number here is from a profiled pass, so the *times* are not comparable
to timed figures (the plan's rule); the load-independent parts are the call counts and the allocation columns.
(b) Attribution is 73–77 % on the encrypted arm, not the plan §2 acceptance's ≥ 90 % — the remainder is named rather
than guessed (the per-operation loop's list/tuple allocation, `TryParseSimpleWhereClause` being called twice per
operation — `:2284` in the fastPatch gate and `:2349` in the locate — and the driver's outer transaction), and stamping
*those* is a further 5.5 increment, not a claim to make now. (c) Validation: build **0 errors**; core suite **1919 / 0
failed / 16 skipped** (158.3 s, i.e. the same as before the stamps — they are free when the profiler is off);
`WritePathProfilerTests` 4/4 (it asserts the disabled path records nothing and the report orders by time, both of which
still hold with 30 stages). **`--gate` was attempted twice and both runs were INCONCLUSIVE (exit 2)** — rep spreads
**2.74×** and **3.34×** against the 2.5× limit, so the machine could not be quieted; exit 2 concludes nothing in either
direction, and today's other attempts on this machine were 2.84×/3.07× (inconclusive) followed by one FAILED and one
PASSED back to back. Because this is a diagnostics-only change — two stages plus two stamps that are three volatile
reads when the profiler is off, with the suite time unchanged (158.3 s against 157.97 s before) and no behaviour
touched — the gate cannot arbitrate it either way; the correctness evidence is the suite, and the gate is recorded as a
documented failed-to-quiet re-run rather than as a pass.

Artifact: `results/dual-mode-20260921_204131.json` (the run the table above is taken from).

### 2026-09-21 — 5.3 default-job UPDATE/DELETE — BLOCKED (session 2 of 2: the mechanism is named, the remaining lever is a capability)
- Session: 2 of 2 (hard stop)
- Command(s): no new runs — this entry decides on §5.3 session 1's counts and the §5.5 stamps they prompted
- Verdict: **BLOCKED** — the item's durable output exists, no switchable lever survived, and the one remaining lever is a capability change the item's own text anticipated; the hard stop is respected rather than pushed
- Commit: none (record only)
- NEXT: 5.4 — providers re-validation, which the brief requires after every core change and which 5.2's PageBased fix now is

**Why BLOCKED and not REJECTED, and not a half-finished fix.** 5.3's DoD offers two exits: a landed fix, or a
documented `REJECTED` that "names what `UpdateMultiple`'s per-operation work actually is, plus the evidence for why no
clean lever remains". **The naming is done** — §5.3's table, extended by §5.5's two new stages, says the per-operation
work is: `row-locate-index` 10,000 calls (52–73 % of the staged pass on the encrypted arm, 14–18 % on the plaintext
one), then `engine-write` 10,000, `parse` 10,000, `index-maint` 20,000, `in-place-patch` 10,000, `classify` 10,000,
`commit` 1, `row-locate` 1 and `row-snapshot` 1 — so the work is **the locate and the record read**, not the record
layout, the inline capacity, the index configuration, the parser or the re-serialisation. **What is not done is the
second half of that exit** — "no clean lever remains" — because one lever does remain and it is large. Declaring
`REJECTED` would overclaim, and starting the fix would be a capability change taken in an exhausted session; `BLOCKED`
is the third, honest option the brief's timebox rule provides.

**The lever, and its bounded size.** On the encryped default arm the locate is 52–73 % of the staged pass because
`TryLoadWholeFileForRowAccess` returns `null` for encrypted records (`Table.CRUD.cs:4132-4134`), so every update takes
`engine.Read` per row instead of the one-shot snapshot the plaintext arm uses. The whole encryption tax on this arm is
measurable and modest: **raw UPDATE 106,339 vs default 63,636 ops/s = 1.67×** — so a perfect fix of the locate wins
**at most ~1.67× on this cell**, and candidate 1 does not remove the whole of it either (the per-record *decryption*
stays; only the random read goes). That is the number to decide against, and it is why this is a `BLOCKED` with a
ceiling rather than an open-ended invitation.

**What a fix would have to do (handed forward with the ⚠️).** Candidates 1–3 of §5.5 §4, with the warning that the
snapshot's guard **cannot simply be relaxed**: `TryOverwriteFieldsInPlaceActual` needs *plaintext* field offsets, so
feeding it raw ciphertext would corrupt records — the decryption has to happen per record *inside* the new path, exactly
as `TryBulkUpdateContiguousFixedWidth` already does it for encrypted fixed-width records
(`Table.CRUD.cs:2752-2755`). The 4-byte length prefix is plaintext even in encrypted files
(`Storage.Append.cs:1554-1556`), so a whole-file walk is possible; the design is not blocked on missing information,
only on being a new capability rather than a tuning change.

**Refuted by this item, for the record:** the record layout, the inline capacity and the hash-index configuration are
all refuted **by counts** (not by times) in §5.3 §3, and the parser-skipping batch route, the WAL durability, the index
maintenance, the record write and the locate-as-contiguous-attempt were refuted earlier by the plan's §9 priority-1
work. Two of the plan's readings failed their own controls before that. Nothing in this item's search space is left
unmeasured — the remaining work is the capability above.

### 2026-09-21 — 5.4 Providers re-validation — the three arms, the three ladders and the provider suites on the build that landed 5.2's fix
- Session: 1 of 1
- Command(s): `--pk` · `--pk-default` · `--multirowinsert` · the plain comparative run (the SQL/Direct/StructRow ladders) · five provider test suites built and run as executables
- Regime: `REGIME: no SHARPCOREDB_* switches set — harness and product defaults apply.` on every run (the `--multirowinsert` run prints its own `[diag]` line, quoted below)
- Verdict: **KEPT (validation only)** — nothing changed in the product, all suites green, and the item's purpose is met: the numbers after the first core change since the brief was written are recorded per ladder with same-session SQLite references
- Commit: this worklog entry only
- NEXT: 5.5's candidate 1 is the only open lever on the plan's scoreboard (5.3 is `BLOCKED` on it); 5.1's remaining INSERT delta is the other

**1. `--pk` (AppendOnly, the fair-PK scoreboard row) — one run, SQLite in the same process.**

| arm | INSERT | READ | UPDATE | DELETE |
|---|---:|---:|---:|---:|
| SharpCoreDB legacy plaintext | 102,011 | 68,231 | 151,756 | 279,692 |
| **SharpCoreDB FW plaintext** | 111,139 | 117,201 | **306,894** | **600,600** |
| SharpCoreDB FW at-rest | 110,448 | 86,509 | 262,618 | 358,731 |
| SQLite (same run) | 155,219 | 96,568 | 274,134 | 350,018 |
| **FW ratio vs SQLite** | **0.71×** | **1.21×** | **1.12×** | **1.72×** |

Against the brief §4 row (READ 1.05× ahead, UPDATE 1.29× ahead, DELETE 2.19× ahead, INSERT 0.54× behind): READ and
UPDATE hold their ground, DELETE has moved back from 2.19× to 1.72× ahead, and **INSERT has improved from 0.54× to
0.71×** — still the one fair-PK column behind, which is 5.1's item, not a regression from 5.2.

**2. `--pk-default` (pure default configuration, encrypted).**

| arm | INSERT | READ | UPDATE | DELETE |
|---|---:|---:|---:|---:|
| SharpCoreDB (default config) | 104,700 | 61,654 | 121,791 | 257,877 |
| SQLite (same run) | 179,513 | 94,222 | 291,676 | 378,624 |
| **ratio vs SQLite** | **0.58×** | **0.65×** | **0.42×** | **0.68×** |

Against the brief's "pure default (encrypted)" row (INSERT 1.6× behind, READ 1.2× behind, UPDATE 3.6× behind, DELETE
2.1× behind): **UPDATE 3.6× → 2.4× behind and DELETE 2.1× → 1.5× behind**, READ 1.2× → 1.5×, INSERT 1.6× → 1.7× —
i.e. the two columns the plan cares about most here improved while READ moved the other way inside the machine's band.

**3. `--multirowinsert`** — 20,000 rows, 1,000 rows/statement, 20 statements, median of 5: **57,377 rows/s**, median
**17.43 µs/row** (min 0.243 s, median 0.349 s, max 0.383 s), allocation **4,265–4,272 B/row** with 11–15 gen0
collections, `[diag] data file 1,840,000 B · overflow arena 488,890 B`. The profiled pass's stage table is `dispatch`
28.1 %, `table-batch` 20.6 %, `validate` 10.1 %, `encode` 9.7 %, `arena-write` 6.3 %, `index-maint` 5.3 %,
`hash-index` 4.7 %, `parse` 4.0 %, `arena-append` 3.3 %, `commit` 2.1 %, `row-build` 2.0 %, `row-locate` 1.7 %,
`engine-write` 1.0 %. Against the recorded budget for this shape (wall median 17.53 µs/row, 4,937 B/row) this is **flat
on time and −14 % on allocation**, which is what the committed §4b work should look like. ⚠️ **This harness has no
SQLite twin**, so the DoD's ratio clause cannot be met for this arm and is not claimed.

**4. The three ladders (plain comparative run, same session, same process as SQLite).**

| ladder | INSERT | READ | UPDATE | DELETE | ratio vs SQLite (I/R/U/D) |
|---|---:|---:|---:|---:|---|
| SharpCoreDB (SQL) | 72,121 | 49,739 | 45,944 | 60,898 | 0.54× / 0.51× / **0.18×** / 0.16× |
| SharpCoreDB (Direct) | 89,975 | 95,355 | 59,009 | 184,995 | 0.67× / 0.98× / **0.23×** / 0.47× |
| SharpCoreDB (StructRow) | 126,570 | 101,107 | — | — | 0.94× / **1.04×** / — / — |
| SQLite | 134,625 | 96,985 | 253,699 | 391,633 | — |
| LiteDB (reference) | 60,129 | 14,245 | 9,828 | 13,563 | 0.45× / 0.15× / 0.04× / 0.03× |

**Named explicitly, as the DoD asks.** (a) **No provider re-introduces row-by-row overhead on this shape**: the SQL
ladder costs 28 % more than the Direct ladder on UPDATE (45,944 vs 59,009) and 48 % less on READ, so the provider/driver
layer is a *multiplier*, not the source of the UPDATE gap — which is **5.4× behind on SQL and 4.3× behind on Direct**,
nearly the same number on both ladders, so the gap sits in the shared locate/read/patch path. That is an independent
cross-check on 5.3's `BLOCKED` verdict, reached from a second harness. (b) **StructRow does not
measure UPDATE/DELETE at all** (0 in both columns) while its READ is the only SharpCoreDB cell at/ahead of SQLite — so
"StructRow wins" is a claim about INSERT and READ only, and this run is the evidence that the other two columns are
absent rather than fast. (c) BLite's row is empty because the harness itself refuses it (`NotSupportedException` in that
library's BsonDocumentBuilder API), not because of a result here.

**5. The provider test projects, on the current build.** `dotnet test` **cannot run them in this repository**: they are
Microsoft.Testing.Platform projects and .NET 10's SDK refuses the VSTest target outright — the first attempt failed on
all five with `Testing with VSTest target is no longer supported by Microsoft.Testing.Platform on .NET 10 SDK and later`.
Built and run as executables, the way the brief runs the core suite:

| project | tests | failed | wall (build + run) |
|---|---:|---:|---:|
| `SharpCoreDB.EntityFrameworkCore.Tests` | 116 | **0** | 7.8 s |
| `SharpCoreDB.Provider.Sync.Tests` | 135 | **0** | 5.5 s |
| `SharpCoreDB.Functional.Linq2DB.Tests` | 24 | **0** | 10.7 s |
| `SharpCoreDB.Functional.Dapper.Tests` | 3 | **0** | 8.2 s |
| `SharpCoreDB.Functional.EntityFrameworkCore.Tests` | 3 | **0** | 8.8 s |
| **total** | **281** | **0** | — |

Not present in this repository: a **YesSql** provider project, and no separate ADO.NET/`Data.Provider` test project —
that surface is covered inside `SharpCoreDB.Tests`, which is green on this same build (**1919 / 0 failed / 16 skipped**).
These are correctness suites: they do not produce per-row overhead figures, and §4's ladders are what does that.

**Provenance.** The three JSONs the tables above come from are `results/pk_comparative_20260921_185343.json`,
`results/pk_default_20260921_185358.json` and `results/comparative_20260921_185439.json` — the harness writes those
flags' archives relative to the working directory (the repo-root `results/`, which is git-ignored; the same note
follow-up 6 recorded for its `--pk` runs). The `--multirowinsert` arm prints its figures and `[diag]` line to the
console and writes no archive, which is why §3 quotes the console output rather than a file.

### 2026-09-21 — 5.3 default-job UPDATE — the encrypted-snapshot lever LANDED: +20 % on the target cell (supersedes the BLOCKED verdict)
- Session: 3 (the `BLOCKED` entry above handed this forward as a capability; it is now implemented and measured)
- Command(s): `--dual-mode` ×6 unprofiled (4 with the change stashed, 2 with it) · `--dual-mode` with `SHARPCOREDB_MAIN_PROFILE_UPDATE=1` (mechanism check) · the comparative run · core suite ×2 · `--gate`
- Regime: `REGIME: no SHARPCOREDB_* switches set — harness and product defaults apply.`
- Verdict: **KEPT** — correct (suite **1919 / 0 failed / 16 skipped**, twice) and a **measured +20 %** on the default (encrypted) job's UPDATE, with that cell's encryption tax falling from **1.37–2.13×** to **1.09–1.14×**
- Commit: `perf(update)`: serve the encrypted UPDATE locate from one whole-file snapshot (plan §5.3 / §5.5)
- NEXT: the same lever's second half — the 12.6 MB snapshot allocation and the per-record decrypt are now the locate's cost; and `Table.CRUD.cs:2687`'s missing `changedColumn` is still unmeasured

**1. The change, and why it is narrow.** `TryLoadWholeFileForRowAccess` refuses encrypted files, so the PK-less
UPDATE route read one record at a time. The fix adds a **sibling** loader for the UPDATE fast-patch locate
(`TryLoadWholeFileForUpdatePatch`, `Table.CRUD.cs:4214-4241`) that also serves encrypted files, and extends
`TrySlicePayloadFromFile` to decrypt the one record it slices (`:4195-4212`) — the same treatment the contiguous
fixed-width path already gives encrypted spans through `IStorage.DecryptRecordPayload`. Four properties keep it
contained: (a) the **DELETE path keeps the plaintext-only helper**, because it deserializes plaintext keys straight out
of its snapshot and ciphertext would mis-read them; (b) **fixed-width records stay excluded** (their contiguous path
owns that case) and the 32 MB size limit still bounds the snapshot; (c) when a slice cannot be produced the code
**falls back to the per-record read** rather than skipping the row — skipping would silently drop the update; (d) the
**plaintext arm's code path is byte-identical** (its loader returns the same bytes, its slice takes the same branch), so
it is a within-run control for the measurement below rather than a second variable.

**2. The A/B — four pre-change samples against two post-change samples, same session, same flags.** The first two
pre-change samples were taken before the change (one of them a `--gate` run, which prints the same job's medians) and
the other three were taken *inside the same window as the post-change runs*, with the change stashed, rebuilt and
re-run — so grouping is the build, not the clock:

| sample | build | raw (plaintext) UPDATE | **default (encrypted) UPDATE** | tax |
|---|---|---:|---:|---:|
| `--gate` (pre) | no change | 103,117 | 75,501 | 1.37× |
| dual-mode (pre) | no change | 159,282 | 74,645 | 2.13× |
| dual-mode (pre) | no change | 129,334 | 74,072 | 1.75× |
| dual-mode (pre) | no change | 110,944 | 70,956 | 1.56× |
| dual-mode (**post**) | **change** | 98,956 | **86,697** | **1.14×** |
| dual-mode (**post**) | **change** | 100,203 | **91,870** | **1.09×** |

**The target cell is the stable one and the control is the noisy one** — exactly the opposite of the usual pattern here.
Four pre-change samples sit in **70,956–75,501** (spread 1.06×) and both post-change samples sit above the whole
pre-range at **86,697 / 91,870** (spread 1.06×): **median 74,359 → 89,284 = +20.1 %**, with non-overlapping groups, and
the load-independent tax metric agrees (**1.37–2.13× → 1.09–1.14×** — the encrypted arm moved from ~1.6× behind its
plaintext twin to ~1.1×). The plaintext arm's own numbers swing 99k–159k in the same six runs, which is why the target
cell's four-sample stability is what carries the claim.

**3. The mechanism, checked rather than assumed.** `--pk-profile`-style profiling of the same shape
(`SHARPCOREDB_MAIN_PROFILE_UPDATE=1`) shows the encrypted arm now takes the snapshot and stops paying per-record reads:
`row-snapshot` **0 B / 0.0 ms → 1 call / 12.6 MB / 5.0 ms**, and `row-locate-index` **119.5 ms (52.4 % of staged) → 13.4 ms
(14.2 %)** with the staged total falling 227.9 → 94.6 ms. The remaining locate cost is now the slice copy plus one AEAD
open per record — which is the next thing to attack, and it is what bounds this fix (see §5).

**4. A second, larger observation on a different shape — recorded, not claimed.** The plain comparative run (the
SQL/Direct/StructRow ladders, same session, SQLite in the same process) was taken before and after:

| ladder | UPDATE before | UPDATE after | vs SQLite before → after |
|---|---:|---:|---|
| SharpCoreDB (SQL) | 45,944 | 50,646 | 0.18× → 0.18× |
| SharpCoreDB (Direct) | 59,009 | **112,791** | **0.23× → 0.40×** |
| SQLite (same runs) | 253,699 | 284,594 | — |

The **Direct ladder's UPDATE nearly doubled (+91 %)**, far outside that arm's own drift on the same two runs
(INSERT +21 %, READ +23 %, DELETE −2 %), and it is the cell the 5.2/5.3 work pointed at: an encrypted, hash-predicate
update through the direct API. The SQL ladder's UPDATE gained only +10 %, *less* than its own arm's INSERT (+16 %) and
READ (+26 %) drift, so **its ratio is unchanged at 0.18× and no improvement is claimed there** — the SQL driver's
per-statement work is the difference between the two ladders and this change does not touch it. Two samples on a
loaded machine cannot carry more than "consistent with §2, on a second harness"; §2's four-vs-two design is what the
verdict rests on.

**5. Validation, limits and provenance.** (a) Build **0 errors**, no new warnings; core suite **1919 / 0 failed /
16 skipped** on the final source (157.3 s), and it had already been green (189.9 s) on the same source before the
stash round trip. (b) ⚠️ **`--gate` was run once and returned INCONCLUSIVE (exit 2)**: `default UPDATE`'s own rep spread
inside that single run was **3.18×**, larger than the effect being measured — the fourth inconclusive gate of this
session (the other three: 2.84×, 3.07×, 3.34×, with one FAILED and one PASSED in between). The gate therefore cannot
arbitrate this change, and the evidence is the designed A/B above; recording it as a failed-to-quiet re-run rather than
as a pass is the honest form. (c) **The claim is bounded by what was replaced**: the encryption tax on this cell was
1.37–2.13× and is now 1.09–1.14×, so the remaining ~1.1× is the per-record AEAD open (which stays) plus the 12.6 MB
snapshot allocation and read; a fix of *those* is where the next measurement goes, not another snapshot. (d) The
plaintext arm was left byte-identical on purpose, which is why it can be a control in §2 — a deliberate choice, not an
oversight, and it is why the raw arm's numbers are quoted but not used as a denominator.
(e) Artifacts: `results/dual-mode-20260921_{212914,212936,213002,213047,213108,213119}.json` (the six A/B runs, the
last three of which are the stashed-build samples) and `results/comparative_20260921_193451.json` for §4 — the
comparative archive lands in the repo-root `results/` (git-ignored), as recorded in §5.4.

### 2026-09-21 — 5.3 follow-up — `commit` is `CommitSync()` alone: 30.6 ms of 124.1 ms staged on the encrypted arm, and it is the next target
- Session: 1 (extension — the only product change is the diagnostic split of the commit stamp itself)
- Command(s): `--dual-mode` with `SHARPCOREDB_MAIN_PROFILE_UPDATE=1` · core suite
- Regime: `SHARPCOREDB_MAIN_PROFILE_UPDATE=1` only, named because it is the vehicle
- Verdict: **KEPT (instrumentation)** — the split answers the question it was added for, in one run: **`FlushTransactionBuffer()` is 0.0 ms** and **every millisecond of the commit is `CommitSync()`**
- Commit: `perf(diagnostics)`: split the batch-commit stamp into CommitSync and the buffer flush
- NEXT: profile *inside* `Storage.CommitSync()` (`Storage.Core.cs:152`) — WAL, the buffered-overwrite flush (`Storage.Append.cs:1310-1338`) and the metadata save are the three candidates, and the batched-vs-fallback branch of `TryFlushBufferedOverwritesBatched` is one return value to count

**1. Why this was the next thing to look at.** After the encrypted-snapshot fix (§5.3), the stage table on the target
cell changed shape: the locate fell from 119.5 ms / 52.4 % to 13.4 ms / 14.2 %, and the largest single stage became
**`commit` — one call, 29.8 ms, 31.5 %**. The same stamp reads 4.8–12.8 ms on the *plaintext* arm of the same runs, so
the commit is where this cell's remaining encryption gap sits (5.7–32.4 ms across the encrypted arm's reps against
4.8–12.8 ms plaintext: a heavier tail, not a clean multiple, which is exactly why it had to be split before being
attacked).

**2. The split, and its one result.** `Database.Batch.cs:1185-1195` stamped two calls as one. `commit` now measures
`storage.CommitSync()` alone and a new stage `commit-buffer` (`WritePathProfiler.cs`, `StageCount` 30 → 31) measures
`storage.FlushTransactionBuffer()`. The `Add` order is load-bearing and is commented at the site: `Add` closes the
checkpoint its `Stamp` opened, so each pair has to close before the next opens.

| stage | ms | share | calls | alloc MB | B/call |
|---|---:|---:|---:|---:|---:|
| **`commit` (= `CommitSync`)** | **30.6** | **24.6 %** | **1** | 0.5 | 556,568 |
| `parse` | 21.3 | 17.2 % | 10,000 | 5.1 | 531 |
| `row-locate-index` | 20.7 | 16.7 % | 10,000 | 4.1 | 431 |
| `engine-write` | 17.2 | 13.9 % | 10,000 | 2.9 | 302 |
| `in-place-patch` | 16.5 | 13.3 % | 10,000 | 1.7 | 175 |
| `index-maint` | 9.5 | 7.7 % | 20,000 | 0.8 | 43 |
| `row-snapshot` | 6.6 | 5.3 % | 1 | 12.6 | 13,166,800 |
| `row-locate` | 1.1 | 0.9 % | 1 | 0.0 | 32 |
| `classify` | 0.5 | 0.4 % | 10,000 | 0.0 | 0 |
| **`commit-buffer`** | **0.0** | **0.0 %** | **1** | 0.0 | 0 |

**3. The named target, and what is inside it.** `Storage.Core.cs:152` `CommitSync()` is called once per batch and,
on a 10,000-row UPDATE batch, has three plausible components: the **WAL** (append/fsync), the **buffered in-place
overwrite flush** (`Storage.Append.cs:1310-1338` → `TryFlushBufferedOverwritesBatched` at `:1354`, which writes one
page per touched page and falls back to two `WriteRecordInPlace` syscalls per row), and the **metadata save**
(`Database.Batch.cs:1179` `SaveMetadata()` sits immediately before the commit and is *outside* every stamp in this
table). The batched overwrite path's own gate (`overwrites.Count < 64 || path.EndsWith(".ovf")`, `:1356`) does **not**
exclude a 10,000-row batch on the data file, so the branch should engage on **both** arms — which means the 2–6× gap is
*not* explained by branch selection alone, and the next measurement has to distinguish the three components rather
than assume one. One return value in `TryFlushBufferedOverwritesBatched` is worth counting for that reason.

**4. Limits, and why nothing was changed beyond the split.** (a) `commit`'s meaning changed in this build: it no longer
includes the buffer flush. That is recorded rather than silent, and the flush is 0.0 ms on this route so earlier
readings are unaffected in substance — but a stage's meaning changing inside an append-only worklog needs to be said
out loud. (b) The commit path is the durability contract (the buffered in-place overwrites are write-behind, so
anything that writes them differently is a data-integrity change, not a tuning one) and this machine's band is ±20–30 %
with the gate INCONCLUSIVE four times today — so a 5–15 % change there could not be *verified* right now, and this
plan's rule is that an unverified change is not a landed one. Hence: the target is named with its numbers, the
measurement design is one level deeper, and the change waits for a session that can resolve it. (c) Validation: build
**0 errors**; core suite **1919 / 0 failed / 16 skipped** (157.5 s); the profiler's own tests still pass with 31 stages.
(d) Artifact: `results/dual-mode-20260921_2*` — this run's archive is the one whose first table carries `commit-buffer`.

### 2026-09-21 — 5.3 follow-up 2 — the commit is 100 % the buffered-overwrite flush, and coalescing its pages is REFUTED
- Session: 1 (extension)
- Command(s): `--dual-mode` with `SHARPCOREDB_MAIN_PROFILE_UPDATE=1` ×2 (per-page baseline and the coalesced build) · core suite
- Regime: `SHARPCOREDB_MAIN_PROFILE_UPDATE=1` only, named because it is the vehicle
- Verdict: **the stamp is KEPT, the candidate is REVERTED** — `commit-overwrites` names the commit's cost exactly, and the I/O-shape fix built on top of it was refuted by its own measurement and is out of the tree
- Commit: `perf(diagnostics)`: stamp the buffered-overwrite flush inside the commit (plan §5.3 follow-up)
- NEXT: one stage around the flush's **write** half (`RandomAccess.Write`) so read and write can be told apart — the refutation below says the cost is per byte or per write, not per syscall

**1. The split inside `CommitSync`, and it is complete.** `CommitSync` (`Storage.Core.cs:152`) is
`FlushBufferedAppendsAndOverwrites()` + `transactionBuffer.Flush()`, and the first of those
(`Storage.Append.cs:1206-1214`) is appends + **overwrites** + tombstones. Stamping the overwrite flush on its own
settles the whole question in one run, on the encrypted default-job UPDATE arm:

| stage | ms | share | calls | alloc MB |
|---|---:|---:|---:|---:|
| `commit` (= `CommitSync`) | 31.2 | 21.6 % | 1 | 0.5 |
| **`commit-overwrites`** (= `FlushBufferedOverwrites`) | **31.2** | **21.6 %** | **1** | **1.5** |
| `commit-buffer` (= `FlushTransactionBuffer`) | 0.0 | 0.0 % | 1 | 0.0 |

**All of the commit is the write-behind overwrite flush** — appends, tombstones and the transaction-buffer flush are
all free on this route — so the 10,000 buffered in-place overwrites of an UPDATE batch cost ~3.1 µs per row to reach
the disk, and that is where this cell's remaining gap lives.

**2. The candidate that was built on top of it — and refuted.** The flush's per-page path
(`Storage.Append.cs:1472-1510`) costs two syscalls plus a full-page read **and** write per touched page, so a
10,000-row batch whose keys sit in adjacent pages pays ~320 × (read 4 KB + write 4 KB) to persist ~1.5 MB of payload.
Coalescing consecutive page starts into one range (one read, one patch pass, one write, capped at 1 MB so the rented
buffer stays bounded, keeping the idempotent per-record fallback for a partial read) was therefore implemented — and
measured, in the same window, on the same shape:

| build | `commit-overwrites` | its allocation |
|---|---:|---:|
| per-page (baseline) | 31.2 ms | 0.5 MB |
| **coalesced ranges** | **29.8 ms** | **1.5 MB** |
| per-page (after the revert) | 29.8 ms | 0.5 MB |

**The I/O-shape hypothesis is refuted**: merging ~320 touched pages into two ~1 MB ranges did not move the stage
(31.2 → 29.8 ms, inside this machine's band) while it *raised* the allocation threefold from the larger rented
buffers. The flush is therefore **not** paying for its syscall count, which is the one thing coalescing could have
fixed; where the ~320 pages *are* adjacent, only the I/O shape changed and nothing else did. The coalescing is out of
the tree, and the refutation is recorded in the method's own XML remark (with the numbers) so the next reader does not
re-run the same experiment.

**3. What the refutation leaves.** A per-page cost that is neither the syscall count nor the number of ranges can only
be **(a) the bytes moved** (a full page read + a full page written per touched page, ~2.6 MB in this batch) or **(b) the
write primitive itself** — `GetOrOpenWriteHandle`'s file options (write-through / buffering), a flush per write, or the
`pageCache.EvictPage` call that follows every page write. The next measurement is one stage around the `RandomAccess.Write`
inside that loop: if the write half is ~all of the 29.8 ms, the lever is the handle's write options or the number of
flushes, not the paging; if the read half is, the lever is that the read re-fetches a page the process may already have.
That is a one-stage probe on a stage whose four samples so far read 29.8 / 30.6 / 31.2 / 32.4 ms — a tighter metric than
any whole-arm timing on this machine, which is why this axis is worth finishing.

**4. Validation and limits.** Build **0 errors**; core suite **1919 / 0 failed / 16 skipped** (174.1 s). Limits: the
refutation is a single pair of profiled runs (one baseline, one candidate) on a stage with a 1.09× spread across four
samples, so "inside the band" is a claim about ~±10 %, not about a 1.4× effect — a coalescing win would have had to be
≥ 10 % to show, and it was not; the allocation column, which is not windowed like the time column, moved the **wrong**
way and independently supports the revert. The commit path itself was not otherwise touched: the buffered overwrites are
the write-behind durability contract, and the only product change here is instrumentation.

### 2026-09-21 — 5.3 follow-up 3 — the buffered-overwrite flush sorted its entries for no reason: removing that sort is −31 % on the stage
- Session: 1 (extension)
- Command(s): `--dual-mode` with `SHARPCOREDB_MAIN_PROFILE_UPDATE=1` ×2 (preparation split, then after the removal) · `--dual-mode` ×2 unprofiled · core suite
- Regime: `SHARPCOREDB_MAIN_PROFILE_UPDATE=1` only for the stage runs
- Verdict: **KEPT** — `commit-overwrites` **33.8 → 23.4 ms** and its preparation half **14.4 → 6.3 ms** on a stage whose samples span 29.8–33.8 ms, with no semantic change: the ordering the sort provided was not needed by anything downstream
- Commit: `perf(storage)`: stop sorting the buffered-overwrite flush's entries (plan §5.3 follow-up)
- NEXT: the flush still costs 23.4 ms — 6.3 ms of preparation (collect + bucket over 10,000 entries) and ~17 ms of I/O, whose read/write split is still the one unmeasured axis; then the locate's tail

**1. How the split found it.** Follow-up 2 refuted the I/O *shape* as the cost (coalescing 640 syscalls into 4 changed
nothing), which narrowed the flush to whatever both shapes do identically. Stamping the preparation half
(`commit-ovw-prep`, `Storage.Append.cs:1382-1391`: collect the valid entries, sort them, bucket them per page) named it:

| stage | before | share of the flush |
|---|---:|---:|
| `commit-overwrites` (whole flush) | 33.8 ms | 100 % |
| **`commit-ovw-prep`** (collect + **sort** + bucket) | **14.4 ms** | **43 %** |

**2. The sort was removable, and the reason is checkable in the code.** It was
`Array.Sort(entries, 0, n, Comparer<(long, byte[])> .Create(...))` over ~10,000 entries — a delegate-based
comparison, so ~133,000 delegate invocations. Nothing downstream needed the offset order it produced:
`BucketOverwritesByPage` groups by page start and never reads the array in offset order; `FlushOverwritePages` sorts
the ~320 page starts itself before touching the file; within one page every overwrite is a *different* record (the
positions come from the index, so the spans cannot overlap), which makes the patch order inside the page buffer
irrelevant; and `direct` (records crossing a page boundary) is written as independent single records. The only ordering
that ever mattered — the page order — is established where it is used. The removal is therefore a deletion plus a
comment, not a reordering.

**3. The measurement, and its honest limit.**

| stage | with the sort | without it |
|---|---:|---:|
| `commit-ovw-prep` | 14.4 ms | **6.3 ms** |
| `commit-overwrites` | 33.8 ms | **23.4 ms** |
| `commit` | 33.8 ms | **23.4 ms** |

−10.4 ms on a stage whose **six samples across this session read 29.8 / 30.6 / 31.2 / 32.4 / 33.8** — i.e. the effect
is ~7× the stage's own spread, which is why this is a claim and not a candidate. ⚠️ **On the arm**, the same change is
~6 % of a ~165 ms pass and is *not* resolvable here: the two unprofiled samples after it read 99,836 and 158,823
default-arm UPDATE ops/s (against 86,697 / 91,870 before) — a 1.6× spread between two consecutive runs, so the arm
timing neither confirms nor contradicts it. The stage metric is the evidence; that is stated rather than glossed.

**4. Validation and limits.** Build **0 errors**; core suite **1919 / 0 failed / 16 skipped** (159.8 s);
`WritePathProfilerTests` 4/4 with 33 stages. The change is inside the commit path (the durability contract) but it
touches only *ordering*: the same records are written to the same offsets, in the same page order, by the same
primitive — which is why it can be landed on a stage metric. The remaining 23.4 ms is named for the next session: 6.3 ms
of collection/bucketing (two passes over the 10,000-entry dictionary plus one ~320-element dictionary of lists) and
~17 ms of page I/O whose read/write split is still unmeasured. Artifacts:
`results/dual-mode-20260921_{2141*,2142*,2143*}.json` (the two stage runs) and the unprofiled pair alongside them.

### 2026-09-21 — 5.3 follow-up 4 — the flush split three ways: the page READS dominate, and follow-up 2's "refuted" verdict on the coalescing was wrong
- Session: 1 (extension)
- Command(s): `--dual-mode` with `SHARPCOREDB_MAIN_PROFILE_UPDATE=1` ×3 (per-page split, then the coalesced build twice) · core suite
- Regime: `SHARPCOREDB_MAIN_PROFILE_UPDATE=1` only for the stage runs
- Verdict: **CORRECTION + KEPT** — the coalescing is re-landed with a measured −9…−13 % on the flush, and follow-up 2's "refuted" is withdrawn: that comparison still contained the sort and never isolated the I/O half
- Commit: `perf(storage)`: coalesce consecutive pages in the buffered-overwrite flush (plan §5.3 follow-up)
- NEXT: price the reads against the per-record fallback (§3) — a one-line gate flip, and the last unknown in this flush

**1. The flush, split three ways.** Adding the write-half stamp (`commit-ovw-write`) completes the decomposition of
`commit-overwrites` on the encrypted default-job UPDATE arm:

| build | `commit-overwrites` | `commit-ovw-prep` | `commit-ovw-write` | residual (page **reads** + evictions) |
|---|---:|---:|---:|---:|
| per-page (315 pages) | 25.1 ms | 6.1 ms | **1.5 ms / 315 calls** | **17.5 ms** |
| **coalesced (2 ranges)** | **21.8 / 22.9 ms** | 5.7 / 6.0 ms | **0.2 ms / 2 calls** | 15.9 / 16.7 ms |

Two things fall out of it. **Buffered writes are cheap** (1.5 ms for 315 × 4 KB, i.e. ~4.8 µs per write — the handle is
cached and opened with `FileOptions.None`, so there is no write-through to blame), and **the cost is the reads**: ~55 µs
per 4 KB page, ~1.3 MB in total for this batch, which is disk speed rather than page-cache speed — the file's pages are
not resident on this machine. `EvictPage` was checked and is a `ConcurrentDictionary.TryRemove` plus a latch
(`PageCache.Operations.cs:118-154`), so it is not the cost; the reads are.

**2. The correction, which matters more than the number.** Follow-up 2 implemented this same coalescing, measured
31.2 → 29.8 ms and reverted it as "refuted because 640 syscalls became 4 and nothing moved". That comparison was
**confounded**: it still contained the offset sort, which was only removed in follow-up 3 and only then measured at
8–14 ms of the flush, so the coalescing's effect on the I/O half was never isolated — and the numbers above now show it
is real (**25.1 → 21.8/22.9 ms, −9…−13 %**, plus the write half collapsing from 315 calls to 2). The reverted verdict is
withdrawn, the coalescing is re-landed, and the method's own doc comment carries both the history and the correction so
the next reader sees why it survived the second attempt. The general lesson is the same one this plan keeps paying for:
a candidate can be refuted by a measurement that contains another, unremoved cost.

**3. What is left, and the one experiment that prices it.** The ~16 ms of page reads exists *only because the flush does
read-modify-write on whole pages*: the alternative is the per-record path (`WriteRecordInPlace`, one 4-byte prefix write
plus one payload write, **no page read at all**), which `TryFlushBufferedOverwritesBatched` already implements as its
fallback. The plan's comment says that per-record path was the slower one — but that was decided before the sort was
removed and before the halves were separated. **The experiment is a one-line gate flip** (raise the
`overwrites.Count < 64` threshold so a 10,000-record batch takes the fallback) and it prices the two shapes directly:
whichever wins becomes the default, and if the fallback wins the flush drops its ~16 ms of reads entirely. That is the
next session's first run.

**4. Validation, limits, and a note the human should see.** Build **0 errors**; core suite **1919 / 0 failed /
16 skipped** (159.3 s) on the coalesced source. The flush change touches only *how* the same bytes reach the same
offsets; the same records, the same page order, the same primitive — which is why it can be landed on stage metrics
while the arm-level effect (~6 % of a ~165 ms pass) stays inside this machine's band. ⚠️ **The brief's §7 list is
out of date on one item**: it records "record locate as the default-job UPDATE lever — eliminated by measurement", and
that was true only while the *encrypted* arm had no whole-file snapshot; follow-up 6 restored it for that arm and
measured **+20 %**. The correction is recorded here rather than by editing the brief, and the brief's "do NOT redo
these" instruction should be read with that exception when the next session picks up 5.3. Artifacts:
`results/dual-mode-20260921_{2217*,2218*}.json`.

### 2026-09-21 — 5.3 follow-up 5 — the per-record fallback priced: 2.3× slower, so the page path is confirmed and this flush is at its floor
- Session: 1 (extension)
- Command(s): `--dual-mode` with `SHARPCOREDB_MAIN_PROFILE_UPDATE=1` with the batching gate temporarily raised (one-line flip, reverted) · core suite
- Regime: `SHARPCOREDB_MAIN_PROFILE_UPDATE=1` only for the stage run
- Verdict: **REVERTED (the flip) / CONFIRMED (the page path)** — the read-modify-write page flush is the cheaper shape, now by measurement rather than by the code comment that asserted it
- Commit: this worklog entry only
- NEXT: the one hypothesis left in this flush is why its page reads are cold (the page cache's capacity versus the 12.6 MB whole-file snapshot read, and the per-page eviction) — see §3

**1. The experiment.** The flush's remaining ~16 ms is page reads that exist only because it does read-modify-write
on whole pages; the per-record fallback (`WriteRecordInPlace`: one prefix write plus one payload write, **no page read
at all**) is one gate flip away, and the comment asserting it was the slower shape predates both the sort removal and
the three-way split. So the gate was raised (`overwrites.Count < 64` → `< 1_000_000`) for one run and reverted:

| shape | calls | `commit-overwrites` (encrypted arm) |
|---|---:|---:|
| per-record fallback (no page reads) | ~20,000 writes | **59.0 / 54.0 / 53.5 ms** |
| per-page read-modify-write | 315 reads + 315 writes | 25.1 ms |
| **coalesced per-page (shipped)** | 2 reads + 2 writes | **21.8 / 22.9 ms** |

**The fallback is ~2.3× slower than the shipped shape**, so the comment was right and now has a measurement behind it —
and the ~16 ms of page reads is *cheaper* than not reading at all, because 20,000 small writes cost more than 1.3 MB of
serial reads. Nothing changes in the tree except this record: the flip was a probe, and the probe's answer is that this
flush is at its practical floor for the shapes it has.

**2. What that closes.** The flush went from 33.8 ms (start of this thread) to 21.8–22.9 ms across three landed changes
— the preparation sort removed (follow-up 3) and consecutive pages coalesced (follow-up 4) — and its three components
are now each attributed: preparation ~6 ms, writes ~0.2 ms, reads ~16 ms, with the alternative to the reads measured
and rejected. A stage that was the single largest item on this cell is no longer the first place to look.

**3. The one hypothesis left, named rather than chased.** Those reads are at disk speed, which means the file's pages
are **not resident** — and two things in this very path could be pushing them out: the **12.6 MB whole-file snapshot**
the UPDATE locate now reads before the batch (follow-up 6; it evicts whatever the page cache held), and the
**`pageCache.EvictPage` after every page write** in this flush. Both are plausible and neither is measured; the check is
cheap (compare the flush's read cost with and without the snapshot load, and read `PageCache`'s capacity against the
snapshot's size) and it belongs to §5.2's instrumentation budget rather than to this thread. Recorded here so the next
session starts from a named hypothesis instead of the raw number.

**4. Validation.** Build **0 errors**; the probe run's numbers are above; the tree is back to the shipped shape and the
core suite result for it is follow-up 4's (**1919 / 0 failed / 16 skipped**, 159.3 s). Also corrected here: follow-up 4's
provenance line names `results/dual-mode-20260921_{2217*,2218*}.json`; the committed files are
`dual-mode-20260921_{221000,221041,221052}.json`, and the JSON in this follow-up's probe run is
`dual-mode-20260921_221457.json`.

### 2026-09-21 — 5.3 follow-up 6 — the flush's page reads are disk-bound, not cache-thrashed: the flush is at its floor
- Session: 1 (extension)
- Command(s): none new — this entry closes the last hypothesis of the thread by reading the read path, with follow-up 5's numbers
- Verdict: **NO CHANGE (floor reached, and the hypothesis refuted by reading)** — every component of the flush is now attributed and both alternatives to the expensive one have been priced
- Commit: this worklog entry only
- NEXT: the locate's tail (§18–27 ms: slice + one AEAD open per record), `Table.CRUD.cs:2687`'s missing `changedColumn`, and 5.1's INSERT delta — this thread is done

**1. The hypothesis, and why it is dead.** Follow-up 5 left one open question: why are the flush's page reads at disk
speed, and could the 12.6 MB whole-file snapshot (read by the locate before every batch, follow-up 6 of the 5.3 thread)
or the per-page `EvictPage` be evicting them? **Reading `Storage.PageCache.cs` answers the first half**: `ReadBytesRange`
(`:64-103`) uses the cached *file handle* and `RandomAccess.Read` into a fresh buffer — it never touches the DB's page
cache, so the snapshot cannot evict anything from it. The second half is not a defect either: `ReadBytesAt` (`:21-55`,
the record-read path) *does* go through that cache, which is exactly why a page must be evicted after it is overwritten
— the cache would otherwise serve stale bytes. So neither suspect is a bug, and the flush's reads are simply disk I/O on
a machine that is not quiet (the same batch's snapshot read ran at 1.6–2.5 GB/s earlier in the pass, so the file *is*
partly resident; the flushed pages are the ones that fall out).

**2. The flush, fully attributed, with both alternatives priced.**

| component | cost | alternative, and its measurement |
|---|---:|---|
| preparation (collect + bucket; sort removed) | ~6 ms | none left — one pass over 10,000 dictionary entries plus a ~315-element bucket map |
| page **writes** (coalesced into 2 calls) | ~0.2 ms | 315 calls cost 1.5 ms — the coalescing bought that 1.3 ms |
| page **reads** (coalesced into 2 calls, ~1.3 MB) | ~16 ms | per-record writes avoid the reads entirely: **59.0 / 54.0 / 53.5 ms**, i.e. 2.3× worse |
| **total** | **21.8–22.9 ms** | was **33.8 ms** at the start of this thread (−34 %) |

One structural idea remains and is named with its arithmetic rather than left as a hope: for a same-length overwrite the
4-byte length prefix is unchanged, so a payload-only write (`WriteRecordInPlace` minus the prefix write) would need
**one** syscall per record and no page read — 10,000 × ~154 B instead of 315 reads + 315 writes of 4 KB. At this
machine's ~1–2 µs per small buffered write that is 10–20 ms, i.e. it *brackets* the current 16.2 ms rather than clearly
beating it, and it changes a per-record write pattern that follow-up 5 just measured as the worse shape. It is recorded
as the one remaining idea for this flush, not as a candidate, and the thread stops here — the commit is no longer the
largest item on this cell, and three landed changes (sort removal, coalescing, plus the +20 % snapshot fix upstream of
it) took the stage from 33.8 ms to 21.8–22.9 ms with the arm's remaining effect inside this machine's band.

**3. Validation and limits.** No product change in this entry; the shipped shape is follow-up 4's, whose suite run is
green (**1919 / 0 failed / 16 skipped**, 159.3 s) and whose install is committed as `851ac232`. ⚠️ The 1.6–2.5 GB/s
snapshot read and the ~80 MB/s flushed-page reads in the *same* pass are a real inconsistency in the story — the
honest reading is that the OS cache serves the large sequential read and not the batch of page-sized touches, and that
the difference is not attributable from the numbers available here; it is recorded rather than explained away.

### 2026-09-21 — CORRECTNESS FINDING — fix (2)'s `changedColumn` reasons about values, but a relocation invalidates POSITIONS
- Session: n/a (found while reading the flush/locate paths in this thread; the only product change is the test in §5)
- Command(s): `-class SharpCoreDB.Tests.PageBasedRelocationIndexTests` · core suite
- Verdict: **PREDICTION REFUTED, MECHANISM MEASURED (§4)** — a cross-page relocation does **not** break a lookup through the untouched column, because the relocation leaves that index un-fresh and the lookup **rebuilds** it; the probe also shows fix (2)'s pruning is **not being realised on this path**
- Commit: `test(index)`: pin cross-page relocation against the untouched column's index (plan §5.3 correctness finding)
- NEXT: find which line stales the index despite `changedColumn = "payload"` (the in-place control isolates it to the relocation branch) — if the pruning can be made effective it removes an O(rows) rebuild per relocating update, which is fix (2)'s stated purpose and is currently not happening here

**1. The reasoning.** `RepointIndexesAfterRelocation(oldPosition, newPosition, oldPk, newPk, changedColumn)` skips the
invalidation of every loaded index whose column is not `changedColumn` (`Table.CRUD.cs:2199-2218`), and fix (2)
(`124b2a62`) passes the statement's updated column from `UpdateBatchViaPrimaryKeyLookup`
(`Table.BatchUpdate.cs:386-390`). Its justification was *value*-based: "an index over a column the statement did not
touch cannot have gone stale". That is true of values — and irrelevant to **positions**: `RepointIndexesAfterRelocation`
is only called when the record physically **moved** (`updatedPos != pos`), and a moved record invalidates the position
stored in *every* index that contains it, whatever column those indexes are keyed on.

**2. Why the damage is bounded but real.** On PageBased the storage reference is `(page, slot)` and a read resolves
through the slot array, so two cases must be separated. A **within-page** move repoints the slot itself
(`PageManager.UpdateRecord`'s growth branch, `PageManager.cs:556-565`) — the position `(page, slot)` is *unchanged* and
those indexes stay correct, which is presumably why the change measured no harm. A **cross-page** move flags the old
slot `RecordFlags.Deleted` and inserts the record on another page (`:567-576`), so the position the non-invalidated
indexes still hold now reads as `null` (`TryReadRecord` returns false for a deleted slot, `PageManager.cs:700-701`) —
and because fix (2) deliberately does *not* mark those indexes stale, no later operation rebuilds them. A
`WHERE <other_column> = 'x'` therefore **silently misses that row** until the index is rebuilt for an unrelated reason
or the database is reopened. The append/Columnar path cannot reach this (the call site's branch is PageBased-only),
which is what keeps the blast radius to PageBased cross-page relocations rather than turning into the "stale row
returned" regression the pre-load guard exists for.

**3. The test that settles it — and it does not exist.** The suite has no case that (a) uses PageBased, (b) registers
an index on a column *other* than the one being updated, (c) makes the updated record grow past its page so the engine
relocates it to a different page, and (d) then looks the row up through that other column. `PageBasedIndexRebuildTests`
(added by §5.2 follow-up 6) covers the unloaded-index case and `FixedWidthPatchTests` covers the append path, so this
corner is genuinely uncovered — which is why the finding is recorded as an open question rather than as a bug report:
it is a prediction from reading, and the plan's rule is that a prediction becomes a finding when a measurement (here, a
test) says so.

**4. The test was written, and it REFUTES the prediction — which is the outcome worth having.**
`tests/SharpCoreDB.Tests/PageBasedRelocationIndexTests.cs` builds exactly the case §3 asked for: a PageBased table with
a PK, 12 rows of ~1.8 KB payload filling several pages, a **loaded** hash index on `category`, then
`table.UpdateBatch("id", "payload", [(1, <5200 chars>)])` — which does route through the patched site, because
`Table.BatchUpdate.cs:275-278` dispatches a PK-column batch to `UpdateBatchViaPrimaryKeyLookup` and that method passes
`updateColumnName` at `:389`. The test **asserts its own precondition first** — the PK B-tree's storage reference for
row 1 must differ before and after, otherwise it fails with "the record did not relocate" rather than passing silently
— and it passes, so the record really did move across pages and the lookup through `category` still returns the row:

```
Total: 1, Errors: 0, Failed: 0        (the class, on the unmodified product code)
Total: 1920, Errors: 0, Failed: 0, Skipped: 16   (the full suite with it)
```

So the hole §1-§2 describe is **not reachable on this path** — and the follow-up probe named *why*, which is the part worth keeping. Two runs of the same class, one of them with a control (an update of the *same length*, which cannot relocate):

| scenario | relocated? | `row-decode` in the `category` lookup | `page-read` | row found |
|---|---|---|---|---|
| in-place, same length (control) | **no** | **0** | 1 | yes |
| **cross-page relocation** | **yes** | **12** (= the table's row count) | — | yes |
| cross-page relocation, `HasHashIndex("category")` before the lookup | — | — | — | **still true (loaded)** |

Three things follow. **(i)** The untouched column's index is left **loaded but not fresh** by a relocating update, so the lookup takes `EnsureIndexLoaded`'s *rebuild* branch (`Table.CRUD.cs:1423` → the PageBased branch decodes every row) — that rebuild, not a surviving entry, is what makes the row findable, which is exactly why the predicted hole does not reproduce. **(ii)** The control isolates the cause to the relocation branch: an update that cannot relocate leaves the index fresh (0 decodes, 1 page-read). **(iii)** Therefore **fix (2)'s pruning is not being realised on this path at all**: the call site passes `changedColumn = "payload"` (`Table.BatchUpdate.cs:389`) and `RepointIndexesAfterRelocation` should therefore skip `category` (`Table.CRUD.cs:2208-2216`), yet the index demonstrably ends up un-fresh. Which line does it is **not** identified here — the marking sites are `Table.CRUD.cs:258` (per-row `Insert`), `:2215` (the relocation itself), `:3085` (Columnar delete) and `Table.Migration.cs:278/498`, and only the second can run on this path — so the next step is to find what makes it fire for a non-`changedColumn` column, or whether `loadedIndexes` rather than `staleIndexes` is being touched. That is a *performance* finding (the pruning's entire purpose is to avoid this O(rows) rebuild) and a *correctness-relevant* one (as long as the rebuild happens the results are right, which is why the test passes); the two exits of §5 are therefore **not** required, and the O(rows) rebuild per relocating update is the real open item.

**5. The two exits, for whoever writes that test.** (a) **Narrow `changedColumn` to the safe case**: pass it only when
the relocation stayed inside its page, and pass `null` (invalidate everything) when the page changed — the caller
already holds both references, so `(oldPosition / page) != (newPosition / page)` is the whole condition. That keeps
fix (2)'s win for the common in-page case and removes the hole for the rare one. (b) **Revert fix (2)**, which costs
the O(rows) rebuild it was introduced to avoid — but only for statements that actually relocate, and only on the arm
that fix (2) never measured a win on. (a) is the better trade if the test shows the hole is reachable.

### 2026-09-21 — CORRECTNESS BUG CONFIRMED AND FIXED — fix (2)'s pruning made indexed lookups miss rows after a cross-page relocation
- Session: 1 (extension)
- Command(s): `-class SharpCoreDB.Tests.PageBasedRelocationIndexTests` (three probe iterations) · core suite
- Verdict: **FIXED** — `124b2a62`'s pruning is removed at the relocation site (`Table.BatchUpdate.cs:387-396`), with the bug reproduced and the fix verified by the test that failed without it
- Commit: `fix(index)!`: revert fix (2)'s pruning at the relocation site (silently missing rows)
- NEXT: none in this thread — the flush is at its floor, the relocation case is fixed and pinned; the open items are the locate's tail and 5.1's INSERT delta

**1. The chain, and where the first test was fooled.** The finding's own test passed, which I recorded as a refutation;
the probes then showed why, and the third of them found the bug:
- **Probe A** — the relocating update leaves the untouched column's index **loaded but not fresh**, and the lookup
  decodes the whole table (12 rows); the in-place **control** leaves it fresh (0 decodes, 1 page-read). So the cause is
  the relocation branch, not the update path.
- **Probe B** — a temporary throw inside `RepointIndexesAfterRelocation` reports what it actually receives:
  `changedColumn='payload'; loadedIndexes=[category]; oldPosition=65536; newPosition=262144`. The pruning therefore
  **does** apply (it is not a mis-passed argument), and the old position is on a **different page** from the new one.
- **Probe C** — the decisive one: `Select("category = 'alpha'")` finds the row, but **`FindByIndex("category",
  "alpha")` returns ZERO rows for a row that exists**. `Select` only appears to work because `SelectInternal` has a
  **scan fallback** — which *is* probe A's 12 decodes. The first version of the test used only `Select`, so the fallback
  covered for the bug and the test passed.

**2. The bug, in one sentence.** A relocating update invalidates only the updated column's index, but the record moved:
a **cross-page** move flags the old slot `RecordFlags.Deleted` (`PageManager.cs:567-576`) and `TryReadRecord` returns
false for it (`:700-701`), so every index still holding the old position reads a dead slot and any lookup that trusts
the index **without** a scan fallback silently misses the row. `changedColumn` reasons about *values*; the relocation is
about *positions*.

**3. The fix, and why this one.** `UpdateBatchViaPrimaryKeyLookup` now calls
`RepointIndexesAfterRelocation(pos, updatedPos, oldPkValue, newPkValue)` with no pruning column, so every loaded index
is invalidated when the record moved. That costs one O(rows) rebuild on the next indexed use per relocating update;
missing rows cost correctness, so the rebuild is the cheaper of the two — and it is also *faster* than the status quo,
because today `Select` pays a full scan on **every** query against that index until an unrelated rebuild happens. The
comment at the site records the reasoning and the test's name, and it keeps fix (2)'s remaining value visible: if the
pruning is ever reapplied it must be gated on the relocation staying **within its page**, where the slot is repointed
and the reference — and therefore the index — stays valid.

**4. Validation.** Build **0 errors**; `PageBasedRelocationIndexTests` **1 / 0 failed** with the `FindByIndex`
assertion (the same assertion, run with the pruning in place, reported 0 rows); the full suite **1920 / 0 failed /
16 skipped** (156.8 s). The test pins three things at once: the relocation actually happened (asserted, so it cannot
pass by not testing its case), the PK lookup survives it, and the index on the untouched column is usable through a
path **without** a scan fallback — the last of which is what the finding was about.

### 2026-09-21 — SESSION CLOSE (autonomous run) — state of the branch, what was corrected, and where to resume
- Session: the whole autonomous run — **16 commits on `perf/autonomous-20260921`, none pushed**
- Command(s): as recorded per entry; the session's final validation is in §3
- Regime: every measurement carries its own `REGIME:` banner and artifact per entry
- Verdict: **tree clean, build green, core suite 1920 / 0 failed / 16 skipped**
- NEXT: the three open items in §2, in order; on a quiet machine the **first** action is the gate in §3

**1. What landed, with its measured effect.**

| commit | change | effect |
|---|---|---|
| `cad4715c` | page-read / page-update / row-decode stamps restored | the 744 s suite anomaly is **refuted** (190.5 s without the stamps, 170.0/158.0 s with, both 1916/0/16) |
| `2bc3e947` | PageBased no longer pre-loads indexes before a write (+3 guard tests) | PageBased UPDATE **5.9×/4.6× behind → 1.0×/0.8×**; `row-decode` 100,000 → 0 |
| `1c6a98a0` | 5.2 closed (DoD check) + 5.3 session 1 (count-based table) | the default job's per-operation work named |
| `34214ac8` | `row-snapshot` + `row-locate-index` stamps | PageBased pass attribution 42 % → 73.6 % |
| `5403e1ee` | 5.3 `BLOCKED` + 5.4 providers re-validation | 3 arms + 3 ladders + **281 provider tests, 0 failed** |
| `6c8d3150` | encrypted UPDATE locate served from one whole-file snapshot | default-job UPDATE **+20.1 %**; encryption tax 1.37–2.13× → 1.09–1.14× |
| `1966ba31` | commit stamp split (`CommitSync` / buffer flush) | buffer flush 0.0 ms — all of it is `CommitSync` |
| `3e762cdd` | `commit-overwrites` stamp | 100 % of the commit is the overwrite flush |
| `d6b86239` | the flush's pointless offset sort removed | flush 33.8 → 23.4 ms |
| `851ac232` | consecutive pages coalesced in the flush | flush 25.1 → 21.8/22.9 ms, writes 315 calls → 2 |
| `dd06ab11` `480b1b67` `349c64ce` `a08e0ea8` | the per-record fallback priced; the correctness finding; its test; its probes | fallback 2.3× worse (page path confirmed); the finding's prediction refuted *and* its mechanism measured |
| **`514e0b9c`** | **fix (2)'s pruning reverted at the relocation site** | **silently missing rows fixed** (`FindByIndex` returned 0 for an existing row); test fails without it |

**2. What is open, in priority order.**

1. **The locate's tail** on the 5.3 cell (~18–27 ms profiled): one slice copy plus one AEAD open per record, on top
   of the 12.6 MB snapshot. The snapshot itself is the coarsest remaining knob (it reads the whole file for ~1.5 MB of
   touched records); the alternatives are a payload-only write path or a per-record read window, both of which need
   their own experiment. The flush, the commit and the relocation case are **done**.
2. **5.1's INSERT delta** — the fair-PK INSERT arm is 0.71× (was 0.54×) and the default-job INSERT 1.7× behind; the
   plan's acceptance is ≥ 1.0× **and** ≥ 150K ops/s tuned plaintext. `--multirowinsert`'s profile is the shape to
   start from (`dispatch` 28.1 %, `table-batch` 20.6 %), all of it recorded in §5.1's entries.
3. **`--gate` could never be run on a quiet machine in this session**: **six** attempts, **all INCONCLUSIVE (exit 2)**,
   rep spreads 2.74 / 3.07 / 3.34 / 3.18 / 3.52 / **4.32×**, with one `FAILED` → `PASSED` pair back to back in between.
   Nothing was ever claimed as a gate pass. **On a quiet machine the first command is `tools\clean-benchmark.ps1 --gate`** —
   and if it reports INCONCLUSIVE again, that is a statement about the machine, not about the code, so re-run it before
   changing anything.

**3. Housekeeping done here, and the two corrections to published material.**
- `tools/clean-benchmark.ps1` had the project path wrong (`tests\SharpCoreDB.Benchmarks.Comparative`, missing
  `benchmarks\`) — the brief documents the bug and tells the agent not to rely on the script. It is fixed and verified
  by *running it* (its `--gate` invocation above is the proof), so the clean-shell protocol the plan describes now has
  a working entry point.
- The plan document carried an uncommitted **header edit from an earlier session** (a second-opinion addendum note,
  2026-09-17). It is committed as-is rather than left dangling, so the tree is clean and every change has a commit.
- ⚠️ **Brief §7 is out of date on one item**: it lists "record locate as the default-job UPDATE lever — eliminated by
  measurement". That held only while the **encrypted** arm had no whole-file snapshot; restoring it for that arm
  measured **+20 %** (§5.3 follow-up 6). Read §7 with that exception.
- ⚠️ **fix (2) (`124b2a62`) was kept "on correctness grounds" and that ground failed**: its pruning made indexed
  lookups miss rows after a cross-page relocation. It is reverted at its one call site here; if it is ever reapplied it
  must be gated on the relocation staying **within its page**.

**4. Final validation of the branch.** Build **0 errors**; core suite **1920 / 0 failed / 16 skipped** (156.8 s, the
last run before the two housekeeping commits, which touch no compiled file); provider suites **281 / 0 failed** on this
same build; `WritePathProfilerTests` 4/4 with 34 stages. Every stage the profiler gained this session (`row-snapshot`,
`row-locate-index`, `commit-buffer`, `commit-overwrites`, `commit-ovw-prep`, `commit-ovw-write`) has a call-count and
allocation reading in the entries above, and each was added to answer a question that was then answered.

### 2026-09-21 — 5.1 INSERT — the fair-PK arm's budget recorded (it had none), with a double-count corrected and two levers named
- Session: 1 (extension; 5.1 is well past its 2-session timebox — this is the "record the largest stage" exit)
- Command(s): `--pk-profile-insert` (default engine, tuned plaintext fixed-width, 100,000 rows in 10 `InsertBatch` calls) · core suite unchanged (no product change)
- Regime: `REGIME: no SHARPCOREDB_* switches set — harness and product defaults apply.`
- Verdict: **NO CHANGE — measurement + correction only.** Two levers named with their budget; neither is attempted in a session that cannot verify a 2.4× claim
- Commit: this worklog entry only
- NEXT: attack **`validate`+serialize** (589 ms, 23.9 %, 706 B/row) — after §3's refutation this is the fair-PK INSERT arm's **only** remaining product target; `hash-index` is closed. The arm is at **0.71×** against a ≥1.0× acceptance

**1. The budget, which the 5.1 entries never had for this arm** (they carry `--multirowinsert`'s, a different shape:
1,000 SQL rows/statement rather than 10,000 dictionary rows per `InsertBatch` call):

| stage | total ms | calls | share | alloc MB | B/call |
|---|---:|---:|---:|---:|---:|
| `validate` (validation **and** serialization) | 589.1 | 10 | **23.9 %** | 67.3 | 7,058,373 |
| `encode` (the serialization half again) | 531.9 | 10 | 21.6 % | 67.3 | 7,058,333 |
| `index-maint` | 398.2 | 10 | 16.2 % | 91.9 | 9,638,564 |
| — `hash-index` inside it | 324.7 | 10 | 13.2 % | 84.2 | 8,824,644 |
| `arena-write` | 238.6 | 99,000 | 9.7 % | 36.2 | 383 |
| `engine-write` | 140.8 | 10 | 5.7 % | 25.4 | 2,667,937 |
| `arena-append` | 130.6 | 99,000 | 5.3 % | 20.6 | 217 |
| `row-locate` | 55.9 | 10 | 2.3 % | 9.5 | 993,036 |
| `validate-only` | 52.3 | 10 | 2.1 % | **0.0** | 0 |

Timed arm in the same run: **63,676 ops/s (15.70 µs/row, profiled)**; the unprofiled `--pk` arm measured 111,139 ops/s
against SQLite's 155,219 (§5.4), i.e. **0.71×**.

**2. The correction.** `validate` and `encode` are **not additive**: `validate` wraps
`ValidateAndSerializeBatchOutsideLock`, which the method's own comment says "does both", and `encode` is stamped inside
it for the serialization half — which is why both show 67.3 MB and ~500-590 ms. The two together are therefore
**~24 %, not 45.5 %**, and the single largest stage of this arm is validation-plus-serialization at 589 ms. Reading two
stamps that share a scope as two costs is the same error class this plan has paid for before; it is corrected here
rather than propagated into a target.

**3. The two levers, with what argues for each.**
- **Validation + serialization, 589 ms and 6.73 MB per batch call (706 B/row).** `validate-only` — the actual
  validation — is **52 ms and 0 B/call**, so essentially all of it is *serialization*, and 706 B per row is **~4.7×**
  the ~150 B record the fixed-width layout produces (the row's own bytes are ~150 of those 706, so the rest is
  intermediate buffering). The allocation ratio, not the time, is the cleaner clue because it is load-independent: a
  `--pk-profile-insert` run with the serialization split into "layout computation" and "record bytes" would name it in
  one pass (the split is one stamp away, in `FixedWidthCodec.SerializeRow`). **That read was done, and it narrows the
  target to something specific rather than inviting a rewrite:** `FixedWidthCodec.cs` is already the product of earlier
  passes on this exact path — both `SerializeRow` overloads write inline slots directly, keep a throw-away payload array
  from ever existing for strings that fit (`TryWriteInlineStringSlot`, whose own comment cites "plan §9 priority 2" and
  this arm's two-of-three TEXT columns), and hand the arena one batched call instead of one file open per value. What
  remains per row is therefore exactly three things: (i) `new byte[layout.FixedSize]`, the record itself, ~150 B and
  irreducible; (ii) for each value that overflows the inline capacity, one `Table.EncodeVariablePayload` array; and
  (iii) `(variableColumns ??= [])` / `(variablePayloads ??= [])` — **two `List<>` allocated on the first overflowing
  value of every row**, which is scratch, not output, and is the only genuinely wasteful item left. Removing (iii) means
  letting the caller own that scratch (pooled or reused across rows), which changes `PatchVariableOffsets`'s parameters
  and this static class's call surface — a designed change with a blast radius across both storage modes, so it wants
  its own session and its own `--pk-profile-insert` allocation reading (706 → ? B/row) rather than the tail of this one.
  Allocation is the load-independent column, so that verification does not depend on a quiet machine.
- **`hash-index`, 325 ms and 8.8 MB per batch call (882 B/row)**, inside `index-maint`'s 9.6 MB/call. The batch path
  calls `HashIndex.AddBatchKeys` once per index per batch, so 882 B per added key is the whole per-key cost of a hash
  insert. **Inspected before handing it on, so the next session does not re-read the whole method:** its *unsafe*
  backend already avoids the obvious waste — `HashIndex.cs:411+` rents a `byte[][]` and a `long[]` from
  `ArrayPool.Shared` once per batch and fills them in place, with the managed path getting an `EnsureKeyCapacity` hint —
  so the 882 B per key cannot be the batching itself. That leaves two places to look, in this order: the managed
  backend's per-key `Dictionary<string, List<long>>` insert (a key string, a list, a node) when
  `_useUnsafeEqualityIndex` is off, and, when it is on, the per-key `BuildUnsafeKey(NormalizeKey(keys[i]))` calls —
  each of which allocates its key, i.e. once per row rather than once per batch. **This question is now answered, by
  reading rather than by running, and the answer is the managed path:** the backend is chosen by
  `Table.Indexing.cs:26` (`_config?.EnableUnsafeEqualityIndex ?? ResolveUnsafeEqualityIndexFlag()`), whose config
  property defaults to `false` (`DatabaseConfig.cs:443`) and whose only fallbacks are the
  `SharpCoreDB.Indexing.UseUnsafeEqualityIndex` AppContext switch and the `SHARPCOREDB_USE_UNSAFE_EQUALITY_INDEX` env
  var — both unset under this session's REGIME line — while the benchmark harness never sets the property at all (only
  two unit tests do, and they do it to exercise *both* backends). So the fair-PK INSERT arm runs
  `AddBatchKeysLockedCore`'s managed loop, and its 882 B per key is the managed map's per-key insert. That splits one
  more time into (a) the structure's own growth, which is unavoidable for a new key, and (b) — **only when the indexed
  column's collation is not `Binary`** — `CollationExtensions.NormalizeIndexKey`, which allocates a fresh string via
  `ToUpperInvariant()`/`ToUpper()`/`TrimEnd()` *even when the key is already in normal form*, whereas `Binary` returns
  the input unchanged (line 35). Which of the two applies is one `--pk-profile-insert` run away, by reading the column's
  collation next to the allocation column; the safe fix in case (b) is an `char.IsUpper`/`IsWhiteSpace` pre-check so an
  already-normal key skips the copy. Worth noting for whoever owns the *harness* rather than the product: the pooled
  unsafe backend already exists and this arm is not using it, but turning it on for the fair-PK comparison is a change
  to the arm's configuration, i.e. a measurement decision to be declared, not a product change to slip in — and it still
  has to be shown faster for this shape. **And case (b) is now refuted too, which retires this lever as a product
  target:** the Comparative harness contains no `COLLATE` clause and no `CollationType` reference at all, so its indexed
  columns take the DDL default — `SqlParser.DDL.cs:151`, `var collation = CollationType.Binary` — and
  `CollationExtensions.NormalizeIndexKey`'s `Binary` arm returns the input unchanged (its line 35). With `Binary`,
  `NormalizeKey` is free, so the whole 882 B per key is the managed map inserting a new key: one list object plus one
  dictionary node per key, with the map already pre-sized for the batch by `EnsureKeyCapacity(keys.Length)`. That is
  structural to the data structure, not waste inside the loop — and the only way around it is the pooled unsafe backend
  this arm is configured not to use, which is the harness decision above rather than something to change in the product.
  **Conclusion, for the next session's benefit: the hash-index lever is closed as a product target.** The fair-PK INSERT
  arm's remaining honest target is the serialization path (589 ms / 23.9 % / 706 B per row), not the index. This is the
  fifth of my own hypotheses this session refuted — coalescing, then the relocation finding (twice), then the "882 B is
  partly normalisation" split here — and it is recorded rather than quietly dropped.

**4. What was not done, and why that is the right call.** Five sessions into 5.1 with its timebox long expired, a 2.4×
improvement still needs an implementation *and* a same-session `--pk` measurement to be a landed change; this session's
remaining budget cannot do both, and the plan's exit for an expired timebox is explicitly "record the largest stage and
every refuted hypothesis, then move on". Both levers are named with their numbers, the arm's budget now exists, and the
next session can start from a stage rather than from a hypothesis. Artifact: `results/pk_comparative_20260921_*.json`
plus the profile run's console table above.

### 2026-09-21 — Gate attempt 7 (quiet machine) — still INCONCLUSIVE, but the cause is now named: the rep noise is bigger than the effect the gate measures
- Session: 1 · Command: `tools\clean-benchmark.ps1 --gate` (detached, output to file) · Regime: `no SHARPCOREDB_* switches set`
- Verdict: **INCONCLUSIVE #7 — exit 2, worst rep spread 3,09× over a 2,50× limit.** Not a pass, not a fail, nothing concluded. The user closed everything unnecessary on this machine before the run, so this is its noise floor, not leftover load
- NEXT: do **not** re-run to fish for a green. Test the named hypothesis below; if it holds, the fix is in the harness (write policy or warm-up), not in the product

**1. The run's own numbers** (`engine=AppendOnly · reps=3 · tolerance=1,50x`), all six arm-runs per operation:

| op | the six measurements (ops/s) | worst |
|---|---|---|
| INSERT 100.000 | 74.022 · 100.227 · 101.222 · 110.137 · 118.273 · 127.499 | 1,72× |
| READ 10.000 | 42.042 · 55.898 · 74.348 · 82.194 · 97.770 · 112.693 | 2,68× |
| UPDATE 10.000 | 52.425 · 84.134 · 94.552 · 102.369 · 162.140 · 171.614 | 3,27× |
| DELETE 10.000 | 51.474 · 67.178 · 89.751 · 217.827 · 227.874 · 281.622 | 5,47× |

UPDATE 10.000 took **0,06 s in one run and 0,19 s in another** — same work, 3× apart.

**2. The assignment is pinned by arithmetic, not guessed.** The tool reports `raw U 2,04×` and `default U 3,09×`, and
each ratio reconstructs exactly from one pair above — `171.614 / 84.134 = 2,0401` and `162.140 / 52.425 = 3,0931` — so
the raw arm holds {84.134 … 171.614} and the default arm {52.425 … 162.140}. Two consequences follow, and they are the
point of this entry:

- **The arms overlap.** The slowest raw UPDATE (84.134) is *slower* than two of the three default UPDATEs (102.369,
  162.140), and the fastest reading in the whole run (171.614) sits in the raw arm. So this run cannot even rank the two
  configurations, let alone measure a regression against them.
- **The rep noise (3,09×) is larger than the effect the gate exists to detect (tolerance 1,50×).** No number of reps
  fixes that; more reps only make the median of a bimodal distribution look stable. That is why seven attempts produced
  seven INCONCLUSIVE verdicts: the tool is doing its job correctly and refusing to conclude from this signal.

**3. The named hypothesis, and why the write path is the place to look.** The variance is *not* uniform: it is smallest
on INSERT (1,72×) and grows through READ (2,68×) to UPDATE (3,27×) and DELETE (5,47×). What separates those is the
number of writes per measured row, and this project's arena is opened with `FileOptions.WriteThrough` — the very thing
`FixedWidthCodec`'s comment blames for "0.4597 ms per value" and for early INSERT work. A write-through path is
sensitive to the drive's own state (SLC write cache filling, then draining), which produces exactly this signature: a
discrete 3-5× step between runs of identical work rather than a continuous drift. Two other candidates cannot be ruled
out from this output and are listed so the test can separate them: a mid-measurement gen2 GC (allocation is tens of MB
per rep here), and OS page-cache state differing between the arms because each rep rebuilds its database.

**4. The test that decides it, which needs the profiler rather than the gate.** One `--gate`-shaped run with the
profiler enabled on the default arm gives per-stage time *and* call counts per rep; `commit`/`arena-write`/`engine-write`
being 3× across reps while `encode`/`validate` stay flat would confirm the write path, and flat stages everywhere would
point at GC instead. That is one run and it is decisive; a reshaped gate that discards or warms the first rep would be a
*harness methodology change* and must be declared in the plan first, never slipped in to make a gate go green.

**5. Checked and closed, so it does not resurface:** the `[diag] docs layout: IsFixedWidthRecords=False (config
FixedWidthRecordLayout=False, AutoFixedWidthRecords=True)` line in this output is already known and recorded
(worklog:857/943; plan:2079 — "False by default, True only when forced"), and it does not touch this session's 5.1
analysis, which was measured on the `--pk` arm whose config sets the layout explicitly. The gate rows above are the
default arm and have always been non-fixed-width.

### 2026-09-21 — Gate attempt 9: **PASSED** (exit 0) — the session's first pass, and its margin stated honestly
- Session: 1 · Command: `tools\clean-benchmark.ps1 --gate` (same tree, same regime, `no SHARPCOREDB_* switches set`) · Artifact: `tests/benchmarks/SharpCoreDB.Benchmarks.Comparative/results/*.json`
- Verdict: **PASS — "nothing is slower than baseline × 1,50."** Both gate criteria met: rep spread inside the limit *and* every arm inside tolerance
- Commit: this worklog entry only (no product change in attempts 7-9)
- NEXT: treat this as the tree's state, **not** as a machine property (see §3); the one cell that moved the wrong way is `default DELETE 1,33x`, flagged `watch` — first candidate for the next session

**1. The pass, in full.** Rep spread — raw `I 1,12x R 1,44x U 2,18x D 1,49x`, default `I 1,71x R 1,88x U 2,50x D 1,84x`, **worst 2,50x**. Baseline
comparison (`ratio = baseline ÷ current`, so >1 is slower than baseline):

| metric | baseline | current | ratio | verdict |
|---|---:|---:|---:|---|
| raw INSERT | 140.440 | 112.868 | 1,24x | ok |
| raw READ | 104.216 | 99.751 | 1,04x | ok |
| raw UPDATE | 107.082 | 99.538 | 1,08x | ok |
| raw DELETE | 204.393 | 178.107 | 1,15x | ok |
| default INSERT | 123.493 | 105.072 | 1,18x | ok |
| default READ | 81.198 | 71.947 | 1,13x | ok |
| **default UPDATE** | 77.823 | 114.459 | **0,68x** | ok (faster) |
| default DELETE | 98.725 | 74.134 | 1,33x | **watch** |

**The `default UPDATE 0,68x` is this session's UPDATE work showing up in the gate**: the encrypted default-job
UPDATE arm measured +20,1 % when the locate was taken out of the per-record snapshot, and the commit-flush
rework (−34 %) sits in the same path. The gate is not the place that measured that work, so this is
corroboration rather than evidence — the evidence stays the same-session `--default-profile` pair with call
counts and artifact JSON that the earlier entries carry.

**2. `default DELETE 1,33x` (watch) is the one cell that went the wrong way** — 33 % slower than baseline, inside
the 1,50x tolerance so the gate passes, but it is a real movement and not attributed to anything in this
worklog's entries. It is in the *default* arm (encrypted, default job), i.e. the arm this session touched most,
so it deserves a same-shape profile pair (baseline vs current stage table, call counts included) before it is
called noise. Recorded here so it is not discovered later as a mystery.

**3. The margin, stated plainly, because a pass read as a property would be wrong.** The spread check landed on
**2,50x against a 2,50x limit** — the pass holds only because the check is `> 2,50` and not `>= 2,50`. Within the
same hour on the same quiet machine, attempt 7 was INCONCLUSIVE at 3,09x (default UPDATE) and attempt 8 at 2,62x
(raw DELETE), and attempts 1-6 earlier in the session ranged 2,74x-4,32x. So:

- **The gate can pass on this machine, but the same tree can also fail its spread check** — the noise floor
  straddles the threshold. The eight-row table above is therefore best read as "no arm is *systematically*
  slower than baseline", which is what the plan's §2.4 gate is for.
- **What this pass does not licence:** recording a *new baseline* from it. The tool's own advice is to re-run on a
  completely quiet machine for baseline recording, and a run whose spread cell is exactly at the limit is not
  that run — a baseline built from it would bake this run's medians in as truth.
- **What it does licence:** closing the "gate never ran to completion" state of this session. Attempts 7-9 also
  answered the question attempt 7's entry left open — the offending cell moves between runs (default UPDATE,
  then raw DELETE, then default UPDATE again) rather than staying put, which fits general rep-to-rep variance
  more than one systematic bimodal mechanism. The write-path/GC hypothesis stays open but is now the *second*
  priority behind `default DELETE 1,33x`, because that one is a measurement rather than a hypothesis.

### 2026-09-21 — Gate attempt 10 PASSED as well, and `default DELETE` now looks systematic, not noise: 1,33x twice, 0,27 % apart
- Session: 1 · Command: `tools\clean-benchmark.ps1 --gate` (attempt 10; same tree/regime as attempt 9) · Artifact: `results/*.json`
- Verdict: **PASS again** (worst spread 2,48x). The entry above called `default DELETE 1,33x` "inside tolerance, unattributed, watch"; this run sharpens that to **reproducible**, and below is the triage that follows
- NEXT: separate "this session" from "the six days before it" with one run on the pre-session tree (`92ac7b56`) — see §3. `default DELETE` does **not** block the gate (1,33x < 1,50x), so this is a finding, not a failure

**1. The two runs, side by side.**

| cell | attempt 9 | attempt 10 |
|---|---|---|
| `raw DELETE` | 178.107 → 1,15x | 158.997 → 1,29x |
| `default DELETE` | 74.134 → **1,33x** | 74.333 → **1,33x** |
| worst rep spread | 2,50x | 2,48x |
| verdict | PASSED | PASSED |

`default DELETE`'s current value differs by **0,27 %** between two runs while the reps *inside* each run disagree by
2,5x. That is the important part, and it is a general property of this gate: **the spread banner describes within-run
rep variance, while the ratio the gate reports comes from the median, and the median reproduces across runs far better
than the reps do.** A ratio that lands on the same value twice is therefore a movement of the tree, not of the machine —
which is exactly what the "watch" label on its own could not distinguish.

**2. What this does and does not change.** The previous entry's *verdict* stands untouched: 1,33x is inside the 1,50x
tolerance, so the gate passes and nothing is blocked. What changes is the *attribution question*: `default DELETE` is
now a reproducible ~33 % slowdown against the 2026-09-15 baseline rather than a candidate noise blip, and the session's
own evidence base has a hole that this finding sits in — **DELETE was never measured once during the whole session**
(grepping the worklog for DELETE tables returns only the two gate rows above). `raw DELETE` is shakier (1,15x then
1,29x, and its current value moved 12 % between runs), so it is listed but not concluded.

**3. The next test, chosen because it is one run and it discriminates between two very different stories.** The baseline
is six days old (`708ccb73`, 2026-09-15, whose own message says it is "the deliverable that would have caught the Flush
regression"), so a DELETE gap can predate this session entirely — including the possibility that the baseline was
recorded on a different machine state. Running the same gate on the pre-session tree (`92ac7b56`, this branch's base)
answers that in one run:

- if `default DELETE` is ~1,33x there too → **pre-existing** (six days or the baseline's own provenance), not this
  session; the finding goes to the plan as an open baseline question, not as a regression to fix here;
- if it is ~1,0x there → **this session** caused it, and the candidates are named and few, all in DELETE's path: the
  commit-flush rework (page coalescing, the −34 % INSERT win), the relocation index fix, and the PageBased index-preload
  removal.

The profiler route is *not* available for this arm as it stands: `SHARPCOREDB_MAIN_PROFILE_UPDATE=1` exists for the
default arm's UPDATE, but there is no `MAIN_PROFILE_DELETE` counterpart (`--pk-profile-delete` profiles the *fair-PK*
arm, not the default one). Adding that switch would be a ~10-line harness addition mirroring the UPDATE one, and it is
worth doing only *after* the pre-session run says the movement belongs to this session.

### 2026-09-21 — The pre-session run settles it: `default DELETE` is 1,33x on this session's tree and **1,00x** on `92ac7b56`, so the ~33 % slowdown is ours
- Session: 1 · Command: `git worktree add --detach <...>-pre92ac 92ac7b56` + `tools\clean-benchmark.ps1 --gate` there (same instrument: the fixed script from this branch, copied in; same baseline file, which is committed and unchanged since 2026-09-15)
- Verdict: **attribution settled — this session caused the `default DELETE` movement.** The experiment was the one the previous entry proposed, and it answered the question it was designed for
- NEXT: bisect it; test point 1 is already running (see §3)

**1. The pre-session tree's own numbers.** That run was INCONCLUSIVE too (spread 3,08x), so it printed no comparison
table — but the gate's "current" column *is* the median of three reps, so the median can be taken from the rep lines
directly. The run's six DELETE values are 65.425 · 103.398 · 267.748 · 108.193 · 98.416 · 318.841, and the block→arm
grouping is learned from attempt 9, whose table is ground truth (raw median 178.107, default median 74.134 — which pins
that run's triplets as {48.666, 74.134, 89.353} = default and {178.107, 173.393, 258.417} = raw). Applying the same
grouping:

| tree | default DELETE (median of 3) | vs baseline 98.725 | raw DELETE (median of 3) | vs baseline 204.393 |
|---|---:|---:|---:|---:|
| pre-session `92ac7b56` | 98.416 | **1,00x** | 267.748 | 0,76x |
| this session (HEAD) | 74.134 / 74.333 | **1,33x**, twice | 178.107 / 158.997 | 1,15x / 1,29x |

The pre-session median landing **0,3 % from baseline** is what validates the grouping: a pairing error would not
reproduce the baseline that closely by accident. And it is the decisive contrast — 1,00x before this session, 1,33x
after it, with the current value reproducing within 0,27 % across two runs. `raw DELETE` stays inconclusive (faster than
baseline before, slower now, but its own value moves 12 % between runs).

**2. What this means for the session's claims, stated plainly.** Nothing that measured *faster* is affected, and the
gate's verdict does not change (1,33x is inside tolerance, so it passes). But the session now owns a reproduced ~33 %
slowdown it did not previously know about, in the arm it touched most, and the worklog has no DELETE measurement
anywhere that could have caught it — the gate caught it. That is the gate doing exactly what the plan built it for.

**3. The bisect, started.** The session's commits that can move DELETE are few, and the widest-reaching one is the
buffered-overwrite flush rework (`d6b86239` stop sorting the flush entries, `851ac232` coalesce consecutive pages),
because it changes what the commit writes rather than only how one route computes. Test point 1 is therefore the commit
immediately before that pair, `1966ba31` (diagnostics-only, so behaviourally the session start plus the earlier
behavioural commits: the PageBased preload removal, the encrypted-locate snapshot, the relocation revert):
`~1,33x` there → the culprit is earlier than the flush rework; `~1,00x` there → the flush pair (or the revert) it is.
Each test point is one `--gate` in its own worktree, ~4-7 min, and only ~2 of 3 runs reach the comparison table because
of the spread check — so a bisect here is 2-4 runs, not 1-2. Worktrees are removed when done, and nothing is pushed.

### 2026-09-21 — Bisect test point 1 (`1966ba31`, before the flush rework) = **0,98x**, parity — so the DELETE slowdown lives in one of three later commits
- Session: 1 · Command: gate in a detached worktree at `1966ba31`, same instrument and baseline · INCONCLUSIVE (spread 2,83x), read via the validated median method below
- Verdict: **not reproduced before the flush rework.** The culprit is in `d6b86239` (stop sorting the flush entries), `851ac232` (coalesce consecutive pages), or `514e0b9c` (revert of the relocation pruning) — next test point started, see §3
- NEXT: one run at `a08e0ea8` (contains the flush pair, excludes the revert) decides between those two groups

**1. A method finding, earned by using it twice, worth having in the record.** These gate runs keep ending INCONCLUSIVE
on the spread check and printing no comparison table, which used to mean "unreadable". It does not: the table's
"current" column is the median of three reps, so the median can be read off the rep lines — the only unknown is which
three of the six values belong to the same arm. The block order is not simply alternating, and it is not the same in
every rep; the mapping that reproduces attempts 9 and 10 *exactly* is **blocks {1,4,5} = `default`, {2,3,6} = `raw`**:

| run | {1,4,5} median | table's `default` | {2,3,6} median | table's `raw` |
|---|---:|---:|---:|---:|
| attempt 9 | 74.134 | 74.134 ✓ | 178.107 | 178.107 ✓ |
| attempt 10 | 74.333 | 74.333 ✓ | 158.997 | 158.997 ✓ |

Two independent confirmations on values that agree to the digit, so the mapping is not a guess: it is the key that
makes **every** INCONCLUSIVE gate run readable after the fact. It also means attempts 7 and 8 (both INCONCLUSIVE) could
be re-read from their console output if their logs are still around — worth doing before the next machine comparison.

**2. Test point 1's numbers.** `1966ba31`'s six DELETE values: 57.603 · 200.022 · 208.374 · 104.162 · 100.558 · 278.695.
By the mapping, `default` = {57.603, 104.162, 100.558} → median **100.558** → **0,98x** against baseline 98.725, and
`raw` = {200.022, 208.374, 278.695} → median **208.374** → 0,98x against 204.393.

| tree | `default DELETE` median | vs baseline |
|---|---:|---:|
| pre-session `92ac7b56` | 98.416 | 1,00x |
| **`1966ba31`** (before the flush rework) | **100.558** | **0,98x** |
| this session (HEAD) | 74.134 / 74.333 | 1,33x, twice |

Three points on a line, and the middle one sits with the pre-session tree: the slowdown was introduced **after**
`1966ba31`. Note that the raw arm tracks it here (0,98x at parity, 1,15-1,29x at HEAD), so what changed affects both
arms — which argues for a change on the *commit* path rather than one inside a single route.

**3. What is left, and the ordering of the next test.** Only three commits between `1966ba31` and HEAD can move
behaviour: `d6b86239` and `851ac232` (the buffered-overwrite flush pair, which change *what the commit writes*) and
`514e0b9c` (the revert of the relocation-pruning fix, which is in the UPDATE/index path and is *later* than the pair).
Testing `a08e0ea8` — the last commit before the revert, so it contains the flush pair and not the revert — splits those
two groups in one run: `~1,33x` there puts the cause in the flush pair, `~1,00x` puts it in the revert. That run is
started. If it lands on the flush pair, the next step is one more run between `d6b86239` and `851ac232` to pick which
half, since "stop sorting" and "coalesce pages" have very different mechanisms: removing a sort is expected to be
neutral-to-better, while coalescing merges writes and could plausibly hand the commit more work per page.

### 2026-09-21 — Bisect test point 2 (`a08e0ea8`) = **1,34x** → the buffered-overwrite flush pair caused it, and the relocation revert is excluded
- Session: 1 · Command: gate in a worktree at `a08e0ea8` (contains the flush pair `d6b86239`+`851ac232`, excludes the revert `514e0b9c`) · INCONCLUSIVE (2,57x), read via the validated mapping
- Verdict: **cause located to the flush pair.** Test point 3 started at `d6b86239` to pick which half
- NEXT: if the sort removal alone is the cause, the fix must restore ordering *for the coalescing's benefit*; if it is the coalescing, the fix is inside it — and either way the trade-off in §3 has to be decided deliberately, not silently

**1. The numbers.** `a08e0ea8`'s six DELETE values: 60.357 · 236.312 · 250.593 · 73.844 · 99.674 · 390.893. By the
validated mapping, `default` = {60.357, 73.844, 99.674} → median **73.844** → **1,34x** against baseline 98.725, and
`raw` = {236.312, 250.593, 390.893} → median **250.593** → 0,82x against 204.393.

| tree | `default DELETE` median | vs baseline |
|---|---:|---:|
| pre-session `92ac7b56` | 98.416 | 1,00x |
| `1966ba31` (before the flush pair) | 100.558 | 0,98x |
| **`a08e0ea8`** (flush pair, no revert) | **73.844** | **1,34x** |
| HEAD | 74.134 / 74.333 | 1,33x, twice |

**2. What this excludes, which is nearly as useful as what it finds.** The slowdown is present at `a08e0ea8` and absent
at `1966ba31`, so it belongs to the flush pair — and that **clears the two candidates this session had left open**:
`514e0b9c` (the revert of the relocation-pruning fix) is not the cause, and neither is `6c8d3150` (the encrypted UPDATE
locate snapshot) nor `2bc3e947` (the PageBased preload removal), all of which are already present at `1966ba31`. The
DELETE movement is in the commit/flush path, not in the UPDATE work.

**3. The trade-off this exposes, stated plainly because it is mine.** The flush rework is where this session's INSERT win
came from — `d6b86239` removed a sort and `851ac232` coalesced consecutive pages, together worth the 33,8 → 21,8-22,9 ms
commit-flush improvement. The same pair now stands accused of `default DELETE` +33 %. One plausible mechanism ties the
two halves together: coalescing merges *consecutive* entries, which works best — or only — on ordered input, so removing
the sort may have quietly disabled the very thing the next commit added, leaving the commit to write pages in arrival
order. That is a hypothesis for test point 3, not a conclusion: `d6b86239` alone (sort removal, no coalescing yet) will
say whether the two halves are separable or whether it is the *combination* that hurts. Either way, if the ordering is
what matters, the fix is not "revert the sort" (that would give back the INSERT win) but to sort only where coalescing
needs it, or to coalesce on a key that does not require a full sort.

### 2026-09-21 — Bisect test point 3 (`d6b86239`) = **1,07x**: the sort removal is nearly free, and the rest of the slowdown arrives with the page coalescing (`851ac232`)
- Session: 1 · Command: gate in a worktree at `d6b86239` (sort removal only, no coalescing yet) · INCONCLUSIVE (2,88x), read via the validated mapping
- Verdict: **the bisect is complete — `851ac232` (coalesce consecutive pages in the buffered-overwrite flush) owns the `default DELETE` slowdown**, not the sort removal
- NEXT: make the coalescing pay for itself instead of reverting it — that is a product change with two measurements to keep (the flush win and the DELETE cost), so it belongs in its own session

**1. The four points, which together separate the pair.**

| tree | `default DELETE` median | vs baseline 98.725 | what it contains |
|---|---:|---:|---|
| pre-session `92ac7b56` | 98.416 | 1,00x | — |
| `1966ba31` | 100.558 | 0,98x | neither commit |
| **`d6b86239`** | **92.148** | **1,07x** | sort removal only |
| `a08e0ea8` | 73.844 | **1,34x** | sort removal + coalescing |
| HEAD | 74.134 / 74.333 | 1,33x, twice | + the revert |

`d6b86239`'s six DELETE values: 62.427 · 185.763 · 218.358 · 116.305 · 92.148 · 391.078 → `default` = {62.427, 92.148,
116.305} → median 92.148; `raw` = {185.763, 218.358, 391.078} → median 218.358 (0,94x).

**2. Reading the steps, with their sizes attached, because they are not the same size.** Removing the sort moved the
default arm 0,98x → 1,07x, i.e. about **9 %** — small enough that a single run per point cannot call it signal rather
than scatter (my one reproducibility datum for the median is 74.134 vs 74.333, 0,27 %, which is far tighter than 9 %,
but that is one pair and not a variance estimate). Adding the coalescing moved it 1,07x → **1,34x**, about **25 %**, and
that point is corroborated by HEAD's two independent runs at 1,33x. So the actionable finding is unambiguous — the
coalescing commit owns the movement — while the sort removal's 9 % is left explicitly unresolved rather than attached to
a story it may not have earned.

**3. What the fix has to reconcile.** `851ac232` was written to make the buffered-overwrite flush cheaper, and it did:
the commit-flush measurement went 33,8 → 21,8-22,9 ms. The same commit costs the default arm ~25-33 % on DELETE. The
mechanism worth testing first is the one test point 3's tiny 9 % hints at: coalescing merges *consecutive* entries, so
its benefit depends on the entries being contiguous, and DELETE's entries may rarely be — in which case the coalescing
pass is pure overhead on that path. A fix along those lines (skip the merge when the entries do not overlap/abut, or
fold the coalescing into a form that is free when it cannot help) keeps both numbers instead of trading one for the
other, and it has to be measured on **both** the flush time and the DELETE cell in the same session, because this whole
finding exists precisely because a change was measured on only one of the two.

**4. Housekeeping.** The three bisect worktrees (`-pre92ac`, `-pre-flush`, `-bisect2`, `-bisect3`) are removed after
this entry; the branch is untouched by all of them, nothing is pushed, and every measurement above came from the same
instrument (this branch's `tools\clean-benchmark.ps1` copied into each worktree) against the same committed baseline
file.

### 2026-09-21 — A `SHARPCOREDB_MAIN_PROFILE_DELETE` switch, and its first table refutes my own fix direction for the DELETE movement
- Session: 1 · Changes: harness-only (`Program.cs`: the switch + the phase wrapper, mirroring `MainProfileUpdateOverride`); build 0 errors · Run: `SHARPCOREDB_MAIN_PROFILE_DELETE=1 dotnet run ... -- --gate`
- Verdict: **the buffered-overwrite flush is not in DELETE's path at all** — `commit-overwrites 0,0 ms / 1 call` — so "make the coalescing free where it can't help" (the previous entry's proposed fix) cannot be the fix, and DELETE's cost is `parse` + `commit`
- NEXT: the same profile on the `d6b86239` worktree, comparing `parse` and `commit` milliseconds, is now the one run that can say where the 33 % lives — the switch makes that a five-minute test instead of a bisect

**1. Why the switch was needed.** The default arm had `SHARPCOREDB_MAIN_PROFILE_UPDATE` and no DELETE counterpart, so the phase the
gate flagged at 1,33x was the one phase with no stage table. It is now the tenth line of the same pattern: reset+enable
before `sw.Restart()`, disable+report after the phase prints, and the `--gate` path picks it up per job run. One caveat
recorded in the code comment's spirit: **a profiled run must not be read as a gate result** — this one returned
INCONCLUSIVE (spread 3,00x) precisely because profiling perturbs the timings it is measuring.

**2. The table, at HEAD, for one job run** (`DELETE 10.000` in 0,03 s, 2,81 µs/delete — a fast rep; the *shape* is what
matters and it is stable because it is per-phase work, not a wall-clock aggregate):

| stage | total ms | calls | share | alloc MB | B/call |
|---|---:|---:|---:|---:|---:|
| `parse` | 6,3 | 10.000 | **51,9 %** | 3,6 | 381 |
| `commit` | 4,7 | 1 | **39,1 %** | 10,0 | 10.477.872 |
| `engine-write` | 0,5 | 1 | 3,7 % | 0,3 | 262.640 |
| `index-maint` | 0,4 | 1 | 3,7 % | 0,2 | 160.080 |
| `classify` | 0,2 | 10.000 | 1,5 % | 0,0 | 0 |
| **`commit-overwrites`** | **0,0** | **1** | 0,0 % | 0,0 | 64 |
| `commit-buffer` | 0,0 | 1 | 0,0 % | 0,0 | 0 |

**3. What it refutes, in my own previous words.** The entry above proposed the fix as "skip the merge when entries do not
overlap/abut, or make the coalescing free when it cannot help". That presumes the coalescing runs on DELETE; the table
shows it does not — the flush is stamped, it is called once, and it costs **0,0 ms**. So DELETE's own work is statement
parsing (10.000 statements, half the phase) plus a single commit that allocates 10,0 MB. The remaining explanations for a
33 % movement are therefore narrow, and the table even orders them: either **`commit`** grew inside (it is 39 % of the
phase and is the only place the flush's changes could surface indirectly), or **the environment DELETE inherits** did —
the harness runs this phase right after the UPDATE phase on the same file, and the coalescing deliberately changed *what
the UPDATE phase writes* (315 page writes to 2), which changes page-cache and dirty-page state for whoever comes next.
This is the sixth hypothesis of mine that this session's evidence has retired or narrowed, and it is retired by the
instrument built to test it, not by an argument.

**4. The next run, now five minutes instead of a bisect.** Same switch on the `d6b86239` worktree (sort removal without
coalescing, the last tree where `default DELETE` was still 1,07x) and the same table: if `commit` is materially smaller
there while `parse` is unchanged, the movement is inside the commit and the fix belongs in the flush's interaction with
it; if `commit` matches while the phase is still slower, the movement is environmental and the fix — or the measurement —
belongs in the harness, which would be a methodology finding rather than a product bug.

### 2026-09-21 — The profiled comparison clears `851ac232`: DELETE's own work is byte-identical pre- and post-coalescing, so the gate's DELETE cell is environmental
- Session: 1 · Run: `SHARPCOREDB_MAIN_PROFILE_DELETE=1` in a worktree at `d6b86239`, same instrument (current `Program.cs` copied in, since that tree predates the switch) · INCONCLUSIVE (2,71x) as a gate, read only as a stage table
- Verdict: **the DELETE movement is not DELETE's work.** `commit` is 4,6 vs 4,7 ms with **allocation identical to the byte** (10.477.872 B/call), and every other stage matches too — so my own `851ac232` did not make DELETE slower; it changed the *state* the DELETE phase runs in. Per the rule written down before the run, that is the environmental branch: the finding belongs to the harness/plan, not to a product fix
- NEXT: the harness question (should the gate's DELETE cell be measured on its own database?) goes to the plan as a methodology item; the product work returns to the fair-PK INSERT and the locate tail, neither of which this touches

**1. The comparison, side by side.**

| stage | `d6b86239` (pre-coalescing) | HEAD (post) | delta |
|---|---:|---:|---|
| `commit` | 4,6 ms / 1 call / 10.477.872 B per call | 4,7 ms / 1 call / **10.477.872** B per call | ~2 %, allocation **identical** |
| `parse` | 2,4 ms / 10.000 / 381 B | 6,3 ms / 10.000 / 381 B | 2,6x on time, calls and allocation identical |
| `engine-write` | 0,6 ms / 262.640 B | 0,5 ms / 262.640 B | identical allocation |
| `index-maint` | 0,4 ms / 160.080 B | 0,4 ms / 160.080 B | identical allocation |
| `classify` | 0,3 ms / 10.000 / 0 B | 0,2 ms / 10.000 / 0 B | identical |
| `commit-overwrites` | **0,0 ms / 1 call** | **0,0 ms / 1 call** | the flush is not in DELETE's path in either tree |
| phase wall-clock | 0,03 s (360.937 ops/s) | 0,03 s (356.338 ops/s) | 1,3 % apart |

**2. Why this is the load-independent kind of evidence.** Both runs landed in the fast-rep mode (0,03 s), so their
wall-clocks cannot compare the gate's slow cells — but allocation is the column that does not depend on machine load,
and there **every stage matches to the byte** across two different product trees. Combined with `parse`'s 2,6x on
identical calls and identical allocation, the picture is consistent: the phase's variance lives in *scheduling and
state*, not in work performed. `parse` moving 2,6x while doing exactly the same thing 10,000 times is itself a
demonstration that per-stage wall-clock at this scale is noisy, which is precisely why the allocation column has been
this session's acceptance signal everywhere else.

**3. The correction this forces on my own record, stated because I accused myself two entries ago.** That entry said the
bisect was "complete" and `851ac232` "owns" the slowdown. The bisect was right about *where* the movement appears — the
gate's `default DELETE` cell steps from 1,07x to 1,34x exactly at that commit, reproduced across trees — but the
mechanism I then proposed (the merge running on DELETE's entries, "pure overhead on that path") is wrong on two counts:
the flush costs 0,0 ms inside DELETE, and DELETE's own stages are unchanged between the trees. What the commit really
does is change **what the UPDATE phase before it writes** (315 page writes to 2, and a different dirty-page/page-cache
state), and the harness then runs DELETE next on the same file. So the honest statement is: *the commit changed the
environment that the later phase is measured in; whether it costs a user anything on a DELETE is unknown, and the
existing measurement cannot separate the two.*

**4. What that means for the gate, and why it is not an excuse.** The gate's per-arm job runs INSERT → READ → UPDATE →
DELETE against one database, so the last phase's number is not independent of the earlier ones — a property that has
been quietly true the whole time and only became visible because a change deliberately altered an earlier phase's write
pattern. Two ways to handle it, both requiring the plan's agreement because they change what the §2.4 gate means:
measure the DELETE phase on its own freshly created database (isolates it, costs one extra setup per arm), or report
each phase's numbers as "in sequence on one database" and stop treating late phases as standalone regression signals.
Until one of those is chosen, the `default DELETE 1,33x` must be recorded as **unattributed and possibly an artefact of
the measurement order**, not as a regression, and the gate's PASS on it must not be read as evidence that DELETE is
fine either. That is the state of the evidence, and it is written down rather than resolved by preference.

**5. Housekeeping.** The `-bisect4` worktree is removed after this entry; the branch is untouched, nothing is pushed,
and the product (`src/`) has not been modified by any of the profiling work.

### 2026-09-21 — STATE AT SESSION END (supersedes the gate status in the earlier `SESSION CLOSE` entry, which predates the nine commits after it)
- Commits: **34** on `perf/autonomous-20260921` · tree clean · nothing pushed, nothing on `release/*` · all five bisect worktrees removed
- Build: **0 errors**, 295 warnings (unchanged all session) · core suite **1920 / 0 failed / 16 skipped** · providers **281 / 0**
- Regime for every number below: `no SHARPCOREDB_* switches set` unless a run says otherwise; same machine, same committed baseline (`708ccb73`, 2026-09-15)

**1. The gate, which is no longer the blocker.** **Two passes** (attempt 9: worst spread 2,50×; attempt 10: 2,48×), both
"nothing is slower than baseline × 1,50", after eight INCONCLUSIVE attempts (2,74×-4,32×). The machine's noise floor
straddles the 2,50× spread limit, so **a pass is possible but not guaranteed on this machine**, and the gate's ratios
come from the median — which reproduces across runs far better than the reps do (74.134 vs 74.333 ops/s, 0,27 %).

**2. `default DELETE 1,33×` — attributed, then explained, and now a decision for the plan.** Bisected over five trees to
the flush-coalescing commit, then cleared as DELETE's own work: its stages are identical pre/post **down to the
allocation byte**, so the cell measures the state the *UPDATE* phase leaves behind in a single-database job. A PROPOSED
amendment sits in the plan (line ~198) with two options and a recommendation; **until it is accepted, `1,33×` is neither
a regression nor a clean bill.**

**3. Two habits from this session that the next one should reuse rather than re-derive.**
- **Reading an INCONCLUSIVE gate run.** The comparison table's "current" column is the median of three reps, and the
  six rep values group as **blocks {1,4,5} = `default`, {2,3,6} = `raw`** — confirmed digit-exactly on attempts 9 and 10,
  so any INCONCLUSIVE run's medians can be taken from its console output.
- **Allocation is the load-independent acceptance column.** Every wall-clock claim this session that had to survive a
  noisy machine was settled on bytes per call, and the one that was not (`parse` 2,4 vs 6,3 ms at identical call counts)
  demonstrated why.

**4. Next session's first actions, in order, with their acceptance.**
1. **Accept or reject the PROPOSED plan item** on phase independence (plan §2, line ~198). Cheapest first action, and
   it decides whether the DELETE cell is ever read as a verdict.
2. **fair-PK INSERT** (0,71×; the arm's only remaining product target). One `--pk-profile-insert` with a stamp split
   inside `FixedWidthCodec.SerializeRow` names the 706 B/row; the named candidate is the two `List<>` scratch
   allocations per row (scratch, not output). **Acceptance: the allocation column, not ops/s** — so it is verifiable on
   this machine. `hash-index` is closed as a product target (managed map, collation `Binary`, batch path already pooled).
3. **The encrypted UPDATE locate's tail** (slice + one AEAD open per record), then whatever the plan's §5 order says.

**5. What this session left deliberately undone**, so it is not mistaken for an oversight: the DELETE cell's
methodology (above), the 9 % the sort removal may or may not own (single sample per bisect point), and `raw DELETE`'s
movement (its own value moves 12 % between runs, so it was listed and not concluded). Six of my own hypotheses were
refuted by evidence during the session — coalescing (twice), the relocation finding, a per-call/per-row unit slip, the
"882 B/key is partly normalisation" split, and my own proposed fix for the DELETE cell — each with its refutation
recorded rather than dropped.

**6. Six commands that reproduce the session's key results** (all from the repo root; output to a file because these
runs are minutes long): `tools\clean-benchmark.ps1 --gate` · `$env:SHARPCOREDB_MAIN_PROFILE_DELETE='1'; dotnet run -c
Release --project tests/benchmarks/SharpCoreDB.Benchmarks.Comparative -- --gate` · `--pk-profile-insert` ·
`--pk-profile` · `git worktree add --detach <dir> <commit>` for any bisect point (copy `tools\clean-benchmark.ps1` and,
for DELETE profiles, `Program.cs`, into the worktree so the instrument matches) · `git worktree remove --force <dir>`.

### 2026-09-22 — the plan's phase-independence item is ACCEPTED, and the two items it gated are landed on the allocation column: the fair-PK INSERT's per-row scratch and the encrypted locate's ciphertext copy
- Session: 1 (this session took the three items the previous `NEXT:` block listed, in that order)
- Command(s): `--pk-profile-insert` ×2 (before/after, same machine, same code path) · `--dual-mode` with `SHARPCOREDB_MAIN_PROFILE_UPDATE=1` ×2 (before/after) · `--pk` ×2 (a stash round trip: after-build then before-build) · core suite · `--gate` (attempt 11) · provider test projects built ×6
- Regime: `REGIME: no SHARPCOREDB_* switches set — harness and product defaults apply.` (every run printed it; each profile run had exactly one switch set and it is named with its numbers below)
- Verdict: **KEPT** — the plan amendment is ACCEPTED (option 2), and both product changes are accepted on the **load-independent** column (allocation and call counts), because this machine's gate cannot arbitrate anything today
- Commit: `docs(perf)`: accept the phase-independence amendment and log the session (plan §2, WORKLOG, brief §4) · `perf(insert,update)`: drop the codec's per-row scratch and the encrypted locate's ciphertext copy (plan §9 priority 2, §5.3 tail)
- NEXT: re-measure the fair-PK INSERT ratio against a same-session SQLite arm on a quiet machine (≥ 1.0× and ≥ 150K ops/s are both still unmet), then §5.4's provider **re-run** — this session added two default interface members (`IStorage`, `ICryptoService`), so every provider project compiles (checked) but has not been re-run; the locate's remaining tail and the 12.6 MB snapshot are named below and unchanged

**1. The plan decision, which was the cheapest first action and is now recorded rather than pending.** The PROPOSED item at plan §2 line ~198 is **accepted as option 2** — keep the single-database sequence and label every late phase "in sequence on one database", so `default DELETE 1,33×` stays neither a regression nor a clean bill. Option 1 (a freshly created database per late phase) is deferred on two *verifiable* conditions, not a preference: it changes what the gate compares, which invalidates the committed baseline (`708ccb73`, 2026-09-15) and requires a `--write-baseline` re-record — and the two re-record attempts of 2026-09-16 both returned INCONCLUSIVE (2,97× and 2,77×) on the same noise floor that has since produced nine inconclusive gate attempts. Adopting option 1 without a quiet machine would leave the gate comparing against a baseline it cannot re-record. The plan now carries the gate's status explicitly too: **not a blocker, and a pass is a chance rather than a certainty** (attempt 11, §5 below).

**2. fair-PK INSERT — the plan's named candidate was the two `List<>` allocations, and it was worth 158 B/row.** The stamp split the plan asked for was added first, and it answered its own question before the fix: `encode-layout` is **0 B/row over 100,000 calls** on this arm (the fixed-width layout is computed once per table and cached), which refutes "layout computation" as the owner of the 706 B/row and moves the question to the codec's scratch. `--pk-profile-insert`, 100,000 rows via 10 `db.InsertBatch` calls, tuned plaintext fixed-width, default engine:

| stage | before (calls) | B/call before | after (calls) | B/call after |
|---|---:|---:|---:|---:|
| `encode` (nested inside `validate`) | 10 | 7,058,333 | 10 | **5,474,352** |
| ↳ per row | | **706.0** | | **547.4** |
| `encode-layout` (new) | — | — | 100,000 | **0** |
| `encode-scratch` (new) | — | — | 99,000 | **48** |
| `arena-write` (unchanged) | 99,000 | 383 | 99,000 | 383 |
| `index-maint` / `hash-index` (unchanged) | 10 | 9,638,561 / 8,824,641 | 10 | 9,638,635 / 8,824,715 |
| `validate-only` (unchanged) | 10 | 0 | 10 | 0 |

**−1,583,981 B per batch call = −158.4 B/row = −22.4 % of the arm's serialization**, and the change is a per-thread scratch pair in `FixedWidthCodec` cleared in a `finally` — no caller-owns-scratch redesign, no public API change, and the nested-call case falls back to private lists so the re-entrancy question is answered in code rather than assumed. The profile's timed arms in those two runs read **78,438 → 81,638 ops/s (12,75 → 12,25 µs/row)**; that is one pair, on a machine whose own gate says its reps swing 2,91×, so it is recorded and **not** used as the acceptance.

**3. The encrypted UPDATE locate's tail — the ciphertext copy per record is gone; the AEAD open stays, because it is the encryption contract.** `--dual-mode` with `SHARPCOREDB_MAIN_PROFILE_UPDATE=1`, 10,000 updates, `row-locate-index` B/call:

| arm | before (alloc MB · B/call) | after (alloc MB · B/call) |
|---|---|---|
| **default (encrypted, at-rest records)** | 4.1 · **431** | **2.7 · 279** |
| raw (plaintext) — the control | 2.7 · 279 | 2.7 · **279 (unchanged)** |

**−152 B per record (−35 %)**, and the encrypted arm's per-record allocation is now *identical* to the plaintext arm's — i.e. the encryption tax on this stage's allocation has gone to zero. What remains (279 B) is arm-independent scratch (the resolved-row list plus the sliced record), which is exactly why keeping the plaintext arm byte-identical makes it a control rather than a second variable. The change is one optional range overload on `IStorage` (`DecryptRecordPayload(byte[], int, int)`) plus a span overload on `ICryptoService`, so a frame that already lies in the snapshot — or in a contiguous range read — is decrypted where it lies; the copy path stays as the documented fallback for a storage without it, and the same treatment removes the per-record cipher copy from the encrypted contiguous fixed-width path as well. The stage's *times* are not evidence here (5,4–24,2 ms across six reps of identical work), which is why the acceptance is the allocation column. **Provenance:** the B/call figures above exist only in the two runs' console reports (the profiled arms write no archive, exactly as `--multirowinsert` does — plan §5.4's note), and the two run archives `tests/benchmarks/SharpCoreDB.Benchmarks.Comparative/results/dual-mode-20260922_202538.json` (before) / `..._202941.json` (after) hold the six arms' **ops/s reps only**, not the allocation column, so they are committed as run provenance and not as this change's evidence.

**4. The unprofiled `--pk` pair is recorded and explicitly not claimed.** `--pk`, one run per build with the source stashed between them, same session: fixed-width plaintext INSERT **118,992 → 133,217 ops/s**, same-session SQLite **192,650 → 165,232**, ratio **0,62× → 0,81×**. Both denominators moved further than the change could explain (SQLite itself moved 14 % in the opposite direction), so per §3.4 of the brief this is **not evidence** — the arm's ratio still has to be re-measured with an interleaved ladder on a quiet machine, which is the first item in `NEXT:`. (Artifacts: `results/pk_comparative_20260922_182729.json` is the after-build run and `..._182748.json` the stashed before-build one — both in the repo-root `results/`, which is git-ignored, per §5.4's convention.)

**5. Validation.** Build **0 errors / 273 warnings** (identical warning count before and after, so no new warning was introduced — the two `CS0419` ambiguities the new `IStorage` overload created were fixed by making those crefs signature-explicit). Core suite **1920 / 0 failed / 16 skipped** on the final source (92.3 s, `SharpCoreDB.Tests.exe`, exit 0). The six provider test projects (`Provider.Sync`, `Functional`, `Functional.Dapper`, `Functional.EntityFrameworkCore`, `Functional.Linq2DB`, `EntityFrameworkCore`) all build with **0 errors**; their **re-run** is `NEXT:` rather than claimed. `--gate` **attempt 11: INCONCLUSIVE (exit 2)**, worst rep spread **2,91×** (default UPDATE 2,91×, raw UPDATE 2,69×) against the 2,50× limit — documented rather than re-run for a green, because attempts 9 and 10 passed on this same machine (2,50× / 2,48×) and the machine's noise floor straddles the limit in both directions; the gate's per-operation ratios are unreadable in this run and nothing is concluded from it either way. Per §3.6 of the brief, `REGRESSED` would mean "re-run on a quiet machine", never "revert", and INCONCLUSIVE says nothing about the code.

**6. What is refuted or left, so the next session does not re-derive it.** (a) **"Layout computation owns the 706 B/row" is refuted** on the fixed-width arm — 0 B/row over 100,000 calls, because the layout is cached per table. (b) **The remaining 48 B/row in `encode-scratch` is output, not scratch**: it is the arena-bound payload array, which `OverflowArena` caches and serves back from `Read`, so pooling it would need the arena's contract to change — out of scope for a scratch fix. (c) **The locate tail's remaining 279 B/record is not encryption-related** (identical on both arms), so the next candidate there is arm-independent scratch, and the AEAD open itself is not removable at all — it is the encryption contract §3-1c keeps. (d) **The coarser knob is still the whole-file snapshot** (12.6 MB read for ~1.5 MB touched, 1 call per batch), untouched by this session; plan §5.3's remaining options for it (a payload-only write path, or a per-record read window) are unchanged and still unmeasured.

### 2026-09-22 (session 2, unattended) — §5.4 re-validated on the final build, the fair-PK ratios re-measured as a ladder, and the A/B that refuted my own first reading of the new codec
- Session: 2 of 2026-09-22 (unattended — the user went to bed, so this is the quiet machine the plan kept asking for; nothing else ran on the CPU during the measurements)
- Command(s): `--gate` (attempt 12) · `--pk` ×3 · `--pk-default` ×2 · `--multirowinsert` ×7 (1 in a `--detach` worktree at `112709e2`, then 3 interleaved pairs) · the plain comparative run ×3 · the six provider/EF/sync suites + `VectorSearch.Tests` by their EXEs · `dotnet build SharpCoreDB.CI.slnf`
- Regime: `REGIME: no SHARPCOREDB_* switches set — harness and product defaults apply.` (every run printed it)
- Verdict: **§5.4's DoD is met for this build** (all three harness arms plus the provider suites re-run, and one table per ladder against same-run SQLite arms); **5.1's DoD item 2 is met too** — the per-stage budget for the `--multirowinsert` shape is in §4 (calls, share, B/call, µs/row, B/row); **the fair-PK INSERT ratio did not move** — 0,68× median here against 0,71× published, while the allocation fix is real at −22,4 %; and the gate is INCONCLUSIVE again (2,76×) *on a quiet machine*, which makes the noise floor a property of the machine rather than of the load
- Commit: this worklog entry + the brief's §4 refresh (no product change in this session)
- NEXT: 5.1 INSERT remains the only losing fair-PK column and its allocation budget is now **exhausted** as a lever (see §4 below) — the next thing that can move it is the SQL-layer per-statement work in 5.1's own list, or a decision to accept the allocation-only win; §5.4's provider numbers are current as of this build; and the locate's 12.6 MB snapshot is still the coarser knob, unmeasured

**1. The quiet machine did not give the gate a verdict — that is now a machine property, not a load property.** Attempt 12 returned **INCONCLUSIVE (exit 2)** with a worst rep spread of **2,76×** (raw DELETE 2,76×, default UPDATE 2,74×) against the 2,50× limit, run with nothing else on the CPU. The four attempts of 2026-09-21/22 now read 2,50× (passed) / 2,48× (passed) / 2,91× / 2,76×, and between attempts 11 and 12 the only commits are docs, so the spread moved without the code moving. Conclusion, recorded so nobody re-derives it: **on this machine the gate's own rep spread sits on the 2,50× limit, so a verdict is a coin toss and the gate is informative-trend at best.** The acceptance rule this plan already carries (the load-independent allocation column) is therefore not a workaround — it is the only column that has been reproducible here.

**2. The fair-PK ratio, as a ladder instead of a single sample** (`--pk`, final build, three consecutive runs, each arm already a median of three, ratio = SharpCoreDB ÷ same-run SQLite, fixed-width plaintext arm):

| run | SC INSERT | SQLite INSERT | **INSERT** | **READ** | **UPDATE** | **DELETE** |
|---|---:|---:|---:|---:|---:|---:|
| 1 | 130.464 | 190.989 | 0,68 | 1,05 | 1,16 | 1,22 |
| 2 | 135.164 | 191.080 | 0,71 | 1,08 | 1,32 | 1,92 |
| 3 | 135.264 | 199.418 | 0,68 | 1,08 | 0,80 | 1,86 |
| **median** | 135.164 | 191.080 | **0,68** | **1,08** | **1,16** | **1,86** |

Read against the brief's published row 1 (READ 1,21× ahead, UPDATE 1,12×, DELETE 1,72×, INSERT **0,71×** behind): three of the four columns are consistent and **INSERT is slightly worse (0,68× vs 0,71×)** — but note *why* the numbers are not directly comparable: this session's same-run SQLite INSERT reference is **191–199K ops/s** where the earlier publication used **155K**, i.e. SQLite itself moved ~25 % faster on a quieter machine while SharpCoreDB's INSERT arm moved 111K → 135K. **The honest reading is therefore two-part**: the −22,4 % allocation win is real and measurable (session 1, deterministic), and it buys **no** ops/s on this shape, because the removed bytes were cheap gen0 scratch and the arm's cost sits where the allocation column already said it does not — in per-statement SQL work. That is the predicted outcome, not a surprise: allocation was chosen as this arm's acceptance *because* the time column is unusable here.
**3. §5.4 — the provider re-validation, now current for this build.** Three harness arms re-run, plus every suite the brief names, all by their own EXE (MTP — `dotnet test` refuses here: "*Testing with VSTest target is no longer supported by Microsoft.Testing.Platform on .NET 10 SDK and later*"). The three ladders, medians of three consecutive plain comparative runs, ratio = SharpCoreDB ÷ same-run SQLite:

| ladder | INSERT | READ | UPDATE | DELETE |
|---|---:|---:|---:|---:|
| SharpCoreDB (SQL) | 0,63× (91.160 / 145.365) | 0,65× (63.241 / 97.005) | 0,22× (63.609 / 289.110) | 0,30× (114.111 / 384.350) |
| SharpCoreDB (Direct) | 0,88× (127.793 / 145.365) | **1,34×** (130.411 / 97.005) | 0,38× (108.989 / 289.110) | 0,61× (233.923 / 384.350) |
| SharpCoreDB (StructRow) | 0,96× (139.649 / 145.365) | **1,20×** (116.638 / 97.005) | not measured by the arm | not measured by the arm |
| LiteDB (context) | 0,43× | 0,15× | 0,04× | 0,04× |

Against the brief's row 2 (the same no-PK document-CRUD job: READ 0,76×, UPDATE 0,24×, DELETE 0,31×, INSERT 0,67×) the SQL ladder lands on the same cells (0,65 / 0,22 / 0,30 / 0,63), so **that row is now re-measured against same-run SQLite** — the previous session had explicitly left it undone. Two readings worth keeping: the **StructRow ladder is at parity on INSERT (0,96×)** and ahead on READ, which is where the alloc-cheap paths show; and **the Direct ladder's DELETE (0,61×) is twice the SQL ladder's (0,30×) with four times its absolute READ (130K vs 63K)**, i.e. the SQL driver's per-statement work is still the separator between ladders — 5.3's standing finding, unchanged by this session's fixes.

Suites, all on the final build, all green: `SharpCoreDB.Functional.Tests` **38 / 0 failed**, `SharpCoreDB.Functional.Dapper.Tests` **3 / 0**, `SharpCoreDB.Functional.Linq2DB.Tests` **24 / 0**, `SharpCoreDB.Functional.EntityFrameworkCore.Tests` **3 / 0**, `SharpCoreDB.EntityFrameworkCore.Tests` **116 / 0**, `SharpCoreDB.Provider.Sync.Tests` **135 / 0** (together **319 tests, 0 failed, 0 errors, 0 skipped**), plus `SharpCoreDB.VectorSearch.Tests` **248 / 0**. The ADO.NET provider has no separate suite — its tests live in the core suite (`tests/SharpCoreDB.Tests/DataProvider/*`), which is the **1920 / 0 failed / 16 skipped** run from session 1 — and `SharpCoreDB.Provider.YesSql` has no suite at all, so for it the compile-level check is what exists: **`dotnet build SharpCoreDB.CI.slnf -c Release` is 0 errors**, which builds `SharpCoreDB.Data.Provider`, `Provider.YesSql` and every other CI project against the two default interface members this session added.

**4. The `--multirowinsert` arm: a same-build A/B that first looked like a 13 % regression, and the ladder that refuted it.** The shape is 20,000 rows at 1,000 rows/statement, median of five, and its allocation column is the most stable measurement in this repository (five samples inside 0,2 %):

| | before (`112709e2`, worktree) | after (HEAD) | delta |
|---|---:|---:|---:|
| allocated per row (5 samples) | 4.266 (4.272 / 4.267 / 4.266 / 4.266 / 4.266) | **4.106** (4.112 / 4.107 / 4.105 / 4.106 / 4.106) | **−160 B/row (−3,75 %)**, deterministic |
| median µs/row (first pair) | 13,19 | 15,22 | −13 % — **refuted below** |
| median rows/s | 66.392 | 67.403 | +1,5 % (interleaved medians) |

The first single pair looked like a 13 % slowdown, which would have been a reason to revert the codec change rather than keep it. Three **interleaved** pairs (after/before/after/before/after/before, same machine, minutes apart) give per-pair rows/s ratios of **1,015 / 1,082 / 0,897** and medians of **67.403 (after) vs 66.392 (before)** — the arms overlap, so **the time is unchanged within this machine's noise and the −13 % reading was an artefact of comparing two isolated runs**. The allocation delta reproduces in every run of this session on both shapes (706 → 547 B/row on the fair-PK arm, 4.266 → 4.106 B/row here). That is the plan's own rule (§2.7 / brief §8.1) doing exactly what it is for: the deterministic column decides, the wall clock is quoted and not concluded.

**The per-stage budget for this shape, which is 5.1's DoD item 2** (profiled pass of the same run: 20,000 rows during 20 statements of 1,000, so per-statement stages divide their B/call by 1,000 for B/row; † marks a stage that nests what is below it, so the shares are not additive — plan §11's caveat):

| stage | total ms | calls | share | alloc MB | B/call | µs/row | B/row |
|---|---:|---:|---:|---:|---:|---:|---:|
| `dispatch` † | 191,7 | 22 | 29,7 % | 78,3 | 3.733.497 | 9,59 | 3.733 |
| `table-batch` † | 133,3 | 20 | 20,6 % | 40,3 | 2.115.433 | 6,67 | 2.115 |
| `validate` † | 46,5 | 20 | 7,2 % | 12,1 | 635.436 | 2,33 | 635 |
| `index-maint` | 46,3 | 20 | 7,2 % | 19,7 | 1.032.995 | 2,32 | 1.033 |
| `encode` † | 43,1 | 20 | 6,7 % | 12,1 | 635.396 | 2,16 | 635 |
| — `hash-index` inside it | 42,2 | 20 | 6,5 % | 18,2 | 951.792 | 2,11 | 952 |
| `row-build` | 34,5 | 20.000 | 5,3 % | 7,6 | 400 | 1,73 | 400 |
| `commit` | 26,9 | 20 | 4,2 % | 2,5 | 132.251 | 1,35 | 132 |
| `arena-write` | 17,4 | 20.000 | 2,7 % | 8,9 | 467 | 0,87 | 467 |
| `encode-scratch` (new) | 17,2 | 20.000 | 2,7 % | 0,9 | **48** | 0,86 | 48 |
| `parse` | 15,4 | 20 | 2,4 % | 8,5 | 443.388 | 0,77 | 443 |
| `arena-append` | 12,2 | 20.000 | 1,9 % | 4,7 | 246 | 0,61 | 246 |
| `engine-write` | 6,3 | 20 | 1,0 % | 3,4 | 178.470 | 0,32 | 178 |
| `row-locate` | 5,0 | 20 | 0,8 % | 2,0 | 104.672 | 0,25 | 105 |
| `stmt-split` | 4,1 | 22 | 0,6 % | 5,7 | 269.413 | 0,21 | 269 |
| `validate-only` | 3,4 | 20 | 0,5 % | **0,0** | 0 | 0,17 | 0 |
| `encode-layout` (new) | 0,3 | 20.000 | 0,0 % | 0,0 | **0** | 0,02 | 0 |

Three things this table says that the timing column cannot. **(a)** The two new stages behave exactly as the fair-PK arm predicted on a *second* shape: `encode-scratch` is **48 B/row** (the arena-bound payload array — one per row here, because only the 20-character `email` value overflows the 16-byte inline capacity while `name` and `payload-N` fit inline, which is why `arena-write` fires 20,000 times and not 60,000) and `encode-layout` is **0 B/row**. **(b)** `validate-only` is **0 bytes and 0,17 µs/row** across 20,000 rows — validation is genuinely free on this path, and `validate`'s 635 B/row is entirely the serialization nested inside it (the same reading session 1 corrected for the fair-PK arm). **(c)** The per-statement stages now carry the arm: `dispatch` at 29,7 % and `table-batch` at 20,6 % of a 646 ms profiled pass are both *nested containers* whose own work is the per-statement bookkeeping 5.1 names (statement build, split, parse, dispatch) — together with `stmt-split` (269 B/row per statement, 0,21 µs/row) that is where a future INSERT lever would have to come from, not from the row serialization, which this session has now measured down to 48 B/row of output.

**5. `--pk-default` (the pure-default encrypted arm) — the UPDATE gap narrowed, and it is the one cell this session's tail fix could plausibly touch.** Two runs, ratio = SharpCoreDB ÷ same-run SQLite:

| run | INSERT | READ | UPDATE | DELETE | SQLite, the same runs |
|---|---:|---:|---:|---:|---|
| 1 | 0,59 (117.858) | 0,51 (55.697) | 0,50 (157.539) | 0,72 (278.318) | 201.112 / 108.337 / 315.527 / 387.682 |
| 2 | 0,61 (122.277) | 0,50 (56.102) | 0,47 (146.520) | 0,65 (272.278) | 200.059 / 111.169 / 313.471 / 416.717 |

The brief's published row 3 is INSERT 0,58×, READ 0,65×, UPDATE **0,42× (2,4× gap)**, DELETE 0,68×, so UPDATE looked like it had moved (0,42× → 0,47–0,50×). **That reading is refuted by a same-build interleaved A/B, and the refutation is the useful result.** Three alternating pairs (after/before/after/before/after/before, HEAD vs a `--detach` worktree at `112709e2`, same machine, each run carrying its own SQLite arm) give UPDATE ratios of **0,53 / 0,50 / 0,50 after** against **0,57 / 0,51 / 0,54 before** — per-pair deltas of −0,04 / −0,01 / −0,04, i.e. **the two builds are indistinguishable on that cell and the after-build is if anything a hair behind**. What actually moved is SQLite's own UPDATE reference (315K in the session that published 0,42×, 257–303K here) while SharpCoreDB's UPDATE arm sits at 146–166K in both builds. So `--pk-default`'s UPDATE "improvement" is **the same reference-drift artefact** that produced the INSERT and READ readings above — recorded here so no future session credits the locate fix with it. The allocation win on that path is real (431 → 279 B/record, deterministic); its ops/s effect on this machine is **zero within noise**, on both cells it could plausibly have touched.

**6. State at the end of session 2, and what the next session should not re-derive.**
- **The gate cannot arbitrate on this machine** (four attempts straddling the limit: 2,50× pass, 2,48× pass, 2,91×, 2,76× — with docs-only commits between the last two). Quote a verdict when one arrives; do not plan around one, and do not read an INCONCLUSIVE as a pass.
- **The fair-PK INSERT arm's allocation budget is spent.** The 706 B/row is now fully attributed: 383 B arena traffic (`arena-write`, unchanged, closed as a target), ~158 B scratch (**removed**, session 1), 48 B payload array (output — the arena caches and serves it), and the remainder the record plus the caller's row list. Nothing left there is a lever on ops/s, and the ladder above shows ops/s did not move.
- **§5.4 is satisfied for this build** — three harness arms, one table per ladder against same-run SQLite, the provider/EF/sync/vector suites green by EXE, and `SharpCoreDB.CI.slnf` building clean. It becomes due again after the next core change.
- **5.1's DoD item 2 is satisfied by §4's budget table**, and that table is also the argument for where item 1 (the ≥ 1.0× ratio) would have to come from if anyone attacks it again: the per-statement stages (`dispatch`, `table-batch`, `stmt-split`, `parse`) and not the row serialization, which is now 48 B/row of output plus the record itself. Note that the previous session's 5.1 exit said exactly this and stopped for lack of budget; this session adds the measurement, not a fix.
- **Still open, untouched, and a design item rather than a tuning item:** the encrypted UPDATE locate's **12.6 MB whole-file snapshot** (one call per batch, ~1.5 MB of it touched) — plan §5.3's own options are a payload-only write path or a per-record read window, and the per-record window was already measured *worse* (2,3×, 5.3 follow-up 5). The remaining per-record allocation after this session is 279 B and is arm-independent, so it is not an encryption cost any more.
- **Artifacts:** the `--pk`, `--pk-default` and comparative runs archive their JSON under the repo-root `results/` (git-ignored — `pk_comparative_20260922_*.json`, `comparative_20260922_*.json`); the profiler tables above (including the `--multirowinsert` budget) are console output, because neither `--multirowinsert` nor the PK arms archive a stage table.

<!-- APPEND-ENTRIES-BELOW -->








































