# SELECT Benchmark - Zero Rows Root Cause Analysis

## 🔍 Critical Issue: All Queries Returning 0 Rows

**Observation**:
```
Inserted records: 0
✓ Time: 48ms | Results: 0 rows
```

**Impact**: ALL benchmark results are invalid when no data exists.

---

## 🧪 Diagnostic Approach

### Step 1: Add Logging to InsertBatch

Added detailed diagnostics:

```csharp
Console.WriteLine("  Inserting 10,000 records...");
db1.ExecuteBatchSQL(inserts);
Console.WriteLine("  Batch insert completed");

var countResult = db1.ExecuteQuery("SELECT COUNT(*) FROM users");
var firstValue = countResult[0].Values.FirstOrDefault();
Console.WriteLine($"  Inserted records: {firstValue}");

// ✅ CRITICAL: If count is 0, try individual inserts as fallback
if (firstValue?.ToString() == "0")
{
    Console.WriteLine("  ⚠️ Batch insert returned 0 rows, trying individual inserts...");
    foreach (var sql in inserts.Take(100))
    {
        db1.ExecuteSQL(sql);
    }
    var recount = db1.ExecuteQuery("SELECT COUNT(*) FROM users");
    Console.WriteLine($"  After individual inserts: {recount[0].Values.FirstOrDefault()} records");
}
```

### Step 2: Possible Root Causes

Based on the `ExecuteBatchSQL` implementation analysis:

#### Cause 1: Parse Failure

```csharp
var parsed = ParseInsertStatement(sql);
if (parsed.HasValue)
{
    // Use InsertBatch (fast path)
}
else
{
    // Fallback to normal execution
    nonInserts.Add(sql);
}
```

**Issue**: If `ParseInsertStatement` fails silently, inserts go to `nonInserts` list but may not execute.

**Test**: Add logging to see if parsing fails:
```csharp
Console.WriteLine($"  Parsed {insertsByTable.Count} tables, {nonInserts.Count} non-inserts");
```

---

#### Cause 2: Transaction Not Committing

```csharp
storage.BeginTransaction();
try
{
    table.InsertBatch(rows);  // Batch insert
    storage.CommitAsync().GetAwaiter().GetResult();  // ✅ CRITICAL!
}
catch
{
    storage.Rollback();
    throw;
}
```

**Issue**: If `CommitAsync()` fails silently or returns before flush, data not persisted.

**Test**: Check if exception is thrown during batch.

---

#### Cause 3: Table Not Found

```csharp
if (tables.TryGetValue(tableName, out var table))
{
    table.InsertBatch(rows);
}
```

**Issue**: If `tableName` doesn't match exactly (case sensitivity?), batch is skipped.

**Test**: Verify table name exists:
```csharp
Console.WriteLine($"  Table 'users' exists: {db1.tables.ContainsKey("users")}");
```

---

#### Cause 4: Decimal Parsing Issue

```
INSERT INTO users VALUES (..., 30000, ...) 
                              ^
                              salary DECIMAL
```

**Issue**: `SqlParser.ParseValue()` might fail to parse integer as DECIMAL.

**Test**: Try explicit decimal format:
```csharp
$"INSERT INTO users VALUES ({i}, 'User{i}', 'user{i}@test.com', {20 + (i % 50)}, {30000 + (i % 70000)}.0, '2025-01-01')"
                                                                                                             ^^^ Add .0
```

---

#### Cause 5: Date Format Issue

```
INSERT INTO users VALUES (..., '2025-01-01')
```

**Issue**: `SqlParser.ParseValue()` might expect different date format.

**Test**: Try ISO 8601 format:
```csharp
$"..., '2025-01-01T00:00:00')"
```

---

## 🎯 Immediate Fixes Applied

### Fix 1: Fallback to Individual Inserts

```csharp
if (firstValue?.ToString() == "0")
{
    Console.WriteLine("  ⚠️ Batch insert returned 0 rows, trying individual inserts...");
    foreach (var sql in inserts.Take(100)) // Try first 100
    {
        db1.ExecuteSQL(sql);
    }
}
```

**Benefit**: If batch fails, at least get SOME data for testing.

### Fix 2: Diagnostic Logging

