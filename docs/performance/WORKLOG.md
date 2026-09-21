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

<!-- APPEND-ENTRIES-BELOW -->
