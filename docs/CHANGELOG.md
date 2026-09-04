# Changelog

All notable changes to SharpCoreDB will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Hardening

- **Upgrade/downgrade policy documented + format-compat regression tests** - new
  `docs/manual/upgrade-and-downgrade.md` records the compatibility matrix: reading legacy
  (variable-length, pre-marker) databases with the current version is supported; opening a database
  that already contains commit-time tombstone markers with a version that predates them is **not**
  supported (negative length-prefix markers), and the recommended read-only-first upgrade order.
  `FormatCompatPolicyTests` locks in the two forward-compatibility guarantees: legacy files read
  back and accept marker writes across reopens, and commit-time tombstone markers stay stable
  across reopen cycles while rows appended afterwards coexist.

- **Auto engine selection no longer lands on PageBased (production hardening)** - with default
  configuration (`StorageEngineType.Auto` + `WorkloadHint.General`) `GetOptimalStorageEngine`
  returned PageBased, which is not yet OLTP-ready (measured UPDATE ~26K ops/s vs ~245K ops/s on the
  fixed-width Columnar path). Auto selection now routes General / WriteHeavy / unknown hints to
  AppendOnly/Columnar; PageBased remains reachable only through an explicit
  `StorageEngineType.PageBased` until its UPDATE/DELETE fast paths reach parity. Regression tests
  assert the mapping AND that a default database creates Columnar fixed-width PK tables that engage
  the single-pass contiguous DELETE path (no `.pages` artifacts). Full suite 1768 tests, 0 failed.

### Performance

- **Fixed-width record layout is now the default for new columnar PK tables (B7)** ÔÇö
  `DatabaseConfig.AutoFixedWidthRecords` (default `true`) creates new directory-mode Columnar tables
  with an explicitly declared PRIMARY KEY in the fixed-width layout (constant record size,
  out-of-line overflow arena, in-place UPDATE/DELETE). Tables without a declared PK, PageBased and
  single-file (.scdb) layouts are unchanged; existing tables are never rewritten (the persisted
  per-table flag stays authoritative). Fair-PK harness (`--pk`, AppendOnly): INSERT ~128K,
  UPDATE ~51K, DELETE ~75K vs 87K/41K/65K on the legacy layout.
- **Single-pass contiguous UPDATE / DELETE (B8/B9)** ÔÇö batch `UPDATE/DELETE ... WHERE pk = literal`
  statements with strictly ascending keys on a **plaintext** fixed-width columnar table now read the
  target records as **one contiguous byte range** (storage cached handle) and patch/remove them in
  memory: no per-row pread. UPDATE ~45K ÔåÆ **~84K ops/s** (gap vs SQLite 5.1├ù ÔåÆ **3.2├ù**); DELETE
  ~55-77K ÔåÆ **~135K ops/s** (gap ~4-6├ù ÔåÆ **2.1├ù**). Any shape deviation (gaps, descending keys,
  PK writes, CHECK constraints, variable-length indexed columns, per-record encryption) falls back
  to the generic per-row loop before any row is touched. New primitives:
  `IStorage.HasBufferedOverwrite`, `IStorage.ReadBytesRange` (shared-handle range read) and the
  runtime `AreRecordsEncrypted` gate (so default-config plaintext databases benefit without
  `NoEncryptMode`). Fixed-size hash-indexed SET columns are re-pointed with one lock per index
  (`HashIndex.RemoveBatchKeys`/`AddBatchKeys`).
- **FW contiguous path: per-key B-tree probe replaced by decode verification + `DeleteBulk` (M3)** -
  the shared B8/B9 probe resolved every key with one `Index.Search` (~10K per batch) and the FW
  DELETE removed the PK entries one `Delete` at a time. The probe now locates only the FIRST key
  through the tree, computes the remaining positions by the fixed-width stride, reads the
  contiguous span once and verifies every record by its length prefix AND its decoded fixed-width
  PK slot (equal to the batch key); the DELETE removes the PK entries with one `DeleteBulk` pass.
  The same batches are rejected as before (gaps/tombstones surface as prefix mismatches; a
  differing PK falls back like a tree miss). Fair-PK median-of-3 (sequential, uncontended):
  fixed-width UPDATE ~245K and DELETE ~172K ops/s on this branch (master baseline reported in the PR).
- **Sequential ascending-PK batch resolution for legacy DELETE (Fase B: legacy fast paths)** -
  `DeleteMultipleKeys` on a legacy (variable-length, plaintext, non-fixed-width) Columnar table now
  resolves a strictly-ascending INTEGER-PK literal batch with a single sequential decode pass that
  starts at the first target's position and early-exits once every target matched — instead of one
  B-tree search + row decode per target. Strictly gated: page-based/fixed-width/encrypted layouts,
  non-PK or non-ascending keys, sparse batches spanning more than 2 MB and physically unordered
  files (detected by a monotonicity pre-pass) all fall back to the existing per-row path, so the
  result is identical. Two regression tests cover an ascending batch over an **unordered** physical
  layout (must delete exactly the requested keys across reopen) and re-validate the existing
  legacy prefix delete; full suite 1766 tests, 0 failed. Legacy `--pk` DELETE stays within noise on
  this harness; the gate is groundwork for the wide-row legacy arms.
- **Buffered in-place UPDATE overwrites are now flushed per storage page (C6, Fase B)** - the
  UPDATE commit path buffered one record per row (B7) and flushed each with two pwrites
  (length prefix + payload), so ~10K-row UPDATEs were dominated by per-row write syscalls
  (~88K ops/s on the fair-PK fixed-width table). `FlushBufferedOverwrites` now batches: the
  current on-disk page content is read once, the row payloads are patched into the copy, and each
  touched page is written once (payloads crossing a page boundary take the direct path first;
  length prefixes are unchanged because same-length overwrites are the only in-place case).
  Fair-PK harness (`--pk`, same machine as master): fixed-width UPDATE ~88K -> **~153-164K ops/s**
  (+75-85%, gap vs SQLite ~2,75x -> ~1,7x); legacy UPDATE ~53-56K -> ~64-70K (+20-25%); DELETE
  unchanged within noise. Runs under the same commit lock; rollback semantics unchanged.
- **Duplicate-key hash-index removal is no longer quadratic (P5)** - `HashIndex.RemoveBatchKeys`/
  `RemoveBatch` previously removed every position from a key's list with one O(list) shift per
  duplicate, i.e. O(m·n) for a key holding n rows with m duplicate-key deletions in one batch.
  Batch removal now keeps the direct allocation-free path for single-row keys and defers
  duplicate-key positions into a per-key set that is applied in one O(list) compaction. New
  regression tests cover full and partial duplicate-group deletes on both index backends
  (managed `List` + unsafe native backend) including a reopen; full suite 1764 tests, 0 failed.
