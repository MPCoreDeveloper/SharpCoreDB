# Checksum Mismatch Fix Summary

## Root Cause
The `InvalidDataException: Checksum mismatch for block 'table:bench_records:data'` was caused by **SingleFileTable continuously overwriting the same data block** instead of accumulating rows.

### The Problem Flow
1. **Insert Row 1**: Write row to `table:bench_records:data` → Calculate checksum → Store checksum in registry
2. **Insert Row 2**: OVERWRITE `table:bench_records:data` with NEW single row → Calculate NEW checksum
3. **Insert Row 3-5000**: Continue overwriting, checksums constantly changing
4. **SELECT operation**: Read block, calculate checksum, compare with stored checksum from step 2
5. **ERROR**: Checksums don't match because the registry has checksum from step 1, but disk has data from final insert

This caused:
- Data loss (only last row preserved)
- Checksum failures (block content changed without registry update)
- Race conditions between writes and reads

## Solution Implemented

### SingleFileTable Changes
Changed the table implementation to properly **accumulate rows in a JSON array format**:

```csharp
private List<Dictionary<string, object?>> ReadAllRowsInternal()
{
    // Read all existing rows from the block
    var data = _storageProvider.ReadBlockAsync(_dataBlockName).GetAwaiter().GetResult();
    // Deserialize from JSON array
    return System.Text.Json.JsonSerializer.Deserialize<List<...>>(json) ?? new();
}

private void WriteAllRowsInternal(List<Dictionary<string, object?>> allRows)
{
    // Serialize ALL rows to JSON array
    var json = System.Text.Json.JsonSerializer.Serialize(allRows);
    var data = Encoding.UTF8.GetBytes(json);
    
    // Write atomically - single WriteBlockAsync call
    _storageProvider.WriteBlockAsync(_dataBlockName, data).GetAwaiter().GetResult();
}
```

### Key Improvements

1. **Atomic Operations**: All Insert/Update/Delete operations now:
   - Acquire `_tableLock`
   - Read ALL rows from storage
   - Modify the list
   - Write ALL rows back in ONE operation
   - Release lock

2. **Single Checksum Per Table**: Each table now has ONE block with ALL rows, ensuring:
   - Checksum is calculated on complete, consistent dataset
   - No stale checksums
   - No race conditions between write and registry update

3. **Proper Row Accumulation**: Each operation properly accumulates rows:
   - `Insert()`: Reads existing rows, appends new row, writes all
   - `InsertBatch()`: Reads existing rows, appends all new rows, writes all
   - `Update()`: Reads all, finds and modifies matching row, writes all
   - `Delete()`: Reads all, removes matching rows, writes all

### SQL Parser Implementation
Added proper SQL parsing for:
- `CREATE TABLE`: Parse column definitions and types
- `INSERT INTO`: Parse column names and values, insert into table
- `UPDATE`: Parse SET clause and WHERE condition
- `DELETE`: Parse WHERE condition
- `SELECT`: Parse column list, FROM table, WHERE condition with filtering

## Checksum Flow After Fix

```
Insert Row 1:
  - Read block (empty) → []
  - Add row → [{...}]
  - Serialize → JSON string
  - WriteBlockAsync → Calculate SHA256 on [{...}] → Store checksum1
  
Insert Row 2:
  - Read block → [{...}]  ← OLD data still there
  - Add new row → [{...}, {...}]
  - Serialize → JSON string
  - WriteBlockAsync → Calculate SHA256 on [{...}, {...}] → Store checksum2
  
SELECT:
  - Read block → [{...}, {...}]
  - Calculate SHA256 → Matches checksum2 ✅
  - Filter rows where age > 30
  - Return results
```

## Files Modified

1. **src/SharpCoreDB/DatabaseExtensions.cs**
   - Rewrote `SingleFileDatabase` class
   - Removed broken `SingleFileSqlParser` references
   - Added `SingleFileTable` class with proper row accumulation
   - Implemented SQL parsing for DDL/DML operations

2. **src/SharpCoreDB/Storage/Scdb/SingleFileDatabase.Batch.cs**
   - Removed `SingleFileSqlParser` reference
   - Uses `database.ExecuteSQL()` instead

## Impact on Benchmarks

- **SCDB_Single_Unencrypted_Select()**: Now works correctly without checksum errors
- **All Single-File Benchmarks**: Proper data accumulation ensures data integrity
- **Performance**: Slightly slower than before due to JSON serialization, but correct and consistent

## Testing Notes

The benchmark setup in `StorageEngineComparisonBenchmark.cs`:
- Global Setup: Creates table, pre-populates 5,000 records
- Insert benchmarks: Each iteration adds 1,000 records → All accumulated atomically
- Update benchmarks: Modifies random records → All rows read/written atomically
- Select benchmarks: Filters rows where age > 30 → Works with accumulated data
- Iteration Cleanup: Calls `ForceSave()` to persist metadata

All checksums now stay consistent throughout the entire benchmark run.
