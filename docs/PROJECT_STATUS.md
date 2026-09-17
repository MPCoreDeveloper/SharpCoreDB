# SharpCoreDB Project Status

**Version:** 2.1.0-RC.3 (pre-release, `release/v2.1.0.0-RC.3`, net11.0 / C# 15 preview) · 2.0.0.2 (stable, `master`)
**Status:** ✅ Release candidate for the v2.1 line · ✅ v2.0 stable shipped · ✅ Performance-first line
**Last Updated:** September 17, 2026

## Current Status

The **v2.1 release candidate line (`2.1.0-RC.3`)** is validated and packaged: **2,897 tests across 16
suites — 0 failed, 0 errors, 16 skipped** (core 1,916), the write-path regression gate **passed**, and
the full benchmark arm set (comparative, fair-PK, pure-default, PageBased, multi-row INSERT) measured
in the product-default regime. Release notes: [`2.1.0-RC.3_WHAT_CHANGED.md`](2.1.0-RC.3_WHAT_CHANGED.md).

The **v2.0 stable packages (`2.0.0.2`)** remain the net10.0 / C# 14 line on `master`, including:

- `SharpCoreDB` (embedded engine — v2.0 performance release)
- `SharpCoreDB.Server` / `SharpCoreDB.Client`
- `SharpCoreDB.Data.Provider` / `SharpCoreDB.EntityFrameworkCore`
- `SharpCoreDB.Extensions` (including FluentMigrator integration)
- `SharpCoreDB.Analytics`, `SharpCoreDB.VectorSearch`, `SharpCoreDB.Graph`, `SharpCoreDB.Graph.Advanced`
- Optional Event Sourcing, Projections, CQRS, Distributed, Functional family packages

## v2.1.0-RC.3 Release Status

- ✅ **Storage default changed and validated** — the fixed-width inline capacity ships at 16 on both
  storage paths (+12–19 % on the tracked batched multi-row INSERT shape)
- ✅ **2,897 tests / 0 failures / 16 skipped** across all 16 test projects
- ✅ **Write-path regression gate PASSED** (nothing slower than baseline × 1.5)
- ✅ **Fair-PK UPDATE/DELETE now ahead of SQLite** on the fixed-width layout (0.8× / 0.5×)
- ⚠️ **INSERT still ~1.4× behind SQLite** on the fair-PK shape (1.9× on the pure default config)
- ⚠️ **PageBased UPDATE still 4.9× behind** (instrumented, not yet fixed)
- ✅ **Backward compatible**: existing databases open unchanged; the layout change is upgrade-only
- ✅ **Linux/macOS data-integrity defect fixed** — the AES-GCM cipher caches were shared across threads,
  which is only safe on Windows (dotnet/runtime#53320); they are thread-affine now, so concurrent
  encryption cannot corrupt a cipher's native state or repeat a nonce off Windows

| Measured 2026-09-17 (median of 3, product-default regime) | SharpCoreDB fixed-width | SQLite | ratio |
|--------------------------|-------:|-------:|-------:|
| READ — fair PK | 105,652 | 93,551 | **1.13× ahead** |
| UPDATE — fair PK | 324,560 | 258,213 | **0.80× (ahead)** |
| DELETE — fair PK | 736,046 | 342,452 | **0.47× (ahead)** |
| INSERT — fair PK | 108,661 | 156,946 | 1.44× behind |
| UPDATE — pure default config | 79,337 | 284,996 | 3.6× behind |
| PageBased UPDATE — fair PK, fixed-width | 58,464 | 289,159 | 4.9× behind |

> Absolutes are from a loaded development machine and are **not** portable — SQLite's own reference
> columns moved between sessions, so the ratios are the comparable part. Full tables and caveats:
> [`2.1.0-RC.3_WHAT_CHANGED.md`](2.1.0-RC.3_WHAT_CHANGED.md).

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
  - ✅ **Fixed-width record layout for hot tables (2.1.0-RC.3)** — the constant-stride record with the
    overflow arena is the default for new PK tables, and the inline capacity now ships at 16 on both
    storage paths; on the fair-PK shape the fixed-width layout is **ahead** of SQLite on UPDATE
    (0.8×) and DELETE (0.5×).
  - ✅ **Deferred index maintenance on bulk DELETE (2.1.0-RC.3)** — the contiguous bulk-delete fast
    path honours the deferred-index default; this is the lever that closed the DELETE column.
  - [ ] Storage-level DELETE reuse (free-slot reuse / compaction on PageBased deletes)
  - [ ] **INSERT deficit on the fair-PK shape** (~1.4× behind SQLite; 1.9× on the pure default
    config) — the single-row statement path, not the storage I/O. Tracked in
    `docs/performance/INSERT_UPDATE_PERFORMANCE_PLAN.md` §11.
  - [ ] **PageBased UPDATE** (4.9× behind on the fixed-width layout) — profiled, needs the same
    instrumentation the append-only batch path has.
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

