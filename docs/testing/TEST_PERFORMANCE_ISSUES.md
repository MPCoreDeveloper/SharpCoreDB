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

### ✅ Resolved (Previously Deferred)

1. **Single-file parameterized `ExecuteCompiled` hang**
   - **Root cause found:** `FastSqlLexer.NextToken()` had an infinite loop — the `?` parameter placeholder character did not match any case in the switch expression, and the default case returned an `Unknown` token without advancing `position`. Since `Tokenize()` discards `Unknown` tokens and re-reads, this caused an infinite loop.
   - **Fix applied (3 files):**
     - `FastSqlLexer.cs`: Added `Parameter` token type for `?`, added `AdvanceUnknown()` safety method for default case.
     - `EnhancedSqlParser.Expressions.cs`: Added `?` placeholder handling in `ParseLiteral()`.
     - `QueryCompiler.cs`: Allow null `whereFilter` when parameter placeholders are present (parameterized queries use `BindPreparedSql` at execution time).
   - Additionally, `IAsyncDisposable` was implemented across all storage providers with proper disposal ordering.
   - Test `SingleFileDatabase_ExecuteCompiled_WithParameterizedPlan_ReturnsRows` now passes in ~1 second.

## Validation Snapshot

- CI-style verification baseline: **1,490 passed, 0 failed, 0 skipped**.
- Locale collation suite: **21 passed, 0 failed, 0 skipped**.
- Engine limitation regression suite (`EngineLimitationFixTests`): **10 passed, 0 failed, 0 skipped**.

## Follow-up Work

- ✅ ~~Implement async disposal lifecycle for single-file provider.~~ Done — `IAsyncDisposable` implemented across all storage providers.
- ✅ ~~Re-enable and stabilize the previously skipped parameterized `ExecuteCompiled` single-file test.~~ Done — test passes in ~1 second.
- Keep this document synchronized with `docs/PROJECT_STATUS.md` after each remediation pass.
