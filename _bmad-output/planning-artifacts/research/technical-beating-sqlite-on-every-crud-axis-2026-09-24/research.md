---
title: 'technical research: beating SQLite on every CRUD axis'
type: 'technical'
topic: 'Beating SQLite on every CRUD axis'
decision: 'Choose the engineering levers that let SharpCoreDB meet or beat SQLite on INSERT/READ/UPDATE/DELETE in every shipped arm (tuned plaintext, pure default, encrypted default, PageBased).'
source: 'native run — 24 external sources, 3 rounds, 11 digests'
status: complete
preset: 'standard'
validation: 'normal'
created: '2026-09-24'
updated: '2026-10-09'
claims: { verified: 23, unverified: 3, disputed: 0, overturned: 1 }
---

# technical research: beating SQLite on every CRUD axis

**Decision this research serves:** choose the engineering levers that let SharpCoreDB meet or beat
SQLite on INSERT/READ/UPDATE/DELETE in every shipped arm.

---

## Executive summary

**The evidence says: the gap is a record-layout gap, not an engine-speed gap, and the fix is three
published mechanisms that we are each exactly half-way to — plus two call-site shapes the runtime
already provides and we have not taken (round 3, below).**

Four findings drive that answer. The first three are the layout-and-crypto case; the fourth is round 3's
and is the only purely mechanical one.

1. **Our remaining deficit is concentrated in the arms where a row's *bytes* must move.** The
   tuned plaintext fixed-width PK shape is already at or ahead of SQLite on READ, UPDATE and DELETE;
   the pure-default and encrypted arms are behind on **all four** operations. That pattern is not a
   runtime or algorithmic shortfall — it is the signature of an update that *relocates* the row
   instead of *editing* it.

2. **Every major page-based engine solves this with indirection, and we already own the pointer
   half.** PostgreSQL addresses rows through 4-byte `(offset, length)` item identifiers, so the row
   bytes may move within the page while every reference stays valid `[2]`. It then adds HOT — which
   requires *only* that the update touch no indexed column and that the page have free space — to
   avoid index writes entirely, converting the item id into a redirect `[3]`. It keeps records
   fixed-size in-page by pushing oversized values out of line with TOAST `[4]`. We have the
   fixed-width stable-slot mechanism and `OverwriteRecordAtSameLength`; what we lack for the default
   shape is (a) the indexed-column test, (b) deliberate in-page slack, (c) off-page values. Those
   three are the leverage.

3. **The encryption tax is an API contract, not a bug, so it can only be fixed in the layout.**
   `AesGcm` exposes only whole-buffer `Encrypt`/`Decrypt` with a separate tag and a per-message
   nonce; there is no incremental, seekable or field-level update `[5]`. Any byte change therefore
   re-encrypts the whole record — which is precisely our measured `in-place-patch` 8.3 ms encrypted
   vs 1.2 ms raw at *identical* 175 B/call. The only way to make encrypted UPDATE cheap is to make
   the encrypted unit **small and constant-size** — the same layout conclusion reached from the
   storage-engine side. Two independent lenses agreeing is this run's strongest signal.

4. **Round 3 (2026-10-09): the runtime already ships the two primitives our write path lacks, and we call
   one of them in its weaker form.** `System.RandomAccess` exposes scatter/gather `Read`/`Write` that take
   a *list* of buffers, and `ZstandardEncoder` is a reusable span-in/span-out compressor with
   `GetMaxCompressedLength()` and `Reset()` `[17][20]`. Our in-place record overwrite still issues the
   length prefix and the record as **two separate single-buffer writes**
   (`Storage.Append.cs:469-470`), and our block codec still round-trips every block through a
   `MemoryStream` with two full copies (`Services/BlockCompressor.cs`). Neither is part of the layout
   problem: both are call-site shape, both change **no bytes on disk**, and both sit on the UPDATE path
   the default arms lose on. Round 3's own limit is stated with it — the API pages do **not** document how
   many syscalls the vectored form issues, so *"one write instead of two"* is a measurement to run, not a
   result to cite.

**Biggest caveat.** SQLite publishes no CRUD benchmark, so no ratio here is a vendor figure — every
comparison is our own measurement, and SQLite's own documented tuning surface (`journal_mode`,
`synchronous`, `cache_size`, `mmap_size`, `page_size`, `locking_mode`, `temp_store`,
`wal_autocheckpoint`) can swing its write throughput by more than 2× `[7][12]`. "Beating SQLite" is
only a well-formed claim when it names **both** sides' tuning. That is comparator hygiene, not a
footnote.

**Where the "different league" claims already hold and must not regress:** analytics/columnar SIMD
aggregates, vector search, encryption-at-rest-by-default, and pure-.NET embedding (no P/Invoke,
AOT-friendly). Nothing in this research suggests trading those for row-store parity.

---

## D1 — SQLite's speed mechanics and its real limits

**How it is fast.** SQL text is compiled once into bytecode and executed from a `sqlite3_stmt`;
re-preparing per call re-pays compilation, which is the documented reason to prepare once and reuse
`[1]`. Data lives in separate B-trees per table and per index, all inside one file, and the
**pager** — not the b-tree — owns rollback, atomic commit and file locking `[1]`. That separation is
the structural reason a per-page write stays cheap: durability bookkeeping is centralized instead of
paid per structure. The tokenizer deliberately calls the parser because that "runs faster" `[1]`.

**Its layout.** Page size defaults to **4096 B**, any power of two from **512 to 65536** `[1]`;
page numbering starts at 1; the maximum database is ≈281 TB and the minimum is a single 512-byte page
`[10]`. Ordinary tables are keyed by `rowid`; `WITHOUT ROWID` tables store the record as the key of
an index B-tree and suppress redundant PK columns from secondary-index suffixes `[10]`. Each index
entry is *(indexed columns → row key)* `[10]`, which yields the single most transferable structural
fact in this dimension: **an UPDATE of a non-indexed column need not touch any index at all**, while
an UPDATE of an indexed column must delete and reinsert the entry.

**Its durability trade.** WAL is "significantly faster in most scenarios", gives readers that do not
block writers, keeps I/O more sequential, and "uses many fewer fsync() operations" `[8]` — but it is
*worse* for large transactions: "WAL works best with smaller transactions", rollback journal is
likely faster above roughly 100 MB, and >1 GB can fail outright `[8]`. It is also "perhaps 1% or 2%
slower" for read-mostly workloads `[8]`. Two constraints land on end users: `page_size` cannot be
changed after entering WAL mode, and read-only WAL databases cannot be opened unless the `-shm`/
`-wal` files exist or are creatable `[8]`.

**Its reliability record, stated honestly.** A WAL-reset corruption bug was present in **every
release from 3.7.0 (2010-07-21) through 3.51.2 (2026-01-09)**, fixed in 3.51.3 (2026-03-13); it needs
two or more connections on one file committing/checkpointing simultaneously `[8]`. This is
differentiation context only. It is **not** a speed lever, and a marketing claim built on "SQLite is
unsafe" would be dishonest: the vendor's own telemetry puts the occurrence rate at or below that of
SSD malfunction.

**Its own admission about slow writes.** SQLite ships a named FAQ entry, *"INSERT is really slow — I
can only do few dozen INSERTs per second"* `[11]`, and the fix it documents is transaction batching
and `synchronous` configuration — not the engine. It also answers *"I deleted a lot of data but the
database file did not get any smaller"* `[11]`: SQLite defers physical reclamation into a freelist
and requires an explicit `VACUUM`. **Consequence:** our append-plus-compaction design is not inferior
by construction — SQLite performs an equivalent deferred reclaim, just inside pages.