- **Commit-time tombstones now batch the marker writes (C5)** - the DELETE commit phase read the
  whole file once (#373) but still applied one 4-byte negative-prefix marker per row
  (one pwrite each). `TombstoneRecords` now patches every marker into the in-memory snapshot first
  (markers may straddle page boundaries, so patching happens on the contiguous buffer) and flushes
  each touched storage page once, byte-for-byte equivalent. Fair-PK harness (`--pk`, median of runs,
  same machine as master): legacy DELETE ~70K -> **~81K ops/s** (+16%); fixed-width DELETE
  ~97K -> **~141K ops/s** (+45%) - the DELETE gap vs SQLite on fixed-width drops to ~2.4x.
- **Bulk descending PK-delete on the generic DELETE path** - `IIndex` now offers `DeleteBulk`;
  `BTree.DeleteBulk` sorts each batch in **descending key order** so consecutive removals run along
  the rightmost leaf path (dramatically fewer internal-separator promotions than deleting in
  arbitrary per-row resolution order). `DeleteRecordsCore` collects the batch's PK keys once and
  removes them through `DeleteBulk` instead of per-key `Delete`. Correctness is unchanged (identical
  key set, one visit per key). Fair-PK harness (`--pk`, AppendOnly legacy): DELETE stays ~69-72K
  ops/s on strictly ascending batches (within noise), with the win concentrating on unordered key
  sets (hash-filtered subselects, reverse/random batches) that previously paid a separator
  promotion per jump. Regression test drives the generic bulk path on a legacy-layout table and
  reopens to prove tombstones + PK-index rebuild do not resurrect rows.
- **BTree separator-delete corruption fixed (correctness)** - `BTree.Delete` removed separator keys
  from internal nodes without repairing the child-pointer mapping, so sizable delete batches could
  leave whole key ranges unreachable (and scans/COUNT(*) under-counted). Internal separators are
  now replaced by their in-order successor from the right subtree's leftmost leaf (leaf underflow is
  harmless), with empty-neighbour fallbacks keeping child counts consistent. Full suite
  **1655/1655**.
- **DELETE now survives a reopen (durability)** ÔÇö Columnar deletes were logical only (index removal),
  so the on-load PK-index rebuild resurrected deleted rows from the untouched `.dat`. Logically
  deleted rows are now counted (`_pendingLogicalDeletes`) and physically compacted at flush/dispose
  (`Table.CompactPendingDeletes`, outside a transaction, Columnar tables with a PK). Flush
  compaction rewrites only the data file (live PK positions via B-tree traversal, single-pass index
  rebuild) so the cost is proportional to the remaining rows (~0.4s for a 90K-live table); the
  overflow arena is reclaimed on the next explicit VACUUM/compaction. Regression: delete half the
  rows, `Flush`, reopen ÔÇö exactly the remaining rows come back. Measured DELETE in the `--pk`
  harness now includes this durability rewrite (~18.6K ops/s when deleting 10K of 100K rows).
