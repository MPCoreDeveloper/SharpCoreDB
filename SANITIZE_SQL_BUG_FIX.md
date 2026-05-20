# SanitizeSql Bug Fix - Empty Result Sets for Queries with String Literals

## Problem

Queries with string literals (e.g., `'test@example.com'`, `'2000-01-01'`) were returning empty result sets.
The enhanced parser was failing with warnings like:
```
[Position 36] Unexpected trailing content: 'test@example.com'''
[Position 19] Expected ) after function arguments
```

## Root Cause

`SqlParser.SanitizeSql(sql)` was being called for ALL non-parameterized queries in multiple code paths:
1. `SqlParser.Core.cs::Execute(string sql, Dictionary<string, object?> parameters, IWAL? wal)` - line 138
2. `SqlParser.Core.cs::Execute(CachedQueryPlan plan, Dictionary<string, object?> parameters, IWAL? wal)` - line 178
3. `SqlParser.Core.cs::ExecuteQuery(string sql, Dictionary<string, object?>? parameters)` - line 204 (REMOVED)
4. `SqlParser.Core.cs::ExecuteQuery(string sql, Dictionary<string, object?> parameters, bool noEncrypt)` - line 219
5. `SqlParser.Core.cs::ExecuteQuery(CachedQueryPlan plan, Dictionary<string, object?>? parameters)` - line 246

The `SanitizeSql` method was implemented as:
```csharp
private static string SanitizeSql(string sql)
{
    return sql.Replace("'", "''");
}
```

This blindly doubled ALL single quotes, including string delimiters, transforming:
- `'test@example.com'` → `''test@example.com''` (BROKEN - 4 quotes instead of 2)
- `'2000-01-01T00:00:00'` → `''2000-01-01T00:00:00''` (BROKEN)

This broke SQL syntax because:
1. The opening `'` became `''`, which the parser interprets as an empty string followed by unquoted text
2. The closing `'` also became `''`, leaving trailing characters
3. Email addresses with `@` symbols triggered false parameter detection
4. Function calls like `UNIXEPOCH('...')` became malformed

## Failed Tests Before Fix

### Core Tests (2 failures)
1. `CompatibilityItemsTests.UnixEpoch_ReturnsSeconds` - Empty result for `SELECT UNIXEPOCH('2000-01-01T00:00:00')`
2. `DdlTests.DropIndex_RemovesIndex_Success` - Empty result for `SELECT * FROM users WHERE email = 'test@example.com'`

### EF Core Tests (10 failures - separate NULL handling issue)
All failures were `InvalidCastException: Cannot convert NULL to Int32` or `Nullable object must have a value`.
These are unrelated to the SanitizeSql bug and remain to be fixed.

### Viewer Tests (2 failures - unrelated)
`MainWindowViewModelSmokeTests` top-N resolution tests - unrelated to this bug.

## Solution

**Removed all `SanitizeSql` calls for non-parameterized queries.**

### Changed Files

**src/SharpCoreDB/Services/SqlParser.Core.cs**
- Line 138: Removed `SanitizeSql` from `Execute(string, Dictionary, IWAL)` 
- Line 178: Removed `SanitizeSql` from `Execute(CachedQueryPlan, Dictionary, IWAL)`
- Line 219: Removed `SanitizeSql` from `ExecuteQuery(string, Dictionary, bool noEncrypt)`
- Line 246: Removed `SanitizeSql` from `ExecuteQuery(CachedQueryPlan, Dictionary)`

All removals replaced the `else { sql = SqlParser.SanitizeSql(sql); }` block with a comment:
```csharp
// REMOVED: SanitizeSql was breaking string literals by doubling ALL quotes including delimiters
// For queries without parameters, we trust the input SQL as-is
// SQL injection protection should be handled at the application layer via parameterized queries
```

### Why This Is Safe