**Its retracted numbers.** The vendor's comparative page is explicitly marked obsolete — *"This
document is very very old … the numbers here have become meaningless"* — yet it is the only place the
vendor published write-path deltas: `nosync` at 1.4–1.7× on UPDATE/DELETE tests and `-DNDEBUG`
"roughly doubling" speed `[12]`. Cited here **direction-only**, confidence **low**, never as current.

**Its comparison surface, which is the operative fact for our goal.** Everything the vendor documents
as making SQLite faster lives in a *tuning* layer, not an architecture change: journal mode,
`synchronous`, `cache_size`, `mmap_size`, `page_size`, `locking_mode=EXCLUSIVE`, `temp_store`,
`wal_autocheckpoint` `[7]`. `locking_mode=EXCLUSIVE` is documented as "possibly resulting in a small
performance increase" and works by *never releasing file locks* `[7]` — i.e. SQLite buys it by giving
up multi-process access, which an embedded engine's generator **cannot** do. That is the one place
where a shape qualifier, not a ratio, is required.

---

## D2 — Record layout and in-place update: how page engines avoid relocating rows

Three mechanisms recur across every page-based engine surveyed. Each has a precise precondition, and
each maps onto a mechanism we already half-own.

### 2.1 Indirection: the slot is stable, the bytes are not

- PostgreSQL's page carries `ItemIdData`, described as an "array of item identifiers pointing to the
  actual items. Each entry is an **(offset, length) pair. 4 bytes per item**." Free space sits between
  the pointer array (growing from the start of the page) and the items (growing from the end), with
  `pd_lower`/`pd_upper`/`pd_special` as the boundaries `[2]`.
- **The consequence is the whole idea:** indexes and pages reference the *item identifier*, not a byte
  offset, so the item's bytes may be rewritten at a different offset within the page without
  invalidating anything `[2]`. In an append-only engine the equivalent stability has to come from
  somewhere else — which is exactly why our fixed-width layout is fast and our variable-length layout
  is not.
- The row header is **23 bytes** plus an optional **null bitmap of one bit per column**; user data
  starts at `t_hoff` aligned to `MAXALIGN`. Column offsets are *not* stored — they are recomputed by
  walking `attlen`/`attalign`, and the docs state there is "no way to directly get a particular
  attribute, **except when there are only fixed width fields and no null values**" `[2]`.
  **This is our exact documented weakness**: any patch path that must find a field offset by walking
  the encoded row pays for the absence of a fixed layout, and it pays again on every patch.

### 2.2 HOT: skip index maintenance when nothing indexed changed

- The stated motivation is ours verbatim: "updates require new versions of rows to be added to
  tables. This can also require new index entries for each updated row, and removal of old versions
  of rows and their index entries can be expensive" `[3]`.
- HOT **qualifies only when** (a) "the update does not modify any columns referenced by the table's
  indexes", and (b) "there is sufficient free space on the page containing the old row" `[3]`.
- When it qualifies, **no new index entries** are needed, and intermediate versions are removable
  during normal operation rather than by periodic VACUUM: "indexes always refer to the page item
  identifier of the original row version … its item identifier is converted to a **redirect** that
  points to the oldest version that may still be visible" `[3]`.
- Condition (b) has a documented mitigation — lower `fillfactor` — and the docs note HOT still happens
  without it because rows "naturally migrate to new pages" `[3]`. Observability is first-class:
  `pg_stat_all_tables` reports HOT vs non-HOT updates `[3]`. **We have no equivalent counter**, so we
  currently cannot tell an in-place patch from an append from the product's own metrics.

### 2.3 Out-of-line values: keep the in-page record constant-size

- PostgreSQL forbids cross-page tuples, so oversized values are "compressed and/or broken up into
  multiple physical rows" transparently — TOAST `[4]`. An out-of-line value is stored as a pointer
  datum with no inline data, so the containing tuple's size becomes independent of the value's size `[4]`.
- The `varlena` length word has **two encodings**: a 4-byte, 4-byte-aligned header, and a **1-byte
  header for values under 127 bytes**, which is *not* aligned — "this omission of alignment padding
  provides additional space savings that is significant compared to short values" `[4]`.

### 2.4 The alternative we are *already* on, and why it cannot be tuned into parity

RocksDB is an LSM key-value store whose wiki index for update/delete work is a list of dedicated
operators — `Merge` (read-modify-write), `Single Delete`, `DeleteRange`, `Compaction Filter`,
`Write Stalls`, `TTL` `[13]`. In an LSM, update and delete are **bookkeeping problems**, not edits;
reclaim is deferred until compaction, and the failure mode even has a name, `Write Stalls` `[13]`.

InnoDB shows the third option and its price: four row formats, `DYNAMIC` the default, where long
variable-length columns move entirely off-page. The price is real and documented — `REDUNDANT`/
`COMPACT` cap the index key prefix at **767 bytes** versus **3072 bytes** for `DYNAMIC`/`COMPRESSED`,
a format mismatch between source and replica can make DDL succeed on one side and fail on the other,
and changing format is a **table-rebuilding** operation `[15]`.

**Conclusion D2 supports.** SharpCoreDB's default shape is the LSM column (append a version, defer
reclaim behind `ColumnarAutoCompactionThreshold`) and its fixed-width PK shape is the page column.
"Beating SQLite on the default shape" therefore does **not** mean making the LSM faster; it means
giving the default shape the page column's in-page edit. PostgreSQL did not change storage model to
get HOT — it added indirection, a precondition test, and free-space policy. Those are the levers.

---

## D3 — .NET 11 and hardware/crypto accelerators

### 3.1 The runtime is genuinely one notch faster, and the wins are shape-dependent

- .NET 11 is in **RC**; GA "is expected in November 2026" `[9]` — the same horizon as the mission.
- **Runtime Async is now the default for `net11.0`**: it no longer requires
  `<EnablePreviewFeatures>`, and "the runtime libraries themselves are compiled with
  runtime-async=on" `[9]`.
- **Measured**, same process and runtime, differing only in compiler lowering: at depth 30 the
  runtime-async form reads **0.21× the mean time and 0.07× the allocation**; at depth 1, 0.75×/0.72×;
  and it **loses (1.30×) on a single-`Yield` path** `[6]`. **Transferable caution:** our async
  surfaces (`InsertBatchAsync`, `ExecuteBatchSQLAsync`, `Execute*Async`) are *shallow*, so they
  belong to the shallow end of that curve, not the 5× headline. Any win must be measured, not assumed.
- Named JIT work: "**bounds check elimination**, redundant checked context removal, **devirtualization**,
  switch expression folding, constant-folding `SequenceEqual`, and redundant branch elimination" `[9]`.
  The .NET 10 post shows the mechanism concretely — assertions derived from `switch` targets let the
  JIT prove span indices bounded, and `CORINFO_HELP_RNGCHKFAIL` plus **six jumps to it evaporate**,
  shrinking the method from 103 to 70 bytes `[14]`. That is a *code-shape* dependency: the win
  arrives only where the code switches on a length and then indexes.
