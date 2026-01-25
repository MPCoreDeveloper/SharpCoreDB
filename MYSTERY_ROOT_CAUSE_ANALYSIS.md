# 141/200 Mystery - ROOT CAUSE ANALYSIS

## Key Finding 🎯

**All 200 rows ARE in memory**, but only 141 appear in SELECT results!

### Debug Evidence
```
[DEBUG] Direct table count: 141
[DEBUG] First order ID: 1
[DEBUG] Last order ID: 200
[DEBUG] Missing order IDs: 2,3,6,7,11,14,17,22,25,32,35,38,40,43,46,49,51,54,62,65...
```

## Root Cause Hypothesis

The 59 missing rows are being **lost during serialization/deserialization**:

1. ✅ Rows 1-200 are inserted into memory successfully
2. ✅ Rows are flushed to disk via storage engine
3. ❌ When rows are read back from disk, `DeserializeRowFromSpan()` fails for 59 specific rows
4. ❌ Those rows return `null` and are skipped during SELECT

## Next Steps to Verify

Run the demo with the new debug output in `ScanPageBasedTable`:

```
[ScanPageBasedTable] ✅ Total records found: XXX, deserialization failures: YYY, after filtering: ZZZ
```

This will show:
- `Total records found`: How many raw records exist on disk
- `deserialization failures`: How many fail to deserialize (should be 59 if hypothesis is correct)
- `after filtering`: Final row count (should be 141)

## If Hypothesis is Correct

The bug is in **serialization/deserialization logic**, NOT in:
- WAL buffering
- Batch insertion
- Query filtering
- Storage persistence

The fix would target `DeserializeRowFromSpan()` to handle whatever malformed data is on disk.

## Files to Investigate

- `Table.PageBasedScan.cs` - Contains `DeserializeRowFromSpan()`
- `StorageEngine.Read()` - May be returning corrupted data
- Row serialization format - May have alignment or boundary issues

