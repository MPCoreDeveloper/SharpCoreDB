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
3. **Reconcile the two UPDATE harnesses** (§1.4) so absolute numbers are comparable across documents.
4. **A regression gate.** The benchmark must be runnable as a non-gating (nightly/manual) CI job so
   future work cannot silently regress INSERT/UPDATE the way UPDATE did between 2.0 and 2.1
   (26.5K vs 37.3K is inside the noise band, but the band is the problem).

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
   made encryption-aware first, with the reopen round-trip matrix as the gate. Until that lands the
   default stays exactly as shipped, and the gap stays documented rather than silently claimed closed.
3. **Document the two modes as a first-class choice** with the measured cost of each — including the
   caveat that a default database currently writes table data as plaintext.
4. **A test that fails if the promise regresses**: default config ⇒ a plaintext scan of the data files
   must not find a known inserted value; `NoEncryptMode=true` ⇒ it may. The probe above is the
   template; it ships as a permanent regression test rather than a throwaway.

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

---

## 4. Phase 2 — the structural fix: in-place UPDATE on the SQL path

**Goal:** stop copying rows on update. Half of this already exists (`TryOverwriteFieldsInPlace`,
runtime offsets, `TotalInPlacePatches` instrumentation, PK fast path); the remaining work is coverage
and the engine underneath it.

### 4a. Route the SQL UPDATE path through the existing in-place machinery

`#7/#8` gave `ExecuteUpdate` a single-pass `UpdateAffectedCount(where, updates)` and a PK fast path
for `pk = value`, and WP14 wired `UpdateMultiple` to `TryOverwriteFieldsInPlace`. What is still
missing is the general SQL `UPDATE ... WHERE <non-PK>` path reaching the same in-place attempt
instead of the append path.

- **Change:** make "try in-place first, fall back to append" the default in the one place where a row
  is rewritten, rather than something two of the three call paths do.
- **Evidence for the win:** the existing in-place path already produces **0 file growth** where the
  append path grew +90 KB per 2,000 updates — the write amplification is real and already measured.
- **Expected effect:** the bulk of the UPDATE gap on the SQL path.
- **Risk:** the in-place decision must be conservative — any column whose encoded width can change
  (TEXT/JSON/BLOB) must fall back, exactly as `TryOverwriteFieldsInPlace` does today.

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

---

## 5. Phase 3 — INSERT: from competitive to ahead

INSERT is already 73.5–84.3K (SQL) / 108.5–132.1K (Direct) / 125.8–138.4K (StructRow) against
SQLite's 133.7–145.1K, and WP14's batch fast path already bought +80%. The remaining, ranked items:

1. **Extend the StructRow insert path to the SQL INSERT path.** WP14 did this for the *batch* INSERT
   (`object[]` rows, no per-row dictionary); the single/multi-row SQL INSERT still pays dictionary
   allocation and column-name lookups.
2. **WAL flush policy.** Batch flushes are already collapsed to one fsync per batch; verify with the
   §2 instrumentation whether per-statement fsync is still paid on the non-batch SQL path, and expose
   an explicit `Synchronous`/group-commit setting rather than an implicit one.
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

`PageBased` UPDATE measures **~26K ops/s vs ~245K on the fixed-width Columnar path**, and the
`CHANGELOG` hardened `Auto` routing so it is never selected implicitly — leaving a trap where a user
who *explicitly* asks for `StorageEngineType.PageBased` silently gets a ~10× slower write path.

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





