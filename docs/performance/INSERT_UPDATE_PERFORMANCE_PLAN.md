# SharpCoreDB — Insert & Update Performance Plan

**Status:** draft for owner review · **Date:** 2026-09-13 · **Branch:** `release/v2.1.0.0-RC.3`
**Companion docs:** [`V2_PERFORMANCE_PLAN.md`](V2_PERFORMANCE_PLAN.md) (the living v2.x roadmap),
[`../benchmarks/V198_V20_V21_PERFORMANCE_COMPARISON.md`](../benchmarks/V198_V20_V21_PERFORMANCE_COMPARISON.md),
[`../benchmarks/SHARPCOREDB_COMPARATIVE_BENCHMARKS.md`](../benchmarks/SHARPCOREDB_COMPARATIVE_BENCHMARKS.md),
[`../benchmarks/default-config-pk.md`](../benchmarks/default-config-pk.md)

---

## 0. Mandate and scope

Write throughput is the one area where SharpCoreDB is still clearly behind SQLite, and it is the
area the owner wants addressed **before** any new C# 15 / net11 release decision.

**In scope:** INSERT, UPDATE (and DELETE where the same machinery is involved) on the default
configuration, across the SQL, Direct and StructRow API ladders.

**Explicitly out of scope for this plan:** publishing packages, bumping the version, and any GA
decision. The branch stays at `2.1.0-RC.3`; no release mechanics run until the owner says so.

**Non-negotiable constraints** (they decide what a "fix" may look like):

1. Existing test suites stay green: core **1791**, VectorSearch **248**, Search **58**, HybridSearch **6**.
2. Durability is not traded away: WAL semantics, crash-consistency, and the reopen round-trip matrix
   (`ReopenRoundTripMatrixTests`, `FormatCompatPolicyTests`) must keep passing.
3. On-disk format changes need a compatibility story (the repo already carries a
   read-only-first upgrade policy for one similar case).
4. Encryption must keep working exactly as configured; "faster when nothing is encrypted" may never
   become "encrypted data written as plaintext".
5. All documentation in English.

---

## 0.1 Decisions (locked by the owner, 2026-09-13)

| # | Question | Decision |
|---|---|---|
| 1 | PageBased engine | **Bring it to parity** — give it the same in-place update/delete fast paths as the fixed-width Columnar path |
| 2 | Phase 2b record layout | **Option B — two-region record**: fixed-width fields stay in the stable slot, long variable values live in the overflow arena addressed by offset. No disk cost, more work. |
| 3 | Format change on the v2.1 line | **Allowed, via a magic-header versioned upgrade.** The table-file magic already reserves version bytes (`PersistenceConstants.EncryptedTableMagic = 53 43 44 42 01 01 00 00`) and `Core/File/PageHeader` carries `MagicNumber` + `Version` validated against `CurrentVersion` — the upgrade hook exists. A format change must ship *with* a migration/upgrade path, and existing files must still open. |
| 4 | INSERT target | **Beat SQLite, not merely match it** |
| 5 | Scope | **Everything** — core SQL, Direct and StructRow paths *and* the bulk APIs (`InsertBatch`/`UpdateBatch`) and the ADO.NET / YesSql / Sync providers |
| 6 | Encryption posture (§3-1b) | **Security stays the default and must be real.** "Encrypted by default" is the product promise; `NoEncryptMode=true` remains the explicit *raw-speed* opt-out for benchmarks and speed-critical deployments. **Every published number carries both an encrypted and an unencrypted column**, so the trade is a visible, documented option rather than a hidden tax. |

Consequences: §6 is promoted from an owner call to a real work package; §5's INSERT target moves above
parity; §4b's format change is in scope **with** a migration path; §8's targets apply to every API
ladder and provider, not just core SQL; and decision 6 adds a **security-consistency audit** (§3-1c)
ahead of the write-performance work, because the default today is not what the documentation claims.

---

## 1. Current state (measured, with sources)

### 1.1 Where we stand

From the repository's own comparative harness (100K inserts / 10K reads, updates, deletes). The v2.0
and v2.1 columns are the version-comparison run in
[`V198_V20_V21_PERFORMANCE_COMPARISON.md`](../benchmarks/V198_V20_V21_PERFORMANCE_COMPARISON.md) §2;
SQLite/LiteDB are that run's same-machine references.

| Operation | v2.1 SQL | v2.1 Direct | v2.1 StructRow | SQLite | vs SQLite | vs LiteDB |
|---|---:|---:|---:|---:|---:|---:|
| **INSERT** | 73.5–84.3K | 108.5–132.1K | **125.8–138.4K** | 133.7–145.1K | ~1.7× slower | **~1.5× faster** |
| UPDATE | **26.5–40.9K** | **46.7–51.7K** | — | **269.6–279.8K** | **~7–10× slower** | ~5× faster |
| DELETE | 20.9–60.7K | 118.9–132.5K | — | 339.8–363.6K | ~6–14× slower | ~3–9× faster |
| READ | 51.8–58.1K | 105.8–119.2K | 69.6–100.0K | 89.0–89.4K | ~1.5× slower (Direct is faster) | ~5–8× faster |

**The headline:** INSERT is already within striking distance (and StructRow INSERT already matches or
beats SQLite), while **UPDATE is the real gap at ~5–10× slower**, and it is the operation the owner
called out first. DELETE shares the same root cause.

### 1.2 The root cause is structural, and it is already diagnosed

Every retrospective in this repository reaches the same conclusion, in its own words:

> "Root cause is structural: SharpCoreDB's row-store updates/deletes are row-copy based, while SQLite
> uses fixed-length C records with direct field offsets and in-place writes. This is the targeted
> v2.1+ engine work (in-place records), **not** something the runtime or allocations fix."
> — `V198_V20_V21_PERFORMANCE_COMPARISON.md` §3.3

So allocation trimming and SQL-parsing work are exhausted as levers here. The remaining work is in the
**storage layer's update semantics**.

### 1.3 What already landed (do not redo these)

| Item | Where | Result |
|---|---|---|
| WP1–WP7, WP9, WP14 (hot-path logging, prepared execution, allocation cuts, regex → generated, DI caching, index/read tuning, provider fast paths, StructRow reads, SQL batch-INSERT fast path) | `V2_PERFORMANCE_PLAN.md` §3 | SQL INSERT **54.5K → 98.2K (+80%)**; SQL READ gap vs SQLite closed ~16× → ~1.5× |
| #7/#8 single-pass DML | same §3.5 | SQL DELETE/UPDATE no longer materialize twice; PK fast path for `pk = value` |
| In-place UPDATE engine (fixed-width) | same §3.3 | FW updates no longer append a version: **0 file growth** where it previously grew +90 KB per 2,000 updates |
| `UpdateMultiple` → `TryOverwriteFieldsInPlace` with runtime offsets | same §3.4 (WP14) | Works even with a leading variable-length column, but on AppendOnly the engine still appends the new version |
| Auto engine routing hardened | `CHANGELOG.md` | `StorageEngineType.Auto` no longer selects PageBased for General/WriteHeavy (it is not OLTP-ready) |

**So half the structural fix exists.** `TryOverwriteFieldsInPlace` already knows how to patch a row in
place; the gaps are (a) which call paths reach it, and (b) the engine still appending underneath.

### 1.4 Two measurement defects that block attribution

These have to be fixed before any optimization, because right now the numbers cannot tell us where
the time goes:

1. **Machine noise is larger than the effects we are chasing.** The run-to-run spread is documented as
   ±20%, with DELETE ranging **21K–91K across runs of the same build**. "Small differences (≈±20%) are
   measurement noise, not signal" (`V198_V20_V21_PERFORMANCE_COMPARISON.md` §1).
2. **Two harnesses disagree by roughly an order of magnitude on UPDATE.** The comparative harness
   reports SQL UPDATE at **26.5–40.9K ops/s**, while the in-place-engine work reports "~3.5–5.3K
   ops/s with 0 file growth vs ~1.5K ops/s before". Both are real measurements of UPDATE; they are
   measuring different things (per-operation cost vs batch throughput). Until that is reconciled we
   cannot say whether the remaining cost is **row copying**, **fsync/WAL policy**, or **index
   maintenance** — and those three have completely different fixes.

---

## 2. Phase 0 — a trustworthy measurement protocol (prerequisite)

Small, mechanical, and it unblocks everything else. **Same lesson as the vector-search work: the
first attempt at that build optimization was aimed at the wrong phase until instrumentation said so.**

**Deliverables**

1. **One canonical protocol.** Fixed harness invocation, fixed dataset, N≥5 reps, report
   *min–median–max* (never a single run), discard the first (cold-JIT) rep, and record the machine's
   background-load state. Publish the JSON alongside the numbers, as the v2.0 run already does.
2. **Per-stage instrumentation of the write path**, behind a flag so it costs nothing when off. For
   UPDATE: `parse → plan → WHERE resolve → row locate (index) → row decode → in-place decision →
   engine write → WAL append/flush → index maintenance → commit`. For INSERT the same minus locate.
   This is the only way to answer the §1.4 question of *row copying vs fsync vs index maintenance*.
   **Implemented (2026-09-13):** `SharpCoreDB.Diagnostics.WritePathProfiler` — stages `validate`,
   `encode`, `index-maint`, `row-locate`, `in-place-patch`, `engine-write`, `wal-append`, `wal-flush`,
   `commit`; enabled with `WritePathProfiler.Enable()` or `SHARPCOREDB_WRITE_PROFILE=1`; **zero cost
   when off** (`Stamp()` returns 0, `Add` no-ops before reading a timestamp). Wired so far on the
   UPDATE path in `Table.CRUD.cs`; the INSERT path and the WAL flush are the next wiring points.
   **INSERT path wired (2026-09-15), and it moved attribution from ~19 % to ~89 % of a multi-row INSERT's
   wall time.** Added: `engine-write` + `index-maint` + `commit` in the batch critical sections (only one of
   the four had `engine-write`, and `index-maint`/`commit` had **no** writer anywhere), `parse` around the
   `VALUES` scan, `row-build` around the parser's literal-to-typed conversion, `commit` around
   `RunInStorageTransaction`'s `CommitSync`, and — from the storage work earlier the same day —
   `arena-write`/`arena-append`/`arena-load`/`validate-only`. `row-build` is the one stage that had to be
   **added**: folding it into `parse` would have merged two different costs under one number.
   Result on the multi-row workload (20,000 rows, 1,000 rows/statement, one profiled pass): `validate`
   34.2 %, **`index-maint` 18.2 %**, `row-build` 14.3 %, `parse` 10.7 %, `engine-write` 5.7 %, `commit` 4.3 %
   (0.98 ms per statement — the flush boundary the storage transaction moved), `row-locate` 1.6 %.
   **Still not wired:** `wal-append` and `wal-flush` have no writer at all, and the stage report remains a
   sum of stages rather than wall time where they nest (the outer `validate`/`encode` wrap the arena stages,
   so the raw total exceeds the pass).
   **Allocation attribution added (2026-09-15)** — the same `Stamp`/`Add` pair now also measures
   `GC.GetAllocatedBytesForCurrentThread()` per stage, which is what a 6.2 KB/row measurement needed: the
   time stages could not say where the garbage came from, and four hand-checked suspects (the PK key's
   `ToString`, the per-key index list, the UTF-8 key buffer, per-record encryption) each measured small or
   nil. It resolved the cost to `arena-write` **1,256 B/row** (for ~50 B of payload), `hash-index`
   ~950 B/row (`Dictionary` growth), `parse` 962 B/row and `row-build` 536 B/row, and three of those four
   are now reduced (see the CHANGELOG). The column is per-thread and therefore a **floor** — `Parallel.For`
   serialisation allocates on workers this counter does not see — and the report prints a warning if any
   checkpoint is left open rather than silently mis-attributing the next stage's bytes. **Next lever, by
   measurement:** `arena-append` at **63.3 ms** is one `FileOptions.WriteThrough` open per row for the
   overflow arena — the exact cost the append buffer removed for single-row INSERTs — but routing the arena
   through that buffer changes its durability window, so it needs the same owner decision as §5 item 2.
   Guarded by `WritePathProfilerTests` (free when off, attributes real workload, report ordering).
3. **Reconcile the two UPDATE harnesses** (§1.4) so absolute numbers are comparable across documents.
4. **A regression gate.** The benchmark must be runnable as a non-gating (nightly/manual) CI job so
   future work cannot silently regress INSERT/UPDATE the way UPDATE did between 2.0 and 2.1
   (26.5K vs 37.3K is inside the noise band, but the band is the problem).
   **Implemented (2026-09-15).** `--gate` runs the §2 protocol above against a committed baseline
   (`tests/benchmarks/SharpCoreDB.Benchmarks.Comparative/baselines/dual-mode-baseline.json`) and exits
   **0** (within tolerance), **1** (a metric regressed beyond `--gate-factor`, default **1.5**), or **2**
   (too noisy to conclude — the per-metric rep spread exceeded 2.5×). It prints min–median–max per metric
   before the verdict, because a run whose reps disagree by 4× cannot support a conclusion either way.
   `.github/workflows/benchmarks.yml` runs it nightly and on `workflow_dispatch`; it is its own workflow,
   so no branch-protection rule can require it and it can never block a merge.

   **Why it was not optional.** It was written *because* one of these regressions had already happened
   twice on this branch: the deferred-DELETE reconcile at `Table.Flush()` cost **4×** on random-key
   DELETE (294,185 → 70,248 ops/sec) and all 2,321 tests stayed green. The 1.5× tolerance follows from
   the same evidence — both real regressions were ≥2×, while the documented band is ±20%, so a tighter
   factor would have fired on noise instead.

   **Known limits, stated here rather than discovered later.** The committed baseline comes from one
   machine and absolute ops/sec do not transfer, so on a GitHub-hosted runner (a different CPU every run)
   the job is *trend* evidence, not a verdict; a fixed or self-hosted runner is required for the latter.
   Re-recording is manual (`--write-baseline`) and reviewable, never automatic — a baseline recorded
   *during* a regression silently blesses it for every later run.

**Acceptance:** reported numbers reproduce within ±10% on a quiet machine, and the per-stage
instrumentation accounts for ≥90% of wall time in a write loop.

5. **Both encryption modes in every number (§0.1-6).** The tool is
   `SharpCoreDB.Benchmarks.Comparative --dual-mode` — three arms (raw / default / at-rest records),
   medians over alternating reps, JSON archived under the project's `results/`. Every table this plan
   publishes reports
   **encrypted (default) and unencrypted (`NoEncryptMode=true`)** side by side, per operation, on the
   same run. Neither mode may be quoted alone: the difference is a product decision the user makes, so
   hiding either half of it would be the same mistake as quoting build times without recall.

---

## 3. Phase 1 — the encryption layer: a free fix, a decided posture, and an audit

**What this phase is now.** It started as "remove the encryption tax"; it is now three things, in this
order: an **audit** of what the encrypted default actually protects (§3-1c), a decision about the
posture (§3-1b, decided — §0.1-6), and one genuinely free optimisation on the read side (§3-1a).

**Correction to the first draft of this plan.** It framed this phase as "already quantified, lowest
risk, no semantic change". That was wrong, and reading the write path properly is what showed it:
`PersistenceConstants.cs` documents `NoEncryptMode=false` (= `DatabaseConfig.Default`) as
**encryption enabled**, so removing the work is a *security-posture and format decision*, not a free
optimization. It stays first because it is the largest single measured delta on the write path — but
it is split in two, and only 1a is free.

The A/B that sized it (`docs/benchmarks/default-config-pk.md`, 3 reps, same window, **only
`NoEncryptMode` differs**):

| Operation | Gain with `NoEncryptMode=true` |
|---|---:|
| UPDATE | **1.62×** |
| DELETE | **1.59×** |
| INSERT | **1.31×** |
| READ | 1.45× |

### 1a. Read side — exception-driven probing on the normal path *(free; do it)*

`Storage.ReadWrite.cs:99-124` and `Storage.PageCache.cs:146-167` do the following on **every**
plaintext file:

