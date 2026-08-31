# SharpCoreDB Project Status

**Version:** 2.0.0  
**Status:** ✅ Production Ready (core .NET packages) · ✅ Performance-first release shipped  
**Last Updated:** August 28, 2026

## Current Status

SharpCoreDB core .NET packages are release-labeled on `2.0.0` and build successfully, including:

- `SharpCoreDB` (embedded engine — v2.0 performance release)
- `SharpCoreDB.Server` / `SharpCoreDB.Client`
- `SharpCoreDB.Data.Provider` / `SharpCoreDB.EntityFrameworkCore`
- `SharpCoreDB.Extensions` (including FluentMigrator integration)
- `SharpCoreDB.Analytics`, `SharpCoreDB.VectorSearch`, `SharpCoreDB.Graph`, `SharpCoreDB.Graph.Advanced`
- Optional Event Sourcing, Projections, CQRS, Distributed, Functional family packages

## v2.0 Release Status

- ✅ **Performance-first release shipped** — benchmark gap vs SQLite closed (16–52x → parity/win)
- ✅ **2,412 tests / 0 failures** across all 15 test projects
- ✅ **Native AOT smoke** publishes + runs (exit 0)
- ✅ **100% backward compatible** with v1.9.x

| Measured (two-run range) | v2.0 | SQLite | LiteDB |
|--------------------------|-----:|-------:|-------:|
| READ — Direct / StructRow | 70–126K ops/s | 87–97K | 14–16K |
| READ — SQL | 51–59K ops/s | 87–97K | 14–16K |
| INSERT (batch) | 91–133K ops/s | 145–150K | 66–77K |
| UPDATE (batch) | 41–59K ops/s | 241–296K | 10–11K |
| DELETE (batch) | 30–142K ops/s | 320–367K | 13–14K |

> Full analysis: [`docs/manual/performance.md`](manual/performance.md) ·
> plan: [`docs/performance/V2_PERFORMANCE_PLAN.md`](performance/V2_PERFORMANCE_PLAN.md)

## FluentMigrator Status

- Embedded mode integration: available
- gRPC migration mode integration: available
- See `docs/migration/FLUENTMIGRATOR_EMBEDDED_MODE_v1.7.0.md` and
  `docs/migration/FLUENTMIGRATOR_SERVER_MODE_v1.7.0.md`

## Documentation Governance

- Canonical docs entry points: `README.md`, `docs/INDEX.md`, `docs/README.md`, `docs/manual/README.md`
- Obsolete/superseded phase-planning artifacts are removed during documentation maintenance.

## Roadmap Issue Closure Tracking

- ✅ `#125` Enforce database grants in Connect and session creation — completed and closed.
- ✅ `#124` Per-database grants model for tenant isolation — completed and closed.
- ✅ `#123` DatabaseRegistry runtime attach/detach APIs — completed and closed.
- ✅ `#122` Runtime tenant database provisioning APIs (gRPC + REST) — completed and closed.
- ✅ `#121` Tenant catalog in master database for SaaS lifecycle metadata — completed and closed.

## Roadmap / TODO (v2.1)

