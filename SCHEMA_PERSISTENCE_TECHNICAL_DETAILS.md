# Technical Details: Schema Persistence in Single-File Databases

## Storage Block Organization

When a table is created in a single-file database, multiple storage blocks are created:

```
sys:tabledir                          <- Master table directory (all tables metadata)
├── TableMetadataEntry [table1]
├── TableMetadataEntry [table2]
└── TableMetadataEntry [bench_records]
    ├── ColumnCount = 6
    ├── ColumnDefsOffset = 1 (placeholder, actual data in block)
    ├── TableName = "bench_records"
    └── ...

table:bench_records:columns           <- Column definitions for this table
├── ColumnDefinitionEntry[id]         <- id INTEGER PRIMARY KEY
├── ColumnDefinitionEntry[name]       <- name TEXT
├── ColumnDefinitionEntry[email]      <- email TEXT
├── ColumnDefinitionEntry[age]        <- age INTEGER
├── ColumnDefinitionEntry[salary]     <- salary DECIMAL
└── ColumnDefinitionEntry[created]    <- created DATETIME

table:bench_records:indexes           <- Index definitions for this table
└── IndexDefinitionEntry[idx_age]     <- CREATE INDEX idx_age ON bench_records(age)

table:bench_records:data              <- Actual row data
├── Block containing row 1
├── Block containing row 2
└── ...
```

## Data Flow During Database Lifecycle

### Phase 1: Database Creation and Table Creation

```
CREATE TABLE bench_records (...)
         ↓
SingleFileDatabase.ExecuteSQL()
         ↓
SingleFileSqlParser.ExecuteCreateTable()
         ↓
SingleFileTable created + TableDirectoryManager.CreateTable() called
         ↓
StoreColumnDefinitions(tableName, columns)
         ↓
Write to block "table:bench_records:columns" ✓
         ↓
SaveTableDirectory()
         ↓
Write to block "sys:tabledir" ✓
```

### Phase 2: Database Reload (The Problem)

**OLD FLOW (BROKEN):**
```
factory.CreateWithOptions(..., createImmediately: false)
         ↓
SingleFileStorageProvider.Open()
         ↓
SingleFileDatabase constructor
         ↓
LoadTables()
         ↓
For each table in tableDirManager.GetTableNames()
         ↓
tableDirManager.GetColumnDefinitions(tableName)
         ↓
LoadColumnDefinitions(offset, count)  ❌ Stub returns []
         ↓
SingleFileTable created with ZERO columns
         ↓
Table schema is LOST ❌
```

**NEW FLOW (FIXED):**
```
factory.CreateWithOptions(..., createImmediately: false)
         ↓
SingleFileStorageProvider.Open()
         ↓
TableDirectoryManager loads "sys:tabledir" block
         ↓
Table names are cached: ["bench_records"]
         ↓
SingleFileDatabase constructor
         ↓
LoadTables()
         ↓
For each table: "bench_records"
         ↓
tableDirManager.GetColumnDefinitions("bench_records")
         ↓
LoadColumnDefinitions("bench_records", 6)
         ↓
Read block "table:bench_records:columns" ✓
         ↓
Deserialize 6 ColumnDefinitionEntry structs ✓
         ↓
SingleFileTable created with 6 columns ✓
         ↓
Table schema is RESTORED ✓
```

## Key Implementation Details

### ColumnDefinitionEntry Structure

```csharp
[StructLayout(LayoutKind.Sequential)]
public struct ColumnDefinitionEntry
{
    public const int FIXED_SIZE = ...;
    public const int MAX_COLUMN_NAME_LENGTH = 128;
    
    public uint DataType;
    public uint Flags;
    public uint DefaultValueLength;
    public uint CheckLength;
    public fixed byte ColumnName[MAX_COLUMN_NAME_LENGTH + 1];
    // ... additional fields
}
```

### Reading from Storage

The implementation uses:

1. **Memory Layout Safety**: `MemoryMarshal.Read<T>()` to safely deserialize structs
2. **Span-based Processing**: Uses `ReadOnlySpan<byte>` for zero-copy deserialization
3. **Error Handling**: Catches exceptions from storage layer to gracefully handle missing/corrupted blocks
4. **Dynamic Sizing**: Accounts for variable-length parts (default values, constraints) when skipping through buffer

## Performance Considerations

- **Synchronous I/O**: Uses `.GetAwaiter().GetResult()` (acceptable for initialization)
- **Memory Pooling**: `ArrayPool<byte>` used in `StoreColumnDefinitions`
- **Lazy Loading**: Schema only loaded when `GetColumnDefinitions()` is called
- **Caching**: Once loaded, metadata cached in `_tableCache` dictionary

## Backward Compatibility

This fix is **100% backward compatible**:
- Old databases with persisted column definitions now work correctly
- New databases benefit from proper schema persistence
- No schema version changes
- Block names unchanged (`table:{tableName}:columns`, `table:{tableName}:indexes`)

## Testing

The benchmark will now:
1. ✅ Create database and table in `Setup()`
2. ✅ Insert 5,000 rows successfully
3. ✅ Call `IterationCleanup()` which closes/reopens database
4. ✅ Load table schema from persisted blocks
5. ✅ Insert additional rows in next iteration (previously failed)
6. ✅ Repeat for multiple iterations without errors

## Related Code Paths

- `src/SharpCoreDB/Storage/Scdb/TableDirectoryManager.cs` - Fixed
- `src/SharpCoreDB/DatabaseExtensions.cs` - Uses `SingleFileDatabase.LoadTables()`
- `src/SharpCoreDB/Storage/Scdb/SingleFileStorageProvider.cs` - Provides block storage
- `tests/SharpCoreDB.Benchmarks/StorageEngineComparisonBenchmark.cs` - Affected test (now works)