- **NativeAOT: "faster interface dispatch using a shared dispatch helper … improving throughput for
  interface-heavy workloads"** `[9]`. Our per-row `IStorageEngine`/`IStorage` dispatch is exactly that
  shape, which makes an AOT recommendation in our own docs a defensible, testable claim.
- **Intrinsics:** AVX-VNNI-512 multiply-add, improved hardware-intrinsic cost modeling, a
  single-instruction `Math.BigMul` on x64, new Arm SVE2 intrinsics `[9]`; plus SIMD lane
  construction/composition APIs (`CreateGeometricSequence`, `Zip`, `Unzip`, `Concat`) across
  `Vector128/256/512/64<T>` and `Vector<T>` `[9]`. **These are `System.Runtime.Intrinsics` APIs**, so
  adopting them satisfies the repo's `SIMD_STANDARDS.md` ban on `System.Numerics.Vector<T>`.
- **Libraries:** Zstandard in `System.IO.Compression`, improved Base64, IEEE-754 decimal floats `[9]`.
  In-box Zstd removes the "add a package" objection to a page/WAL compression mode.
- **C# 15** (default on `net11.0`): collection expression arguments, union types, closed hierarchies,
  extension indexers, **labeled break and continue**, memory safety `[9]`.
- **Deployment caveat, new in 11:** "updated **minimum hardware requirements** … requiring more modern
  instruction sets" `[9]`. A `net11` build cannot run on as old a CPU as a `net10` build — a product
  decision, not a footnote. *Note: the .NET 10 overview `[16]` lists AVX10.2 support, which the 11
  overview does not repeat — treat per-ISA availability as something to verify against the runtime
  docs before relying on it.*

### 3.2 The encryption tax is an API contract, so layout is the only lever

- `AesGcm` exposes exactly two data operations, both whole-buffer single-call:
  `Encrypt(nonce, plaintext, ciphertext, tag, associatedData)` and the matching `Decrypt` `[5]`.
- The **tag is a separate output buffer** and the **nonce is per-message input** `[5]` — so
  per-record framing means a per-record nonce and a per-record tag.
- **There is no incremental, streaming or seekable update API** — the page enumerates the complete
  public surface (`Encrypt`, `Decrypt`, `Dispose`, and properties) `[5]`. The doc names
  `Microsoft.Bcl.Cryptography v11.0.0-rc.1.26425.128` `[5]` — the same `26425.128` build as the SDK
  pinned in `global.json`, so the API described is the one we compile against.
- **This explains our own measurement, and it is not a defect.** Our profiled `commit-overwrites`
  reads 19.5 ms / 1.60 MB encrypted vs 3.4 ms / 0.55 MB raw ("3× the bytes"), and `in-place-patch`
  8.3 ms vs 1.2 ms at *identical* 175 B/call. You cannot patch one field of an AEAD record; you must
  re-encrypt the record. **Therefore the only way to make encrypted UPDATE cheap is to make the
  encrypted unit small and constant-size** — which is D2's conclusion from the other side. This is
  the run's strongest cross-dimension finding.

### 3.3 The runtime ships two primitives our write path does not use

Rounds 1–2 read the *feature list*; round 3 read the **API surface** of the storage-relevant types
(`digests/d3-dotnet-accelerators-r3-1.md`).

- **Scatter/gather file I/O is a documented first-class API, not a P/Invoke.** `System.RandomAccess`
  exposes `Read`/`Write` and their async forms both as a single `Span`/`Memory` **and as a list of
  buffers** (`IReadOnlyList<Memory<byte>>` / `IReadOnlyList<ReadOnlyMemory<byte>>`). The class summary is
  *"Provides offset-based APIs for reading and writing files in a thread-safe manner"*, and the only
  documented restriction is *"Only regular disk files are supported. Unseekable files, like pipes, are
  not supported."* `[17]` — both satisfied by our file-backed design.
- **The Zstd object model is not just a stream wrapper.** `ZstandardEncoder` is sealed and
  span-in/span-out with **`GetMaxCompressedLength(long)`**, `Compress(ReadOnlySpan<byte>, Span<byte>, …)`,
  `Flush`, **`Reset()`**, `SetPrefix(ReadOnlyMemory<byte>)` and `SetSourceLength(long)` `[20]`;
  `ZstandardDictionary` can be **`Create`d or `Train`ed in-box** `[21]` — no external `zstd` tool, so a
  dictionary mode stays inside the "pure .NET, no P/Invoke" constraint; and
  `ZstandardCompressionOptions` carries twelve properties including `TargetBlockSize` and `Dictionary`
  `[19]`. **No remarks section exists on any of those pages**, so which properties land in the *frame
  header* — and therefore what a reader must reproduce — is undocumented, with one exception sourced
  outside the API surface: the frame's `Dictionary_ID` field itself (`[22][23]`, compatibility ruling).
- **Honest limit, stated because the recommendation depends on it:** none of the pages fetched in
  this round documents a syscall count, an allocation or a throughput. *"One vectored write is one
  syscall"* is a **hypothesis for our harness**, never a citation (see Open questions).
- **Seekability does not rescue the compressed-block case:** `ZstandardStream` is seekable
  (`CanSeek`, `Seek`, `SetLength`, `SetSourceLength`) `[18]`, but reaching a byte inside a zstd frame
  still means decoding that frame — so this does **not** weaken 3.2's and D2's layout conclusion.

### 3.4 Repo cross-check — which of these the code already calls

*Every statement below is read off the file named; no behaviour is inferred from an API's existence
(`digests/d3-dotnet-accelerators-r3-2.md`).*

- **Zstd is shipped on the *stream* path:** `Services/BlockCompressor.cs:58` builds a `ZstandardStream`
  with an options object (`Quality`, `AppendChecksum = true`), which `docs/net11/UPGRADE.md:20-24`
  documents. `ZstandardEncoder`, `ZstandardDictionary`, `TargetBlockSize`, `EnableLongDistanceMatching`
  and `WindowLog2` appear **nowhere** under `src/` — those levers are open.
- **The codec round-trips through a `MemoryStream` per block** (compress → `ToArray()`, decompress →
  `data.ToArray()`): an allocation/copy opportunity, **not a defect** — the bytes and the frame are
  correct.
- **`AesGcm` is saturated** (3.2): per-thread cipher cache, `stackalloc` nonce/tag, pooled ciphertext,
  `IsSupported` surfaced as `IsHardwareAccelerated` (`Services/AesGcmEncryption.cs`, ~line 214). No
  `AesGcm` API is left to adopt, so 3.2's layout conclusion is the only crypto lever — **do not re-open it**.
- **`RandomAccess` is already the primitive, used one buffer at a time.** It appears at 15 call sites
  across `Services/Storage.Append.cs` and `Services/Storage.PageCache.cs` (e.g. `PageCache.cs:93` inside a
  short-read loop). The only place one logical write is two calls is `Services/Storage.Append.cs:469-470`
  (`WriteRecordInPlace`: the 4-byte prefix, then the record) — on the in-place patch path, i.e. the UPDATE
  cell the default arms are measured short on.
- **The loud-failure compatibility pattern already exists:** `Services/BlockCompressor.cs:58-61` records
  that `Zstd` mode throws `PlatformNotSupportedException` on `net10` rather than misreading. Any new
  format-affecting compression feature must copy that pattern, not invent a second one.

---

## D4 — What "beating SQLite" can legitimately mean (comparator hygiene)