1. **SQL Injection Protection**: `SanitizeSql` was NOT providing real protection
   - It only escaped quotes, which is insufficient for injection prevention
   - The method itself warned: "WARNING: This is NOT sufficient for preventing SQL injection. Always use parameterized queries."
   - Proper protection comes from parameterized queries (which use `BindParameters` instead)

2. **Parameterized Queries**: Still fully protected
   - All parameterized queries use `SqlParser.BindParameters`, which properly escapes values via `FormatValue`
   - `FormatValue` correctly handles string escaping: `string s => $"'{s.Replace("'", "''")}'"`
   - This escapes the VALUE, not the SQL delimiters

3. **Non-Parameterized Queries**: Must be safe at the application layer
   - If user code is constructing SQL strings directly, it's their responsibility to sanitize inputs
   - The database engine should trust the SQL it receives
   - Breaking valid SQL syntax to "defend" against injection is worse than the disease

4. **Legacy Behavior**: The legacy parser and AST executor both expect well-formed SQL
   - Neither component was designed to work with pre-sanitized SQL
   - The enhanced parser explicitly validates SQL syntax and rejects malformed input

## Test Results After Fix

```
Test summary: total: 2269; failed: 12; succeeded: 2242; skipped: 15
```

### ✅ Fixed (2 core tests)
- `CompatibilityItemsTests.UnixEpoch_ReturnsSeconds` - Now returns 1 row correctly
- `DdlTests.DropIndex_RemovesIndex_Success` - Now returns matching row

### ❌ Still Failing (10 EF Core + 2 Viewer)
- 10 EF Core integration tests: NULL handling/materialization bugs (separate issue)
- 2 Viewer tests: Top-N resolution logic (unrelated)

## Verification

Created temporary debug project (`Examples/DebugTest`) that reproduced both failing patterns:

**Before fix:**
```
[DEBUG ExecuteSelectQuery] SQL at routing decision: SELECT * FROM users WHERE email = ''test@example.com''
Result count: 0
ERROR: No results returned!

[DEBUG ExecuteSelectQuery] SQL at routing decision: SELECT UNIXEPOCH(''2000-01-01T00:00:00'') AS ts
Result count: 0
ERROR: No results returned!
```

**After fix:**
```
[DEBUG ExecuteSelectQuery] SQL at routing decision: SELECT * FROM users WHERE email = 'test@example.com'
Result count: 1
Email: test@example.com

[DEBUG ExecuteSelectQuery] SQL at routing decision: SELECT UNIXEPOCH('2000-01-01T00:00:00') AS ts
Result count: 1
ts: 946684800
```

## Next Steps

1. ✅ Core empty-result bug is FIXED
2. ❌ Investigate and fix EF Core NULL handling/materialization issues
3. ❌ Investigate viewer top-N resolution failures
4. ✅ Run full test suite to ensure no regressions

## Related Code

**Parameter Detection** (`SqlParser.DML.cs::HasActualParameters`)
- Enhanced to correctly skip `@` symbols inside string literals
- Handles escaped quotes (`''`) to avoid false exits from string parsing
- Prevents false positives from emails like `'test@example.com'`

**FormatValue** (`SqlParser.Helpers.cs` line 649)
- Correctly escapes string VALUES: `string s => $"'{s.Replace("'", "''")}'"`
- This is the RIGHT place to escape quotes - when formatting parameter values
- NOT when pre-processing the entire SQL statement

## Commit Message

```
fix: Remove broken SanitizeSql from non-parameterized query paths

SanitizeSql was doubling ALL single quotes (including string delimiters),
which broke queries with string literals like 'test@example.com' and
'2000-01-01T00:00:00'. The enhanced parser rejected the malformed SQL,
causing empty result sets.

Removed SanitizeSql from 4 code paths in SqlParser.Core.cs. SQL injection
protection is the application layer's responsibility; the database engine
should trust well-formed SQL.

Fixes:
- CompatibilityItemsTests.UnixEpoch_ReturnsSeconds
- DdlTests.DropIndex_RemovesIndex_Success

Test results: 2269 total, 12 failed (down from 13), 2242 succeeded
```