```csharp
var effectiveNoEncrypt = noEncrypt || this.noEncryption;      // both false by default
...
try { result = this.crypto.Decrypt(this.key, dataToCopy); }   // copy the buffer...
catch { result = /* copy it again, raw */ }                   // ...then throw, every single time
```

For a plaintext file the **`catch` is the normal path**: a buffer copy, a full `crypto.Decrypt`
attempt and an exception are paid per read only to discover there is nothing to decrypt. Reading the
magic header first (8 unambiguous bytes) turns it into a branch. `ReadAllBytes` is worse: it
allocates the whole file twice before giving up on decrypting it. **Nothing security-relevant
changes** — the same files decrypt, the same files pass through raw.

**Implemented (2026-09-13):** `Storage` now memoises the verdict per path
(`Services/Storage.ReadEncryption.cs`), so `ReadBytes` and the page-cache read probe **once** and then
take a branch; the whole-file writers re-seed the verdict (`Write`/`WriteBytes` — plaintext is recorded
outright, otherwise the answer is forgotten so the next read re-probes), which makes a stale verdict
impossible. Guarded by `StorageReadEncryptionCacheTests` with a counting `ICryptoService` double: ten
reads of a plaintext file cost **one** decrypt attempt, `NoEncryptMode` costs **zero**, an encrypted
file still decrypts once per read, and a write that changes a file's framing is picked up.

The *throughput* effect is deliberately **not** claimed yet: §3-1d showed the raw-versus-default
difference sits inside this machine's noise band, so it needs the higher-rep protocol before/after.

### 1b. Write side — the encryption posture *(decided: security stays the default, and must be real)*

Four write-side sites encrypt purely on `!NoEncryptMode`:

| Site | Who calls it |
|---|---|
| `Storage.ReadWrite.cs:29` / `:41` (`Write`) | `meta.dat` (`Database.Core.cs:592`), users file (`UserService.cs:51`) |
| `Storage.ReadWrite.cs:158` / `:170` (`WriteBytes`) | `Core/File/TransactionBuffer.cs:370` — **every buffered write flushed out of a transaction** |

Meanwhile the per-record table path is gated *differently*: `Storage.Append.cs:94`
(`UseRecordEncryption = enableAtRestRecordEncryption && !noEncryption`) is **false by default**, so
record appends are written plaintext — while `PersistenceConstants.cs` documents `NoEncryptMode=false`
as "per-record AES-256-GCM ciphertext behind the magic header". **The two policies disagree.**

**Decision (§0.1-6):** security is the default and the opt-out is explicit.

- `NoEncryptMode = false` (default) = **encrypted, and it must mean it** — the documentation may not
  promise more protection than the code delivers, and the default may not pay AES on some paths while
  writing plaintext on others.
- `NoEncryptMode = true` = the documented **raw-speed** option, kept first-class for benchmarks and
  speed-critical deployments.
- Both are benchmarked and published side by side, per operation, in every number this plan produces.

### 1c. Security-consistency audit — **measured, and it confirms the worst reading**

Deliverable 1 of this audit (below) was executed on 2026-09-13 with a temporary probe: create a
database in three configurations, insert 200 rows whose text value contains a unique marker
(`ZZMARKERSECRETZZ`), then scan every file for that marker as UTF-8 **and** for the 8-byte record magic
(`53 43 44 42` = "SCDB").

| Config | `docs.dat` (records) | `docs.ovf` (overflow arena) | `meta.dat` |
|---|---|---|---|
| **A — default** (`NoEncryptMode=false`) | 5,600 B · **no magic header** | 4,490 B · **marker found as PLAINTEXT at offset 4** | 536 B |
| B — `NoEncryptMode=true` (raw) | 5,600 B · no magic header | 4,490 B · **plaintext** | 508 B |
| C — `+EnableAtRestRecordEncryption=true` | 11,208 B · **magic header, no plaintext** | 10,098 B · encrypted | 536 B |

**What this proves:**

1. **The default does not encrypt the user's data.** In configuration A the inserted text value is
   found verbatim in `docs.ovf`; the record files carry none of the per-record encryption magic. Table
   data — records *and* the overflow arena — is plaintext on disk by default.
2. **A and B are byte-identical on the data files** (5,600 B / 4,490 B, both plaintext). The difference
   between the two modes on those paths is **zero**, which means per-record data encryption is
   controlled exclusively by `EnableAtRestRecordEncryption` (default **off**) — not by `NoEncryptMode`.
3. **Metadata is encrypted by default.** `meta.dat` is 28 B larger in A and C than in B (536 vs 508) —
   the AES-GCM nonce+tag — i.e. the `Storage.Write` path really does encrypt, exactly as
   `Storage.ReadWrite.cs:29/41` says.
4. **Per-record encryption roughly doubles the file size** (5,600 → 11,208 B) *and* removes the
   plaintext, so configuration C is what "encryption with the magic header" actually means.

**Conclusion, and why it outranks the performance work:** the default pays AES on the metadata and
transaction paths **while the user's data sits on disk in the clear**. That is the cost *without* the
guarantee — the opposite of the product promise that security is built in — and it means the measured
"encryption costs 1.31× INSERT / 1.62× UPDATE" cannot be read as "the price of encrypting your data",
because the data is not encrypted.

**Remaining deliverables of the audit:**

1. ~~Establish empirically what a default database writes~~ — **done, table above**.
2. **Make the coverage consistent with the decided posture (§0.1-6 = "the default must be true").**
   Per-record at-rest encryption becomes what `NoEncryptMode=false` already claims it is, so the
   default protects table data, the arena and metadata alike — and `NoEncryptMode=true` becomes the
   single, documented raw-speed escape. The alternative (rename the mode so the default stops claiming
   protection it does not provide) is recorded only as the fallback if the measured cost turns out to
   be unacceptable; it is not the direction the owner chose.

   **Measured blast radius of the naive flip (2026-09-13).** Changing *only* the
   `EnableAtRestRecordEncryption` default from `false` to `true` — literally one initializer — produced
   **≥45 test failures across 12 classes**, among them `ReopenRoundTripMatrixTests` (the durability
   matrix), `SqlInPlaceUpdateTests`, `FixedWidthPatchTests` (in-place patching, "file does not grow"),
   `DirectoryFixedWidthDefaultTests` (reopen with an empty value) and `SingleFileDirectoryParityTests`.
   These are **functional** failures — durability, in-place field patching, directory/single-file
   parity — not tests that merely assert "plaintext is expected". The change was reverted and the core
   suite is green again (**1791 total, 0 failed, 16 skipped**).

   **Conclusion: making the default true is a work package, not a config flip.** The write path, the
   in-place patch path, the overflow arena and the reopen path all assume plaintext records and must be
   made encryption-aware first, with the reopen round-trip matrix as the gate. *(That work package has
   since landed — see the two re-measurements below — and the default was flipped to
   `true` on 2026-09-13.)*
   **Re-measured 2026-09-13 after §3-1f/§3-1g/§3-1h/§3-1i** (the same one-line flip, reverted again):
   the blast radius dropped from **≥45 failures across 12 classes** to **21 across 10** — the
   durability matrix, the contiguous patch paths and the at-rest scan/index defects that made up the
   first wave are closed.

   **Re-measured again after the second wave** (at-rest index build, `GetAllRecords` offsets and
   compaction — see the list below): **21 → 2 failures**, and the last two were then resolved:

   1. `EncryptionCoverageTests(Default)` — the §3-1c-4 tripwire, updated in the same commit as the flip:
      the default is now *expected* to keep a known inserted value out of the table data files.
   2. `CompiledQueryTests.CompiledQuery_1000RepeatedSelects_CompletesUnder8ms` — a latency budget an
      at-rest default blew, because a full-scan-shaped compiled query decrypted the whole data file per
      execution. The cause was the read path, and the fix deliberately is **not** a cache (invalidation
      across the append/in-place/tombstone paths would be too easy to get wrong): `ReadAllRecords`
      opened **two `FileStream`s per record** — one for the length prefix, one for the payload — so 1000
      queries over 100 rows meant ~200,000 handle open/close pairs. It now reads the file once into a
      buffer and walks it in memory: **1000 compiled queries went from >2000 ms to 552 ms** (budget
      2000 ms), with the plaintext walk byte-for-byte unchanged and very large files still on the
      incremental path.

   **Result: the flip is done.** `EnableAtRestRecordEncryption` defaults to `true`, and the full suite is
   green with it — **1834 tests, 0 failed, 16 skipped**. The default now protects table data, the
   overflow arena, metadata and transaction files alike, and `NoEncryptMode=true` is the single
   documented raw-speed opt-out.

   **The second wave consisted of three pre-existing defects of the opt-in flag itself**, all guarded
   by `AreRecordsEncrypted` so plaintext behaviour is byte-for-byte untouched:
   - the lazily built hash index came out **empty** for an at-rest file (the raw walk read the magic
     header as a record length), so every indexed lookup missed — `EnsureIndexLoaded` now walks
     `ReadAllRecords` for at-rest files;
   - `AppendOnlyEngine.GetAllRecords` yielded **buffer** offsets for an at-rest file while every caller
     resolves records by **physical** offset, so the StructRow numeric/SIMD paths filtered every row
     away — it now yields `(physical offset, decrypted payload)` pairs;
   - `AppendOnlyEngine.CompactTable` matched its active set (physical positions) against the decrypted
     buffer walk, so **compaction dropped nearly every row** of an at-rest table (and would have
     rewritten the file as plaintext); it now collects from the records' physical offsets, and the
     brand-new temp file keeps the at-rest format because `Storage` encrypts brand-new files.
3. ~~Document the two modes as a first-class choice~~ — **done:** the caveat now lives on
   `DatabaseConfig.EnableAtRestRecordEncryption` itself (the property states that the default stores
   table data as plaintext while metadata is encrypted, what enabling it costs, and that flipping the
   default is a work package), so it cannot be missed by anyone who never opens this plan.
4. ~~A test that fails if the promise regresses~~ — **done:** `EncryptionCoverageTests` (3 cases) scans
   the table payload files (`*.dat` / `*.ovf`; journal/WAL files are deliberately excluded because they
   legitimately contain the INSERT statement text) for a known inserted value and asserts the documented
   posture: default ⇒ present, `NoEncryptMode=true` ⇒ present, at-rest ⇒ **absent**. It simultaneously
   pins that the flag covers the overflow arena too, and that the rows round-trip through a reopen in
   every configuration.

Only after (2) — the posture is made true — does "encryption costs 1.3–1.6×" become a number that
means something, and only then do §3-1a and the rest of the plan proceed.

### Acceptance
1a: the decrypt attempt disappears from the profile for plaintext files, with no measurable regression
and every encryption test green. 1b: whichever posture is chosen, the gain is measured with the §2
protocol and all guards above pass.

---

### 1d. The measured cost of each mode *(first dual-mode runs, 2026-09-13)*

`SharpCoreDB.Benchmarks.Comparative --dual-mode` (added for this plan) runs one CRUD workload in three
configurations and prints the columns together: medians over 3 reps per arm, with the arm order
alternated per rep so machine drift hits every arm. Two consecutive runs on the same machine:

| Run | operation | raw | default | at-rest | raw/default | raw/at-rest |
|---|---|---:|---:|---:|---:|---:|
| 1 | INSERT | 142,906 | 145,751 | 115,805 | 0.98× | **1.23×** |
| 1 | READ | 125,484 | 92,719 | 11,170 | 1.35× | **11.23×** |
| 1 | UPDATE | 150,636 | 153,564 | 10,460 | 0.98× | **14.40×** |
| 1 | DELETE | 148,734 | 149,984 | 10,564 | 0.99× | **14.08×** |
| 2 | INSERT | 147,541 | 142,747 | 122,374 | 1.03× | **1.21×** |
| 2 | READ | 130,586 | 129,414 | 11,185 | 1.01× | **11.68×** |
| 2 | UPDATE | 110,556 | 151,717 | 10,370 | 0.73× | **10.66×** |
| 2 | DELETE | 138,266 | 94,732 | 10,792 | 1.46× | **12.81×** |

**What reproduces, and therefore what may be concluded:**

- **At-rest per-record encryption is nearly free for INSERT (+21–23%) and catastrophically expensive
  for READ/UPDATE/DELETE: ~11–14× slower** (≈10.5K ops/s vs ≈130–150K). Both runs agree, and the
  run-to-run spread is small relative to the effect.
- That completes the picture for §0.1-6: "make the default true" is not a test-fixing exercise.
  Flipping the default today would multiply the cost of exactly the operations this plan targets —
  UPDATE by an order of magnitude — because the read, in-place-update and delete paths are not
  encryption-aware (the same root cause as the ≥45 failures in §3-1c). **The plan's order (make the
  paths encryption-aware first, then flip the default) is now supported by measurement, not assumption.**
- INSERT being cheap under at-rest encryption is good news for §5's "beat SQLite" target: the append
  path already handles the encrypted framing.

**What does NOT reproduce, and therefore must not be quoted as a result:**

- The **raw versus default** column. Across the two runs it moved between 0.73× and 1.46× **in both
  directions** (run 1: default 1.35× slower on READ; run 2: default 1.37× *faster* on UPDATE) — the
  machine's documented ±20%+ noise band, not signal. Resolving a 1.0–1.5× effect needs more reps
  and/or a quiet box. **Do not publish a raw-versus-default claim from these runs.**
- Consequence for the §3-1a READ hypothesis: run 1's 1.35× is *consistent with* the
  decrypt-attempt-with-`catch` waste found by reading the code, but it is not proof. §3-1a must still be
  measured with a proper protocol (more reps) before and after.

**Harness caveat:** `--dual-mode` uses the comparative harness's tuned configuration (`BuildConfig`:
async durability, group-commit off, high-speed insert mode, page cache, memory mapping, validation
disabled), so absolute ops/sec are *not* product defaults. That is acceptable for this table because the
arms differ **only** in encryption settings — which is what the A/B isolates — but a pure-default variant
is a separate run (§2's protocol, item 5).

Evidence: `tests/benchmarks/SharpCoreDB.Benchmarks.Comparative/results/dual-mode-*.json`, archived next
to the `comparative_*.json` evidence that earlier benchmark documents cite.

#### 1d-2. Re-measured with the flipped default, and what the "11× tax" really was *(2026-09-13)*

Once §3-1c deliverable 2 landed, `default` and `at-rest` are the same configuration, so the harness now
runs **two** arms (`raw` = `NoEncryptMode=true`, `default` = the shipped encrypted default; it reads the
product default from a fresh `DatabaseConfig` so an arm can never drift from it). The first two-arm run
reproduced the old at-rest column exactly — and then the cause turned out not to be encryption:

| operation | raw | default (first two-arm run) | ratio |
|---|---:|---:|---:|
| INSERT | 133,322 | 119,431 | 1.12× |
| READ | 124,897 | 11,035 | **11.32×** |
| UPDATE | 112,741 | 10,418 | **10.82×** |
| DELETE | 128,722 | 10,843 | **11.87×** |

`Storage.ReadBytesFrom` — every per-record read — asked `FileHasEncryptedHeader` whether the file is
encrypted, and that probe did `File.Exists` + `new FileInfo(path).Length` + a fresh `FileStream`
open/read/close, **per record**. The `raw` arm short-circuits the probe (`UseRecordEncryption && …`),
which is why only the encrypted arm paid it. A micro-probe isolated it: the storage layer's per-record
read cost **57 µs encrypted vs 4.9 µs raw**, while `AesGcm` construction (the next suspect) measures
0.77 µs.

**Fix:** the probe now does exactly one 8-byte `RandomAccess.Read` through the cached read handle — no
`File.Exists`, no `FileInfo`, no handle open. A short read is the same answer the removed length check
gave, and a failed handle open the same answer `File.Exists` gave. There is deliberately **no cache** of
the verdict: the answer can change when a file is created or replaced (compaction rewrites through a
brand-new temp file), and every candidate invalidation point is a chance to serve a stale format verdict.

| operation | raw | default (after) | ratio before → after |
|---|---:|---:|---:|
| INSERT | 137,197 | 122,077 | 1.12× → **1.12×** |
| READ | 119,588 | 77,746 | 11.32× → **1.54×** |
| UPDATE | 104,890 | 58,972 | 10.82× → **1.78×** |
| DELETE | 119,039 | 63,646 | 11.87× → **1.87×** |

**What this changes:** the "catastrophically expensive at-rest READ/UPDATE/DELETE" reading in the table
above is **an artifact of that probe, not the price of encryption**. The honest cost of the shipped
encrypted default is ≈1.5–1.9× on READ/UPDATE/DELETE and ≈1.1× on INSERT on this machine (3 reps per arm,
one run — a *published* number still wants §2's protocol with more reps, and the raw-vs-default column is
the noise-prone one). It also reframes §8: the UPDATE target (≥120K) is now a row-copy problem rather than
an encryption problem, since the default already runs at ~59K on this harness.

Note that §3-1e's 5–7× at-rest UPDATE diagnosis (the loss of the contiguous bulk path) is a *different*
shape — ascending `pk = literal` batches — and §3-1f's fix stands on its own measurement (261.6 ms →
43.3 ms). This section is the random-key per-row workload.

