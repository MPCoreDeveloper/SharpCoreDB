# Table.cs Refactoring Plan - Split into Partials

## Current Size Analysis
- **Total Lines**: ~1050 lines
- **Major Sections**:
  1. Properties & Fields (~80 lines)
  2. CRUD Operations (Insert, Select, Update, Delete) (~450 lines)
  3. Serialization (Read/Write methods) (~350 lines)
  4. Index Management (~70 lines)
  5. Helper Methods (~100 lines)

## Proposed Partial Class Structure

### 1. `Table.cs` (Main/Core) - ~150 lines
- Class declaration and interface implementation
- Public properties (Name, Columns, ColumnTypes, etc.)
- Private fields
- Constructors
- Dispose pattern
- SetStorage/SetReadOnly

### 2. `Table.CRUD.cs` - ~300 lines
- Insert method
- Select methods (Select, SelectWithLock, SelectInternal)
- Update method  
- Delete method
- Helper methods: CompareObjects, EvaluateWhere, TryParseSimpleWhereClause, GetRowsViaDirectIndexLookup

### 3. `Table.Serialization.cs` - ~350 lines
- EstimateRowSize
- WriteTypedValueToSpan
- ReadTypedValueFromSpan
- WriteTypedValue (legacy)
- ReadTypedValue (legacy)
- GenerateAutoValue
- GetDefaultValue
- IsValidType
- ReadRowAtPosition
- ParseValueForHashLookup

### 4. `Table.Indexing.cs` (NEW - for lazy loading) - ~250 lines
- CreateHashIndex (original + new lazy version)
- EnsureIndexLoaded (NEW)
- HasHashIndex
- GetHashIndexStatistics
- GetIndexLoadStatistics (NEW)
- IndexLoadStatus record (NEW)
- TotalRegisteredIndexes property (NEW)
- LoadedIndexesCount property (NEW)
- StaleIndexesCount property (NEW)
- ProcessIndexUpdatesAsync
- IncrementColumnUsage
- GetColumnUsage
- TrackAllColumnsUsage
- TrackColumnUsage
- IndexUpdate record
- IndexMetadata record (NEW)
- IndexManager class

### 5. `Table.Scanning.cs` - ~100 lines
- ScanRowsWithSimd
- CompareRows
- FindPatternInRowData

## Benefits of This Split

1. **Easier Navigation** - Each file has clear responsibility
2. **Safer Editing** - Smaller files = less chance of corruption
3. **Parallel Work** - Different features in different files
4. **Better Organization** - Related methods grouped together
5. **Lazy Loading Implementation** - Entire feature in one file (Table.Indexing.cs)

## Implementation Order

1. **Create** `Table.CRUD.cs` - Move CRUD methods
2. **Create** `Table.Serialization.cs` - Move serialization
3. **Create** `Table.Scanning.cs` - Move scanning methods
4. **Create** `Table.Indexing.cs` - Move existing index methods + add lazy loading
5. **Update** `Table.cs` - Keep only core properties and constructors
6. **Build & Test** - Verify all partials compile together
7. **Implement Lazy Loading** - Add to `Table.Indexing.cs`

## File Size After Split

| File | Lines | Purpose |
|------|-------|---------|
| Table.cs | ~150 | Core properties, constructors |
| Table.CRUD.cs | ~300 | CRUD operations |
| Table.Serialization.cs | ~350 | Type serialization |
| Table.Scanning.cs | ~100 | Query scanning |
| Table.Indexing.cs | ~250 | Index management + lazy loading |
| **Total** | ~1150 | (100 lines added for lazy loading) |

## Next Steps

1. Start with creating partial classes
2. Move methods systematically
3. Build after each file to catch errors early
4. Once split is complete and stable, add lazy loading to Table.Indexing.cs