- [ ] **Close UPDATE/DELETE gap vs SQLite** (in progress — details in
  `docs/performance/V2_PERFORMANCE_PLAN.md` §3.4 / §3.5):
  - ✅ **In-place UPDATE for columnar/append-only (Issue #6)** — fixed-width / unchanged-length
    records overwrite their existing slot (`TryUpdateInPlace`); no new version, no file growth.
  - ✅ **Single-pass SQL DELETE/UPDATE (Issue #7/#8)** — `DeleteAffectedRows` / `UpdateAffectedCount`
    return the affected rows/count from the table operation itself, so the SQL paths no longer
    materialize matching rows twice for RETURNING / change-tracking.
  - ✅ **PK fast path in `Delete` / `DeleteMultiple` / `UpdateMultiple`** — a simple `pk = value`
    WHERE resolves via the primary-key B-tree directly (single search + one read) instead of
    full-row materialization + per-row re-search.
  - ✅ **Field-level in-place patch on the columnar UPDATE path (fixed-width layout step)** — when
    the row's storage position is known, only the updated fields are patched at their **actual**
    record offsets (`ComputeActualColumnOffsets` + `TryOverwriteFieldsInPlaceActual`); a fixed-size
    field keeps the record length unchanged, so the write is in-place (no full re-serialize, no
    file growth) — even for columns after variable-length TEXT columns. Registered hash indexes are
    loaded up front so append/logical-delete DML never leaves stale entries (stale-rebuild fix).
  - ✅ **Out-of-line overflow (B1, opt-in)** — `DatabaseConfig.FixedWidthRecordLayout`: constant-size
    records with TEXT/BLOB in a per-table overflow arena; every UPDATE (fixed or variable column) is
    an in-place overwrite (no `.dat` growth). Flag persisted in metadata.
  - ✅ **Arena GC (B3)** — `CompactStorage` compacts the overflow arena with the data file
    (live-offset collection + copy-on-compact + slot re-point).
  - ✅ **Constant-offset read wins (B4)** — early-WHERE re-enabled for fixed-width tables (numeric
    direct-offset reads incl. columns after variable columns; string arena-payload compare;
    StructRow numeric-SIMD batch filter). Fixed a latent offset-0 arena-block bug.
  - ✅ **1.x → 2.0 migration path (B5)** — fixed-width flag persisted per table; legacy databases
    auto-migrate on reopen with `FixedWidthRecordLayout` (or via `MigrateTableToFixedWidth`).
  - ✅ **Arena free-list (B6)** — freed overflow blocks reused in place for same-length values
    (no `.ovf` growth); fixed a latent offset-0 block leak on update.
  - ✅ **Single-file fixed-width (B6)** — `.scdb` tables store binary fixed-width records +
    overflow block instead of JSON (constant-size updates, format detected on reopen, JSON tables
    migrate via `MigrateTableToFixedWidth` or the config flag).
  - ✅ **PageBased auto-conversion (B6)** — `MigrateToFixedWidth` converts PageBased → Columnar
    in-process before the fixed-width rewrite; auto-migration on reopen covers PageBased too.
    Fixed a pre-existing PageBased data-loss bug (page cache never flushed on dispose).
  - ✅ **Cross-session free-list (B6)** — the directory-mode arena derives its free-list from the
    records on load, so dead blocks are reused across sessions without persisting the free-list.
  - [ ] Storage-level DELETE reuse (free-slot reuse / compaction on PageBased deletes)

**Single-file `.scdb` (A-track):**
  - ✅ **PK hash index (A1)** — `FindByPrimaryKey` / point `SELECT … WHERE pk = value` are O(1)
    (was O(N) cache scan); maintained on all mutations and rebuilt on reopen/rollback.
  - ✅ **In-place block overwrite (A2)** — `WriteBlockAsync` reuses the table block offset when the
    JSON fits; a same-length update does not grow the `.scdb` (pinned by a test).
  - [ ] Delta/incremental flush (A3); unify single-file onto the columnar format (A4)
- [ ] **.NET 11 / C# 15 migration** (after Nov 2026 GA) — Runtime Async, AVX-VNNI-512/SVE2 behind
  `SIMD_ENABLED`, optional Zstandard compression.
- [ ] **Native AOT warning cleanup** — interface-based B-tree factory (replace `GetMethod`/
  `Activator.CreateInstance`), source-gen `.scdb`/`ParseVectorValue` JSON.
- [ ] **Single-file metadata parity:** make `SingleFileDatabase` explicitly implement
  `IMetadataProvider` to align metadata discovery with directory-mode `Database`.
  - **Why:** some consumers probe metadata with `db is IMetadataProvider`; explicit implementation
    improves compatibility and predictability.
  - **Acceptance:** probing via `IMetadataProvider` works consistently for both directory and
    single-file databases.

