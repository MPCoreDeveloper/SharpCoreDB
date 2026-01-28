# QueryCompiler Fix Summary

## Problem
The test `CompiledQuery_1000RepeatedSelects_CompletesUnder8ms` was failing with:
```
1000 compiled queries with cached plans should complete in <2000ms; took 3591ms
```

## Root Cause Analysis

### Investigation Steps
1. Used debugger to confirm `stmt.CompiledPlan` was `null`
2. Added diagnostic logging to `QueryCompiler.Compile()` 
3. Discovered the compilation was failing with:
   ```
   InvalidOperationException: The binary operator GreaterThan is not defined for the types 'System.Object' and 'System.Object'.
   ```

### Root Cause
When compiling WHERE clauses like `value > 500`, the QueryCompiler was attempting to create `Expression.GreaterThan(left, right)` where:
- `left` = Dictionary value (type: `object`)
- `right` = Literal constant (type: `int`)

The original code tried to convert both to the same type, but when both ended up as `object` type, the `Expression.GreaterThan()` method failed because LINQ expression trees cannot create comparison operators for `object` types.

## Solution

### Changes Made to `src\SharpCoreDB\Services\QueryCompiler.cs`

1. **Use IComparable for object-typed comparisons** (new method `CompareUsingIComparable`):
   - When either operand is `object` type, use `IComparable.CompareTo()` instead of direct comparison operators
   - Example: `left.CompareTo(right) > 0` instead of `left > right`

2. **Handle numeric type mismatches** (new method `GetCommonNumericType`):
   - Finds the widest common numeric type for two operands
   - Precedence: decimal > double > float > long > int > short > byte
   - Prevents cast exceptions when comparing Int32 with Decimal, etc.

3. **Refactored `ConvertBinaryExpression`**:
   - Prioritize `IComparable` for object-typed values
   - Use `GetCommonNumericType()` for strongly-typed numeric comparisons
   - Fall back to `IComparable` if no common type exists

### Code Structure
```csharp
if (left.Type == typeof(object) || right.Type == typeof(object))
{
    return CompareUsingIComparable(left, right, op);
}

if (left.Type != right.Type)
{
    var commonType = GetCommonNumericType(left.Type, right.Type);
    if (commonType != null)
    {
        // Convert to common type
    }
    else
    {
        return CompareUsingIComparable(left, right, op);
    }
}
```

## Results

### Performance Improvement
- **Before**: 3,591ms+ for 1000 queries (FAILED)
- **After**: 137-1466ms for 1000 queries (PASSED)
- **Improvement**: ~2.5-26x faster (queries now use compiled expression trees)

### Test Results
- ✅ All 9 CompiledQueryTests passing (1 skipped by design)
- ✅ Compilation now succeeds for queries with WHERE clauses
- ✅ Handles complex WHERE clauses with multiple conditions
- ✅ Properly converts between numeric types (Int32, Decimal, etc.)

## Files Modified
1. `src\SharpCoreDB\Services\QueryCompiler.cs`
   - Fixed `ConvertBinaryExpression()` method
   - Added `CompareUsingIComparable()` method
   - Added `GetCommonNumericType()` method

2. `tests\SharpCoreDB.Tests\CompiledQueryTests.cs`
   - Removed workaround skip logic
   - Test now runs and passes successfully

## Technical Details

### Why IComparable?
- `IComparable.CompareTo()` is implemented by all comparable types (Int32, Decimal, String, etc.)
- Returns: -1 (less), 0 (equal), or 1 (greater)
- Works with runtime-typed values without requiring compile-time type information
- Handles boxing/unboxing automatically

### Expression Tree Pattern
```csharp
// Convert to IComparable
var left = Expression.Convert(objectValue, typeof(IComparable));
var right = objectValue2; // Keep as object for CompareTo parameter

// Call CompareTo
var compareCall = Expression.Call(left, compareToMethod, right);

// Compare result with 0
var zero = Expression.Constant(0);
var result = Expression.GreaterThan(compareCall, zero); // compareTo > 0
```

## Future Considerations
- Consider caching compiled WHERE filters to avoid recompilation
- Could optimize by detecting numeric types at parse time
- May want to add support for custom IComparer implementations
- Consider adding telemetry to track compilation success rate

## Related Issues
- Fixes the "141/200 mystery" where compiled queries were falling back to SqlParser
- Improves performance of repeated SELECT queries by 2.5-26x
- Enables true compiled query execution using expression trees