Evidence: `dual-mode-20260914_174541.json` (before) and `dual-mode-20260914_180312.json` (after), both
archived in the benchmark results directory.

---

### 1e. Why at-rest UPDATE costs 5–7× — diagnosed *(2026-09-13)*

A targeted diagnosis on a fixed-width 2,000-row table (insert 2,000 rows, then `UPDATE ... WHERE id = k`
through `ExecuteBatchSQL`), with `WritePathProfiler` enabled and the directory size sampled before and
after the update phase:

| config | update phase (2,000 rows) | bytes after insert → after update | growth |
|---|---|---|---|
| raw (`NoEncryptMode=true`) | 44.8 ms | 73,431 → 73,431 | **0%** |
| default (`NoEncryptMode=false`) | 31.3 ms | 73,459 → 73,459 | **0%** |
| at-rest (`+EnableAtRestRecordEncryption`) | **234.8 ms** | 185,475 → 185,475 | **0%** |

Four conclusions, the second of which refutes the working hypothesis:

1. **At-rest UPDATE is 5.2–7.5× slower** on this workload — the same direction and order as the
   benchmark's 10.7–14.4× (§1d), on a much smaller table.
2. **It is NOT write amplification.** File growth during the update phase is **0% in every
   configuration**, so records are still patched in place. "Encrypted rows append a new version" is
   therefore wrong, and so is the simpler story that the cost is row copying.
3. **It is per-row fallback work.** `Table.UpdateMultiple` carries several fast paths that are
   explicitly gated on *plaintext* records — `TryBulkUpdateContiguousFixedWidth` ("plaintext
   fixed-width table with physically adjacent PK-ordered records"), `TryLoadWholeFileForRowAccess` /
   `TrySlicePayloadFromFile` ("reading the small plaintext file once"), and `fastPatch` (raw bytes at
   cached field offsets). With the per-record magic header present none of them can apply, so every
   row falls through to the generic machinery: deserialize → patch → serialize → **encrypt** → write,
   with per-record AEAD on top. That is the 5–7×.
4. **The disk cost is real and separate:** the same data occupies **185,475 vs 73,459 bytes (2.5×)**
   with per-record encryption — framing overhead paid on every record of a small-row table.

**Actionable next step** (previously the vague "make the paths encryption-aware", now precise): teach
the three plaintext-gated fast paths to operate on a decrypted record payload — decrypt once, apply
the existing raw-byte patch, re-encrypt — instead of falling through to the generic per-row path.

**Instrumentation gap this diagnosis exposed — now closed, and it sharpened the answer.** The
profiler's UPDATE wiring sat on the single-row `Table.Update` path, but batch workloads go through
`Table.UpdateMultiple` (in `Table.CRUD.cs`, called from `Database.Batch.cs`), so the profile accounted
for only ~4 ms of a 235 ms phase. Both of that method's paths are now attributed — the contiguous bulk
patch (`RowLocate`) and the per-row in-place attempt (`InPlacePatch` + `EngineWrite`) — and
`WritePathProfilerTests` pins both down (a PK-ordered batch for the first, a PK-less table with an
indexed WHERE column for the second).

Writing that test produced the sharper diagnosis: **for a PK-ordered fixed-width batch — the shape the
benchmark uses — the contiguous fast path takes the whole batch and returns early.** Zero per-row work,
a single attributed call. That path is gated on plaintext records, so an encrypted file cannot use it
and every row falls into the per-row in-place loop instead. **The 5–7× is therefore mostly the loss of
the bulk path, not slow per-row code** — which is also why file growth is 0%: the per-row path still
patches in place, it just does it one row at a time, with a decrypt and an encrypt around each row.

**The fix is consequently narrower than "make everything encryption-aware":** teach
`TryBulkUpdateContiguousFixedWidth` to operate on a decrypted run of record payloads — decrypt the run,
apply the existing contiguous patch, re-encrypt — and keep the per-row loop as the (already correct)
fallback. That is the next work item, and it is now scoped to one method plus its re-encryption.

---

### 1f. The encryption-aware bulk path — implemented *(2026-09-13)*

**Goal:** make `TryBulkUpdateContiguousFixedWidth` work on encrypted records, so an at-rest database
regains the bulk path that §3-1e identified as the 5–7×.

**Landed as:** one new `IStorage` member (`byte[]? DecryptRecordPayload(byte[] payload)`, default
`null`, implemented by `Storage` as a passthrough to its private `DecryptRecord`); the plaintext-only
preconditions dropped from *both* contiguous gates (`TryBulkUpdateContiguousFixedWidth` and
`TryBulkDeleteContiguousFixedWidth`); and `TryReadContiguousFixedWidthRecords` taught the encrypted
stride (`stride + AesGcmEncryption.OverheadSize`, i.e. +28 — constant, because a fixed-width record's
plaintext length never changes). It reads the physical span once, then decrypts and repacks each record
as `[len:4][plaintext]`, so every caller's slicing stays identical in both modes. The write side needed
nothing: it already went through `TryUpdateInPlaceSameLength`, which encrypts — which is exactly why
per-row in-place updates kept working at rest with 0% growth while the bulk path refused.

**Verified:** `FixedWidthBulkUpdateTests` + `FixedWidthBulkDeleteTests` + `FixedWidthPatchTests` +
`ReopenRoundTripMatrixTests` + `SingleFileDirectoryParityTests` = 44 tests, 0 failed. Two tests were
rewritten to pin the new behaviour: `PerRecordEncryption_UsesContiguousFastPath_AndPersists` (the bulk
counter advances on an at-rest table, and a reopen proves the patched records decrypt back to the new
values) and `PerRecordEncryption_ContiguousDeletes_EngageBulkPath_AndSurviveReopen`.

**A blocking, pre-existing defect surfaced while validating this — see §3-1g.**

**Measured** (same machine, same probe — 2,000 ascending `pk = literal` UPDATEs, i.e. the §3-1e shape;
`src/` swapped between the two commits, nothing else changed):

| src | arm | time | bulk batches | vs raw |
|---|---|---|---|---|
| `747aae09` (before) | raw | 43.6 ms | 1 | 1.00× |
| `747aae09` (before) | at-rest | **261.6 ms** | **0** | **6.00×** |
| `ae83be57` (this change) | raw | 47.2 ms | 1 | 1.00× |
| `ae83be57` (this change) | **at-rest** | **43.3 ms** | **1** | **0.92×** |

The §3-1e figure (5.2–7.5×) reproduced at **6.00×** before the change; afterwards the at-rest penalty
for this shape is gone — the bulk path now engages on the ciphertext span (`batches=1`) and the
per-record AEAD open/seal is invisible next to the per-row reads it replaces (~218 ms of 261.6 ms
eliminated).

The whole-workload dual-mode run (`results/dual-mode-20260914_064721.json`, archived with this change)
still reports at-rest UPDATE at **12.75×** raw, but that workload updates **random** keys: its records
are not physically adjacent, so the contiguous path cannot engage by design and what it measures is the
per-row loop — which §3-1f does not change. The remaining at-rest costs stay honest and unchanged:
INSERT at **1.11×** raw (per-record AEAD, now visible as its own line) and the ~2.5× disk framing from
§3-1c.

### 1g. A blocking, pre-existing defect: at-rest tables cannot be scanned *(found 2026-09-13)*

While validating §3-1f, a full scan of an at-rest database returned **zero rows**. Isolated with a
throwaway probe (insert → flush → query), both modes:

| Case | plaintext | `EnableAtRestRecordEncryption = true` |
|---|---|---|
| `SELECT id`, in-session, 20 rows | 20 | **0** |
| `SELECT id`, after reopen, 2000 rows | 2000 | **0** |
| `SELECT COUNT(*)`, after reopen | 2000 | **0** |
| one single-row DELETE → reopen → scan | 1999 | **0** |
| 1000-row contiguous DELETE → reopen → scan | 1000 | **0** |

The rows are not gone: PK lookups still resolve (after a reopen, `WHERE id = 1500` returns exactly one
row, read off disk). Only the *walk* sees nothing.

**Root cause (the first hypothesis — "the scan reads the magic header as a record length" — was wrong;
the walk is fed an already decrypted buffer):** `Storage.ReadBytes` returns, for an at-rest table file,
a *decrypted, header-stripped re-pack* of the records (`DecryptTableFileToPlaintext`, built from
`ReadAllRecords`). The columnar scan then walks that buffer and, for every row, keeps it only when
`Index.Search(pk).Value == <record position in the buffer>` — a "is this the current version?" test
comparing a **buffer offset** against a **physical file offset**. For an at-rest file those differ by
the 8-byte header plus 28 bytes of GCM frame per preceding record, so **every** row failed the test and
the scan returned nothing. Same shape in the parallel scan and in the `StructRow` scan. Second
consequence: hash indexes are rebuilt **from that scan**, so they came back empty after a reopen on an
at-rest database (`WHERE name = 'user1500'` empty after reopen, while `WHERE id = 1500` worked).

**Fixed as:** `IStorage.ReadBytesWithRecordOffsets(path, noEncrypt, out long[]? physicalOffsets)` —
default returns the plain buffer with a null map; `Storage` returns the decrypted buffer **plus each
record's physical offset** in walk order (plaintext files keep a null map, because there the buffer
offset already is the physical offset, and `noEncrypt` is honoured so raw readers are unchanged). The
three stale-version checks now compare against the physical offset: `ScanRowsWithSimdAndFilterStale`
(which also advances its record ordinal for the zero-length and early-WHERE skip paths, or the map
would slip by one), `ExtractValidColumnarRows`, and the parallel scan — the last one takes the
sequential scan for at-rest files, since its partitions work on buffer offsets. `ExtractValidColumnarRows`
also learned to skip tombstones and empty slots instead of ending the scan at the first one.

**Verified:** new `AtRestScanTests` (8 cases) — scan + `COUNT(*)` in-session and after reopen, scan after
a single and a contiguous batch DELETE, hash-index lookup after reopen, filtered scans through the
early-WHERE paths (with a full scan after them to prove the offset map stayed aligned), and the
`StructRow` scan — each expectation run in **both** modes, all green.

**Not a regression from §3-1f, and not from §3-1a:** the probe reproduced identically with this work
stashed, and again with `src/` checked out at `8e6ab4fe` (before the read-encryption memoisation). The
defect is older than this plan; it went unnoticed because §3-1c established that the *default* config
stores records as plaintext, so almost nothing exercises `EnableAtRestRecordEncryption = true`. With it
fixed, §3-1c deliverable 2 (flip at-rest to the default) is **no longer blocked by scans**.

### 1h. The numeric-WHERE defect — fixed *(found and fixed 2026-09-13)*

Writing the §3-1g tests surfaced a pre-existing, mode-independent defect: a **simple equality on a
numeric column was compared as TEXT**. Probe: ten rows with `score = i * 0.5`, then a simple WHERE —

| query | raw | at-rest |
|---|---|---|
| `WHERE score = 5.0` | **0 rows** | **0 rows** |
| `WHERE score = 5` | 1 row | 1 row |
| `WHERE id = 10` (control) | 1 row | 1 row |
| `WHERE age = 110` (control, INTEGER) | 1 row | 1 row |

**Root cause:** `Table.Scanning.EvaluateWhere` compared the row value's *text* against the literal
(`case "=": return rowValue.ToString() == value;`) — and `double.ToString()` renders 5.0 as `"5"`, so
`score = 5.0` could never match while `score = 5` matched by accident. The ordering operators
(`CompareValues`) parsed the literal with `CultureInfo.CurrentCulture`, so on a comma-decimal machine a
literal like `875.0` did not even parse. `WHERE price = 19.99` therefore returned nothing — a silent
wrong answer for every real-valued column.

**Fixed as:** `ValuesEqual` / `CompareValues` delegate to a new `TryCompareNumeric` that compares
numbers **numerically** against an invariant-culture parse of the literal (int, long, short, byte,
double, float, decimal), falling back to the historical ordinal string comparison for non-numeric values
and unparseable literals. String columns are untouched (a numeric-looking literal is not coerced).

**Verified:** new `NumericWhereEqualityTests` (19 cases) — every equivalent literal form (`5`, `5.0`,
`5.00`, `5.000`), a non-representable decimal (`19.99`), all six comparison operators with decimal
literals, INTEGER equality, unchanged string semantics, an explicit `nl-NL` culture run, and a
storage-layout matrix (raw / at-rest × fixed-width / variable-length).

### 1i. A second defect the same tests exposed: the hash-index build — fixed *(found and fixed 2026-09-13)*

`CREATE TABLE` registers a hash index for **every** column (`SqlParser.DDL.cs:425/432`) and
`Table.EnsureIndexLoaded` builds it lazily on the first query by walking the raw data file. That walk
decoded each record with the **variable-length** record parser, so on a default (fixed-width) table it
only indexed the rows that happened to parse correctly — and because the query planner prefers the hash
index, a plain `WHERE <non-unique column> = value` returned a **single row instead of every match**.

Evidence (identical data, identical literals, five rows, three of them `age = 25`):

| query | fixed-width (default) | variable-length |
|---|---|---|
| `WHERE age = 25` | **1 row** | 3 rows |
| `WHERE age = 25 AND id <= 5` (no index path) | 3 rows | 3 rows |
| `WHERE score = 5.0` where three rows share the value | 3 rows | 3 rows |

The build also stopped at the **first tombstone** (`length <= 0 → break`), which hid every live row
physically behind a deleted one from the index.

**Fixed as:** the build decodes with the layout the records were written in
(`DeserializeRowFixedWidth` for fixed-width tables, `DeserializeRowFromSpan` otherwise), skips tombstoned
slots (`length < 0` → skip `-length` bytes) and empty slots instead of ending the scan.

**Verified:** new `HashIndexBuildTests` (4 cases, both layouts) — duplicate-value equality on INTEGER and
TEXT columns, a decimal literal on REAL, and a delete-then-reopen run that proves the tombstone neither
hides later rows nor resurrects the deleted one. The two `NumericWhereEqualityTests` cases that had
exposed it (`age = 25` with three matches) now pass unchanged.

---

### 1f-h. §1f hand-over design, kept for the record *(superseded by §1f — the line numbers below are stale)*

**Where it stops today:** one precondition, `Table.CRUD.cs:2437` —
`this.storage.AreRecordsEncrypted(DataFile) ||` — rejects the entire path. Everything after it is
already encryption-agnostic in shape:

| Piece | Location | What it assumes |
|---|---|---|
| `stride = 4L + layout.FixedSize` | `:2445` | the *physical* distance between two records; used for the range read, the patch slice and the index re-pointing |
| `TryReadContiguousFixedWidthRecords` | `:3829` | ONE `storage.ReadBytesRange`, then verifies each record by its 4-byte prefix (`== layout.FixedSize`) and its decoded PK slot |
| patch loop | `:2532-2540` | copies the payload, patches it with the existing `TryOverwriteFixedWidthInPlace`, writes it back through `engine.TryUpdateInPlaceSameLength` |
| index re-pointing | `:2560-2586` | reads the old slot values from the returned buffer with the same stride |

