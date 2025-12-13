# Hash Index Usage Investigation Report

## Problem Statement
SharpCoreDB benchmarks show:
- **Point queries: 17-20x slower than SQLite** (expected: 0.5-1.5x)
- **Range queries: 2,900x slower than SQLite** (expected: 2-5x)

Despite hash indexes being created in `CreateUsersTable()`.

## Evidence: Indexes ARE Created

In `BenchmarkDatabaseHelper.CreateUsersTable()`:
```csharp
// Line 73-76
database.ExecuteSQL("CREATE INDEX idx_users_id ON users (id)");
database.ExecuteSQL("CREATE INDEX idx_users_email ON users (email)");
database.ExecuteSQL("CREATE INDEX idx_users_age ON users (age)");
database.ExecuteSQL("CREATE INDEX idx_users_is_active ON users (is_active)");
```

? Indexes are created during setup
? No exceptions during index creation

## Hypothesis: Indexes Not Being Used

### Possible Causes

#### 1. Lazy Loading Not Triggered
**File**: `DataStructures/Table.Indexing.cs`

The indexes use **lazy loading** - they're registered but not built until first query.

**Check**:
```csharp
// In Table.Indexing.cs - EnsureIndexLoaded()
// Does this get called during SelectInternal()?
```

**Evidence**:
- Memory usage is 800x higher than SQLite
- Suggests full table scan loading all rows into memory
- Hash index lookup should be ~0 allocations

#### 2. WHERE Clause Parser Not Detecting Index
**File**: `Services/SqlParser.cs`

The WHERE clause `WHERE id = @id` might not be recognized as index-eligible.

**Check**:
```csharp
// In SqlParser - GetQueryPlan()
private string GetQueryPlan(string tableName, string? whereStr)
{
    // Does this return "INDEX SCAN" or "FULL TABLE SCAN"?
}
```

**Evidence from benchmarks**:
```csharp
// SelectUserById uses parameterized query:
"SELECT * FROM users WHERE id = @id"
// Parameters: { "id", targetId }
```

#### 3. Index Lookup Not Being Called
**File**: `DataStructures/Table.CRUD.cs`

In `SelectInternal()`, the code should:
1. Parse WHERE clause
2. Check if index exists for column
3. Use `GetRowsViaDirectIndexLookup()` if available
4. Fall back to full scan if not

**Check**:
```csharp
// In Table.CRUD.cs - SelectInternal()
// Does it call TryParseSimpleWhereClause()?
// Does it call GetRowsViaDirectIndexLookup()?
// Or does it always do full scan?
```

#### 4. Parameterized Queries Breaking Index Detection
The benchmark uses:
```csharp
database.ExecuteQuery("SELECT * FROM users WHERE id = @id", parameters);
```

But the WHERE parser might expect:
```csharp
"SELECT * FROM users WHERE id = 1"  // Literal value
```

**Evidence**: SQLite handles both, but our parser might not.

## Diagnostic Steps Needed

### Step 1: Add Query Plan Logging
```csharp
// In ComparativeSelectBenchmarks.SetupAndPopulateSharpCoreDB()
// After creating indexes, test query plan:

var testQuery = "SELECT * FROM users WHERE id = 1";
var plan = GetQueryPlan(testQuery);
Console.WriteLine($"Query Plan: {plan}");
// Expected: "INDEX SCAN on idx_users_id"
// If actual: "FULL TABLE SCAN" -> Parser issue!
```

### Step 2: Check Index Load Status
```csharp
// After first query in benchmark:
var table = database.GetTable("users");
var indexStats = table.GetIndexLoadStatistics();

foreach (var kvp in indexStats)
{
    Console.WriteLine($"Index {kvp.Key}:");
    Console.WriteLine($"  - IsLoaded: {kvp.Value.IsLoaded}");
    Console.WriteLine($"  - IsStale: {kvp.Value.IsStale}");
    Console.WriteLine($"  - UniqueKeys: {kvp.Value.UniqueKeys}");
}
```