```csharp
Console.WriteLine("  Inserting 10,000 records...");
db1.ExecuteBatchSQL(inserts);
Console.WriteLine("  Batch insert completed");
Console.WriteLine($"  Inserted records: {firstValue}");
```

**Benefit**: See exactly where failure occurs.

---

## 📊 Expected Output After Diagnostic Build

### Scenario A: Parse Failure
```
  Inserting 10,000 records...
  Batch insert completed
  Inserted records: 0
  ⚠️ Batch insert returned 0 rows, trying individual inserts...
  After individual inserts: 100 records
```
**Diagnosis**: `ParseInsertStatement()` failing → Fix parser

### Scenario B: Transaction Failure
```
  Inserting 10,000 records...
  ❌ Batch insert failed: [exception message]
```
**Diagnosis**: Transaction/commit issue → Check storage engine

### Scenario C: Table Name Mismatch
```
  Inserting 10,000 records...
  Batch insert completed
  Inserted records: 0
```
**Diagnosis**: Table not found in batch logic → Check table name case

### Scenario D: Data Type Issue
```
  Inserting 10,000 records...
  ❌ Batch insert failed: Type mismatch for column salary
```
**Diagnosis**: DECIMAL parsing issue → Fix format

---

## 🔬 Next Steps

### 1. Run Diagnostic Build

```sh
cd SharpCoreDB.Benchmarks
dotnet run -c Release
# Select option 4
```

**Watch for**:
- "Batch insert completed" message
- "Inserted records: X" value
- Any error messages

### 2. Analyze Output

Based on the output, identify which scenario (A-D) occurred.

### 3. Apply Targeted Fix

- **Scenario A**: Fix `ParseInsertStatement()` regex/parsing
- **Scenario B**: Fix storage transaction logic
- **Scenario C**: Fix table name lookup (case sensitivity)
- **Scenario D**: Fix data type format (DECIMAL, DATE)

---

## 💡 Workaround Options

### Option 1: Skip Batch Insert (use individual inserts)

```csharp
// Instead of:
db1.ExecuteBatchSQL(inserts);

// Use:
foreach (var sql in inserts)
{
    db1.ExecuteSQL(sql);
}
```

**Pros**: Guaranteed to work  
**Cons**: 10x slower (10,000 individual SQL calls)

### Option 2: Use BulkInsertAsync

```csharp
var rows = new List<Dictionary<string, object>>();
for (int i = 1; i <= 10000; i++)
{
    rows.Add(new Dictionary<string, object>
    {
        ["id"] = i,
        ["name"] = $"User{i}",
        ["email"] = $"user{i}@test.com",
        ["age"] = 20 + (i % 50),
        ["salary"] = (decimal)(30000 + (i % 70000)),
        ["created"] = DateTime.Parse("2025-01-01")
    });
}
await db1.BulkInsertAsync("users", rows);
```

**Pros**: Purpose-built for bulk data  
**Cons**: Requires `Database` instance (not `IDatabase`)

### Option 3: Test with Simpler Schema

```csharp
// Remove problematic columns
db1.ExecuteSQL("CREATE TABLE users (id INTEGER PRIMARY KEY, age INTEGER) STORAGE = PAGE_BASED");

for (int i = 1; i <= 10000; i++)
{
    inserts.Add($"INSERT INTO users VALUES ({i}, {20 + (i % 50)})");
}
```

**Pros**: Isolates data type issues  
**Cons**: Doesn't test full schema

---

## ✅ Status

**Build**: ✅ Successful  
**Diagnostics**: ✅ Added  
**Fallback**: ✅ Implemented  
**Ready to Test**: ✅ Yes

**Next Action**: Run benchmark and analyze diagnostic output to identify root cause.

---

## 🎯 Success Criteria

After running the diagnostic build, we should see:

```
  Inserting 10,000 records...
  Batch insert completed
  Inserted records: 10000
✓ Time: 48ms | Results: 7000 rows
```

If we see 0 records, the fallback will kick in:
```
  Inserted records: 0
  ⚠️ Batch insert returned 0 rows, trying individual inserts...
  After individual inserts: 100 records
✓ Time: 48ms | Results: 70 rows
```

This will tell us definitively if the issue is with batch logic or something else.