The important part: **the write goes through `TryUpdateInPlaceSameLength`, the same API the per-row path
uses — and that one already encrypts** (`Storage.Append` → `ShouldEncryptWrites` / `EncryptRecord`).
That is precisely why per-row in-place updates keep working at rest with 0% growth (§3-1e). So the write
half needs nothing.

**The change, in two parts:**

1. **Stride.** For an encrypted file the physical record is `[len_cipher:4][nonce(12)][cipher][tag(16)]`
   (`PersistenceConstants`), so the physical stride is `layout.FixedSize + 4 + 28` and the length prefix
   holds `FixedSize + 28`. A fixed-width record's plaintext length never changes, so the ciphertext
   length never changes either — **the physical stride is constant too**, which is what keeps an
   equal-length in-place overwrite valid.
2. **Layout of the buffer handed back.** The caller slices with the *plaintext* stride
   (`i * stride + 4 + offset`), so the range read must return a plaintext-laid-out buffer when the file
   is encrypted: read the physical span once (`physicalStride * count`) and, per record, decrypt the
   payload and repack it as `[len:4][plaintext]` into a buffer with the plaintext stride. That preserves
   the single-I/O-read advantage; the added cost is one copy and one AEAD decrypt per record, in memory.

**Two small enablers:** `Table` needs to decrypt a single record payload (`Storage.DecryptRecord` is
private — expose it as `internal`, or route through the `ReadAllBytes` path that already returns the
whole file with records decrypted), and the DELETE mirror `TryBulkDeleteContiguousFixedWidth` (`:3907`)
carries the identical plaintext gate and should get the same treatment.

**Gate before this can ship:** `FixedWidthBulkUpdateTests`, `FixedWidthPatchTests`,
`ReopenRoundTripMatrixTests` and `SingleFileDirectoryParityTests` all green **with
`EnableAtRestRecordEncryption = true`**, then re-run the 2,000-row measurement from §3-1e. Expected:
most of the 5–7× gone (the bulk path returns), leaving the honest remainder — per-record AEAD (measured
at +21–23% on INSERT) and the 2.5× disk framing.

---

## 4. Phase 2 — the structural fix: in-place UPDATE on the SQL path

**Goal:** stop copying rows on update. Half of this already exists (`TryOverwriteFieldsInPlace`,
runtime offsets, `TotalInPlacePatches` instrumentation, PK fast path); the remaining work is coverage
and the engine underneath it.

### 4a. Route the SQL UPDATE path through the existing in-place machinery — **verified: it already is**

`#7/#8` gave `ExecuteUpdate` a single-pass `UpdateAffectedCount(where, updates)` and a PK fast path for
`pk = value`, and WP14 wired `UpdateMultiple` to `TryOverwriteFieldsInPlace`. The general SQL UPDATE path
then reaches `UpdateColumnarRow`, which locates the row and writes it through `engine.TryUpdateInPlace`
(`Table.CRUD.cs:1712`), appending only when the engine refuses (length changed, transaction active). This
plan asked for that to *become* the default; measurement (2026-09-13) shows it already is. 2,000 rows with
an indexed TEXT column, 2,000 batch `UPDATE … WHERE id = i`, shipped default config:

| updated column | raw (`NoEncryptMode=true`) | default (encrypted) | file growth | in place |
|---|---:|---:|---:|---|
| `score = 9.5` (REAL) | 32,449 ops/s | 56,259 | **0 B** | ✓ |
| `age = 33` (INTEGER) | 152,220 | 46,215 | **0 B** | ✓ |
| `name = 'Test0001'` (same length) | 10,420 | 9,641 | **0 B** | ✓ |
| `name = 'much-longer-name-0001'` | 71,868 | 51,754 | **0 B** | ✓ |

Zero growth in every shape *is* the §4a evidence: no update falls back to the append path, and the old
"the append path grew +90 KB per 2,000 updates" figure no longer reproduces. The overflow arena does its
part too — a same-length TEXT update reuses the freed block in place (B6 free-list), which is why the
growth stays at zero there as well.

**What the profile says instead** (`WritePathProfiler.Reset/Enable/Snapshot` around the batch):

| shape | wall time | profiled write stages |
|---|---:|---|
| same-length TEXT | 210 ms | in-place-patch 17 ms/2000 + engine-write 5 ms/2000 → **~10%** |
| longer TEXT | 29 ms | in-place-patch 4 ms + engine-write 3 ms → ~24% |
| INTEGER | 40 ms | row-locate 30 ms (batch fast path) |

So the write path is **no longer the UPDATE bottleneck**: for the worst shape ~90% of the wall time sits
in the SQL/batch layer above it, and the same-length TEXT shape is a **10× outlier** against the
different-length shape (10.4K vs 71.9K ops/s in raw) although both end in place with zero growth.

**That outlier is now fixed, and it was the arena — not the SQL layer.** Splitting the timing into
execution versus flush killed the "flush" theory (1–2 ms of a 249 ms batch), and the hash index turned out
not to be the cause either (8.9K with it, 11.3K without). What differed was *which* write the update
needed: a different-length TEXT value appends a new arena block (fast, buffered append), while a
same-length value **reuses a freed block in place** — and `Storage.WriteRecordInPlace` special-cased the
arena file to open and close a **fresh `FileStream` per overwrite**, i.e. one handle open/close pair per
row. It now uses the cached write handle like every other table file (safe because whole-file replacement
drops handles — the `InvalidateFileHandles` work from §1d-2, which is exactly what the special case was
guarding against):

| shape (raw, 2,000 rows) | before | after |
|---|---:|---:|
| same-length TEXT, indexed column | 8,902 ops/s | **19,109 ops/s** |
| same-length TEXT, no index | 11,293 ops/s | **35,711 ops/s** |
| different-length TEXT, indexed | 65,547 ops/s | 44,637 ops/s (unchanged within noise) |

A second fix came out of the same hunt: `WritePathProfiler.Stamp()` re-armed itself from
`SHARPCOREDB_WRITE_PROFILE` after an explicit `Disable()`, so "disabled" meant "until the next write".
An explicit `Disable()` now wins over the environment variable — otherwise the disable assertion in
`WritePathProfilerTests` fails for anyone running the suite with that variable set (it did, during this
investigation).

### 4a-2. The per-call handle sweep *(2026-09-13)*

Three separate hotspots of the same class — **a handle or a metadata probe per call instead of per path** —
were found and fixed during this work: the per-record walk helpers in `ReadAllRecords` (§1d-2), the
`FileHasEncryptedHeader` read probe (§1d-2, ~11× → ~1.2–2.2×), and the arena in-place overwrite (§4a).
Because "found by accident" is not a method, the remaining sites were then audited on purpose
(`new FileStream`, `File.OpenHandle`, `RandomAccess.Read/Write`, `File.Exists`, `new FileInfo` across
`Services/`, `DataStructures/` and `Storage/`), triaged by call frequency:

| site | frequency | verdict |
|---|---|---|
| `AppendBytes` (`FileMode.Append` + `FileOptions.WriteThrough`) | **once per single-row INSERT** | **resolved in §5: opt-in buffered appends (`EnableBufferedAppends`, default off) replace the per-row open with one flush per boundary — measured 25× end-to-end (732 → 18,775 INSERT/s)** |
| `AppendBytesMultiple` | once per *batch* | fine (65 KB buffer, one write-through for the batch) |
| `FlushBufferedAppends` | once per file per commit | fine |
| `TryUpdateInPlaceSameLength`, `ReadBytesFrom`, `ReadBytesRange` | per record | fine — cached read handle already |
| `BufferOrWriteOverwriteInPlace`, `FlushBufferedOverwritesBatched` | once per path / per flush | fine |
| `DecideEncryptWrites`, `EnsureAppendInitialized` | once per path (memoised) | fine |
| `LoadPageFromDisk` | once per page-cache **miss** | fine — a page holds many records |
| tombstone batch writer, compaction, migration, arena load | per operation, not per row | fine |

**Verdict: the per-record hot paths are clean.** The single remaining hot per-call open is the
`synchronous` append in `AppendBytes`, which is a durability setting rather than an oversight — it is
recorded in §5 with the 1,000×-class measurement and left for the owner's call.

### 4b. Two-region records for variable-width columns *(decided: Option B)*

The reason variable-width updates cannot go in place is that their encoded length changes. SQLite
sidesteps this with fixed-length records; the decision (§0.1-2) is to solve it with a **two-region
record** instead:

- **Stable slot:** the fixed-width fields live at fixed offsets, so any update to them is a pure
  in-place patch with no length question at all.
- **Overflow region:** long variable values (TEXT/JSON/BLOB beyond an inline threshold) live in the
  overflow arena and are addressed by offset, so replacing a long value rewrites arena bytes and
  patches one offset — it never moves the record.

**Why B and not "reserve maximum width" (Option A, rejected):** A buys fixed offsets by making every
row pay for the widest value a column ever holds, which inflates files for exactly the schemas that
made row-copying expensive in the first place. B costs only the pointer and reuses machinery the
repository already has — `DataStructures/OverflowArena.cs` plus the fixed-width overflow-arena path,
including the two reopened-arena bug fixes recorded in the `CHANGELOG`.

**Format handling (§0.1-3):** this is an on-disk change and it ships with a versioned upgrade. The
table-file magic already reserves version bytes (`EncryptedTableMagic = 53 43 44 42 01 01 00 00`) and
`Core/File/PageHeader` validates `Version` against `CurrentVersion`, so the hook exists: bump the
version, write new files in the new layout, and provide the upgrade/migration path for existing files
(old files must keep opening, per §0.5).

### 4c. Index maintenance on update

Every update currently touches the row's indexes. For an update that does **not** change an indexed
column's value, that maintenance is pure overhead.

- **Change:** skip index maintenance when the indexed value is unchanged (compare the old and new
  encoded value for the indexed columns), and batch dirty-page/index-cache flushes.
- **Evidence to collect first:** the §2 per-stage instrumentation will say whether this is 5% or 40%
  of the update cost. Do not do this before the instrumentation exists.

**Verdict (2026-09-14): dropped — the premise does not hold on the acceptance workload.** The `--pk` UPDATE
workload is `UPDATE docs SET score = <new> WHERE id = <pk>` on a table whose only explicit index is
`idx_docs_name ON docs(name)`, and that update never changes `name` or `id` — so every index-maintenance call
there *is* work for an unchanged value, i.e. exactly §4c's case. Two independent measurements on that shape
(50K rows, 10K updates by PK, single batch transaction):

| measurement | result |
|---|---|
| with `idx_docs_name` | 74,016 ops/s |
| **without the index at all** (the ceiling §4c could reclaim) | 73,209 ops/s → **1.01×** |
| `WritePathProfiler` stage profile | only `row-locate` recorded; `index-maint` never fires on the batch path |

Removing the index completely moves throughput by ~1%, so the ceiling for "skip unchanged-value maintenance"
is 1% here — and §2's instrumentation is not wired into the batch UPDATE path at all, so the per-row figures
quoted earlier do not describe this workload. Not worth the risk, and not to be re-attempted without a shape
where index maintenance actually dominates (many indexes, high-cardinality updates).

**The same run established something bigger than §4c: the published comparison measures the wrong posture.**
The at-rest posture costs ~2.5–2.9× on this exact workload, and the `--pk` harness has been reporting the
**raw** posture, because `BuildConfig(engine)` defaults to `NoEncryptMode = true`:

| arm (50K rows, 10K UPDATE by PK, same machine, same shape) | UPDATE ops/s |
|---|---:|
| SharpCoreDB, `NoEncryptMode = true` — what the harness publishes | 217,456 (harness arm: 234,394) |
| SharpCoreDB, product default (at-rest records) | **74,016** |
| SQLite reference | 279,003 |

So the honest UPDATE gap is **~3.8× slower than SQLite in the default posture**, not the ~1.2× the raw arm
suggests. The tax survives on a fixed-size-only table (0.40× vs 0.34× with TEXT), which rules out the overflow
arena and points at the per-record GCM work itself. §0.1-6 already requires every target to carry the
`NoEncryptMode` column beside it — the `--pk` runner does not, so making it report both postures, and
attributing the GCM cost (per-record nonce generation / cipher instance vs raw AES throughput), is the next
measurement task.

**Follow-up (2026-09-14, executed): both postures are now published, and the GCM hypothesis is partly
disproven.** The `--pk` runner gained a third SharpCoreDB arm (`fixed-width, at-rest default`), its labels
carry the posture, and it prints the gap per posture plus a per-operation at-rest tax:

| arm | INSERT | READ | UPDATE | DELETE |
|---|---:|---:|---:|---:|
| SharpCoreDB FW, plaintext | 120,832 | 114,747 | 238,446 | 162,449 |
| **SharpCoreDB FW, at-rest (product default)** | **63,532** | **75,875** | **147,458** | **147,964** |
| SQLite | 180,200 | 101,020 | 268,956 | 366,695 |

Gaps vs SQLite in the shipping posture: **UPDATE 1.8×, DELETE 2.5×, INSERT 2.8×, READ 1.33×**. At-rest tax
(same arm, same knobs): **INSERT 1.90×, READ 1.51×, UPDATE 1.62×, DELETE 1.10×** — i.e. the tax is spread
across the whole write path, not concentrated on one operation.

Crypto-level split (120-byte payload, 200K iterations):

| part | µs/call |
|---|---:|
| `Encrypt` as shipped | 1.34 |
| `Decrypt` as shipped | 1.01 |
| fresh `AesGcm` per call (inside both) | 0.69 |
| OS CSPRNG nonce per call | 0.10 |
| **raw AEAD, pooled cipher + counter nonce** | **0.27** |

Per-call cipher setup is ~59% of an `Encrypt` call, so pooling the cipher and replacing the per-record CSPRNG
nonce is worth ~5× on that call — but it is only ~15% of the measured UPDATE tax and less of the INSERT tax:
one 1.34 µs `Encrypt` cannot explain +7.4 µs/row. The remainder is **not** the index build either: with hash
indexes off the at-rest INSERT tax is still 1.76× (103,754 → 59,083 ops/s). Separately, hash indexes cost
**28% of INSERT in both postures** (74,605 → 103,754 plaintext), which makes them a posture-independent lever
of their own.

**Open:** ~6 µs/row of the at-rest INSERT cost was unattributed. Next probe: per-row allocation/GC counts
and the pooled-cipher prototype **in the product** (not only in a micro-benchmark), because the standalone
`Encrypt` timing may not survive the insert path's allocation pressure.

**Both were then executed, and the attribution closed.** The allocation/GC probe on the acceptance INSERT
shape (50K rows, 5×10K batches) gave:

| arm | ops/s | allocated B/row | gen0 |
|---|---:|---:|---:|
| plaintext, TEXT columns | 94,317 | 2,139 | 15 |
| at-rest, TEXT columns | 58,837 | 2,743 | 22 |
| plaintext, fixed-size only | 318,036 | 1,098 | 9 |
| at-rest, fixed-size only | 217,357 | 1,260 | 10 |

The fixed-size arm's +1.5 µs/row matches **one** `Encrypt` call (1.34 µs) almost exactly, while the TEXT arm
cost +6.4 µs/row — i.e. **3–4 GCM calls per row**: the record itself *plus one per variable value*, because
each TEXT value becomes its own overflow-arena block and each block is encrypted with its own nonce and its
own cipher. The tax was never the arena *I/O* and never the index build: it was the per-call cipher import,
paid several times per row. (`WritePathProfiler` recorded **no stages** on the batch INSERT path — §2's
instrumentation does not cover it, which is why the profiler could not answer this.)