- SQLite's tuning surface is large and documented `[7]`; the vendor's own (obsolete) benchmark shows
  `nosync` at 1.4–1.7× on write tests `[12]`. Direction-only, low confidence — but sufficient to
  establish that **an untuned SQLite and a tuned SQLite are different comparators**.
- SQLite publishes **no CRUD benchmark** — neither `speed.html` (obsolete, 2.7.6 era) nor any other
  vendor page supplies a current ops/s figure `[12][11]`. Every ratio, in our favour or against us, is
  therefore *our* measurement of *their* engine: the credibility of the claim rests entirely on the
  measured protocol, not on a citation.
- The vendor's own workload guidance — "group multiple operations together into a single transaction"
  `[12]`, "WAL works best with smaller transactions" `[8]` — is the guidance our harness should follow,
  and it puts an upper bound on how large our batch-insert path should grow.
- `locking_mode=EXCLUSIVE`'s "small performance increase" `[7]` is unobtainable for us without
  abandoning multi-process access, which `AGENTS.md` forbids for the server. The honest form of the
  claim is therefore: **beat tuned SQLite on the shapes we ship, and name the shape** — not "beat
  SQLite".

---

## Round 3 — the lever ledger, and what each lever costs the on-disk format

*Added 2026-10-09 (round 3). Rounds 1–2 asked what makes SQLite fast and what .NET 11 offers; this round
asks the question the plan actually needs: **which of those levers are already shipped here, which are
open, and which ones change the file.** Repo state is cited to `src/…`; API surfaces to `[17]`–`[22]`, and
the one frame-format fact to `[23]`. The
full evidence is in `digests/d3-dotnet-accelerators-r3-1.md` (API surface), `…-r3-2.md` (repo state) and
`…-r3-3.md` (this ledger).*

| # | Lever | Where it acts | Status | On-disk / compat impact | Falsifiable prediction |
|---:|---|---|---|---|---|
| 1 | List-form `RandomAccess.Write` for prefix + record | `Services/Storage.Append.cs:469-470` (in-place patch → UPDATE) | **REJECTED on measurement** (2026-10-09, session 77) — the one-syscall reading holds **only on Unix**: the list form routes to `PWriteV` there, while Windows issues **the same 2 OS writes** for both forms (counted exactly with `GetProcessIoCounters`: 1.000 ops per 500 iterations each) and is 0,982× on time, i.e. noise | **none** — same bytes at the same offsets (and no code change was made) | *Applied as written:* the prediction was "if the UPDATE ratio does not move, the 'one call = one syscall' reading is wrong and the item is dropped" — on this platform the OS-call count does not move at all, so it was dropped before any `src/` edit; the Unix half is **open** as a Linux-only candidate (needs a Linux measurement, not a citation) |
| 2 | Reusable span-based `ZstandardEncoder` + `GetMaxCompressedLength` + pooled output | `Services/BlockCompressor.cs` | **landed** (2026-10-09, session 77) — stream path kept for Brotli/GZip and as the Zstd fallback branch | **readable both ways, but NOT byte-identical**: the frame differs from the stream writer's by a few bytes in *either* direction (4 KB at quality 5: 83 → **77** B; 64 KB: 784 → **785** B). Old frames still decode through the new reader and new frames still decode through a plain `ZstandardStream` — both pinned by tests | *Confirmed:* allocation per block **2.480 → 0 B** at 4 KB and **33.896 → 0 B** at 64 KB (the returned frame is the only surviving allocation), red→green. *Corrected:* the "identical frame, level and checksum" cell this row originally carried — the level mapping is unchanged, the bytes are not |
| 3 | `TargetBlockSize` / `WindowLog2` / `EnableLongDistanceMatching` | same | **open**; property existence verified `[19]` | **none for readability** (legal frame fields), but *which* land in the header is undocumented | Needs an A/B at our block sizes; under ~2 % it is dropped rather than kept "because it is free" |
| 4 | `ZstandardDictionary` (trained) **or** `SetPrefix` | same, and only if a block is still variable after the structural item | **open — format-affecting** | **breaks** old readers, and the mechanism is now sourced: the frame's `Dictionary_ID` marks the requirement `[23]` and .NET takes the dictionary as caller-supplied state `[22]`, so it must live in our file header behind a magic/version gate | Only worth a format bump if its gain on small, self-similar blocks beats the header cost and the migration work — measured, never assumed |
| 5 | `AesGcm` API surface | `Services/AesGcmEncryption.cs` | **closed — saturated** | n/a | No lever remains; re-opening this is a known waste of a session (3.2, 3.4) |
| 6 | Span-based `ParseValue` / prepared-statement binding | row codec + statement binding | **open** | **none** (source-level only) | C# 15 labeled `break`/`continue` `[9]` can remove scanner sentinels; measure before claiming |
| 7 | NativeAOT interface dispatch | every row, every arm | **BLOCKED** — VS C++ workload absent (brief §9 row 6) | none | Unchanged from rounds 1–2; this round found no reason to re-open it |

### The compatibility ruling

