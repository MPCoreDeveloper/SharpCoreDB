# THE KEY FIX: Auto-Flush Dirty Data Before SELECT

## The Problem (141/200 Mystery)
- **Symptom:** Inserting 200 orders via loop, but only 141 visible in validation queries
- **Root Cause:** WAL batch size = 100 rows per batch
  - Rows 1-100: Flushed after first batch completes
  - Rows 101-200: Stuck in WAL buffer, not flushed
  - Validation queries ran before rows 101-200 were persisted
  - Result: Only ~141 rows visible (first batch + partial second batch)

## The Solution (Yesterday's Fix - Now Restored)
**FLUSH DIRTY DATA BEFORE SELECT QUERIES**

Instead of:
- ❌ Periodic Flush() during insert loop (inefficient)
- ❌ Calling db.Flush() after every 100 rows (manual burden)

Do:
- ✅ Check `_metadataDirty` flag BEFORE running SELECT
- ✅ If dirty (unflushed changes), call Flush() automatically
- ✅ Then run the SELECT - it sees all data

## Implementation

### Modified ExecuteSQL() and ExecuteSQL(parameters)
```csharp
if (parts[0].Equals(SqlConstants.SELECT, StringComparison.OrdinalIgnoreCase))
{
    // ✅ Flush dirty data BEFORE SELECT
    if (_metadataDirty || _batchUpdateActive)
    {
        Flush();
    }
    
    ExecuteSelectQuery(sql, null);
    return;
}
```

### Why This Works
1. **Inserts are efficient** - Queue in WAL buffer without periodic flushing
2. **SELECTs are correct** - Auto-flush before running ensures fresh data
3. **Clean architecture** - No manual flushing needed in user code
4. **Transparent** - Users don't need to know about dirty flags

## Data Flow
```
Insert 1-200:  Queue in WAL buffer, set _metadataDirty = true
INSERT queries: Add to buffer...
                Add to buffer...
                Add to buffer...

Validation runs SELECT:
  - Detects _metadataDirty = true
  - Calls Flush() automatically
  - All 200 rows now persisted
  - SELECT runs against fresh data
  - Returns all 200 rows ✅
```

## Files Changed
- `src/SharpCoreDB/Database/Execution/Database.Execution.cs`
  - Added flush-if-dirty check before SELECT (two overloads)
  
- `tests/SharpCoreDB.DemoJoinsSubQ/SchemaSetup.cs`
  - Removed periodic Flush() calls from insert loop
  - Much cleaner seed code

## Result
✅ All 200 orders visible to validation queries
✅ Clean, elegant solution with no manual flushing needed
✅ Efficient buffering + correct SELECTs
✅ This was the proven fix from yesterday

## Key Insight
The _metadataDirty flag is PERFECT for this:
- Set when DML operations occur
- Checked before SELECTs
- Automatically flushes buffered changes
- No special transaction semantics needed
- Works transparently to user code