**Option B is therefore implemented** in `CryptoService`: one `AesGcm` cached per key (keyed by the full key
bytes with structural comparison, never a fingerprint), and nonces built as
`[random prefix(8)][operation counter(4)]` where the counter is the field that already guards GCM exhaustion
(2^32), so a nonce cannot repeat within an instance and two instances collide only on their 64-bit prefixes
(2^-64, independent of record count — stronger than the previous random nonce per call, whose collision
probability grew with the number of records). `ResetEncryptionCounter` swaps the prefix *first*, so even a
reset without key rotation cannot replay a nonce. Ciphertext and tag are written straight into the result
buffer (the pooled ciphertext rent was copying into a buffer that was allocated anyway). The security-relevant
properties are pinned by `CryptoServiceNonceTests`: uniqueness + counter sequence, key-switch round-trips,
thread safety under concurrency, the prefix swap on reset, and per-instance prefixes.

Published result (same run, four arms):

| arm | INSERT | READ | UPDATE | DELETE |
|---|---:|---:|---:|---:|
| SharpCoreDB FW, plaintext | 119,160 | 122,549 | 240,244 | 171,026 |
| SharpCoreDB FW, at-rest | 88,906 | 91,283 | 183,434 | 159,045 |
| SQLite | 186,832 | 98,807 | 285,226 | 379,299 |

At-rest tax: **INSERT 1.90× → 1.34×, READ 1.51× → 1.34×, UPDATE 1.62× → 1.31×, DELETE 1.10× → 1.08×**; on the
same arm shape that is **INSERT +40%, UPDATE +24%, READ +20%, DELETE +8%**. Gaps vs SQLite in the shipping
posture: **UPDATE 1.8× → 1.6×, INSERT 2.8× → 2.1×, DELETE 2.5× → 2.4×, READ 1.33× → 1.08×**. The residual
~1.3× is the framing itself (28 bytes per record, one extra allocation) and the AES work, not setup — which is
what a storage-format change would have to attack, not another crypto tweak.

---

## 5. Phase 3 — INSERT: from competitive to ahead

**Measured first (the sweep behind §4a-2): the single-row append path pays a synchronous write-through per
record.** `Storage.AppendBytes` — the non-transactional single-record append behind every one-at-a-time
SQL `INSERT`, the Direct API and the provider ladders (§0.1-5) — opens a `FileStream` with
`FileOptions.WriteThrough` *per record*, while `AppendBytesMultiple` opens one for the whole batch.
Isolated at the `IStorage` level (5,000 × 64-byte appends, same process, same storage instance):

| mode | single `AppendBytes` | batched `AppendBytesMultiple` | ratio |
|---|---:|---:|---:|
| `NoEncryptMode=true` | **477.97 µs/record** | 0.43 µs/record | **1,122×** |
| encrypted default | **466.09 µs/record** | 2.02 µs/record | **231×** |

So a row-at-a-time insert runs at ~2.1K rows/s while the same bytes through the batch API run at ~2.3M
rows/s: **the batch/durability machinery already exists (and the transaction path already buffers
appends), but the single-append path never uses it.** Note also that the append path ignores the
configured `WalDurabilityMode` — it is unconditionally write-through, so a caller who explicitly chose
`Async` (or a `HighPerformance`/`BulkImport` config, which set `NoEncryptMode`) still pays a flush per
row.

**That makes this a durability decision, not a free optimisation**, and it is the first Phase 3 item: the
fix is to let the single-append path honour the configured durability (buffered append + the existing
flush-on-commit boundary) instead of hard-coding `WriteThrough`, with the crash-consistency question
answered explicitly — a process crash is already safe (the OS cache survives it), a power loss is the
open question and is what the WAL/`WalDurabilityMode` settings exist to answer. Because it changes when
bytes reach the platter, it needs the owner's call and a crash-recovery test, not a unilateral edit.

**Decision taken (owner, option A): implemented — opt-in, off by default.** `DatabaseConfig.EnableBufferedAppends`
routes single-row appends through the append buffer the transaction path already uses;
`AppendBufferFlushThresholdBytes` (default 1 MB) and `AppendBufferFlushIntervalMs` (default 10 ms) bound the
window, and every structural boundary flushes first: `Database.Flush()`, commit, `BeginTransaction`,
compaction, fixed-width migration, overflow-arena compaction, `DROP TABLE`, dispose. Option (b) — letting
`WalDurabilityMode` govern the table append — was **not** taken, because the project's own benchmark doc
establishes that the mode is only honoured by `GroupCommitWal` (default off): it would have made the data file
promise something the WAL itself does not.

MEASURED end-to-end (2000 single-row `INSERT` statements through `ExecuteSQL`, Release build):

| config | single-row INSERT ops/s | factor |
|---|---:|---:|
| `NoEncryptMode=true`, buffering off | 732 | — |
| `NoEncryptMode=true`, buffering on | **18,775** | **25.6×** |
| encrypted (default posture), buffering off | 974 | — |
| encrypted (default posture), buffering on | **23,822** | **24.4×** |

Honest residual: that is ~25×, not the 100×+ the storage-level ratio (477.97 µs → 0.43 µs/record) suggests,
because the SQL/engine path (~50 µs/row: statement parse, plan-cache lookup, index updates) becomes the floor
once the ~500 µs append is gone. The 0.43 µs/row ceiling stays reachable only through a batch API; lifting the
single-statement path past ~20K rows/s is a separate item (prepared statements / statement cache), not part of
this change.

The default configuration is byte-for-byte unchanged, which
`BufferedAppendTests.DefaultConfig_DoesNotBuffer_FirstInsertIsAlreadyOnDisk` pins.

**The "just cache the handle" fix was tried, measured and reverted — it breaks readers.** The obvious way
to remove the open cost without touching durability is a persistent write-through handle. It works
(512 µs → 264 µs for a 64-byte record with `FileOptions.WriteThrough` unchanged) — but a **live write
handle (`access=Write`) makes an ordinary reader fail**: `File.ReadAllBytes` asks for `FileShare.Read`,
which does not permit our write access, so the data file becomes unreadable to any other process while the
database is open. The suite caught it in **10 tests** — the encryption-coverage scan, the Known-Issues
plaintext checks and the DDL drop tests, all of which read (or delete) the data file. That is *why* the
append path opens per call: it is a constraint to design around, not a cost to optimise away.

| variant (64-byte record, raw mode) | µs/record | durability | holds the file? | viable |
|---|---:|---|---|---|
| **today: open per record + `WriteThrough`** | **512** | per record | no | ✓ shipping |
| cached write-through handle | 264 | per record | **yes** | ✗ blocks readers |
| cached unbuffered stream + `WriteThrough` | 269 | per record | **yes** | ✗ blocks readers |
| cached stream + `Flush(flushToDisk: true)` per record | 514 | per record (fsync) | yes | ✗ blocks readers |
| cached buffered stream + `WriteThrough` | **4.5** | per 4 KB, not per record | yes | ✗ blocks readers *and* changes durability |

**What makes it safe.** The invariant: every position the engine handed out must be visible to every reader,
and no structural operation may silently lose or duplicate it. Two mechanisms enforce it — an overlay for
point reads, and a flush-first hook for everything that rewrites or replaces a file:

| # | entry point | handling |
|---|---|---|
| 1 | `ReadBytesFrom` (PK point lookup) | overlay from a `position → payload` index over the buffer — the same shape that already existed for buffered *overwrites* |
| 2 | `ReadAllRecords` (scans, index rebuild, arena reload) | on-disk walk + the buffered tail in offset order; also covers a file that does not exist yet because its first rows are still buffered |
| 3 | `ReadBytesWithRecordOffsets` (whole-file snapshots) | flush first when outside a transaction (inside one the buffer belongs to the transaction and flushing it would break rollback) |
| 4 | fixed-width bulk UPDATE/DELETE fast paths | bail to the safe per-record path, exactly like the existing `HasBufferedOverwrite` gate |
| 5 | compaction, fixed-width migration, overflow-arena compaction | flush first — these REPLACE the file, so a later flush would append the same rows a second time |
| 6 | `BeginTransaction` | flush first — `Rollback()` clears the buffer and would otherwise discard rows the caller already has |
| 7 | `DROP TABLE` | flush first — otherwise the pending flush recreates the file the user just deleted |
| 8 | threshold / age / `Database.Flush()` / dispose | the durability boundary; the age bound is "flushed by the next append", so the explicit boundaries never depend on a timer |

Rows 1–2 and 5–8 have dedicated tests in `BufferedAppendTests`; rows 3–4 are guarded by paths the existing
suite already exercises (every full-scan test goes through `ReadBytesWithRecordOffsets`, the fixed-width bulk
update/delete tests through the gates), so they are listed here instead of assumed.

**This work also found a pre-existing default-configuration bug, unrelated to buffering.**
`CREATE TABLE; INSERT; DROP TABLE` failed on Windows with
`IOException: The process cannot access the file ... because it is being used by another process` in the
**default** configuration (at-rest encryption on), while the identical sequence passed with
`NoEncryptMode=true`. The DROP path validated the data file by opening it with `FileShare.None`; on Windows an
exclusive open is refused by *any* live handle — including the read handle SharpCoreDB caches for at-rest
records — even though the `File.Delete` that follows succeeds with those handles present. The probe now asks
for the sharing a delete actually needs (`FileShare.ReadWrite | FileShare.Delete`), so genuine locks still
throw and are retried by the existing backoff loop, while a cached read handle no longer blocks DDL. Fixed in
both the DROP TABLE path and the identical probe in the CREATE path. (The write-decision header probe was also
moved to a short-lived open, so the first INSERT no longer pins a read handle on the data file for the
lifetime of the database.)

INSERT is otherwise already 73.5–84.3K (SQL) / 108.5–132.1K (Direct) / 125.8–138.4K (StructRow) against
SQLite's 133.7–145.1K, and WP14's batch fast path already bought +80%. The remaining, ranked items:

