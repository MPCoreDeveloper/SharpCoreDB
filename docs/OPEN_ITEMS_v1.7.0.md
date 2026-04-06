# SharpCoreDB Open Items Tracker (v1.7.0)

**Date:** 2026-04-06  
**Scope:** Repository-wide unfinished marker scan (`TODO`, `FIXME`, `TBD`, `HACK`, `NotImplementedException`)

## Summary

- Total unfinished markers found: **60**
- Production-code `NotImplementedException` in `src/` and `tools/`: **0** (resolved in this pass)
- Remaining markers are primarily in:
  - `tests/` test doubles and explicitly skipped performance tests
  - `tests/benchmarks/` planned benchmark steps

## Completed in this maintenance pass

- Replaced `NotImplementedException` with intentional unsupported behavior in:
  - `tools/SharpCoreDB.Viewer/Converters/DictionaryValueConverter.cs`
  - `tools/SharpCoreDB.Viewer/Converters/LocalizeExtension.cs`
  - `tools/SharpCoreDB.Viewer/Converters/ObjectToStringConverter.cs`
- Removed obsolete storage doc filenames carrying old version labels:
  - `docs/storage/METADATA_IMPROVEMENTS_V1.5.0.md` → `docs/storage/METADATA_IMPROVEMENTS_v1.7.0.md`
  - `docs/storage/QUICK_REFERENCE_V1.5.0.md` → `docs/storage/QUICK_REFERENCE_v1.7.0.md`
- Updated `docs/release/PHASE12_RELEASE_NOTES.md` version references to `v1.7.0`.

## Open items intentionally left for dedicated follow-up

### 1. Test/benchmark placeholders

Examples include:

- `tests/SharpCoreDB.Tests/JoinRegressionTests.cs` (`TODO` notes around `FlushPendingWalStatements` + `PageManager` restoration)
- `tests/SharpCoreDB.Tests/GenericIndexPerformanceTests.cs` (`Fact(Skip=...)` with performance calibration TODOs)
- `tests/benchmarks/SharpCoreDB.Benchmarks/Zvec/ZvecQueryBenchmark.cs` (planned benchmark TODO steps)

These require dedicated benchmark/test design work and should not be force-completed inside functional feature patches.

### 2. Test-double `NotImplementedException` stubs

Some tests intentionally use partial fake implementations that throw for unsupported members. These are scoped to specific tests and are not runtime product gaps.

## Next recommended execution order

1. Create a focused issue for `JoinRegressionTests` TODOs (`FlushPendingWalStatements`/`PageManager`).
2. Convert benchmark TODO comments into tracked benchmark tasks with acceptance metrics.
3. Replace remaining test-double `NotImplementedException` with `NotSupportedException` only where this improves failure diagnostics without increasing test noise.

## Validation notes

- Targeted build and migration-related tests pass after this maintenance pass.
- Repository still contains known non-blocking warnings outside this cleanup scope (e.g., architecture mismatch warning for external DB2 reference).