1. **Exactly one lever in this round touches the file: a Zstd dictionary or prefix (#4).** #1, #2, #3 and
   #6 write the same bytes, a legal frame, or the same IL.
2. **#4 is not backwards compatible — not with a `net10` build of this product and not with an earlier
   `net11` RC build — and that ruling now stands on evidence rather than on caution.** The zstd **frame
   header carries a `Dictionary_ID`**: its width is the low two bits of `Frame_Header_Descriptor`
   (`fhd & 3` — 1, 2 or 4 bytes, absent when the flag is 0), bit 5 is `Single_Segment_flag`, and bit 3 is
   reserved with a set reserved bit treated as an error `[23]`. The consumer side agrees: 
   `ZstandardDecompressionOptions` exposes only `Dictionary` and `MaxWindowLog2` `[22]`, i.e. the dictionary
   is state the *caller must hand back* to the decoder (through `ZstandardDecoder(ZstandardDictionary)` or
   the dictionary-taking stream constructors `[18]`) before a dictionary-bearing frame decodes at all.
   Any dictionary mode is therefore a **new format version**, gated the way `BlockCompressionMode.Zstd`
   already is: a loud `PlatformNotSupportedException` from a build that cannot decode it, never a silent
   misread. *Still unverified, and separately:* the failure a .NET decoder raises on a missing or
   mismatched dictionary (exception type and message) — no fetched page states it, so that half is a
   two-build experiment, not a citation.
3. **The non-format levers stay inside every rail the brief sets:** no durability change (WAL ordering and
   the write offsets are untouched — identical bytes at identical offsets), no encryption change and no
   flip of the fixed-width layout default (§8 trap 3). **#1 and #2 may therefore land *before* the
   structural item**, which is the only ordering change round 3 makes.

### What round 3 did not find

- **No API that makes a variable-length row updatable in place without changing the layout** — the
  layout conclusion of rounds 1–2 stands, and `ZstandardStream`'s seekability `[18]` does not disturb it.
- **No quantified benefit of the Zstd object model over the stream model** — the object model must be
  measured on our harness; a page proves the call exists, not that it wins.
- **No GC/allocator claim for .NET 11** — the fetched slices still do not cover GC (Open questions,
  unchanged), and no AVX10.2 confirmation for `net11.0`.
- **No direct read of the spec's frame-header table** — the `Dictionary_ID` mechanism is sourced to an
  independent implementation that deep-links the specification's own `#dictionary_id` anchor `[23]`;
  RFC 8878's table itself stays an **Open question**, because the pages carrying it middle-truncate in
  this run's fetch tool.

---

## Cross-dimension insights

These are the findings that only the *combination* of dimensions produces.

1. **Layout and crypto agree, from opposite directions — the strongest signal in this run.** D2
   (storage engines) concludes that variable-length rows need indirection, an indexed-column test and
   off-page values to be updatable in place. D3 (crypto) concludes that because AEAD is whole-record
   and non-incremental `[5]`, the encrypted unit must be *small and constant-size* or encrypted
   UPDATE can never be cheap. Both arrive at the same artifact: **a fixed-size in-page slot whose
   variable-length content lives out of line.** Neither lens alone would have been as convincing, and
   they ran without knowledge of each other's conclusion.

2. **Our "different league" wins are orthogonal and must be protected, not spent.** The research found
   nothing that makes SQLite better at columnar SIMD aggregates, vector search, encrypted-at-rest
   defaults or P/Invoke-free embedding. Every lever below is therefore additive: none requires trading
   a feature the product already wins on.

3. **The cheapest lever in the whole run is in the runtime, not the storage.** NativeAOT's shared
   interface-dispatch helper `[9]` targets "interface-heavy workloads" — and our per-row
   `IStorageEngine`/`IStorage` dispatch is precisely that shape, on every row, in every arm, in both
   plaintext and encrypted modes. It is the only lever that helps *all four operations and all four
   arms at once*, and it costs a build configuration rather than a format change. *(Execution note,
   2026-10-09: the build configuration turned out to be blocked — the VS C++ workload is absent — so the
   hypothesis is untested rather than refuted; see the Recommendations status re-check.)*

4. **Two of our own measurement practices are upstream of any performance claim.** D4 establishes that
   SQLite's tuning can swing its numbers >2× `[7][12]`, while our `REGIME:` banner records only
   `SHARPCOREDB_*` on our side. And D1 establishes that SQLite's own guidance is "batch into
   transactions" `[12]`. So (a) every published ratio needs a two-sided regime banner, and (b) the
   harness's SQLite arm must be documented at a named pragma set or the number is not falsifiable.

5. **HOT's precondition (a) and our existing key-only delete work are the same idea at different
   maturity.** `DeleteByPrimaryKey` already runs key-only with no storage read when no hash index needs
   the row; the UPDATE side has no equivalent gate `[3]`. This is a *completion* of an existing pattern
   rather than a new architecture — the lowest-risk item in the plan. *(Execution note, 2026-10-09: the
   gate shipped as a provably-equivalent no-op removal and the plan's item closed `REJECTED` *as
   specified* — the precondition is unreachable on a Columnar table, where every column is auto-indexed —
   so this insight stands as a mechanism, not as a pending win; see the Recommendations status re-check.)*

6. **Round 3: two of the plan's behind-cells have a *mechanical* address that costs no format change.**
   D1 shows a per-page write stays cheap because durability bookkeeping is centralized in the pager `[1]`;
   D3 shows the runtime already ships the primitives for that shape — vectored writes and a reusable
   span-based Zstd encoder `[17][20]` — and the repo cross-check shows **we already call `RandomAccess`,
   one buffer at a time**, issuing one logical record write as two calls (`Storage.Append.cs:469-470`).
   The combination is the point: the deficit in the default arms is *not* an absence of a primitive, and
   the cheapest work the research can name is call-site shape, not a new storage architecture. It also
   disciplines the plan's order — cheap and format-neutral first, structural second, dictionary last.

---

## Contrary evidence

*A red-team pass was not run: `{workflow.red_team}` resolves to `off` and validation is `normal`. The
counter-arguments below are the ones the purchased evidence itself contains — quoted, not invented.*

- **Against "make the default shape in-place":** RocksDB — a 32k-star production engine — deliberately
  does *not* do it, and instead ships dedicated update/delete operators plus compaction `[13]`. The LSM
  column is a legitimate engineering choice, not a mistake, and the brief's §8 trap 3 records that
  flipping our layout default *towards* fixed-width on the PK-less shape measured a **−24 % regression**.
  So the plan must not assume "in-place is simply better" — it must show the win on the default shape
  specifically, at the same footprint.
- **Against "off-page values are free":** InnoDB documents concrete costs for exactly this layout —
  767 vs 3072-byte index key prefixes, source/replica DDL mismatch, and table-rebuilding format
  changes `[15]`. PostgreSQL's TOAST likewise changes the representation of a stored value in a way
  every C-level function must handle `[4]`. Any out-of-line design we adopt buys in-page stability with
  index-key headroom and migration mechanics, and must say so.
- **Against "net11 will lift the write path":** the run's own numbers show runtime async **losing
  1.30×** where there is a single suspension `[6]`. The headline 5× is a deep-chain microbenchmark.
  Treating `net11.0` as a free ~2× on our async surfaces is exactly the "inferred reading" failure this
  repo's plan has already paid for five times.
- **Against "so we beat SQLite":** the only *current* vendor-published material points the other way —
  SQLite's FAQ names slow INSERTs as a user-reported problem and answers it with batching `[11]`, which
  is what our own batch path already does. Nothing here shows SQLite is slow on the shapes we ship badly
  on; the burden of proof stays with our measurements.

---

## Recommendations

Each is bound to the decision and names its confidence basis.

1. **Adopt the two-sided regime banner before any new number is published.** Every comparison must name
   SQLite's pragma set (`journal_mode`, `synchronous`, `cache_size`, `mmap_size`, `page_size`,
   `locking_mode`, `temp_store`, `wal_autocheckpoint`) alongside the existing `SHARPCOREDB_*` line.
   *Basis:* D4 — high-confidence vendor documentation `[7]`, plus the obsolete-but-directional 2× swing
   `[12]`. *Feeds:* `docs/performance/INSERT_UPDATE_PERFORMANCE_PLAN.md` §2 and the harness's `REGIME:`
   output.

2. **Add a per-statement "does this UPDATE touch an indexed column?" gate — the HOT condition (a).**
   When no updated column participates in a loaded index, skip all index-maintenance work on the update
   path, completing the pattern `DeleteByPrimaryKey` already uses. *Basis:* D2 — high-confidence primary
   vendor docs `[3]`, plus our own existing key-only delete path. *Feeds:* the new plan's first work
   item; lowest-risk, measurable per-op.

3. **Give the default (PK-less / variable-length) shape a stable slot: a fixed-size in-page record with
   variable content behind an offset.** The structural item — and the one that must be gated on the
   −24 % trap-3 evidence. *Basis:* D2 `[2][4]` + D3 `[5]` (two independent lenses), with InnoDB's
   counter-costs `[15]` as the price tag. *Feeds:* the new plan's structural item; needs a format-version
   hook (the magic already reserves version bytes) and a migration path.

4. **Ship a NativeAOT dispatch recommendation and measure it.** *Basis:* D3 `[9]`, high-confidence vendor
   feature statement; the *magnitude* is unmeasured on our workload, so this is measure-first, not a
   claimed win. *Feeds:* docs plus a benchmark arm.

5. **Bank the "already winning" list explicitly as non-goals.** Columnar/SIMD aggregates, vector search,
   encrypted-at-rest default, P/Invoke-free embedding. *Basis:* cross-dimension insight 2 — medium
   (absence of contrary evidence across 23 sources). *Feeds:* the new plan's scope section and the canary
   list.