1. **Multi-row `INSERT … VALUES` → the batched core — IMPLEMENTED (2026-09-15).** The statement used to
   lower to one `Table.Insert` call per row (a standalone write-through append each) and now routes to
   `Table.InsertBatch` when nothing needs per-row semantics: no INSERT trigger, no CHECK constraint, no
   unique secondary index, and no active batch update — the batch core implements none of those, and
   `CancelBatchUpdate` depends on the per-row path recording every inserted PK. A **row-count floor of
   1,000** keeps the per-row loop for small statements. Measured with the new `--multirowinsert` mode
   (20,000 rows, 1,000 rows/statement, 20 statements, median of 5, same machine, back-to-back, `src/`
   swapped between the two commits): **2,069.77 → 1,430.30 µs/row (1.45×; 483 → 699 rows/s)**, and the
   saving is exactly the append the change removed — which the profiler confirms.
   ⚠️ **The floor is 2, and getting it right took a re-measurement.** The first version of this change used a
   1,000-row floor, justified by a single observation that "200 statements of 100 rows did not finish inside
   300 s" for the same 20,000 rows, read as per-statement work that scales with table size. Re-measured in
   isolation that shape takes **40.5 s** — the 300 s reading was machine contention, not the code. With the
   routing made observable (§5 item 1b's instrumentation), per-row cost turned out to be **flat in the
   statement size on both paths**, so the high floor was silently withholding the win from ordinary
   statements. Measured at 20,000 rows, `SHARPCOREDB_MULTIROW_REPS=1`, same machine:

   | rows/statement | statements | path | total | µs/row |
   |---:|---:|---|---:|---:|
   | 2,000 | 10 | batched | 29.65 s | 1,483 |
   | 1,000 | 20 | batched | 28.56 s | 1,428 |
   | 500 | 40 | per-row loop | 41.52 s | 2,076 |
   | 500 | 40 | **batched** (floor 2) | — | **1,420.80** |
   | 100 | 200 | per-row loop | 40.46 s | 2,023 |
   | 100 | 200 | **batched** (floor 2) | — | **1,428.73** |

   The model is simply: the loop costs one arena append per variable-length value *plus* one write-through
   table append per row; the batched path costs the arena appends and one append for the whole statement.
   Only the engine transaction (opened once per statement) argues for any floor at all, and two rows is
   where it is amortised. *(Taken further twice the same day — the storage-transaction fix in item 1b, then the
   single-pass `VALUES` scanner in item 1c: the same shape is now **22.72 µs/row**, **91×** the loop it
   replaced and 2.4× off the direct batch API's 9.3 µs/row on the same table.)*
1b. **The overflow arena dominates the fixed-width INSERT path — both per value and per statement
   *(found 2026-09-15, partially attributed)*.**
   *Established.* (a) The arena is heavily used: on the `--multirowinsert` schema, 20,000 rows produce a
   **1,006,670 B `.ovf` against a 760,000 B data file** — the arena is the larger of the two, so values
   genuinely overflow and every one of them is written there. (b) `WritePathProfiler` attributes **100 % of
   the multi-row INSERT time to the `validate` stamp** (validation *and* serialization,
   `Table.CRUD.cs` → `ValidateAndSerializeBatchOutsideLock`) — 1.47–1.59 ms/row across runs. (c) That
   serialization goes `SerializeRowFixedWidth` → `FixedWidthCodec.SerializeRow(…, GetOverflowArena())`, and
   **`OverflowArena.Write` (`OverflowArena.cs:146`) calls `_storage.AppendBytes`** — the same open-per-call,
   `FileOptions.WriteThrough` append §5 measured at **477.97 µs/record**. Two or three overflow values per
   row therefore account for most of the per-row cost, and **`EnableBufferedAppends` does not cover the
   arena** — it only routes single-row appends to the table data file through the append buffer.
   *Now fully attributed (2026-09-15).* Instrumenting the arena and splitting the coarse stamp settled it.
   Stage totals for one profiled pass of 20,000 rows in 20 statements (`SHARPCOREDB_MULTIROW_REPS=1`):

   | stage | total | calls | share |
   |---|---:|---:|---:|
   | validate (outer stamp; wraps the two below) | 27,764.5 ms | 20 | 25.1 % |
   | encode (serialization) | 27,761.6 ms | 20 | 25.0 % |
   | arena-write (whole `Write` call) | 27,718.3 ms | 60,000 | 25.0 % |
   | arena-append (the `AppendBytes` inside it) | **27,583.0 ms** | **60,000** | 24.9 % |
   | validate-only (defaults, NOT NULL, coercion) | **2.9 ms** | 20 | 0.0 % |
   | arena-load (`EnsureLoaded`) | **0.0 ms** | **1** | 0.0 % |

   Three things fall out. **Exactly 60,000 arena writes for 20,000 rows — three per row** — so every one of
   those TEXT columns overflows; nothing inlines, which is itself worth a look. **`arena-append` is 99.4 % of
   `arena-write`**, and 27,583 / 60,000 = **0.4597 ms per value**, matching §5's 477.97 µs/record measurement
   of the same call — so the cost is the per-value write-through *open*, not the free-list, the gate or the
   cache. And **validation is free (2.9 ms) while the arena load runs exactly once**, which refutes the
   per-statement hypothesis that was recorded here first: `arena-load` is instrumented precisely to prove
   that, and "should be once" turned out to be "is once". The per-statement curve behind that hypothesis was
   machine contention (see item 1), not code.
   **Why it matters:** the arena is a **larger lever than the append policy the plan has been focused on**,
   it needs **no format change**, and it explains why the non-PK benchmark shape (variable-length layout,
   values inline) inserts at ~7.7 µs/row while this fixed-width shape costs ~1.6 ms/row.
   **The per-value fix has landed (2026-09-15).** `IOverflowArena.WriteMany` takes a row's payloads at once:
   freed blocks are still reused in place per value, and everything that must be appended goes out in one
   `AppendBytesMultiple` call; `FixedWidthCodec.SerializeRow` collects the variable values, makes that single
   call, and patches the offsets into the slots. Measured (20,000 rows, 1,000 rows/statement, median of 5):
   **1,428 → 523.51 µs/row (2.73×)**, with the profiler's `arena-append` call count falling from 3 per row to
   **exactly 1** and its per-call cost unchanged at 0.4625 ms — the whole gain is the two file opens per row
   that are gone. `WriteMany` is a default interface member, so the single-file arena (in-memory blocks,
   serialized as one provider block) is unaffected, and the one remaining `arena.Write` call site is a
   single-value update path.
   **Resolved — and the per-value open was not the last layer *(2026-09-15)*.** After the `WriteMany` fix the
   arena still cost 0.4625 ms per value, and the reason was *where* the appends happened:
   `AppendBytes`/`AppendBytesMultiple` buffer only when `IsInTransaction`, and `InsertBatchCriticalSection`
   opened an **engine** transaction, not a storage one — so every arena block was written with a write-through
   open during serialization, while `Database.InsertBatch` (which wraps its call in `storage.BeginTransaction()`)
   was fast on the very same schema. `Table.InsertBatch` now runs serialization *and* the critical section
   inside a storage transaction when one is not already open, which is the boundary `Database.InsertBatch`
   always had. Measured: **523.51 → 54.26 µs/row (9.65×)**, `arena-append` 9,653.8 → 20.4 ms (≈0.001 ms per
   value). Cumulative for this shape: **2,069.77 → 54.26 µs/row (38×)**.
   **The arena is no longer the bottleneck.** In the new stage report `encode` leads, `arena-append` is 25.0 ms
   of a 905 ms pass (~2.8 %), and only ~19 % of wall time sits inside instrumented stages at all. So the next
   step is to profile what is *outside* them — parse, `engine.InsertBatch`, PK/hash index maintenance, commit —
   rather than to batch the arena further, which could now save a few percent at most. Separately, the **inline
   threshold** is still evidently too small (all three TEXT columns overflow, none inlines), but the layout is
   fixed per schema, so that stays a format question and belongs with §4b.

1c. **The multi-row `VALUES` scanner — the last non-storage overhead on this shape *(found and fixed
   2026-09-15)*.** With storage out of the way (item 1b), the SQL path still ran at **47.74 µs/row** against
   the direct batch API's **9.3 µs/row** on the same table, and the gap was superlinear in statement length:
   1,000-row statements cost 47.74 µs/row while 100-row statements — *more* statements, less text each — cost
   33.72. `ParseMultiRowInsertValues` advanced by re-slicing the remaining string per tuple
   (`remaining = remaining[(closeParenIdx + 1)..].Trim()`), copying ~45 MB for a 1,000-tuple statement. It now
   walks the statement once by index and parses each tuple straight out of it, using the existing depth- and
   quote-aware scanner to find the closing paren; `ParseInsertValues` already accepted a `ReadOnlySpan<char>`,
   so no tuple text is copied at all. Deliberately no more permissive than before — a trailing separator still
   ends the scan rather than being silently ignored.
   **Measured: 47.74 → 22.72 µs/row** at 1,000 rows/statement, and the length penalty **inverted** (longer
   statements are now cheaper per row than shorter ones), which is the signature of a superlinear term being
   removed. That leaves **2.4×** to the direct API, and what remains is the text-SQL work itself — building
   each literal string and coercing it to a typed value per column — which the direct API never pays.
   **Item 4 — the `object[]` unification — was implemented, measured and REVERTED (2026-09-15), because the
   measurement refuted the premise rather than confirming it.** The batched branch gained a second entry point
   using the same `Table.InsertBatch(object[][], columnOrder)` the direct API uses, with the parser converting
   literals straight into column-ordered arrays instead of a `Dictionary<string, object>` per row, and the
   dictionary path kept wherever a post-insert read needs it (RETURNING; a PK or first-INTEGER column the
   statement omits) — so no observable behaviour changed. It worked and did what it said: `row-build` fell from
   536 to **144 B/row**, and in one comparable pair from 58.5 to 14.7 ms. **It did not pay.** Median
   21.86 → **21.55 µs/row**, inside this machine's ±20 % band, while total allocation went **5,893 → 6,021
   B/row** — because the table's `object[][]` entry point re-maps the statement's column order into a
   full-table-order array, one extra `object[]` per row, which more than cancels the dictionary the builder no
   longer allocates. A change that is neutral on throughput and deterministically worse on allocation is not
   kept, so it was reverted and the finding kept instead. **The finding is the useful part: the row shape is
   not the 2.4×.** The direct API goes through that same entry point and pays that same normalisation, and it
   still measures 9.3 µs/row — so the gap is in what *only* the SQL path does: statement parsing (`parse`
   measured 15.4 % of the pass at 1,000 rows/statement), literal coercion, per-statement dispatch, and the
   WAL/metadata bookkeeping that item 1's `--pk-profile` work has started mapping. Aim the next attempt there,
   not at the row type.
   Guarded by three scanner tests in `MultiRowInsertBatchingTests` (parens and commas inside string literals,
   whitespace/newline separators, trailing-separator stop).
2. **WAL flush policy.** Batch flushes are already collapsed to one fsync per batch; verify with the
   §2 instrumentation whether per-statement fsync is still paid on the non-batch SQL path, and expose
   an explicit `Synchronous`/group-commit setting rather than an implicit one.
   **Verified 2026-09-15 — the fsync is still paid, and it is not a setting.** Both append entry points
   hard-code `FileOptions.WriteThrough` (`Storage.Append.cs:557` single-record, `:1051` batch), and
   `WalDurabilityMode` is read by the WAL only (`Database.Core.cs:197`) — **never** by the table append
   path. So `DurabilityMode.Async`, which is the default in the `HighPerformance`, `BulkImport`,
   in-memory and platform presets (`DatabaseConfig.cs:415`, `:456`, `:540`, `:619`) is silently ignored
   for single-row inserts: the caller asked for async durability and gets a synchronous write per row.
   This is the same class of defect as the encryption-posture mismatch §3-1c found — a configuration
   that promises something the code does not deliver — and it is worth fixing for that reason alone.
   `EnableBufferedAppends` (item 1, opt-in) removes the per-row flush but does **not** make the append
   path honour the mode. **Implemented (owner said do it, 2026-09-15): the mode now governs the append.**
   `Async` engages the same append buffer as the explicit opt-in — one `BuffersAppends` predicate at both
   append entry points plus the auto-flush bound — so the four presets that ask for asynchronous writes now
   get them, while `WalDurabilityMode`'s default `FullSync` stays write-through per record and
   `EnableBufferedAppends` stays the explicit route for a caller who keeps `FullSync`. Measured on 20,000
   standalone single-row `INSERT` statements (1 row/statement, no batch, no transaction, same session):
   **1,127.61 → 119.79 µs/row (887 → 8,348 rows/s, 9.4×)**, and because the fixed-width layout is the default
   for an explicit `PRIMARY KEY` that includes the arena, whose per-value appends ride the same buffer.
   **Correction to this item's premise:** it claimed the arena "pays the same write-through *open* per row".
   It does not — the multi-row path runs in a storage transaction, so `IsInTransaction` was already true and
   the arena was already buffered; the 63.3 ms `arena-append` in the batch report is per-payload *buffered*
   work (`ConcurrentDictionary` insert, locks, length bookkeeping — ~1 µs per value), a different target.
   **The trade, stated correctly:** with `Async`, rows still in the buffer are lost by a process crash *as
   well as* by power loss, because they have not left managed memory — the sentence this replaced ("a process
   crash is already safe, the OS cache survives it") describes the write-through path only, and believing it
   about the buffered path is exactly the kind of half-truth this plan exists to avoid. What bounds the window
   is the 1 MB / 10 ms auto-flush plus `Database.Flush()`, commit, `BeginTransaction` (flush-first, so a
   rollback cannot discard pre-transaction rows), compaction, fixed-width migration, overflow-arena
   compaction, `DROP TABLE` and dispose. Guarded by `AsyncDurabilityAppendTests` (12 cases, listed in the
   CHANGELOG); a real power-cut test is not automatable in-process, so what is covered is every boundary that
   keeps the window bounded.
3. **One serialization pass.** The `Table.CRUD.cs` comments already flag "typed column buffers to
   eliminate 75% of allocations" work; confirm with the instrumentation whether a row is encoded more
   than once on the batch path.
4. **Second-order:** avoid the `Dictionary<string, object>` row shape entirely for the SQL path by
   extending the existing `object[]` fast path to non-batch statements.

**Target (§0.1-4): beat SQLite, not merely match it** — SQL INSERT above the ~150K reference, and
StructRow INSERT clearly ahead. Every ladder and provider is in scope (§0.1-5), so the `object[]`
fast path, the bulk APIs and the provider materialization paths all have to reach the same target.

---

## 6. Phase 4 — PageBased engine to UPDATE/DELETE parity *(decided: parity)*

**Measured 2026-09-15** (`--pk --engine=pagebased`, the harness this section names, against the same run's
append-only columns). The trap is real, and it is **specific to UPDATE**:

| arm (fixed-width plaintext) | INSERT | READ | UPDATE | DELETE |
|---|---:|---:|---:|---:|
| PageBased | 107,200 | 226,847 | **29,407** | 242,215 |
| append-only (default) | 108,649 | 113,540 | **420,187** | 223,207 |
| SQLite | 188,906 | 94,800 | 270,573 | 379,152 |

PageBased is **twice as fast at READ**, level at INSERT, slightly ahead at DELETE — and **14× slower at
UPDATE** (10.3× vs SQLite, where append-only is 0.6×, i.e. ahead). The earlier "~26K vs ~245K" note had the
right shape; this is the current numbers for it, and it re-scopes the work: parity here is an
**UPDATE-only** package, not a general "PageBased is slow".

**First attribution attempt — failed, and recorded so it is not repeated.** `PageManager.UpdateRecord` calls
`RecomputeFreeSpace` twice on its growth path, and that calls `GetUsedDataEnd` — a LINQ scan with
`RecordFlags.HasFlag` (which boxes) over every slot — so a plausible cause sat right there. Rewriting it as a
plain indexed loop with a bitwise test moved UPDATE from **29,407 to 36,208** plaintext and **27,697 to
28,842** at-rest, both **inside this machine's ±20 % band**. It is therefore *not* the dominant cost. The
rewrite is kept — removing a boxed `HasFlag` and a LINQ delegate per slot cannot be worse — but it claims no
win, and the comment in the code says so.

**Next, and this time profile rather than guess — DONE (2026-09-15).** The `--pk` harness now gets the same
treatment `--multirowinsert` got: `--pk-profile [--engine=…]` runs one untimed pass of this exact arm with the
write-path profiler on and prints its stage report, leaving the timed arms untouched. Both engines, back to
back in one session — the absolute values here are contention-affected (this run's own INSERT arm read 66K
against §8a's 109K), so it is the ratio and the shares that carry the signal:

| arm | UPDATE | attributed | biggest stages |
|---|---:|---:|---|
| AppendOnly | **13.38 µs** (74,757 ops/s) | ~79 % | `row-locate` 51.5 % (one call — the contiguous in-place path), `commit` 29.7 %, `parse` 18.8 % |
| PageBased | **45.84 µs** (21,815 ops/s) | ~24 % | **`arena-write` 60.3 %**, `arena-append` 18.3 %, `parse` 17.3 %, `commit` 3.1 %, `row-locate` 1.0 % |

**Instrumentation added, and what it forced me to correct (same day).** Five PageBased branches of the
batch-update core (`Table.BatchUpdate.cs` — `UpdateBatch`, `UpdateBatchViaPrimaryKeyLookup`,
`UpdateBatchViaBulkSelect` and both multi-column siblings) plus `UpdatePageBasedRow`
(`Table.CRUD.cs:1934`) now carry `encode` / `in-place-patch` / `engine-write` / `index-maint` / `hash-index`
stamps; all of them previously emitted nothing at all. **None of them fired for this arm, which is itself the
answer.** The report shows 10,000 `parse` calls and a single `row-locate` call, so the batch is dispatched
**per statement** into `SqlParser.ExecuteUpdate` → **`Table.UpdateAffectedCount`** (`Table.CRUD.cs:1730`) —
not into the batch-update core. That also closes the loop on §6's first code fact against the current source:
`UpdateAffectedCount` gates its contiguous fast path on `TryBulkUpdateContiguousFixedWidth` (which the
`row-locate` stamp shows declining after 1.1 ms), its PK fast path on `StorageMode != StorageMode.PageBased`
(`:2260`) and its raw-byte `fastPatch` on `StorageMode == StorageMode.Columnar` (`:2226`). On PageBased every
one of the 10,000 statements therefore takes the **generic per-op route**: `SelectInternal` full
materialization (`:2340`) plus a full re-serialize — and that re-serialize is where the measured
**2,829 B/update** of arena traffic comes from, **2.6×** the INSERT path's ~1.1 KB per row for the same
record. **What is still unstamped is now down to two named regions** in that route — the `SelectInternal`
materialization at `:2340` and the post-patch write after it — and stamping those two is the remaining work
before the last ~75 % can be split between materialization, serialization and the page write. The
instrumentation added here stays: it covers real entry points that other shapes use, and both were blank.
**Two code facts found while looking, which already re-scope the package** (both need profiling to
quantify, but neither is a guess about the page manager's inner loop):

1. **The PK-equality fast paths are switched off for PageBased.** `ResolveUpdateRows`
   (`Table.CRUD.cs:1938`) and the delete-side twin (`:2134`) both open with
   `StorageMode != StorageMode.PageBased && …` before the "single B-tree search + one read" path, and the
   same guard appears at `:3153`, `:3309` and `:3461`. So a PageBased `UPDATE … WHERE id = ?` pays full-row
   materialization plus a per-row re-search where append-only pays one search — on the batch shape that is
   10,000 materializations against one contiguous pass.
2. **A PageBased update can cost two writes.** `PageBasedEngine.Update` returns a *new* storage reference
   when the record relocates (`PageBasedEngine.cs:172-174`), and `UpdateColumnarRow` (`:1791-1830`) treats
   `TryUpdateInPlace == false` as "append a new version and re-point the indexes" — so a record that grows
   out of its page is written once by the page manager's relocation *and* once by the table's `engine.Insert`.
   Append-only never takes that branch for a fixed-width row because its position is stable.

That points the parity work at **making position-based fast paths trustworthy under PageBased relocation**
(either re-pointing the index as part of the page move, or keeping page records position-stable), rather
than at the page manager's slot scans — which is what the first attempt looked at and what the numbers
rejected. The `Auto`-routing constraint below is unchanged: parity is not a reason to make PageBased the
implicit default.

**Decision (§0.1-1): bring it to parity** — port the same in-place update/delete fast paths to
PageBased. Its page structure is arguably the natural home for them (which is why it was built), and
it pairs naturally with §4b's two-region record: a page-local slot with an overflow-arena reference
is the same idea at a different granularity.

**Constraints:** the hardened `Auto` routing must stay hardened (parity is not a reason to make
PageBased the implicit default), and parity must be proven with the §2 protocol on the same harness
that produced the 26K/245K pair, not on a friendlier one.

---

## 7. Phase 5 — DELETE parity

DELETE shares the row-copy root cause (SQL 20.9–60.7K vs SQLite 339.8–363.6K). The bulk-delete PK
contiguous path and the B-tree-probe removal already landed; what remains is the same in-place
machinery as Phase 2 (a delete should be a tombstone or an in-place free, not a rewrite) plus making
the SQL path reach the contiguous fast path where one exists.

**Lower priority than UPDATE** (Direct-API DELETE is already 118.9–132.5K), but it falls out of
Phase 2's engine work almost for free, so sequence it there rather than as its own project.

**Measured (2026-09-14), and the answer is not the row copy.** The delete path had NO stage coverage at all
before this measurement — `WritePathProfiler` recorded nothing for a 10K-row DELETE because the fixed-width
fast path bypasses `DeleteRecordsCore` entirely, so the profiler is now wired into that fast path
(`RowLocate` around the contiguous span read, `IndexMaintenance` around the PK `DeleteBulk` + every loaded
hash index, `EngineWrite` around the tombstone/buffer step). With it, on the acceptance DELETE shape (10K
deletes by PK, 50K rows, one batch transaction):

| arm | ops/s | wall µs/row | in stages | uninstrumented |
|---|---:|---:|---:|---:|
| plaintext, with `idx_docs_name` | 103,495 | 9.66 | 4.29 | 5.37 |
| at-rest, with `idx_docs_name` | 100,209 | 9.98 | 5.15 | 4.83 |
| at-rest, **without** the index | 140,489 | 7.12 | 4.01 | 3.11 |

Stage shares at-rest: **`index-maintenance` 81.9%** (42.2 ms of 10K deletes), `row-locate` 16.3%,
`engine-write` (the tombstone) **1.8%**. Two conclusions that change the plan:
- **the at-rest tax on DELETE is zero (1.03×)** — unlike INSERT/UPDATE, there is no encryption cost to chase;
- **index maintenance is the cost (a 1.40× win from dropping one index, 82% of the instrumented time)**, and it
  is paid *per loaded index per row*: the delete decodes each indexed column out of the fixed-width record —
  which for a TEXT column means an overflow-arena read (and, at-rest, a decrypt) — purely to compute the hash
  key. So the lever is **not** "make the delete cheaper" but "do the index removal in bulk / deferred", which
  is what `DeferredIndexUpdater` exists for. The tombstone (the part the plan expected to matter) is noise.

**Follow-up (2026-09-14, decode vs removal vs parse).** With `IndexDecode` and `Parse` now separate stages,
the same workload (10K deletes by PK, 50K rows, one batch transaction) splits as follows — TEXT column
indexed vs INTEGER column indexed, which isolates the cost of a *variable-length* index key:

| arm | ops/s | wall µs/row |
|---|---:|---:|
| TEXT indexed, plaintext | 97,528 | 10.25 |
| TEXT indexed, at-rest | 91,917 | 10.88 |
| INTEGER indexed, plaintext | 193,001 | 5.18 |
| INTEGER indexed, at-rest | 154,004 | 6.49 |

Stage shares (at-rest): `index-maintenance` **58.6%** (42.2 ms, only **7 calls** — the PK `DeleteBulk` plus one
`RemoveBatchKeys` per loaded index), `parse` **19.3%** (13.9 ms, **10,000 calls** — 1.4 µs per statement),
`index-decode` **11.3%**, `row-locate` 7.1%, `commit` 2.6%, `engine-write` **1.2%**. So:
- the public decode hypothesis was **wrong**: decoding the indexed columns (arena reads, decrypts at rest) is
  11%, not the bulk — the removal itself is, and a TEXT key costs roughly **3× an INTEGER key** there
  (42.2 ms vs 14.4 ms), i.e. string hashing/equality on the key is the expensive part;
- **SQL statement parsing is 1.4 µs per statement and 19–45% of a DELETE batch** — a share that was completely
  invisible before `Parse` existed, and it is the same cost the `--pk` harness pays for every row of its
  UPDATE/DELETE phases;
- the tombstone (1.2%) is confirmed as noise for the third time.

**Verdict (2026-09-14): the two biggest DELETE costs contain no cheap win — the next step is a design
decision.** Both were inspected after the split:
- the **classification path is already lean**: `IsInsertStatement` is a span prefix check, `TryScanCanonicalDml`
  is a quotes-aware span scan with no regex, and both `TryParseUpdateForBatch` and `TryParseDeleteForBatch` run
  their cheap `UPDATE`/`DELETE` prefix check *before* the regex fallback. The measured 1.4 µs/statement is two
  canonical scans plus per-table list bookkeeping plus the profiler's own two `Interlocked` calls — there is no
  allocation or regex to delete. So a "prepared DELETE statement" path would buy far less than the 19–45% share
  suggests, and is not the lever.
- `HashIndex.RemoveBatchKeys` already defers duplicate-key removals and compacts in a single pass, and
  `BTree.DeleteBulk` already sorts descending so consecutive deletes ride the rightmost leaf chain. The 42.2 ms
  in **seven** calls is therefore most plausibly the **PK B-tree bulk delete itself** (10K individual deletes
  with separator promotions/rebalances) — inherent to per-key deletion, not a fixable inefficiency.

**The lever is to stop doing it per key**: write the tombstone and SKIP index maintenance, relying on machinery
that already exists — readers treat the negative length prefix as a deleted record (`ReadBytesFrom` returns
null, `ReadAllRecords` skips the slot) and a reopen/compaction rebuild drops the stale entries. That is the same
"defer the maintenance" shape the update path already uses (`DeferredIndexUpdater`), and it could remove most of
the 58.6%. It is deliberately NOT implemented here: it changes read-path behaviour for every index consumer
(stale entries must be tolerated everywhere, including the whole-file slice fast paths) and the accumulation
must be bounded by a rebuild trigger — that is a decision plus its own test matrix, not a micro-fix.

**Instrumentation coverage (2026-09-14, §2).** Covered now: `Table.InsertBatch` (Validate — validation *and*
serialization — plus RowLocate around the batch PK probes; the path had none), the fixed-width bulk-delete fast
path (RowLocate / IndexMaintenance / IndexDecode / EngineWrite), bulk-update per-row hash-index maintenance
(IndexMaintenance), the batch dispatcher's statement classification (Parse) and the batch commit (Commit).
Still uncovered, recorded honestly: the *second* batch dispatcher path and parser internals below the
dispatcher. **`WalAppend` is now wired (2026-09-15)** — the per-statement SQL `INSERT` path's `wal?.Log(…)`
call, in both the VALUES and `INSERT … SELECT` branches — and it measured **0.025 µs/statement with zero
allocation**, which refutes the per-statement-WAL-fsync hypothesis outright. `WalFlush` still has no writer:
nothing on this path flushes the log per statement.

### 7a. Deferred index maintenance — implemented and measured *(2026-09-15)*

The lever §7 identified is now built, behind `DatabaseConfig.EnableDeferredDeleteIndexes` — and it is the
**default** (opt-out). When enabled, a DELETE writes only the durable tombstone and *skips* the PK B-tree
removal (`MarkPrimaryKeyIndexStale`) and the per-key hash removal; the PK B-tree is rebuilt from the data
file (skipping tombstones) at reopen, and uniqueness is verified against the stored position
(`IsPrimaryKeyTaken`), so a tombstoned entry is treated as free. Point lookups already tolerated a
tombstoned position (null read). The opt-out restores the eager behaviour and keeps
`GetHashIndexStatistics` exact between deletes.

**Three findings from building it, all measured:**

1. **Marking a loaded hash index stale is wrong here.** It made the *next* DELETE's
   `EnsureAllRegisteredIndexesLoaded()` rebuild the index from the file — O(n) *per delete*. Measured
   catastrophic: DELETE raw **97K → 1.4K ops/s**. The hash removal is now skipped without a stale mark;
   the index keeps tombstone-tolerant stale entries and is rebuilt on reopen.
2. **An O(n) rebuild at `Table.Flush()` is also wrong.** `RebuildPrimaryKeyIndexFromDisk()` over the
   acceptance file measured **1380 ms (plaintext)** / **36 ms (at-rest)** for 20K records — the plaintext
   path pays ~69 µs/record, which dwarfs the batch win. The flush-time rebuild was removed; reconciliation
   happens only when `DeferredDeleteIndexThreshold` is crossed at a non-transactional delete, or at reopen.
3. **The fix turns it into a win.** `--dual-mode` (random-key CRUD, `DELETE … WHERE name = 'User{i}'`,
   100K inserts / 10K deletes, medians of 3 alternating reps), deferral off → on. The same-session
   interleaved A/B on a quiet machine (2026-09-15) is the conservative figure:

| operation | raw | default (at-rest) |
|---|---:|---:|
| DELETE, same session (final) | 153,158 → **294,185** (**1.92×**) | 76,424 → **101,733** (**1.33×**) |
| DELETE, earlier same-session A/B | 124,844 → 222,752 (1.78×) | 84,937 → 100,605 (1.18×) |

The plaintext multiplier is machine- and load-dependent (1.78×–1.92× across same-session A/Bs), so the
low end is what the documentation quotes.

**A third pitfall, found by measurement after the first two.** The obvious reconcile point is
`Table.Flush()` — a committed-data boundary, and where the first version of this work put it. It is
wrong. The reconcile is a full O(n) `RebuildPrimaryKeyIndexFromDisk`, and the transactional batch path
would pay it once per batch — i.e. at the same frequency as the deletes the deferral skipped — so the
deferral buys nothing and the rebuild is on top. Measured: DELETE raw **294,185 → 70,248 ops/s** with the
Flush reconcile in place. It was reverted, and `DeferredDeleteIndexThreshold`'s default was raised to
**100,000** for the same reason (the old 10,000 default is crossed by a single 10,000-delete batch, which
is exactly the acceptance shape). What bounds the staleness is the table's own key count — a B-tree holds
one entry per unique key — plus the free rebuild a reopen performs.

**Open item (bounded reconcile).** Removing only the stale keys (`O(m log n)` instead of `O(n)`) would
let a session reclaim memory without the O(n) pass. Note that it costs roughly the same as the *eager*
per-key path, so it can bound memory but will not restore the throughput win — which is why the deferred
design deliberately does no in-session maintenance at all.

A focused probe isolates the same effect on a smaller shape (20K rows, 10K deletes, one batch):
plaintext **62,142 → 122,748 ops/s (2.0×)**, at-rest **43,250 → 53,333 ops/s (1.23×)**.

**Honest scope:** the `--pk` harness is unchanged by this (its ascending-PK DELETE rides the contiguous
fixed-width fast path, which never reaches `DeleteRecordsCore`) — the lever targets the *random-key* path.
On-disk behaviour and the default configuration are unchanged; `DeferredDeleteIndexTests` pins that
deleted rows stay gone to every reader, a deleted PK can be re-INSERTed, nothing resurrects across a
reopen, and the default is untouched. All six suites are green: **core 1869, VectorSearch 248,
EntityFrameworkCore 116, Functional.Linq2DB 24, Search 58, HybridSearch 6** (0 failed across all).

---

## 8. Acceptance targets

Targets are stated against the same-machine SQLite reference, because absolute ops/sec on this machine
are noise-dominated (§1.4).

| Operation | Current (SQL) | Current vs SQLite | Target | Target vs SQLite |
|---|---:|---:|---:|---:|
| UPDATE | 26.5–40.9K | ~7–10× slower | **≥ 120K** | ≤ 2.5× slower |
| INSERT | 73.5–84.3K | ~1.7× slower | **≥ 150K** | **faster than SQLite** (§0.1-4) |
| DELETE | 20.9–60.7K | ~6–14× slower | **≥ 150K** | ≤ 2.5× slower |

**Scope of the targets (§0.1-5): every ladder, not just core SQL** — Direct API, StructRow, the bulk
APIs (`InsertBatch`/`UpdateBatch`), and the ADO.NET / YesSql / Sync providers. A win that only lands on
one path is not a win; the provider paths are where real applications write.

**Every target is stated twice, for both modes (§0.1-6).** The table above is the *default
(encrypted)* column; each target table must carry the `NoEncryptMode=true` column beside it, from the
same run. If the encrypted column misses a target while the unencrypted one passes, that is a
legitimate result to publish — the user then has a real choice with a real price — but it may never be
published as a single number.

Sequencing: the §3-1c audit establishes what the encrypted default actually protects; §3-1a removes the
read-side work that buys nothing; Phases 2a/2b close the UPDATE gap. **Every number in the target
columns is a hypothesis to be measured under the §2 protocol, not a promise** — the vector-search work
in this branch taught that lesson twice.

### 8a. Where the targets stand after the v2.1 work *(measured 2026-09-15, quiet machine)*

Both harnesses re-run on a quiet machine (the owner was away), so these are the least noise-contaminated
figures the project has published for these shapes. `--pk` is the tuned harness; `--pk-default` is the
**untuned product default** (pure `DatabaseConfig`), which is the number a new user actually gets.

| arm | INSERT | READ | UPDATE | DELETE |
|---|---:|---:|---:|---:|
| `--pk` SharpCoreDB FW, plaintext | 108,649 | 113,540 | **420,187** | 223,207 |
| `--pk` SharpCoreDB FW, at-rest (product default) | 88,352 | 69,228 | 290,981 | 185,052 |
| `--pk` SQLite | 188,906 | 94,800 | 270,573 | 379,152 |
| `--pk-default` SharpCoreDB (pure default) | 81,284 | 67,015 | 134,119 | 120,889 |
| `--pk-default` SQLite | 128,121 | 80,594 | 219,141 | 255,780 |

Gaps vs SQLite: `--pk` fixed-width plaintext **UPDATE 0.6× (ahead), READ 0.8× (ahead), DELETE 1.7×,
INSERT 1.7×**; at-rest **UPDATE 0.9× (ahead), DELETE 2.0×, INSERT 2.1×**; pure default **UPDATE 1.6×,
DELETE 2.1×, INSERT 1.6×, READ 1.2×**.

Against the §8 targets on this run: **UPDATE ≥120K met (420K tuned / 291K at-rest / 134K pure default),
DELETE ≥150K met tuned (223K / 185K; pure default 121K), INSERT ≥150K not met** (109K tuned plaintext,
88K at-rest, 81K pure default). The INSERT target was set from a noisier machine; re-stating it on this
run is a §2-protocol task, not a claim that the target moved.

The two rows that were the plan's headline problem — UPDATE and DELETE — are now either ahead of SQLite
or within ~2×, down from the ~7–10× and ~6–14× that opened this plan. The residual is concentrated in
INSERT (per-record framing + the SQL ladder) and in the at-rest tax, both of which §3-1f/§4c and the
`DeferredDeleteIndexes` work have already reduced but not eliminated.

**Re-run under the §2 protocol (2026-09-16) — the caveat above is closed.** The three arms were re-run on the
current build, which includes the change that made the append path honour `WalDurabilityMode`: one process at a
time, launched detached with its output to a file, nothing else on the CPU, medians of 3 reps.

| arm | INSERT | READ | UPDATE | DELETE |
|---|---:|---:|---:|---:|
| `--pk` SharpCoreDB FW, plaintext | 81,344 | 110,162 | 230,722 | 168,804 |
| `--pk` SharpCoreDB FW, at-rest | 66,237 | 61,191 | 263,388 | 167,647 |
| `--pk` SharpCoreDB legacy, plaintext | 73,297 | 69,744 | 155,104 | 134,926 |
| `--pk` SQLite | 160,508 | 93,187 | 286,597 | 350,365 |
| `--pk-default` SharpCoreDB (pure default) | 50,777 | 44,269 | 125,771 | 100,998 |
| `--pk-default` SQLite | 147,356 | 76,137 | 210,634 | 264,855 |

Gaps vs the matching SQLite arm: **FW plaintext** UPDATE 1.24×, READ **0.85× (ahead)**, DELETE 2.08×,
INSERT 1.97×; **FW at-rest** UPDATE 1.09×, READ 1.52×, DELETE 2.09×, INSERT 2.42×; **legacy** UPDATE 1.85×,
READ 1.34×, DELETE 2.60×, INSERT 2.19×; **pure default** UPDATE 1.68×, READ 1.72×, DELETE 2.62×, INSERT 2.90×.

⚠️ **These columns are not comparable with the 2026-09-15 table arm by arm, and the reason is the reference:**
SQLite itself came back **15 % lower on INSERT and 6 % higher on UPDATE** between the two runs, so the machine
or OS state differs and absolute ops/sec did not transfer *even on the same box*. The comparison that survives
is the **ratio** — which is what the §2 protocol says to use, and why every row above is stated as a gap.

**And the ratio flags something real: tuned FW UPDATE went from 0.6× (ahead of SQLite) to 1.24× (behind).**
SQLite's UPDATE *rose* 6 % across the two runs while the FW arm *fell* 45 % (420,187 → 230,722), so this is not
the reference drifting upward — it is a relative regression on the UPDATE arm, and it is now the first thing to
bisect. The cheapest test needs one run and no code: the harness already has the A/B built in — re-run `--pk`
with `SHARPCOREDB_BUFFERED_APPENDS=0`, which reverts the append path to writing through, and see whether the FW
UPDATE column returns to ~420K. If it does, append buffering is the cause and the fix is to narrow
`BuffersAppends` so it governs appends without also governing the version append an update falls back to.

**`--dual-mode` (same protocol, medians of 3, rep-interleaved)** — the encryption comparison, now
protocol-compliant rather than trend-only:

| operation | raw (plaintext opt-out) | default (encrypted) | raw/default |
|---|---:|---:|---:|
| INSERT | 90,664 | 81,247 | 1.12× |
| READ | 78,401 | 70,867 | 1.11× |
| UPDATE | 102,727 | 45,056 | **2.28×** |
| DELETE | 216,402 | 68,142 | **3.18×** |

The split is the useful part: the encrypted default costs ~11–12 % on the append-and-scan operations and
**2.3–3.2× on the mutating ones**. That is the physically expected shape — appends write ciphertext
sequentially, whereas an update or delete reads a page, decrypts, modifies it and re-encrypts — and it means the
encryption tax is a **mutation tax**, so it belongs in the §4/§6 accounting rather than in the INSERT budget. It
also explains most of the `--pk-default` gap: that arm is Columnar *and* encrypted, and its UPDATE/DELETE
columns are the ones carrying this cost.

---

## 9. Execution order and dependency graph

```
Phase 0 (measure) ──► Phase 1 (encryption tax)            <- independent, cheapest, best ratio
        |
        └──────────► Phase 2a (SQL path reaches in-place)  <- needs Phase 0 to attribute
                             |
                             └──► Phase 2b (FW record slots / overflow arena)  <- format decision
                                          |
                                          └──► Phase 5 (DELETE falls out)

Phase 3 (INSERT)     -- independent; only needs Phase 0   (target: beat SQLite)
Phase 4 (PageBased)  -- parity with the in-place paths; pairs with Phase 2b
```

**Suggested first slice:** Phase 0 (protocol + instrumentation, §2) and **the §3-1c
security-consistency audit** — then §3-1a (the free read-side probing fix). The audit comes first
because until it is done, "encryption costs 1.3–1.6×" is a number without a meaning: we would be
measuring the price of protection we have not established we actually get. None of these three changes
the on-disk format, and together they produce the attribution the structural work needs.

**Where we actually are (2026-09-15, end of the insert/attribution session).** Phase 0 is done — the §2
protocol plus the `--gate` regression job (§2 item 4). The §3-1c audit is done and the at-rest default now
protects table data. Phases 1–2 have landed: UPDATE is ahead of SQLite on the fixed-width path, DELETE
moved to deferred index maintenance, and the SQL multi-row INSERT uses the batched core (§5 item 1), standing
at **22.72 µs/row** against the direct batch API's 9.3 (**2.4×**, §5 item 1c).
**The INSERT path is no longer unattributed.** The stages item 1b/1c flagged as never wired are now stamped —
`engine.InsertBatch`, PK/hash index maintenance, commit, the statement-level `parse`, and a new `row-build`
for literal conversion — which took measured attribution from ~19 % to **~89 %** of a multi-row pass, and the
profiler also carries an **allocated-bytes column** per stage (thread-local checkpoints, LIFO-closed, with
left-open checkpoints reported rather than hidden). That is what made the **6.2 KB/row** this path was
generating attributable: `arena-write` 1,256 B/row, `hash-index` ~950 B/row, `parse` 962 B/row, `row-build`
536 B/row — and three of the four are now reduced (`row-build` sizing and mapping, the arena scratch lists,
and a capacity hint in `HashIndex`), for 6,189 → **5,893 B/row** with wall time unchanged inside the noise band.
**The open queue, in the order the measurements argue for:**

1. **The append/durability decision — §5 item 2 — DONE (2026-09-15).** `DurabilityMode.Async` now governs
   the table append instead of being silently ignored, so the presets that ask for asynchronous writes get
   buffered appends rather than a write-through open per record: **1,127.61 → 119.79 µs/row (9.4×)** on
   20,000 standalone single-row INSERT statements, with `FullSync` (the default) unchanged and
   `EnableBufferedAppends` still the explicit opt-in. Coverage, the corrected trade, and the correction to
   this item's own arena premise are in §5 item 2. **What it exposed is the new top item:** with storage
   taken out of the per-statement cost, one single-row statement still costs **~115 µs**, and that is not the
   append. **Profiled the same day — and it is not the WAL either.** On the standalone-statement shape (1 row
   per statement, 20,000 statements) the storage write stamps at **0.735–0.945 µs/statement** and the WAL's
   per-statement `Log` at **0.025 µs with zero allocation**, so neither the append nor a per-statement fsync
   explains it. What *is* attributed comes to **15.7 µs/statement** (~25 %): `arena-write` 6.5 µs
   (serialization plus the buffered per-payload append), `parse` 2.5 µs, `engine-write` 0.8 µs, `row-build`
   0.4 µs, `wal-append` 0.02 µs. **The warm median for this shape is 59.64 µs/statement**, so ~44 µs/statement
   sits in the **statement-dispatch machinery, outside the table and outside the WAL** — precisely the coverage
   gap §7 already names: the second dispatcher path and parser internals below the dispatcher. ⚠️ An earlier
   version of this paragraph quoted 109.91–133.62 µs/statement and called the profiled pass "faster than the
   medians, which is the wrong sign": those figures were **cold `SHARPCOREDB_MULTIROW_REPS=1` passes** (a
   single first pass including JIT warm-up), which is the whole discrepancy. Measured warm, the median and the
   profiled pass agree (59.6 vs 61.9 µs/statement), and the correction is recorded here rather than quietly
   overwritten. **Done the same day, and it found the bucket.** `StatementValidate` is a new stage, and `Parse`
   now also covers DML classification + plan resolution above the parser, so the two phases below the table with
   no stamp are attributed. Measured warm (median of 5, 20,000 statements): the security/parameter validation is
   **0.035 µs/statement — free**, refuting that candidate outright, while `parse` totals **23.3 µs/statement**
   across two calls — **~21 µs of it inside `GetOrAddPlan`**, the plan-cache warm-up in `Database.Execution.cs`
   (`Database.PlanCaching.cs:84`). That call's **return value is discarded** on the DML path, and because
   `GetNormalizedSql` collapses whitespace but keeps literal *values*, statements differing only in their
   literals — exactly what 20,000 distinct INSERTs are — miss the cache every time: a normalized copy, a cache
   key, a `Split` of the whole statement, a `CachedQueryPlan` and a cache insert, per statement, for a plan
   nothing reads. **FIXED (2026-09-15) — and the removal is the whole fix.** Both `ExecuteSQL` overloads no
   longer call `GetOrAddPlan` for DML: nothing reads a DML plan entry (`CachedPlan` is consumed only by the two
   SELECT paths, and `TryGetCachedPlan` has no callers), so the chain was cost without effect, and the
   literal-sensitive key meant it could never have hit for distinct-literal statements anyway. The statement
   classification that existed only to feed it went with it. Measured (`--multirowinsert`, 1 row/statement,
   median of 5): **59.64 → 51.29 µs/statement**, allocation **9.75 → 8.32 B/row**, and `parse` from 465.8 ms
   across two stamps to **71.0 ms across one** — a ~21 µs warm-up replaced by 3.55 µs of real parsing. The
   SELECT path's cache is untouched. Making DML plans *useful* would be a feature: the DML path takes raw SQL,
   so it would have to accept a plan argument first. Attribution **after** the fix: `parse` **3.55 µs** (was
   23.3), `arena-write` ~6–7 µs, `engine-write` ~0.7–2 µs, `row-build` ~0.4–1.4 µs, `stmt-validate` 0.04,
   `wal-append` 0.03. So of a warm **51.29 µs** statement roughly 11–14 µs is attributed, and the remainder is
   the `_walLock` region, `IsSchemaChangingCommand`, `Table.Insert`'s validation block and the `_metadataDirty`
   bookkeeping. ⚠️ Per-stage figures on this shape swing by more than 2× between runs of the same build (the
   profiled pass measured `arena-write` at 130.6 ms and 366.8 ms on two of them), so only medians-of-5 are
   quoted as results and the stage numbers are read as ratios inside one report.
   **Where that variance came from, corrected (2026-09-15):** it was *mostly* self-inflicted. The disagreeing
   runs were made while the nine test suites and other benchmark invocations were in flight — and several
   commands were auto-backgrounded by the tooling while still writing output, so a "quiet" measurement was never
   actually quiet. **But not entirely.** The first isolated `--pk` re-run (2026-09-16: one process, launched
   detached with output to a file, nothing else on the CPU) still shows a **1.3–1.65× rep-to-rep spread on the
   INSERT arms** — legacy 57.1 / 73.3 / 94.3 K ops/s, SQLite 137 / 180 / 161 K — while the small tight arms
   (at-rest INSERT, UPDATE) stay within ~10 %. So the protocol is: **one benchmark at a time, launched detached
   with its output to a file, with no suite run and no second benchmark beside it**, and **quote medians and
   ratios, never a single absolute**, because the INSERT arms carry a real rep-to-rep spread even on a quiet
   machine. That protocol is now cheap to honour: a full `--pk` arm is ~28 s end to end. **Rep ladders, not just
   medians, are worth reading:** the 2026-09-16 ladders rise monotonically on two of the three INSERT arms
   (legacy 57.1 → 73.3 → 94.3 K, pure default 35.1 → 50.8 → 55.8 K), which is a warm-up effect rather than
   random noise — so a median of 3 is conservative, and a single unrepeated run is measuring the JIT.
   **Item 2 — the remaining candidates — closed the same way.** Stamping the single-row `Table.Insert`'s
   validation block as `Validate` and its `SerializeRowExact` as `Encode` (neither had a stamp) settles two of
   the four named candidates: **row validation is free — 0.19 µs/statement, 0 bytes allocated** — while the
   serialization becomes visible at **4.9 µs/statement**, the largest attributed item on this shape, of which
   `arena-write` is 4.4 µs. Final attribution on a clean run (profiled pass 39.5 µs/statement, warm median
   41.89): `encode` 4.9, `parse` 1.6, `engine-write` 1.3, `row-build` 0.4, `validate` 0.19, `stmt-validate`
   0.03, `wal-append` 0.02 — about **8.4 µs/statement (21 %)** — leaving **~31 µs/statement (79 %) in the
   `_walLock` region, `IsSchemaChangingCommand` and the `_metadataDirty` bookkeeping**, none of which has a
   stage. ⚠️ Absolute medians on this shape ranged 41.89–64.68 µs across this session's runs of *the same
   build* (machine load), so cross-run median comparisons are not evidence; the stage *ratios within one
   report* are.
   **Also worth recording: three of the four plausible candidates for this cost are now measured dead** — the
   storage write (0.7–1.3 µs), the WAL (0.02 µs) and row validation (0.19 µs) — and the only one that produced
   a win was the plan-cache warm-up (removed, 21 µs → 3.5 µs of real parsing). The remaining bucket is lock
   acquisition and per-statement metadata bookkeeping, which needs a stage before it can be measured.
   **Item 1 — the `Dispatch` stage — was then added and measured, and it bounds the remainder.** `Dispatch`
   wraps lock acquisition, the shared-parser fetch and the hand-off in `ExecuteSQL(sql)`; on the same shape it
   reports **38.4 µs/statement, essentially the whole profiled pass (770 ms)**, because every other stage nests
   inside it (the table work happens within `sqlParser.Execute`) — so it is an outer envelope, not a new cost.
   Its value is the **gap**: the named stages inside it sum to ~8–11 µs, so **~28 µs/statement sits inside the
   dispatch region and outside every stage**, and its allocation says the same thing more loudly —
   **7,520 B/statement against ~2,000 B/statement across the named stages**, i.e. **~5.5 KB/statement
   unaccounted**, with 27 gen0 collections in the pass. That points at two places: the parser's full `Execute`
   (the `Parse` stamp covers the batch dispatcher's statement classification, not the whole parse) and
   `Table.Insert`'s work after the engine call (the per-row PK check and index updates have no stamp on this
   path). ⚠️ Coverage note, recorded rather than hidden: this stamp is wired on `ExecuteSQL(sql)` only — the
   parameterized and async overloads share an identical block that could not be disambiguated safely, so they
   stay unstamped rather than being edited blind on a hot path.
2. **The remaining text-SQL cost — §5 item 4 is settled (2026-09-15): the row shape is not the gap.** The
   `object[]` unification was implemented (a second batched entry point using the direct API's
   `InsertBatch(object[][], columnOrder)`, with the dictionary path kept wherever a post-insert read needs it)
   and then measured: `row-build` 536 → **144 B/row**, but median **21.86 → 21.55 µs/row** — inside the noise
   band — while total allocation rose **5,893 → 6,021 B/row** because the table's array entry point re-maps the
   statement's column order into a full-order row per row. Reverted, with the full reasoning in §5 item 1c.
   **The 2.4× therefore lives in the SQL-only work:** statement parsing (15.4 % of the pass at 1,000
   rows/statement), literal coercion, per-statement dispatch, and the WAL/metadata bookkeeping. Aim the next
   attempt there rather than at the row type.
3. **§6 PageBased UPDATE parity — profiled (2026-09-15), and the profile refuted both prior explanations.**
   Back to back under identical conditions the trap is 3.4× (AppendOnly 13.38 vs PageBased 45.84 µs/update),
   and **~75 % of the PageBased cost is in code with no stamp at all**: `in-place-patch`, `engine-write`,
   `index-maint` and `encode` have zero calls on that arm, while the largest *measured* cost is the overflow
   arena (6.81 µs/update, **2,829 B/update** — a full record re-serialization, which is the first hard
   evidence for the code fact §6 had recorded by reading). **The next step is instrumentation rather than
   another hypothesis:** stamp the PageBased update path (`UpdateColumnarRow`/`TryUpdateInPlace`, the
   relocation branch and its index re-point) the way the append-only batch path already is, then read whether
   the remaining ~34 µs is the double write, the index re-point, or full-row materialization. Reproduce with
   `--pk-profile [--engine=…]`.
4. **§4b two-region records** — the only remaining format change, and it owns the too-small inline threshold
   §5 item 1b turned up (all three TEXT columns overflow; nothing inlines).
5. **Coverage still missing:** `WalAppend`/`WalFlush` have no writer at all, plus the second batch-dispatcher
   path and parser internals below the dispatcher (§7, 2026-09-14 note).
6. **The INSERT target (§8/§8a):** ≥150K not met — 109K tuned plaintext, 88K at-rest, 81K pure default. §8a
   already records that the target was set on a noisier machine and that re-stating it under the §2 protocol
   is a task, not a claim that the target moved.

**One step the original plan omitted — added by the v2.1 audit: re-validate every provider after core
changes.** §0.1-5 puts every ladder in scope: the Direct API, StructRow, the bulk APIs, and the ADO.NET /
YesSql / Sync providers. A core win that a provider re-introduces as row-by-row overhead is not a win, so
before any INSERT/UPDATE/DELETE number is published, re-run the comparative harness, `--pk`,
`--pk-default` and the `--multirowinsert` mode added with §5 item 1, plus the provider test projects — and
report the SQL, Direct and StructRow ladders **separately**, never as one number.

---

## 10. Decisions

**All decisions are closed by the owner (2026-09-13)** — see §0.1 for the table:

| # | Decision |
|---|---|
| 1 | PageBased → parity with the in-place paths |
| 2 | Two-region record layout (Option B), overflow arena |
| 3 | Format change allowed, via the magic-header versioned upgrade |
| 4 | INSERT must beat SQLite, not match it |
| 5 | Every API ladder and provider is in scope |
| 6 | Security stays the default **and must be real**; `NoEncryptMode=true` stays the documented raw-speed opt-out; **both modes benchmarked side by side** |

Decision 6 is the one that changed the plan's order of work: it turned §3-1b from a performance
question into a **correctness** question, and it added the §3-1c audit ahead of the write-performance
work. The reason is in §3-1c: today's default pays AES on some write paths while storing records as
plaintext on others, so the measured "encryption cost" is the price of a guarantee we have not yet
shown we deliver.

Everything else in this plan is an implementation detail I will decide and verify under the §2 protocol
(overflow-arena growth policy, when a value inlines vs overflows, which index maintenance to skip).