### Step 3: Profile SelectInternal Execution
```csharp
// Add logging in Table.CRUD.cs SelectInternal():

Console.WriteLine($"WHERE clause: {where}");

if (TryParseSimpleWhereClause(where, out var col, out var val))
{
    Console.WriteLine($"  - Detected simple WHERE: {col} = {val}");
    
    if (HasHashIndex(col))
    {
        Console.WriteLine($"  - Hash index EXISTS for {col}");
        EnsureIndexLoaded(col);
        var rows = GetRowsViaDirectIndexLookup(col, val);
        Console.WriteLine($"  - Index lookup returned {rows.Count} rows");
    }
    else
    {
        Console.WriteLine($"  - NO hash index for {col}");
    }
}
else
{
    Console.WriteLine($"  - Could NOT parse WHERE clause");
}
```

### Step 4: Test Without Parameters
```csharp
// In benchmark, try both:
var resultsPrepared = database.ExecuteQuery("SELECT * FROM users WHERE id = @id", parameters);
var resultsLiteral = database.ExecuteQuery("SELECT * FROM users WHERE id = 1");

// Compare performance - if literal is faster, parameterization is the issue
```

## Expected Results After Diagnosis

### If Query Plan Shows "FULL TABLE SCAN"
**Problem**: WHERE clause parser not detecting index-eligible queries
**Fix**: Update `TryParseSimpleWhereClause()` to handle parameterized queries

### If Index IsLoaded = false
**Problem**: Lazy loading not triggering
**Fix**: Ensure `EnsureIndexLoaded()` is called in `SelectInternal()`

### If Index IsLoaded = true But Not Used
**Problem**: `GetRowsViaDirectIndexLookup()` not being called
**Fix**: Fix condition check in `SelectInternal()` to use index path

### If Parameters Break Parsing
**Problem**: `TryParseSimpleWhereClause()` expects literals, not `@id`
**Fix**: Update parser to handle parameter placeholders

## Recommended Fix Priority

### P0 - CRITICAL: Enable Query Plan Logging
Add logging to see actual execution path:
```csharp
// In SqlParser.ExecuteQueryInternal()
var plan = GetQueryPlan(tableName, whereStr);
Console.WriteLine($"[QueryPlan] {sql} -> {plan}");
```

### P1 - HIGH: Fix Parameter Detection
Update WHERE parser to handle `@paramName`:
```csharp
// In TryParseSimpleWhereClause()
// Current: Parses "id = 1"
// Needed: Also parse "id = @id"
```

### P2 - HIGH: Verify Index Loading
Add assert in `SelectInternal()`:
```csharp
if (HasHashIndex(columnName))
{
    EnsureIndexLoaded(columnName);
    Debug.Assert(loadedIndexes.Contains(columnName), 
        $"Index {columnName} should be loaded!");
}
```

### P3 - MEDIUM: Add Index Usage Metrics
Track index usage:
```csharp
private long indexLookupsUsed = 0;
private long fullScansPerformed = 0;

// In SelectInternal():
if (usedIndexLookup)
    Interlocked.Increment(ref indexLookupsUsed);
else
    Interlocked.Increment(ref fullScansPerformed);
```

## Next Steps

1. **Add diagnostic logging** to see actual query execution
2. **Run benchmark with logging** to capture query plans
3. **Identify root cause** from logged output
4. **Apply targeted fix** based on diagnosis
5. **Re-run benchmarks** to verify improvement

## Expected Performance After Fix

If indexes are properly used:

### Point Query (WHERE id = X)
- **Current**: 784-928 ?s (17-20x slower)
- **After Fix**: **20-70 ?s** (0.5-1.5x of SQLite) ?
- **Improvement**: **10-40x faster!** ??

### Range Query (WHERE age BETWEEN X AND Y)
- **Current**: 133,709 ?s (2,900x slower)
- **After Fix**: **500-2,000 ?s** (10-40x of SQLite) ?
- **Improvement**: **60-260x faster!** ??

### Memory Usage
- **Current**: 793,000 bytes (800x more)
- **After Fix**: **5,000-10,000 bytes** (5-10x of SQLite) ?
- **Improvement**: **80-160x less memory!** ??

## Conclusion

The indexes ARE created, but something in the execution path prevents them from being used. The most likely causes:

1. ? Parameterized queries not recognized by WHERE parser
2. ? Lazy loading not triggered on first query
3. ? Index lookup code path not being executed

Once we identify which one, the fix should result in **10-40x performance improvement** for point queries!

---

**Status**: Investigation needed  
**Next**: Add diagnostic logging and re-run benchmarks  
**Priority**: CRITICAL - This is the #1 performance blocker