6. **Adopt the small, shape-matched runtime wins where the JIT's own conditions hold** — switch-on-length
   then index patterns in the row codec, and `System.Runtime.Intrinsics` lane APIs that already satisfy
   `SIMD_STANDARDS.md`. *Basis:* D3 `[14][9]`, high-confidence but microbenchmark-derived; each must be
   measured on our harness. *Feeds:* opportunistic items, scheduled only after 1–3.

7. **(Round 3) Issue the in-place record overwrite as one vectored write.** `WriteRecordInPlace`
   (`Storage.Append.cs:459-471`) writes the 4-byte length prefix and the record as two adjacent calls on
   the single-buffer overload; `RandomAccess` also offers a list-taking form `[17]`. *Basis:* D1's
   page-write centrality `[1]` + D3's API evidence `[17]` + the repo cross-check (3.4) — all three are
   needed, which is why round 1–2 missed it. **Changes no bytes on disk and no guarantee.** *Feeds:* the
   UPDATE cell of arms B/C (the plan's §5.1 table). **State the limit with the item:** the API page does
   not document the syscall count, so the expected win must be demonstrated on the harness, and the
   single-buffer calls stay as the fallback branch.

   **Outcome (2026-10-09, session 77): `REJECTED on measurement` — the item's own limit, made exact.** The syscall saving exists on one platform only. In the runtime, the list form's Unix path is `Interop.Sys.PWriteV` (`RandomAccess.Unix.cs`, `WriteGatherAtOffset`) — one syscall for the whole vector — while Windows has no `pwritev`, so the same managed call issues the same per-buffer writes after pinning each buffer. Counted exactly on this box with a standalone RC.1 probe using `GetProcessIoCounters` deltas: **1.000 OS write operations per 500 iterations for both forms** (2,00 each), and timed min-of-5 over 20.000 operations at **5.628 ns/op vs 5.528 ns/op = 0,982×** — noise. So there is **no win on Windows**, and the Unix half is two signatures away from being reachable: a list form needs the five call sites of `WriteRecordInPlace` to hand over `ReadOnlyMemory<byte>` (they take `ReadOnlySpan<byte>` today) plus a per-call list allocation the current shape does not have. No code was changed. The Unix half stays **open as a Linux-only candidate** and needs a Linux measurement of the same probe before anyone writes it — this box has no .NET SDK in WSL and no invocable Linux leg, so the source evidence is where it stops rather than being promoted to a win.

8. **(Round 3) Replace the block codec's `MemoryStream` round-trip with a reusable span-based
   `ZstandardEncoder`.** `GetMaxCompressedLength` sizes a pooled destination, `Compress` fills it, and
   `Reset()` carries the instance to the next block `[20]`; the frame, level and checksum stay
   byte-identical, so it is format-neutral **(superseded — see the outcome below: `Reset()` and the level
   mapping hold, the *frames* are not byte-identical)**. *Basis:* D3 `[20]` + `Services/BlockCompressor.cs` (2 full
   copies per block today). *Feeds:* allocation/op on the compression arms; must be validated by the
   existing round-trip and reopen canaries, because a codec change that alters a frame is a data-loss bug.

   **Outcome (2026-10-09, session 77): `KEPT` — with one premise corrected and the correction measured in both directions.** The allocation half is confirmed and lands: per-block managed allocation **2.480 → 0 B** at 4 KB and **33.896 → 0 B** at 64 KB (reused thread-static encoder + `Reset()` + a destination rented from `ArrayPool<byte>.Shared` and sized by `GetMaxCompressedLength`; the returned frame is the only surviving allocation), red→green in `BlockCompressor_Zstd_Compress_AllocatesOnlyTheReturnedFrame` (129.496 B allocated with the stream shape, then ≤ 4.096 B). The "**byte-identical, so it is format-neutral**" premise is **false and was replaced**: at quality 5 the encoder's frame is 6 bytes *smaller* on a 4 KB block (83 → 77) and 1 byte *larger* on a 64 KB one (784 → 785), and `GetMaxCompressedLength` is an upper bound (4.174 B of capacity for 84 B of frame). What survives is compatibility by *validity*, pinned in three directions by tests — the new frame decodes through `BlockCompressor.Decompress`, a frame written by the old stream writer still decodes through the same public API, and a new frame stays readable by a plain `ZstandardStream`. `Decompress` stays stream-based: `TryGetMaxDecompressedLength` answers **131.072** (the window bound) for a *stream-written* 4 KB block, so it cannot size the frames already on disk. The codec also runs before encryption, so no durability or encryption guarantee is involved.

9. **(Round 3, conditional) A trained Zstd dictionary for block compression — one gate closed, one open.**
   `ZstandardDictionary.Create`/`Train` is in-box `[21]`, and the dictionary-bearing frame is
   **format-affecting** and breaks every reader without the dictionary, including older builds of this
   product (Round 3 ruling #2). *Basis:* D3 `[21]`; the format mechanism `[22][23]`; repo state (dictionary
   unused anywhere). *Feeds:* **not** a work item yet — gate **(a) is answered**: the frame carries a
   `Dictionary_ID` sized by the header descriptor `[23]` and the decompression API requires the dictionary
   as caller-supplied state `[22]`, so a format-version gate is the only correct shape. What is left is
   gate **(b)**: a measured gain on our block sizes that beats the header cost and the migration. *Never*
   treat it as a tuning knob.

**Recommended order (round 3):** 7 → 8 → 1 → 2 → 3 → (4/6 opportunistic), with 5 as a standing
constraint and 9 gated. Items 7 and 8 change no bytes on disk and no guarantee, so they can land *ahead*
of the structural work; item 1 costs documentation and no code; item 2 is a narrow, reversible branch on
an existing path; item 3 remains the only format-affecting change in the original list and must reproduce
the trap-3 control first.

**Status re-check against the plan (2026-10-09, round 3).** These recommendations were handed to
`docs/performance/BEAT_SQLITE_ALL_AXES_PLAN.md`, whose items **S1–S7 have all since closed** — so what
moved is the *status* of several recommendations, never their reasoning:

| Rec | Plan item | Execution outcome, cited to the plan (§4/§8) |
|---:|---|---|
| 1 | **S1** | ✅ `KEPT` — the harness prints `REGIME (SQLite reference):` and archives `SqlitePragmas` |
| 2 | **S2** | ⛔ `REJECTED` *as specified* — HOT's precondition (a) is unreachable on a Columnar table (every column is auto-indexed), so the 20.000 `index-maint` calls are correct work; the gate is kept as a provably-equivalent no-op removal, and the target became owner decision §9 row 5 |
| 3 | **S3** | ⛔ `REJECTED` — the −24 % trap-3 control reproduced at capacity 24, so arm C's target is an owner decision, not a build |
| 4 | **S4** | ⛔ `BLOCKED` — VS C++ workload absent; the dispatch hypothesis is **untested, not refuted** |
| 5 | §3 non-goals | ✅ in force — the already-won list is protected, not spent |
| 6 | **S6** | ⛔ `REJECTED` on attribution — the JIT shape is already present and worth 0,3 % (`encode-layout`), while index work is ≥ 31,1 % of INSERT |
| 7, 8 | — | **closed (2026-10-09, session 77)** — round 3's additions, both measured before any `src/` edit: **8 landed** (`Services/BlockCompressor.cs`, allocation 2.480 → 0 B per 4 KB block, frame validity pinned in both directions) and **7 rejected on measurement** (Windows issues the same 2 OS writes for either form, so the lever it was named for does not exist on the measured platform). The Unix half of 7 is the one open item, deferred to a Linux measurement |
| 9 | — | **gated** — the frame-ID mechanism is **answered** (`[22][23]`); it now waits only on a measured gain |

**Round 3 therefore changes no verdict, only the order and the open list.** Not one S1–S7 outcome is
refuted; the two cheapest new levers are open precisely because the plan's completed items addressed
layout, index work and build configuration — not call-site shape.

---

## Open questions

| Question | What it would take to answer |
|---|---|
| The exact SQLite cell payload/overflow rule and freeblock coalescing (file-format §1.7) — the fetched slice stopped before the raw numbers | One targeted read of `sqlite.org/fileformat2.html` §1.6–1.7 |
| Does SQLite ever *move* a row within a page on UPDATE, and how does it defragment? | SQLite source (`btree.c`) or a maintainer statement; not in the pages fetched |
| PostgreSQL's `fillfactor` default and tuned range for write-heavy tables | `postgresql.org` `CREATE TABLE` storage-parameters page |
| RocksDB's `Write Stalls` mechanics and thresholds — our `TryAutoCompact` analogue | `github.com/facebook/rocksdb/wiki/Write-Stalls` |
| Any GC-specific improvement in .NET 11 (the fetched slices did not cover GC) | The GC section of the .NET 11 performance post |
| Real per-ISA availability of AVX10.2 on `net11.0` — the 11 overview does not repeat the 10 overview's claim | The .NET 11 runtime-intrinsics docs |
| What a *tuned* SQLite reference number looks like on **this** machine, with the pragma set named | One harness run with the SQLite arm's pragmas pinned and printed |
| **How many syscalls does a vectored `RandomAccess.Write` actually issue**, and does one list-call beat two single-buffer calls on the patch path? (round 3 — the question the write-path recommendation hangs on) | **ANSWERED (2026-10-09, session 77).** On Windows: **2 OS write operations either way** (exact, `GetProcessIoCounters` deltas over 500 iterations per form) and 0,982× on time — so no gain here, and the item is `REJECTED`. On Unix the runtime routes the list form to `PWriteV` (one syscall), which is **source-evidenced but unmeasured on this box** (no .NET SDK in WSL, no invocable Linux leg): the open half is now *the Linux measurement*, not the API question |
| **Does `ZstandardEncoder.Reset()` preserve options and dictionary across frames**, and is one instance safe to reuse concurrently? | **ANSWERED, half of it (2026-10-09, session 77): options are preserved** — a `Reset()`-ed instance produced a frame **byte-identical to a fresh instance's** and both decoded to the input, which is what the landed code relies on (one thread-static encoder per level). The *concurrent-reuse* half is **not** answered and is not relied on: the implementation keeps one instance per thread (`[ThreadStatic]`), so no instance is shared across threads. The dictionary half needs a `ZstandardDictionary` and is inert while rec 9 stays gated |
| **What exactly does a .NET decoder raise on a missing or mismatched dictionary?** — the *mechanism* is answered (`[22][23]`: the frame's `Dictionary_ID` is sized by the header descriptor, and .NET takes the dictionary as caller-supplied decompression state), so what remains is the failure a reader must produce | A two-build experiment on RC.1 — one build writes with a dictionary, another reads without it; the API pages carry no Remarks section `[19][22]` |
| **The specification's own frame-header table, read directly** — the `Dictionary_ID` sizing is sourced to an implementation that deep-links the spec's `#dictionary_id` anchor (`[23]`), not to the normative table | One targeted read of `rfc-editor.org/rfc/rfc8878.txt` §3.1.1.1 or the reference repo's `doc/zstd_compression_format.md#frame-header`; both middle-truncate in this run's fetch tool |

---

## Source appendix

| # | Claim / finding it supports | Publisher | Pub date | Accessed | Confidence |
|---:|---|---|---|---|---|
| [1] | Bytecode VM, prepare-once, per-table/index B-trees, pager owns atomic commit; default page 4096 B (512–65536) | [sqlite.org/arch.html](https://www.sqlite.org/arch.html) | 2025-05-31 | 2026-09-24 | high |
| [2] | `ItemIdData` = 4-byte (offset,length) item identifiers; 23-byte row header; null bitmap; offsets not stored | [postgresql.org · Database Page Layout (PG 18)](https://www.postgresql.org/docs/current/storage-page-layout.html) | PG 18 | 2026-09-24 | high |
| [3] | HOT preconditions (no indexed column changed + page free space), redirect mechanism, `fillfactor`, `pg_stat_all_tables` counter | [postgresql.org · Heap-Only Tuples (PG 18)](https://www.postgresql.org/docs/current/storage-hot.html) | PG 18 | 2026-09-24 | high |
| [4] | TOAST out-of-line values; 1-byte `varlena` header under 127 bytes; pointer datum keeps tuple size independent | [postgresql.org · TOAST (PG 18)](https://www.postgresql.org/docs/current/storage-toast.html) | PG 18 | 2026-09-24 | high |
| [5] | `AesGcm` is whole-buffer only — no incremental/field-level update; separate tag; per-message nonce; `…26425.128` | [learn.microsoft.com · AesGcm](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.aesgcm) | rc.1.26425.128 | 2026-09-24 | high |
| [6] | Runtime async 0.21× mean / 0.07× alloc at depth 30; 1.30× **loss** on a single-`Yield` path | [devblogs.microsoft.com · Performance Improvements in .NET 11](https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-11/) | 2026-09-15 | 2026-09-24 | medium (single source, vendor engineer) |
| [7] | SQLite's tuning surface: `synchronous`, `journal_mode`, `cache_size`, `mmap_size`, `page_size`, `locking_mode=EXCLUSIVE`, `temp_store`, `wal_autocheckpoint` | [sqlite.org · PRAGMA Statements](https://www.sqlite.org/pragma.html) | n.d. | 2026-09-24 | high |
| [8] | WAL faster / fewer fsyncs but "best with smaller transactions"; >100 MB prefers rollback, >1 GB may fail; WAL-reset bug 3.7.0→3.51.2 | [sqlite.org · Write-Ahead Logging](https://www.sqlite.org/wal.html) | 2026-08-25 | 2026-09-24 | high |
| [9] | net11 RC / GA Nov 2026; JIT bounds-check elimination & devirtualisation; AVX-VNNI-512; SVE2; AOT shared dispatch; Zstd; C# 15; raised min hardware | [learn.microsoft.com · What's new in .NET 11](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-11/overview) | 2026-09-09 | 2026-09-24 | high |
| [10] | Page/size limits (281 TB, 512 B min); `rowid` vs `WITHOUT ROWID`; index entry = indexed cols + row key | [sqlite.org · Database File Format](https://www.sqlite.org/fileformat2.html) | n.d. | 2026-09-24 | high |
| [11] | Vendor FAQ names "INSERT is really slow"; "deleted data but file did not shrink" | [sqlite.org · FAQ](https://www.sqlite.org/faq.html) | 2024-11-26 | 2026-09-24 | high |
| [12] | `nosync` 1.4–1.7× on write tests; `-DNDEBUG` ~2×; "group operations into a single transaction" — **page marked obsolete by the vendor** | [sqlite.org · Database Speed Comparison](https://www.sqlite.org/speed.html) | obsolete | 2026-09-24 | low (publisher-retracted) |
| [13] | LSM: update/delete need `Merge`, `Single Delete`, `DeleteRange`, `Compaction Filter`; `Write Stalls` | [github.com/facebook/rocksdb · Basic Operations](https://github.com/facebook/rocksdb/wiki/Basic-Operations) | 2023-08-21 | 2026-09-24 | high |
| [14] | Bounds-check elimination via switch-target assertions: 103 → 70 bytes, six `RNGCHKFAIL` jumps removed | [devblogs.microsoft.com · Performance Improvements in .NET 10](https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-10/) | 2025-09-10 | 2026-09-24 | high |
| [15] | InnoDB `DYNAMIC` default; off-page long columns; 767 vs 3072-byte index prefix; format change rebuilds the table | [dev.mysql.com · InnoDB Row Formats](https://dev.mysql.com/doc/refman/8.4/en/innodb-row-format.html) | MySQL 8.4 | 2026-09-24 | high |
| [16] | .NET 10: JIT inlining / devirtualisation / stack allocation, AVX10.2, NativeAOT enhancements | [learn.microsoft.com · What's new in .NET 10](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-10/overview) | 2025-11-07 | 2026-09-24 | high |
| [17] | `RandomAccess` takes a **list** of buffers (`IReadOnlyList<Memory<byte>>` / `IReadOnlyList<ReadOnlyMemory<byte>>`) in `Read`/`Write` and their async forms; "thread-safe", offset-based; regular disk files only, no pipes; **no syscall count documented** | [learn.microsoft.com · System.RandomAccess](https://learn.microsoft.com/en-us/dotnet/api/system.randomaccess) | net-11.0 | 2026-10-09 | high (signatures/remarks) · syscall behaviour **unverified** |
| [18] | `ZstandardStream`: nine constructors incl. options / encoder / decoder / dictionary forms; seekable (`CanSeek`, `Seek`, `SetLength`, `SetSourceLength`) | [learn.microsoft.com · ZstandardStream](https://learn.microsoft.com/en-us/dotnet/api/system.io.compression.zstandardstream) | rc.1 | 2026-10-09 | high |
| [19] | `ZstandardCompressionOptions`: sealed, 12 properties incl. `Quality`, `AppendChecksum`, `TargetBlockSize`, `Dictionary`, `EnableLongDistanceMatching`; **no Remarks section**, so the frame-header/encoder split is undocumented | [learn.microsoft.com · ZstandardCompressionOptions](https://learn.microsoft.com/en-us/dotnet/api/system.io.compression.zstandardcompressionoptions) | rc.1 | 2026-10-09 | high (property list) · header split **unverified** |
| [20] | `ZstandardEncoder`: sealed `IDisposable`; span-in/span-out `Compress`/`TryCompress`, `Flush`, `GetMaxCompressedLength`, **`Reset`**, `SetPrefix`, `SetSourceLength`; no remarks on reuse semantics | [learn.microsoft.com · ZstandardEncoder](https://learn.microsoft.com/en-us/dotnet/api/system.io.compression.zstandardencoder) | rc.1 | 2026-10-09 | high (surface) · reset semantics **unverified** |
| [21] | `ZstandardDictionary`: sealed `IDisposable`; `Data`; in-box `Create(…)` ×2 and `Train(…)` — no external zstd tool needed; browser/WASI unsupported | [learn.microsoft.com · ZstandardDictionary](https://learn.microsoft.com/en-us/dotnet/api/system.io.compression.zstandarddictionary) | rc.1 | 2026-10-09 | high |
| [22] | `ZstandardDecompressionOptions`: sealed, one parameterless constructor, exactly **two** properties — `Dictionary` and `MaxWindowLog2`; **no Remarks section**, so the decompression side documents no failure mode. The dictionary is state the *caller supplies*, never read out of the frame | [learn.microsoft.com · ZstandardDecompressionOptions](https://learn.microsoft.com/en-us/dotnet/api/system.io.compression.zstandarddecompressionoptions) | rc.1 | 2026-10-09 | high (surface) · mismatch failure mode **unverified** |
| [23] | The zstd **frame header carries a `Dictionary_ID`** whose width is the low two bits of `Frame_Header_Descriptor` (`fhd & 3`: 1 byte, 2 bytes, 4 bytes when the value is 3, absent when 0); bit 5 is `Single_Segment_flag`, bit 3 is reserved, and a set reserved bit is an error | [github.com · klauspost/compress `zstd/framedec.go`](https://github.com/klauspost/compress/blob/master/zstd/framedec.go) | master (2026-10) | 2026-10-09 | high (independent implementation; the file deep-links the specification's `#dictionary_id` and `#window_descriptor` anchors). **Not** the normative table — RFC 8878's own pages middle-truncate in this run's fetch tool |

---

## Staleness map

Freshness bars are the technical pack's (versions ≤ 1 month, ecosystem ≤ 6, landscape ≤ 12, patterns
≤ 2 years), applied per claim class. Earliest re-check is the top of the queue.

| Claim class | Claims | Window | Earliest re-check |
|---|---|---|---|
| version/compatibility | `[9]` net11 GA status & min hardware, `[5]` Bcl.Cryptography build, `[17]`–`[22]` net11 API surfaces (each with the prerelease banner) | 1 month | **2026-10-24** |
| runtime feature/perf | `[6]` runtime async numbers, `[14]` / `[16]` JIT claims | 3 months | 2026-12-24 |
| layout/mechanism | `[1]`–`[4]`, `[10]`, `[13]`, `[15]`, `[23]` | 24 months | 2028-09-24 |
| comparison control | `[7]` pragma surface, `[12]` obsolete benchmark | 6 months | 2027-03-24 |
| reliability | `[8]` WAL-reset bug range | 12 months | 2027-09-24 |
| repo state (round 3) | the code facts in §3.4 — `Services/BlockCompressor.cs:58`, `Services/Storage.Append.cs:459-471`, `Services/AesGcmEncryption.cs` (~214) | on change | re-check on any edit to those files, not on a calendar |

**What ages first:** the .NET 11 claims. `.NET 11` was in RC at access time and GA is expected in
November 2026 — `[9]` says it "was last updated for release candidate 1", and `[6]`'s numbers are from
that RC. Both must be re-read after GA before any net11-based claim is published. `[5]`'s build string
(`rc.1.26425.128`) should also be re-checked against the shipped `Microsoft.Bcl.Cryptography` version.

**Round 3 adds six API pages and one implementation source to the same queue.** `[17]`–`[22]` each carry the
prerelease banner ("may be substantially modified before it's released"), so a post-GA re-read is required
*before* recommendations 7–9 are implemented — those items are written against these signatures. That
re-read mattered for two of them and no longer for the rest: **7 and 8 were implemented against the
measured behaviour rather than the pages** (2026-10-09, session 77 — `[17]`'s vectored-write behaviour
counted on this box and read in the runtime source, `[20]`'s `Reset()` semantics measured on a two-frame
reuse), while `[22]`'s third one still stands for item 9 (the failure
a decoder raises on a mismatched dictionary). `[23]` carries no banner (an independent implementation of a format frozen since zstd
1.0), yet it is re-read on the same date anyway: the fact it is cited for is *this* runtime's decoder
behaviour, not the format text.

**Second:** the obsolete `[12]`. It should be replaced by a measured pragma sweep on our own hardware
rather than re-read.

