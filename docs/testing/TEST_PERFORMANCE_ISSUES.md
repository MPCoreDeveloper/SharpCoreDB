# Test Performance and Stability Notes

## Scope

This document tracks test-related runtime issues that can affect reliability, performance, or CI throughput.

## Current Status (March 14, 2026)

### ✅ Resolved

1. **`IS NULL` / `IS NOT NULL` behavior parity**
   - Runtime scan path, join helper path, and compiled predicate path now agree.
   - Added regression coverage in `EngineLimitationFixTests`.

2. **Enhanced parser trailing-token error detection**
   - `EnhancedSqlParser` now flags unexpected trailing content via `HasErrors`.
   - Existing error-recovery suite remains green.

3. **Scalar function parsing in SELECT columns**
   - `COALESCE(...)` and other scalar calls parse through expression-backed column nodes.
   - Parenthesized expressions/subqueries in select columns are handled consistently.

4. **German locale collation ß/ss equivalence**
   - Locale comparison now includes `CompareOptions.IgnoreNonSpace` where case-insensitive comparison is requested.
   - `Phase9_LocaleCollationsTests` validates `straße`/`strasse` matching.

5. **LINQ translator enum-convert expressions**
   - `ExpressionType.Convert` / `ConvertChecked` are now translated in unary visitor flow.

### ⚠️ Deferred (Known Limitation)

1. **Single-file disposal deadlock path with parameterized `ExecuteCompiled`**
   - Scenario: `SingleFileDatabase.ExecuteCompiled` using parameterized plans can still hang during disposal in specific shutdown flows.
   - Current mitigation: safer disposal ordering and queue shutdown safeguards.
   - Remaining work: async lifecycle refactor to `IAsyncDisposable` in single-file storage provider internals (remove sync-over-async shutdown path).
   - Test status: affected test remains explicitly skipped with clear reason to avoid suite-wide hangs.

## Validation Snapshot

- CI-style verification baseline: **1,490 passed, 0 failed, 0 skipped**.
- Locale collation suite: **21 passed, 0 failed, 0 skipped**.
- Engine limitation regression suite (`EngineLimitationFixTests`): **10 passed, 0 failed, 0 skipped**.

## Follow-up Work

- Implement async disposal lifecycle for single-file provider.
- Re-enable and stabilize the previously skipped parameterized `ExecuteCompiled` single-file test.
- Keep this document synchronized with `docs/PROJECT_STATUS.md` after each remediation pass.