- **Durable DELETE is now in-place via tombstones (no rewrite)** ÔÇö Columnar deletes write a
  tombstone marker (the record's 4-byte length prefix is replaced by the NEGATIVE slot size)
  instead of queueing a flush-time rewrite, and every raw record enumerator/compactor skips the
  slot, so DELETE survives a reopen in O(delete). Non-transactional deletes write the marker at
  delete time; transactional deletes (e.g. any `ExecuteBatchSQL` batch, which runs inside a storage
  transaction) buffer the offsets and apply the markers at COMMIT ÔÇö rollback discards the buffer,
  so a rolled-back delete keeps its row, and the flush-time full-file rewrite (`CompactPendingDeletes`)
  is no longer on the batch-DELETE path. Tombstoned space is reclaimed by the tombstone-aware
  `CompactTable`, including the ULID-migration compaction (which previously produced an empty file
  when tombstones were present because `CompactTable` broke on a negative prefix). Measured `--pk`
  DELETE (10K of 100K rows): ~0.54s/18.6K ops/s (flush rewrite) ÔåÆ **~0.16s/~64K ops/s (legacy)** and
  **~0.13s/~78K ops/s (fixed-width)**; comparative-harness DELETE (docs table): SQL ~0.82s/~12K ÔåÆ
  **~0.24s/~41K ops/s**, Direct ~0.63s/~16K ÔåÆ **~0.17s/~58K ops/s** ÔÇö DELETE is back on par with UPDATE.
- **Whole-file DML resolution + marker range-read (2026-09-03)** ÔÇö batch DELETE/UPDATE on the
  comparative docs table no longer pays one pread pair per touched row: the small (Ôëñ32 MB,
  plaintext, legacy variable-length) `.dat` is read once and every target record is resolved from
  that snapshot (B1 key-only decode for DELETE, raw slice for the UPDATE fastPatch). Positions with
  an in-batch buffered overwrite are detected via `IStorage.HasBufferedOverwriteAt` and always fall
  back to the per-record read, so transaction write-behind semantics stay intact. DELETE tombstone
  markers resolve their record lengths from a single whole-file read instead of one pread per marker.
  The canonical-DELETE scanner (which never consumed `WHERE`, so the structured batch path was dead
  code in the harness) is fixed and covered by `CanonicalBatchDelete_EngagesStructuredPath`.
  Measured (same machine, Release, median of 3): comparative DELETE SQL ~12K ÔåÆ **~59K ops/s** and
  Direct ~16K ÔåÆ **~86K ops/s**; UPDATE SQL ~35K ÔåÆ **~44K ops/s**, Direct ~49K ÔåÆ **~63K ops/s**;
  `--pk` DELETE legacy ~64K ÔåÆ **~68K ops/s**, fixed-width ~78K ÔåÆ **~93K ops/s**. Session plan +
  attribution in `docs/performance/EXECUTION_PLAN_UPDATE_DELETE.md`.
- **Dedicated SQL batch-INSERT fast path (WP14)** ÔÇö `ExecuteBatchSQL` INSERTs no longer build a
  per-row `Dictionary<string, object>`; VALUES clauses are parsed directly into column-ordered
  `object[]` rows (`PreparedInsertStatement.ParseValuesToArray`) and inserted via the new
  `Table.InsertBatch(object[][], columnOrder)` path with full dict-path parity (defaults, AUTO,
  explicit NULL, NOT NULL, PK, hash/B-tree indexes). SQL INSERT throughput measured **+80%**
  (54.5K/s ÔåÆ 98.2K/s in the comparative benchmark), closing the INSERT gap vs SQLite from ~1.9├ù to
  ~1.5├ù. Batch UPDATE also reuses the WP11 in-place field-overwrite fast path (runtime offsets now
  resolve fixed-size fields after variable-length columns; monitored via `Table.TotalInPlacePatches`).
- **AVX-512 validation on real hardware (2026-09-01)** ÔÇö 6-run benchmark on an AVX-512 machine
  confirmed the adaptive SIMD tier (AVX-512 **2ÔÇô26├ù over scalar**, up to **2.7├ù over AVX2** for
  `EuclidSq`/`Normalize`, dims 64ÔÇô1024) and the CRUD profile (beats LiteDB on every operation; INSERT
  at 0.69ÔÇô0.85├ù of SQLite). Full report:
  `docs/benchmarks/AVX512_2026-09-01.md` (+ raw per-run `.md`/`.json` in `docs/benchmarks/avx512-2026-09-01/`).

## [2.0.0.1] - 2026-09-01

### Fixed

- **Single-file data corruption under concurrent writes (critical)** ÔÇö the WAL manager wrote to
  the shared file stream with a bare `Position` + `WriteAsync`, while the background write-behind
  worker wrote data pages under a lock. A concurrent `Position` mutation could land WAL bytes on a
  data page, so a table's data block could read back as WAL/registry bytes instead of JSON after a
  reopen (sporadic `JsonException '0x02'` / "Expected 100 rows, got 0"). All `FileStream.Position`
  use is now serialized through `SingleFileStorageProvider.WriteAt` (header, WAL, delta writes,
  reads, defrag). Regression covered by `VacuumStressTests` (failed ~50% before, stable after).
- **4-part patch versioning** ÔÇö this patch ships as `2.0.0.1` (NuGet shows `2.0.0.1`).

## [2.0.0.0] - 2026-09-01

### Release highlights

- **Performance-first engine** ÔÇö point reads **beat SQLite** on the default engine, batch INSERTs
  beat SQLite on PageBased (**194ÔÇô206K vs 109K ops/s**), and the UPDATE/DELETE gap vs SQLite narrowed
  from ~5ÔÇô7├ù to ~1ÔÇô4├ù (in-place field patches + unified delete core).
- **Single-file storage format v2** ÔÇö dynamic/growable metadata layout (Block Registry, FSM, Table
  Directory) with **automatic crash-safe v1 ÔåÆ v2 migration on open** (original preserved as
  `<file>.backup`).
- **Block-level compression** ÔÇö Brotli/GZip/Zstd with configurable presets
  (`BlockCompressionLevel`, `MetadataCompressionLevel`, `CompressionThreshold`).
- **Envelope encryption + full at-rest metadata encryption** (`EncryptionPassword`, per-file DEK,
  key/password rotation) and **configurable metadata sizing** (`FsmSizePages`,
  `BlockRegistrySizePages`, `TableDirectorySizePages`).
- **4-part versioning** ÔÇö all packages now use `n.n.n.n` (this release: `2.0.0.0`).
- **Full change/benchmark report** ÔÇö see
  [`docs/2.0.0.0_WHAT_CHANGED.md`](2.0.0.0_WHAT_CHANGED.md): everything that changed
  vs the 1.9 line, plus the SharpCoreDB vs SQLite vs LiteDB benchmark tables and graphs.

### SingleFile storage ÔÇö critical compression read-path fixes + configurable presets (PR #352)

- **Fix: zero-copy read paths returning compressed bytes** ÔÇö `GetReadStream()` and `GetReadSpan()`
  served the raw Brotli/GZip bytes when encryption was disabled, causing `JsonException` on
  database reopen. Both methods now check the block's `BlockFlags.Compressed` bit and fall back
  to `ReadBlockAsync` so compressed blocks are always decompressed. Affected databases created
  with `BlockCompression != None` and `EnableEncryption = false` in v1.9.8.
- **Fix: stale `Compressed` flag on block overwrite** ÔÇö `WriteBlockAsync` preserved old flags and
  never updated the `Compressed` bit based on the current write, so a block that grew past the
  compression threshold (256 B default) could be stored compressed but marked uncompressed.
  The flag is now cleared and re-set on every write while preserving all other flags.
- **Configurable compression presets** ÔÇö new `DatabaseOptions.MetadataCompressionLevel`
  (default `Fastest`) and `BlockCompressionLevel` (default `Optimal`) map to the BCL
  `CompressionLevel` via the new `SharpCoreDB.Compression.OptionalCompressionLevel` enum;
  `BlockBrotliCompressionLevel` remains as an obsolete alias. `VacuumMode.Full` preserves the
  block compression level when it creates the temporary file.
- **Zstd support** ÔÇö `BlockCompressionMode.Zstd` (`.NET 11+`, `ZstandardStream`) with a
  `PlatformNotSupportedException` fallback on older runtimes.
- **Regression tests:** `CompressionLevelTests` (31 tests) cover preset defaults, roundtrips,
  size ordering across levels, metadata roundtrips, `GetReadStream`/`GetReadSpan` decompression
  without encryption, and the multi-write stale-flag scenario.


## [2.1.0-preview] - 2026-08-31

### Performance
- **Single-pass SQL DELETE/UPDATE (Issue #7/#8)** ÔÇö the SQL paths no longer materialize matching
  rows twice:
  - `ITable.DeleteAffectedRows(where)` deletes AND returns the affected rows; `ExecuteDelete` uses
    it for RETURNING + `CHANGES()` from a single pass (`Table`/`SingleFileTable` override the
    default; third-party `ITable` implementers keep the two-pass fallback).
  - `ITable.UpdateAffectedCount(where, updates)` applies the update and returns the affected count;
    `ExecuteUpdate` no longer runs a full `Select().Count` for change-tracking.
- **PK fast path extended to batch DML** ÔÇö simple `pk = value` WHERE clauses resolve via the
  primary-key B-tree directly (single search + one read) in `Delete`/`DeleteMultiple`/
  `UpdateMultiple` instead of full-row materialization + per-row PK re-search.
- **Field-level in-place patch on the columnar UPDATE path (fixed-width layout step)** ÔÇö when the
  row's storage position is known (PK B-tree / hash index), only the updated fields are patched at
  their **actual** record offsets (`ComputeActualColumnOffsets` + `TryOverwriteFieldsInPlaceActual`)
  instead of deserialize ÔåÆ mutate ÔåÆ re-serialize of the whole row. A fixed-size field keeps the
  record length unchanged ÔåÆ the write is an in-place overwrite (no file growth), even for columns
  that sit after variable-length TEXT columns. `UpdateAffectedCount`/`UpdateMultiple` now resolve
  rows as (position, row) pairs; variable-width fields that change size still fall back to append.
- **Stale-index regression fix** ÔÇö WHERE-based UPDATE/DELETE entry points load all registered hash
  indexes up front (`EnsureAllRegisteredIndexesLoaded`), so append updates / logical deletes remove
  the stale record from every index (an unloaded index would otherwise be rebuilt from the data
  file including the stale record, resurrecting the pre-update row).
- **Regression tests:** `DmlSinglePassTests` (9 cases) + `FixedWidthPatchTests` (5 cases) ÔÇö
  affected counts, RETURNING pre-delete rows, range/non-indexed WHERE fallbacks, batch PK
  deletes/updates, in-place patch no-growth (after variable columns / by PK), variable-growth
  append fallback, compound WHERE. Full suite green: **1,649 tests, 0 failures**.
- **Single-file `.scdb` (A-track):**
  - **PK hash index (A1)** ÔÇö `FindByPrimaryKey` / `UpdateByPrimaryKey` / `DeleteByPrimaryKey` and
    `SELECT ÔÇª WHERE pk = value` resolve in O(1) instead of an O(N) cache scan (index maintained on
    all mutations, rebuilt on reopen/rollback; numeric literals normalized).
  - **In-place block overwrite (A2)** ÔÇö pinned: a same-length update does not grow the `.scdb`
    (`WriteBlockAsync` reuses the table block offset when the JSON fits).
- **Out-of-line overflow (B1, opt-in):** `DatabaseConfig.FixedWidthRecordLayout` ÔÇö fixed-width
  records with constant size per schema; TEXT/BLOB values in a per-table overflow arena (`.ovf`),
  referenced by a 4-byte offset in the record. Every UPDATE (fixed **or** variable column) is an
  in-place overwrite (`.dat` does not grow). Includes `OverflowArena` (append + cache +
  copy-on-compact), `FixedWidthRecordLayout`, and fixed-width serialize/deserialize/in-place-patch
  wired into the Table dispatcher, PK index rebuild, full-scan guards and StructRow fallback.
  Flag persisted in table metadata, restored from config on reopen.
- **Overflow arena GC (B3)** ÔÇö `CompactStorage` now compacts the overflow arena together with the
  data file: live arena offsets are collected from the current records, the `.ovf` is rewritten
  (copy-on-compact), and the active records' variable slots are re-pointed in place. Dead arena
  blocks from variable updates / deletes are reclaimed.
- **Constant-offset read-path wins (B4)** ÔÇö early-WHERE re-enabled for fixed-width tables using the
  constant slot offsets of `FixedWidthRecordLayout`: numeric predicates read the column directly at
  its slot offset (also when a variable-length column precedes it), string predicates compare the
  arena payload byte-wise against the pre-encoded expected UTF-8, and the StructRow numeric-SIMD
  batch filter (`Vector<T>`) now serves fixed-width tables. Also fixed a latent bug where arena
  block offset 0 (the first block) was treated as "no block" and dropped by compaction / early-WHERE.
- **1.x ÔåÆ 2.0 record-format migration path (B5)** ÔÇö the fixed-width flag is now persisted per table
  in metadata (authoritative on reopen; config no longer overrides the on-disk format). A legacy
  (variable-length) database opened with `DatabaseConfig.FixedWidthRecordLayout = true` auto-migrates
  its columnar tables, and `IDatabase.MigrateTableToFixedWidth(tableName)` provides on-demand
  conversion. A format probe adopts already-fixed-width tables that predate flag persistence and
  skips byte-identical fixed-size-only legacy tables.
- **Arena free-list (B6)** ÔÇö freed overflow blocks are tracked per payload length and reused in
  place (`OverwriteRecordAt`) when a new value has the exact same length, so same-length
  variable-column updates no longer grow the `.ovf` within a session (copy-on-compact still
  reclaims the rest). Also fixed a latent B1 leak where the first arena block (offset 0) was never
  freed on update.
- **Single-file (.scdb) fixed-width (B6)** ÔÇö the fixed-width out-of-line-overflow model now also
  serves single-file tables: with `DatabaseConfig.FixedWidthRecordLayout` the table stores binary
  fixed-width records (variable values in a dedicated overflow block) instead of the legacy JSON row
  array, so value-only updates keep the data block constant-size. The on-disk format is detected on
  reopen (binary blocks are parsed untrimmed), legacy JSON tables migrate via
  `MigrateTableToFixedWidth` (or automatically when the config opts in), and the shared
  `FixedWidthCodec` keeps directory-mode and single-file record formats in sync.
- **Automatic PageBased ÔåÆ Columnar + fixed-width conversion (B6)** ÔÇö `MigrateToFixedWidth` now
  converts page-based tables to Columnar storage in-process (rows re-read via the page engine,
  `.pages` files removed, `DataFile`/`StorageMode`/metadata updated) before rewriting the records
  as fixed-width, and the database-load auto-migration covers PageBased tables as well. Also fixed
  a pre-existing PageBased data-loss bug: single INSERT/UPDATE never flushed the page cache (only
  `CommitAsync`/`Flush` did), so reopened tables returned zero rows ÔÇö dirty pages are now flushed
  when the table/storage engine is disposed.
- **Cross-session arena free-list (B6)** ÔÇö the directory-mode `OverflowArena` derives its free-list
  on load: the fixed-width records in the data file are scanned and every arena block no record
  references is freed, so same-length value updates reuse dead blocks across sessions without
  persisting the free-list itself (single-file tables already restore it per flush via the
  unreferenced-sweep). This closes the last open storage-performance follow-up.
- **Regression tests:** `SingleFilePkIndexTests` (7), `SingleFileWriteTests` (2),
  `FixedWidthRecordLayoutTests` (14, incl. cross-session free-list), `FixedWidthMigrationTests` (8),
  `SingleFileFixedWidthTests` (5). Full suite green: **1,686 tests, 0 failures**.


## [2.0.0-preview.3] - 2026-08-30

### Added
- **Dynamic metadata layout (format v2, #345 Phase 2)** ÔÇö the Free Space Map and Block Registry
  are no longer fixed header regions but **growable named blocks**:
  - the Block Registry is a single growable block rooted at `header.RegistryRootOffset`
    (`[RegistryChunkHeader][BlockEntry...]`), which relocates (grows) automatically when it
    outgrows its current block;
  - the FSM is a named block (`sys:fsm`) tracked in the registry; its serialized bitmap relocates
    (grows) automatically when the database outgrows the initial `FsmSizePages` capacity;
  - `FormatVersion` is bumped to **2** (`FEATURE_DYNAMIC_METADATA`); `BlockRegistrySizePages`
    sizes the **initial** registry block (default 4 pages Ôëê 170 entries; the registry still
    grows on demand beyond that);
  - **automatic v1 ÔåÆ v2 migration on open**: legacy files with fixed-offset metadata are rebuilt
    via a crash-safe temp-file swap (data blocks are never moved ÔÇö checksums/ciphertexts stay
    valid) and the original is preserved as `<file>.backup`;
  - system metadata blocks (`sys:fsm`) are hidden from `EnumerateBlocks()`.
- **Regression tests** ÔÇö format-v1 ÔåÆ v2 migration round-trip (`LegacyMigrationTests`),
  FSM-block growth + data round-trip, dynamic registry growth (300+ blocks), tampered registry
  detection at the new dynamic location.


### Added
- **Block-level Brotli/GZip compression for single-file (`.scdb`) storage** (#344) ÔÇö transparent
  per-block compression applied before encryption on write and removed after decryption on read.
  A per-block `Compressed` flag tracks state, so compressed and uncompressed blocks can coexist in
  one file; defaults to `None` (fully backward compatible). New `DatabaseOptions.BlockCompression`
  and `CompressionThreshold` options.
- **Configurable SingleFile metadata region sizes** (#345) ÔÇö the FSM, Block Registry and Table
  Directory are no longer hard-coded to 4 pages: `DatabaseOptions.FsmSizePages`,
  `BlockRegistrySizePages` and `TableDirectorySizePages` size the regions for large databases
  (>512 MB), and the minimum file extension is now byte-based (~10 MB regardless of `PageSize`).
- **Unicode & large-blob storage regression tests** (#346) ÔÇö CJK, emoji (incl. ZWJ sequences), RTL
  and combining-character roundtrips, plus 16 MB blob block-chaining coverage.
- **Full at-rest encryption for single-file (`.scdb`) databases** ÔÇö beyond block data (#341),
  the **block registry, free-space map and WAL are now encrypted too** (`EncryptionMode = 2`),
  closing the metadata-leakage gap: block/table names, offsets, lengths and allocation patterns
  are no longer visible in plaintext on disk (header + wrapped-key bundle remain the only
  plaintext bootstrap).
- **Envelope-encryption key model** ÔÇö `DatabaseOptions.EncryptionPassword` creates a random
  per-file data-encryption-key (DEK) wrapped by a PBKDF2-HMAC-SHA256-derived key
  (per-file salt, OWASP-2024 iteration default). Raw `EncryptionKey` mode remains supported.
- **Password & key rotation** ÔÇö
  - `IDatabase.ChangeEncryptionPasswordAsync(newPassword)` re-wraps the same DEK with the new
    password (O(1), no data rewrite; increments the header `EncryptionKeyId` rotation counter).
  - `IDatabase.RotateEncryptionKeyAsync(newKey|newPassword)` fully re-keys the database
    (re-encrypts every block + registry + FSM + WAL) via a crash-safe temp-file swap
    (same pattern as Issue #343).
  - Wrong key/password now fails loudly at open (GCM authentication failure) instead of
    silently returning an empty schema.

### Fixed
- **Issue #343 ÔÇö `VacuumAsync(VacuumMode.Full)` crashed with `ObjectDisposedException` under .NET 10
  trimming / Native AOT** (same fix set as the v1.9.8 line on `master`):
  - the full-vacuum stream swap uses direct field assignment (`SwapFileStream`) instead of reflection,
    and error paths read the file size safely (`GetFileSizeSafely`, `-1` fallback);
  - the temp-file extension mismatch that broke full vacuum (`<file>.vacuum.tmp.scdb` vs `<file>.vacuum.tmp`)
    is fixed;
  - single-file (`.scdb`) mode is now fully Native AOT-safe: the row-cache JSON serialization uses the
    source-generated `SingleFileTableJsonContext` + `PolymorphicObjectConverter` (byte-for-byte identical
    output, existing files stay readable), and the batch-flush reflection + `dynamic` calls were replaced
    with internal `TableDirectoryManager`/`BlockRegistry` accessors;
  - regression test `SingleFileDatabase_VacuumFull_Works_And_SurvivesReopen`; `SharpCoreDB.AotSmoke`
    publishes with `PublishAot=true` and runs exit 0 including the single-file full-vacuum path.
  - **Follow-up:** full VACUUM now reloads the block registry / FSM / WAL after the file swap so
    in-memory offsets match the compacted file (fixes stale-offset writes after vacuum).
- **Issue #344 ÔÇö compressed single-file (`.scdb`) databases crashed on reopen + SELECT with
  `JsonException: '0x0B' is an invalid start of a value`**:
  - `WriteBlockAsync` only stamped the per-block `Compressed` flag for brand-new blocks; a table
    row-cache block that was rewritten while it already existed (auto-flush as the JSON grows past
    the compression threshold, or a grow/realloc) was stored compressed **without** the flag, so on
    reopen the raw Brotli/GZip bytes were handed to the JSON parser;
  - `SingleFileTable.EnsureCacheLoaded` now reads the row-cache block through `ReadBlockAsync`
    (which decrypts and decompresses transparently) instead of `GetReadStream` (which returns raw
    on-disk bytes when encryption is off);
  - regression tests `DatabaseFactory_Compression_GrowingTable_ReopenSelectShouldSurvive` and
    `DatabaseFactory_CompressionPlusEncryption_GrowingTable_ReopenSelectShouldSurvive` cover the
    reopen + SELECT path for compression alone and compression + encryption.

## [2.0.0] - 2026-08-28

### Performance-first release ­ƒÜÇ

The v2.0 release closes the v1.x benchmark gap (was **16ÔÇô52x slower than SQLite** on point reads,
updates and deletes). Measured final two-run ranges vs SQLite/LiteDB:

| Operation | v2.0 | SQLite | LiteDB |
|-----------|-----:|-------:|-------:|
| READ ÔÇö Direct / StructRow | 70ÔÇô126K ops/s | 87ÔÇô97K | 14ÔÇô16K |
| READ ÔÇö SQL | 51ÔÇô59K ops/s | 87ÔÇô97K | 14ÔÇô16K |
| INSERT (batch) | 91ÔÇô133K ops/s | 145ÔÇô150K | 66ÔÇô77K |
| UPDATE (batch) | 41ÔÇô59K ops/s | 241ÔÇô296K | 10ÔÇô11K |
| DELETE (batch) | 30ÔÇô142K ops/s | 320ÔÇô367K | 13ÔÇô14K |

### Added
- **`ExecuteQueryStruct(sql, params)`** ÔÇö first-class zero-allocation struct-row SQL reads with a
  cached `VariableLengthSchema` (column layout parsed once, not per row).
- **`FindByPrimaryKey(table, key)` / `FindByIndex(table, col, value)` direct reads** ÔÇö no-SQL
  point lookups (Direct API tier, the benchmark's "Direct" path).
- **`SimpleSelectPlan` zero-reparse SELECT fast path** ÔÇö simple `SELECT ÔÇª WHERE key = @p` plans
  resolve from the query plan cache without re-lexing or re-parsing.
- **SIMD numeric WHERE batch filters** ÔÇö `Vector<T>` batch predicate evaluation for Integer/Long
  columns plus a fixed-offset numeric predicate fast path in columnar scans.
- **Native AOT readiness** ÔÇö AOT-safe `TypeConverter`, `Option<T>` reader,
  `[RequiresDynamicCode]` annotations, source-generated `TableMetadataDto` and
  `SharpCoreDBJsonContext`. `tools/SharpCoreDB.AotSmoke` publishes and runs successfully
  (CREATE/INSERT/query/StructRow/reopen, exit 0).
- **New benchmark APIs & tests** ÔÇö `ExecuteQueryStruct` benchmark path + 5 tests;
  regression test for positional `?` placeholders falling back to the legacy binder.

### Changed
- **Removed hot-path debug file I/O** ÔÇö unconditional `File.AppendAllText` writes to `D:\*.log`
  (per SELECT, per ExecuteSQL, per transaction, per INSERT) are gone. This single artifact was the
  dominant v1.x read bottleneck.
- **`NormalizeSql` is regex-free** with an allocation short-circuit for query-plan-cache keys.
- **All hot-path regexes are compiled** (batch UPDATE/DELETE parsing, provider detection,
  `ExecuteQueryFast`).
- **`HashIndex.Add/Remove` operate on the key only** ÔÇö no full row copies during index maintenance.
- **`UpdateMultiple` no longer copies rows** (`new Dictionary(row)` removed);
  `DeduplicateByPrimaryKey` early-exits on redundant keys.
- **`LookupPositionsUnsafe`** ÔÇö no-copy position lookup under an explicit write-lock contract.
- **DI cached** ÔÇö `IGraphRagProvider` is resolved once instead of per call in
  `GetSharedSqlParser`.
- **Provider fast paths** ÔÇö `OPTIONALLY` keyword check avoids a full parse per `ExecuteReader`;
  span-based single-file and `sqlite_master` detection.
- **Fixed regression** ÔÇö positional `?` placeholders now fall back to the legacy parameter binder
  (previously treated as SQL literals by the fast path).

### Compatibility
- **100% backward compatible** with v1.9.x ÔÇö no public API breaking changes; the fast-path APIs are additive.
- Toolchain locked to **.NET 10 / C# 14** for v2.0.x; .NET 11 / C# 15 planned for v2.1.

### Validation
- **2,412 tests / 0 failures** across all 15 test projects.
- Comparative benchmark recorded after every phase:
  `tests/benchmarks/SharpCoreDB.Benchmarks.Comparative/results/comparative_20260828_*.json`.
- Plan and results: `docs/performance/V2_PERFORMANCE_PLAN.md`.

## [1.9.6] - 2026-08-28

### Fixed
- **Issue 339 ÔÇö `WHERE col IN (...)` silently returned ALL rows (regression)**: every `IN` variant
  (literal lists, parameterized lists, single-value lists, `NOT IN`) was ignored by the predicate
  evaluators and fell through to an "accept all" path:
  - `SingleFileTable.EvaluateSingleCondition` did not recognize `IN`/`NOT IN` at all (single-file
    `.scdb` mode) and returned `true` for every row.
  - `Table.EvaluateWhere` (directory mode) split the value list on spaces ÔÇö `IN ('a', 'b')` lost
    everything after the first value ÔÇö and non-string columns fell into the switch's `default:
    return true`.
  - `SqlParser.EvaluateOperator` (enhanced/AST path) did not strip the surrounding parentheses from
    the value list.
  All three paths now evaluate `IN`/`NOT IN` from the full parenthesized list (quote-trimmed,
  comma-separated) for both string and non-string columns.
- **Single-file parameterized queries threw "Missing required parameter"**: `SingleFileDatabase.BindPreparedSql`
  bound parameters with a local implementation that did not normalize `@`-prefixed keys, so `IN (@p0, @p1)`
  failed against names extracted without the `@` prefix. It now delegates to `ParameterBinder.Bind`, the
  single source of truth for parameter binding.

### Added
- **Regression tests for issue 339**: `WhereInRegressionTests` (12 tests) assert `IN`/`NOT IN` row
  counts for literal and parameterized lists in both single-file and directory mode, and
  `WhereInRegressionEfCoreTests` (2 tests) reproduce the reporter's exact `SharpCoreDBConnection` +
  `.scdb` scenario end-to-end.

## [1.9.5] - 2026-08-27

### Added
- **Regression tests for parameter binding**: `ParametricInsertTests` (9 tests) round-trip
  parameterized INSERT/SELECT/UPDATE with 4ÔÇô11 named parameters and assert the values land in the
  columns the SQL specifies.
- **Regression tests for server parameter pass-through**: `ParameterRoundTripTests` (2 tests)
  validate parameterized INSERT + SELECT over gRPC.
- **ULID specification compatibility tests**: 6 new tests in `UlidTests` validate generation,
  parsing and timestamp extraction against the official ULID test vector
  (`0000XSNJG0MQJHBF4QX1EFD6Y3` / timestamp `1000000000` ms), the 128-bit range (`7ZZZÔÇªZ` accepted,
  `8ZZZÔÇªZ` rejected) and the 48-bit timestamp limit.

### Fixed
- **Issue 336 ÔÇö parameterized INSERT bound values to the wrong columns**: `SqlParser.BindParameters`
  used substring-based replacement, so a parameter name that is a prefix of another (`@t` vs `@tid`)
  corrupted the longer placeholder (e.g. `@tid` ÔåÆ `200id`). Binding is now token-aware via
  `ParameterBinder.Bind` ÔÇö the single source of truth for named and positional parameters ÔÇö and
  replaces every occurrence of each placeholder.
- **Issue 337 ÔÇö SharpCoreDB.Server dropped `request.Parameters`**: `DatabaseService.ExecuteQuery` and
  `ExecuteNonQuery` now translate `request.Parameters` into the parameter dictionary expected by the
  engine. The binary protocol handler now parses bind-message parameter values (and `$n` placeholders)
  and forwards them, and the WebSocket handler forwards parameters as well.
- **ULID encoding was not standards-compliant**: the Crockford Base32 encoder/decoder treated a ULID
  as a plain 128-bit bit stream (RFC-4648 style), so generated ULIDs were not interchangeable with
  other standards-compliant implementations (Python/Java/Go). Encoding now follows the ULID
  specification ÔÇö the first character carries only 3 significant bits ÔÇö and decoding rejects values
  above the 128-bit range. `Ulid.NewUlid(long)` also enforces the 48-bit timestamp limit.
  *Breaking change vs 1.9.4 for previously stored ULID strings, mirroring posseth.global.ulid v2.0.0.*
- **Upgrade path for legacy ULIDs**: new `Ulid.FromLegacy(string)` / `Ulid.TryFromLegacy(...)` convert
  ULIDs generated before 1.9.5 into the current spec-compliant encoding. The 128-bit value
  (timestamp + randomness) is preserved exactly ÔÇö only the Base32 text changes ÔÇö so existing
  `_rowid` values and ULID columns can be migrated one-to-one. The legacy encoder/decoder is kept as
  `Base32.LegacyEncode`/`Base32.LegacyDecode` for migration tooling.
- **Automatic legacy-database detection and one-shot ULID migration**: `Database.NeedsLegacyUlidMigration()`
  tells you whether a database was created before 1.9.5 ÔÇö the ULID encoding generation is recorded in
  the database metadata (directory mode) and in the file-header feature flags (single-file `.scdb` mode),
  so no schema or version guessing is needed. `Database.MigrateLegacyUlids()` rewrites every ULID value
  in every `ULID`-typed column of every table (including hidden `_rowid` primary keys) to the
  spec-compliant encoding, preserving the 128-bit value exactly, and permanently marks the database as
  migrated (subsequent calls are no-ops). Run it once right after upgrading, before writing new rows;
  ULIDs mirrored in plain `TEXT` columns are not rewritten automatically and should be converted with
  `Ulid.FromLegacy` by the application.
- **Flaky `QueryCache_CacheSizeLimit_EvictsLeastUsed`**: the shared (static) trigger registry could
  leak a trigger registered by another test into parallel test runs ("Table audit_log does not
  exist"). Trigger tests now run serialized (`SerialTriggerTests` collection) and clear the registry
  in both setup and teardown.

### Changed
- **Graphical UI moved to SCDMS**: `tools/SharpCoreDB.Viewer` (Avalonia desktop viewer),
  `tools/SharpCoreDB.WebViewer` (Razor Pages web admin portal), `tests/SharpCoreDB.Viewer.Tests` and
  `docs/viewer/*` were removed from this repository. The UI now lives in the standalone repo
  [MPCoreDeveloper/SCDMS](https://github.com/MPCoreDeveloper/SCDMS). See `docs/SCDMS.md`.
- **Documentation is now English-only**: all Dutch-language documentation was translated, including
  the SCDMS migration note, the Examples hub README and the query-routing refactoring plan.
- **NuGet dependencies updated** to their latest stable versions across the whole repository
  (`Directory.Packages.props` and `SharpCoreDB.AppHost`): Aspire.Hosting.AppHost 13.5.3 +
  Aspire.AppHost.Sdk 13.5.3, AWSSDK.Core 4.0.102.1, BLite 5.0.9, MessagePack 3.1.8,
  Microsoft.EntityFrameworkCore.InMemory 10.0.11; the script-client versions (JS `package.json`,
  Python `pyproject.toml`) were synchronized to 1.9.5 and the legacy `SharpCoreDB.nuspec` dependency
  pins were refreshed. Unused Avalonia-related package pins from the removed viewer were deleted.
- **Full version synchronization to 1.9.5** across all packages, internal project references,
  `PackageReleaseNotes`, documentation, NuGet READMEs and test projects.


## [1.9.4] - 2026-08-22

### Added
- **Known Issue 1 ÔÇö opt-in at-rest per-record encryption**: `DatabaseConfig.EnableAtRestRecordEncryption`
  (default `false` for full backward compatibility). When enabled, table data files carry an 8-byte
  magic header and each appended record is AES-256-GCM encrypted; point reads, full scans, PK index
  rebuilds and compaction decrypt transparently. Legacy plaintext files and `NoEncryptMode` remain
  byte-for-byte unchanged; legacy/encrypted file mixing is prevented per file.
- **Known Issue 6 ÔÇö opt-in SQLite integer affinity**: `DatabaseConfig.UseSqliteIntegerAffinity`
  (default `false`). When enabled, `INTEGER` DDL maps to `DataType.Long` (Int64) so values like
  `DateTime.UtcNow.Ticks` fit; the default Int32 path now throws an actionable overflow message
  pointing to `BIGINT`/the flag.
- **Single-file Ôåö directory SQL parity**: single-file mode now handles the full WHERE operator set
  identically to directory mode ÔÇö `LIKE` / `NOT LIKE` (case-insensitive, `%`/`_`, NULL never matches),
  `IS NULL` / `IS NOT NULL`, and `BETWEEN` (inclusive, culture-independent numeric comparison).
  Aggregates (`COUNT`, `SUM`, `AVG`, `MIN`, `MAX`), `GROUP BY`, `IN`, `ORDER BY`, `LIMIT`, `DISTINCT`
  and JOINs already matched via the shared `SqlParser` and are now covered by regression tests.
- **New tests**: `KnownIssuesFixTests` (8, one per known issue incl. backward-compat guards) and
  `SingleFileDirectoryParityTests` (17 parity cases). Final suite: **1,474 tests, 0 failures**,
  15 intentionally-skipped CPU-timing performance benchmarks.

### Changed
- **Version bump 1.9.3 ÔåÆ 1.9.4** across all packable `.csproj` files, `Directory.Packages.props`
  (`SharpCoreDBVersion`), test projects, and documentation (hub docs, per-package READMEs,
  NuGet-readme info, script clients). `DocumentationConsistencyTests` now enforces `1.9.4` as the
  current release label.
- **Known Issue 2 ÔÇö reopen AOORE fix**: `Database.Load()` now pads `DefaultExpressions`,
  `ColumnCheckExpressions` and `ColumnLocaleNames` to the column count, so `ITable.Insert` after a
  reopen no longer throws `ArgumentOutOfRangeException`.
- **Known Issue 3 ÔÇö single-file point operations**: `SingleFileTable.FindByPrimaryKey` /
  `UpdateByPrimaryKey` / `DeleteByPrimaryKey` are now functional (transaction-aware, respect
  `AutoFlush`) instead of returning `null`/`false`.
- **Known Issue 4 ÔÇö read-after-write**: `ExecuteQuery` flushes pending batch-update writes
  (`_batchUpdateActive`) before executing, matching `ExecuteSQL(SELECT)`; plain metadata dirtiness is
  no longer force-flushed per query (avoids page-based engine read regressions).
- **Known Issue 5 ÔÇö SQL validator**: parameter keys are normalized by stripping `@`/`:` prefixes
  (consistent with `SqlParser.ResolveParameter`), removing false "Missing/Unused parameters" warnings
  while genuine mismatches are still reported.
- **Benchmark test fix**: `InsertOptimizationsTests.Baseline_10K_Inserts_Without_Optimizations` used
  an inverted `> 100 ms` assertion (faster machines were marked as failing); replaced by a correct
  functional upper-bound check.

### Fixed
- Directory-mode full-table scan now delegates `LIKE`/`NOT LIKE`/`BETWEEN` single-condition filtering
  to the shared evaluator (previously `BETWEEN` threw "Unsupported operator" and `LIKE` matched NULL
  rows), making directory and single-file semantics identical.

## [1.9.3] - 2026-07-28

### Added
- **SharpCoreDB.Functional.Linq2DB v1.9.3** ÔÇö Full production release of the linq2db adapter.
  - `FunctionalLinq2DbContext` providing `Option<T>`, `Fin<T>`, `Seq<T>` APIs over linq2db (`FindOneAsync`, `QueryAsync` with builder/predicate, `GetAllAsync`, `InsertAsync`/`InsertBatchAsync` (BulkCopy), `UpdateAsync`, `Delete*Async`, `CountAsync`, `ExistsAsync`, `TransactionAsync`).
  - High-performance `BulkCopyAsync` support for batch operations (critical for GraphRAG, AI ingestion, analytics).
  - Complete type mapping schema (`Ulid`, `Guid` (compact N format), `DateTime`/`DateTimeOffset` (ISO), `bool` Ôåö integer for SQLite compatibility).
  - Modern `DataOptions`-based constructors (fixes linq2db deprecation warnings).
  - Comprehensive documentation, examples, and cross-references in root README, `FEATURE_MATRIX`, GraphRAG guide, functional SQL docs, and dedicated package README.

### Changed
- Bumped central `SharpCoreDBVersion` to **1.9.3** in `Directory.Packages.props` and updated all references, package metadata, and documentation.
- All documentation refreshed to highlight the new library as a first-class, production-ready functional LINQ option (especially valuable for agentic/AI and GraphRAG workloads).

### Fixed
- Test projects updated to use compatible SQLite connection strings (`"Data Source=..."`) ÔÇö resolves linq2db `Microsoft.Data.Sqlite` provider parsing errors with SharpCoreDB's `"Path=..."` format.
- `GetByIdAsync` improved with safe fallback and explicit limits.
- All tests in `SharpCoreDB.Functional.Linq2DB.Tests` now pass reliably.
- Build and CI compatibility verified (including Release configuration).

**This is a production-grade release.** The Linq2DB functional adapter is now stable, well-tested, fully documented, and ready for real-world high-throughput use alongside the existing Dapper and EF Core functional packages.

## [1.9.2] - 2026-05-02

### Added
- Explicit backwards compatibility documentation for the optional `SharpCoreDB.Identity` package (confirmed fully compatible with 1.9.1 when paired with matching core version; no API or behavior changes in this release; all Identity tests passing).
- Current test count (2,223) now published in root README, package README patch notes, script client READMEs, and this changelog (per release prep requirements).

### Changed
- **All version numbers updated from 1.9.1 to 1.9.3** across every packable .csproj (Version, internal PackageReference, PackageReleaseNotes), test projects, and all documentation files (root README, docs/INDEX.md, docs/README.md, every src/*/README.md + NuGet.README.md + USAGE.md, script client READMEs, and Identity README).
- DocumentationConsistencyTests.cs updated to enforce "1.9.3" as the current release label in all hub documentation files.
- Root README and per-package documentation now prominently document the changes from 1.9.1 to 1.9.3 (version synchronization, docs refresh, test count publication, release readiness) and the exact current test count of 2,223.
- Identity README expanded with full backwards compatibility section (API stability, dependency pinning guidance, test status).
- All script client (Python/JS) patch notes and documentation labels aligned to 1.9.3.
- Plan execution completed to 100% (all steps from the release prep plan executed, including investigation, documentation, validation, and coverage verification).

### Fixed / Verified
- No remaining current-version "1.9.1" strings in active tags, install commands, or current release labels (only historical references such as "from 1.9.1 to 1.9.3" or "v1.9.1 highlights (previous)" remain, as required for accurate changelog/release notes).
- Identity package: reviewed public surface (SharpCoreDbIdentityService + entities + hasher + options + token provider); confirmed no breaking changes for 1.9.3. Recommended pairing with core at exact same version for optional packages.
- DocumentationConsistencyTests and Identity tests validated as part of release prep.
- Code coverage threshold (18% MIN per CI) verified passing (see validation steps in plan execution).

This release is a pure preparation/synchronization release with zero functional changes and 100% backwards compatibility for all packages including Identity.

## [1.8.0] - 2026-04-29

### Changed
- Synchronized repository versioning for the 1.8.0 release across .NET packages, script clients, and README/NuGet documentation.

## [1.7.2] - 2026-04-28

### Added
- **SIMD LoadUnsafe Optimization**: All 16 columnar SIMD aggregate methods (`SumInt32`, `SumInt64`, `SumDouble`, `MinInt32`, `MinInt64`, `MinDouble`, `MaxInt32`, `MaxInt64` ÔÇö both single-threaded and parallel variants) now use `Vector256.LoadUnsafe(ref data[i])` instead of `Vector256.Create(data.AsSpan(i))`. This eliminates per-iteration `Span<T>` construction and bounds checking overhead in SIMD hot loops, yielding tighter codegen on AVX2 hardware.
- **Auto-ROWID**: Tables created without an explicit `PRIMARY KEY` now receive a hidden `_rowid` column (ULID type, auto-generated). Follows the SQLite rowid pattern ÔÇö invisible in `SELECT *`, visible when explicitly queried via `SELECT _rowid, ...`. See [`docs/features/AUTO_ROWID.md`](features/AUTO_ROWID.md) for full documentation.
- `Table.HasInternalRowId` property (persisted in metadata) to track tables with auto-generated `_rowid`.
- `Table.SelectIncludingRowId()` method for queries that explicitly request `_rowid`.
- `Database.GetColumnsIncludingHidden()` for schema discovery including hidden columns (with `IsHidden` flag).
- `ColumnInfo.IsHidden` property for metadata-driven schema tools.
- `PersistenceConstants.InternalRowIdColumnName` constant (`"_rowid"`).
- 9 dedicated tests for the Auto-ROWID feature in `AutoRowIdTests.cs`.
- **GRAPH_RAG SQL clause**: New top-level `GRAPH_RAG` SELECT clause with `LIMIT`, `WITH SCORE > X`, `WITH CONTEXT`, and `TOP_K` options, plus provider-based execution integration via `IGraphRagProvider`.
- **OPTIONALLY SQL projection mode**: New `OPTIONALLY` keyword after SELECT list enabling `Option<T>` mapping in ADO.NET readers, integrated with `SharpCoreDB.Functional`.
- **SOME/NONE predicates**: New `IS SOME` and `IS NONE` predicates (and NOT variants) supported in parser and runtime evaluators.
- **Major Avalonia UI Viewer update**: SharpCoreDB.Viewer now ships a significantly upgraded Avalonia UI with multi-tab query editor, typed table designer dropdown (including ULID and GUID), multi-language support (EN/DE/FR/ES/IT/NL), and network SharpCoreDB server connection support.
- **FluentMigrator default alignment**: `AddSharpCoreDBFluentMigrator()` now defaults both FluentMigrator generator and processor to SQLite-compatible mode, preventing SQL mismatches between the generator and processor.
- **`Microsoft.Extensions.Logging.Abstractions` bumped to 10.0.7** across all packages.

### Fixed
- Unified `IS NULL` / `IS NOT NULL` behavior across runtime scan, join-helper, and compiled predicate paths.
- Added parser support for scalar function expressions in SELECT columns (including `COALESCE(...)`) and parenthesized subquery expressions.
- Improved `EnhancedSqlParser` malformed SQL detection by flagging unparsed trailing content via `HasErrors`.
- Added LINQ translator handling for `ExpressionType.Convert` / `ConvertChecked` in enum-related comparison scenarios.
- Improved German locale comparison behavior for `├ƒ/ss` equivalence in locale-aware matching.
- Fixed PAGE_BASED mixed-predicate filtering (`column = value AND other_column <= value`) by routing scan-time predicate evaluation through the shared SQL condition evaluator; added regression coverage for `ORDER BY ... LIMIT` retrieval.
- **ColumnStore SIMD consistency**: Cleaned up inconsistent `MaxInt64SIMDDirect` implementation (previously used manual `ref` + `Unsafe.Add` pattern) to use the same `Vector256.LoadUnsafe(ref data[i])` pattern as all other SIMD methods.

### Changed
- Updated project documentation and status reports to reflect current implementation and validation baseline.
- Explicitly documented the remaining deferred single-file parameterized `ExecuteCompiled` disposal deadlock path.
- **Performance test hardening**: `ColumnStore_Average_10kRecords_Under2ms` now runs 10 iterations and asserts the best (minimum) time, with an additional warmup call. This eliminates false failures caused by concurrent test execution, GC pauses, or OS scheduling jitter.
- Ecosystem-wide package version synchronization on `1.7.2`.

## [1.7.1] - 2026-04-15

### Added
- Synchronized package release across the entire ecosystem (`1.7.1`).
- Release automation now publishes all packable SharpCoreDB packages in CI/CD.

### Changed
- Aligned package metadata and version references to the synchronized `1.7.1` release line.

## [1.7.0] - 2026-04-06

### Added
- `SharpCoreDB.Graph.Advanced` package for advanced graph analytics and GraphRAG workflows.
- Functional package family: `SharpCoreDB.Functional`, `SharpCoreDB.Functional.Dapper`, `SharpCoreDB.Functional.EntityFrameworkCore`.
- Expanded optional package guidance for `SharpCoreDB.EventSourcing`, `SharpCoreDB.Projections`, and `SharpCoreDB.CQRS`.

### Changed
- Ecosystem-wide package version synchronization on `1.7.0`.
- Documentation refresh across root/docs/src package README files with per-project features and v1.7.0 changes.
- SIMD aggregate hot loops updated to `Vector256.LoadUnsafe` pattern in columnar paths.

### Fixed
- SQL lexer/parser reliability for parameterized compiled-query execution.
- Metadata flush/reopen reliability paths with backward-compatible metadata format handling.

## [1.6.0] - 2026-03-30

### ­ƒÄë Major Achievement - Phase 12: GraphRAG Enhancement & Vector Search Integration COMPLETE

SharpCoreDB v1.6.0 introduces **GraphRAG (Graph Retrieval-Augmented Generation)** - a comprehensive graph analytics platform with semantic vector search integration for contextually rich search results.

### Ô£¿ Added - Phase 12: GraphRAG Enhancement

#### GraphRAG Engine
- **Real Semantic Search**: Vector search integration with HNSW indexing and SIMD acceleration (50-100x faster than SQLite)
- **Multi-Factor Ranking**: Combines semantic similarity + topological importance + community context
- **Intelligent Caching**: TTL-based result caching with automatic cleanup and memory monitoring
- **Production Performance**: Sub-50ms end-to-end search with linear scaling
- **Enhanced Search Results**: Rich context descriptions combining multiple ranking factors

#### Advanced Community Detection
- **Louvain Algorithm**: O(n log n) modularity optimization - highest accuracy for community detection
- **Label Propagation**: O(m) fast approximation - optimized for large graphs
- **Connected Components**: O(n + m) simple grouping - perfect for basic clustering
- **SQL Integration**: Direct SQL functions for community analysis (`DETECT_COMMUNITIES_LOUVAIN`, `GET_COMMUNITY_MEMBERS`)

#### Comprehensive Centrality Metrics
- **Degree Centrality**: O(n) - Direct connection count measuring popularity
- **Betweenness Centrality**: O(n ├ù m) - Bridge detection for information flow analysis
- **Closeness Centrality**: O(n┬▓) - Distance efficiency measuring accessibility
- **Eigenvector Centrality**: O(k ├ù m) - Influence measurement for prestige analysis
- **SQL Functions**: Direct database functions for all centrality calculations

#### Advanced Subgraph Queries
- **K-Core Decomposition**: Find densely connected subgraphs and core structures
- **Triangle Detection**: Identify mutual relationships and friend-of-friend patterns
- **Clique Detection**: Find complete subgraphs and tightly knit groups
- **Subgraph Extraction**: Extract neighborhoods, paths, and local structures

#### Performance & Optimization Suite
- **Performance Profiler**: Comprehensive operation timing, memory tracking, and benchmarking
- **Memory Optimization**: Batch processing, pooling, and efficient resource management
- **Scaling Strategies**: Horizontal/vertical partitioning for massive graph processing
- **Health Monitoring**: Cache statistics, performance alerts, and diagnostic tools

### ­ƒôÜ Documentation & Examples

#### Comprehensive Documentation Suite
- **API Reference**: Complete XML-documented API with complexity analysis
- **Basic Tutorial**: 15-minute getting started guide for new users
- **Advanced Patterns**: Multi-hop reasoning, custom ranking, production deployment
- **Performance Tuning**: Optimization strategies, scaling guides, troubleshooting
- **Integration Guides**: OpenAI, Cohere, and local embedding provider examples

#### Integration Examples
- **OpenAI Embeddings**: Complete integration with cost tracking and rate limiting
- **Custom Providers**: Extensible interface for any embedding service
- **Production Patterns**: Error handling, caching, monitoring, and scaling

### ­ƒº¬ Testing & Quality Assurance

#### Comprehensive Test Suite
- **20 integration tests** covering all major functionality
- **100% pass rate** with extensive edge case coverage
- **Performance validation** with automated benchmarking
- **Memory safety** verified through comprehensive testing

### ­ƒôè Performance Metrics

#### Benchmark Results
```
GraphRAG Search (k=10):     45ms  (222 ops/sec)
Vector Search (k=10):       12ms  (833 ops/sec)
Community Detection:        28ms  (178 ops/sec)
Enhanced Ranking:            5ms (2000 ops/sec)
```

#### Scaling Characteristics
- **Linear performance scaling** with graph size for all operations
- **Memory efficient**: < 10MB for 10K node graphs with intelligent caching
- **SIMD acceleration**: Hardware-optimized vector operations
- **Batch processing**: Handles large datasets without memory pressure

### ­ƒº╣ Documentation Migration & Cleanup
- Removed obsolete phase-status, kickoff, completion, and superseded planning documents across `docs/archived`, `docs/server`, and `docs/graphrag`.
- Consolidated documentation navigation to canonical entry points:
  - `docs/INDEX.md`
  - `docs/README.md`
  - `docs/server/README.md`
  - `docs/scdb/README_INDEX.md`
  - `docs/graphrag/00_START_HERE.md`
- Updated root `README.md` documentation pointer to canonical index.
- Cleaned stale references to removed files and validated documentation link consistency for removed targets.
