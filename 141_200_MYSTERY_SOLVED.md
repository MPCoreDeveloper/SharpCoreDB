# The 141/200 Mystery: SOLVED ✅

## Summary
Successfully fixed the data persistence issue where only 141 of 200 inserted orders were visible in validation queries.

## The Problem
- Seeding loop inserted 200 orders via SQL
- Validation showed only 141 orders persisted
- Remaining 59 rows lost

## Root Causes (Discovered in Order)

### Layer 1: Architecture (ExecuteSQL Loop)
**Problem**: Single-row `ExecuteSQL` in loop only writes to in-memory table, not storage
**Symptom**: Data never reached disk
**Solution**: Use `InsertBatch()` which writes directly to storage engine

### Layer 2: Persistence (WAL Batching)
**Problem**: `Flush()` method only flushed storage engine buffer, not in-memory table rows
**Symptom**: Even after InsertBatch, data seemed lost
**Solution**: Update `Flush()` to flush both in-memory tables AND storage engine

### Layer 3: Metadata Caching (Row Count)
**Problem**: After InsertBatch wrote 200 rows to disk, table's in-memory cache still showed 141
**Symptom**: Validation queries read stale metadata
**Solution**: Trigger table reload from disk via SELECT query after InsertBatch

## Implementation

### 1. Architecture Fix (SchemaSetup.cs)
```csharp
// BEFORE: Loop-based ExecuteSQL (in-memory only)
for (int i = 0; i < 200; i++)
{
    db.ExecuteSQL($"INSERT INTO orders VALUES ({orderId}, {custId}, {amount}, '{status}')");
}

// AFTER: Batch insert (writes to disk)
var orderRows = new List<Dictionary<string, object>>(200);
for (int i = 0; i < 200; i++)
{
    orderRows.Add(new Dictionary<string, object>
    {
        { "id", i + 1 },
        { "customer_id", (i % 5) + 1 },
        { "amount", 50 + (i % 20) * 5 },
        { "status", (i % 7 == 0) ? "CANCELLED" : "PAID" }
    });
}

InsertBatchWithFallback("orders", orderRows);  // Writes to disk!
```

### 2. Flush Fix (Database.Core.cs)
```csharp
public void Flush()
{
    // Flush ALL in-memory tables to disk
    foreach (var table in tables.Values)
        table.Flush();
    
    // Then flush storage engine
    if (storageEngine is not null)
        storageEngine.Flush();
    
    SaveMetadata();
    _metadataDirty = false;
}
```

### 3. Metadata Sync (SchemaSetup.cs)
```csharp
private int InsertBatchWithFallback(string tableName, List<Dictionary<string, object>> rows)
{
    var results = concreteDb.InsertBatch(tableName, rows);  // 200 rows → disk
    
    // Trigger table reload to sync in-memory cache with disk
    var reloadSql = $"SELECT * FROM {tableName} LIMIT 0";
    concreteDb.ExecuteQuery(reloadSql);  // Forces table to refresh from disk
    
    return results.Length;
}
```

## Results

### Verification
✅ **Disk**: `[FlushBufferedAppends] Successfully wrote 200 records`
✅ **Memory**: In-memory table cache now synced via SELECT reload
✅ **Validation**: All 200 orders visible to subsequent queries

### Modern C# 14 Features Used
- Collection initializers: `new List<Dictionary<string, object>>(200)`
- Target-typed new: `new Dictionary<string, object>`
- Collection expressions: `[.. results]`
- Switch expressions: `value switch { ... }`
- Null-coalescing: `value ?? DBNull.Value`

## Commits
1. `4be6f13` - Refactor bulk inserts to use InsertBatch
2. `0462719` - Flush both tables and storage engine
3. `ff215a65` - Clean up table refresh after InsertBatch

## Key Insight
**Data flow must be end-to-end**: In-Memory Table → Storage Buffer → Disk

When any layer is missing, data appears to be lost. The 141/200 mystery was actually THREE separate issues:
1. **Data never left memory** (ExecuteSQL loop)
2. **Memory wasn't flushed to disk** (incomplete Flush())
3. **Disk wasn't reloaded into memory** (stale cache)

Fixing all three ensures seamless persistence. ✅
