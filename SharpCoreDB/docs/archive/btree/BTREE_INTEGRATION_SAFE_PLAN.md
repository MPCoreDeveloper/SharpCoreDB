# B-Tree Index Integration - Implementation Plan

## Status: READY TO IMPLEMENT

### Probleem
De B-tree `RangeScan()` is perfect geoptimaliseerd, maar wordt NOOIT gebruikt tijdens SELECT queries.

### Root Causes
1. ❌ `_btreeIndexes` dictionary bestaat NIET in `Table` class
2. ❌ `CreateBTreeIndex()` / `HasBTreeIndex()` methodes ontbreken
3. ❌ `SelectInternal()` controleert nooit op B-tree index
4. ❌ `TryParseRangeWhereClause()` helper ontbreekt

### Implementatie Volgorde

#### Stap 1: Voeg B-tree Storage Toe aan Table.Indexing.cs
```csharp
public partial class Table
{
    // NIEUW: B-tree index storage (naast bestaande hashIndexes)
    private readonly Dictionary<string, object> _btreeIndexes = new();
    private readonly Dictionary<string, DataType> _btreeIndexTypes = new();
    
    public void CreateBTreeIndex(string columnName)
    {
        if (!Columns.Contains(columnName))
            throw new InvalidOperationException($"Column {columnName} not found");
        
        rwLock.EnterWriteLock();
        try
        {
            if (_btreeIndexes.ContainsKey(columnName))
                return; // Already exists
            
            var colIdx = Columns.IndexOf(columnName);
            var colType = ColumnTypes[colIdx];
            
            // Store type for later instantiation
            _btreeIndexTypes[columnName] = colType;
            
            // Create B-tree index (empty for now, will be built lazily)
            var indexType = GetBTreeIndexType(colType);
            var index = Activator.CreateInstance(indexType);
            _btreeIndexes[columnName] = index!;
        }
        finally
        {
            rwLock.ExitWriteLock();
        }
    }
    
    public void CreateBTreeIndex(string indexName, string columnName)
    {
        // Wrapper for named indexes
        CreateBTreeIndex(columnName);
        indexNameToColumn[indexName] = columnName;
    }
    
    public bool HasBTreeIndex(string columnName)
    {
        return _btreeIndexes.ContainsKey(columnName);
    }
    
    private bool RemoveBTreeIndex(string columnName)
    {
        rwLock.EnterWriteLock();
        try
        {
            bool removed = _btreeIndexes.Remove(columnName);
            _btreeIndexTypes.Remove(columnName);
            return removed;
        }
        finally
        {
            rwLock.ExitWriteLock();
        }
    }
    
    private static Type GetBTreeIndexType(DataType colType)
    {
        return colType switch
        {
            DataType.Integer => typeof(BTreeIndex<int>),
            DataType.Long => typeof(BTreeIndex<long>),
            DataType.Real => typeof(BTreeIndex<double>),
            DataType.Decimal => typeof(BTreeIndex<decimal>),
            DataType.String => typeof(BTreeIndex<string>),
            DataType.DateTime => typeof(BTreeIndex<DateTime>),
            _ => throw new NotSupportedException($"B-tree not supported for type {colType}")
        };
    }
}
```

#### Stap 2: Voeg Helper Methods toe aan Table.QueryHelpers.cs
```csharp
public partial class Table
{
    /// <summary>
    /// Parses range WHERE clauses like "age > 30", "salary BETWEEN 50000 AND 100000"
    /// </summary>
    private static bool TryParseRangeWhereClause(
        string where, 
        out string column, 
        out string rangeStart, 
        out string rangeEnd)
    {
        column = string.Empty;
        rangeStart = string.Empty;
        rangeEnd = string.Empty;
        
        if (string.IsNullOrWhiteSpace(where))
            return false;
        
        where = where.Trim();
        
        // Handle: column > value
        if (where.Contains('>') && !where.Contains('='))
        {
            var parts = where.Split('>');
            if (parts.Length == 2)
            {
                column = parts[0].Trim();
                rangeStart = parts[1].Trim().Trim('\'', '"');
                rangeEnd = GetMaxValueForType(column); // Use MAX
                return true;
            }
        }
        
        // Handle: column >= value
        if (where.Contains(">="))
        {
            var parts = where.Split(new[] { ">=" }, StringSplitOptions.None);
            if (parts.Length == 2)
            {
                column = parts[0].Trim();
                rangeStart = parts[1].Trim().Trim('\'', '"');
                rangeEnd = GetMaxValueForType(column);
                return true;
            }
        }
        
        // Handle: column < value
        if (where.Contains('<') && !where.Contains('='))
        {
            var parts = where.Split('<');
            if (parts.Length == 2)
            {
                column = parts[0].Trim();
                rangeStart = GetMinValueForType(column); // Use MIN
                rangeEnd = parts[1].Trim().Trim('\'', '"');
                return true;
            }
        }
        
        // Handle: column BETWEEN x AND y
        if (where.Contains("BETWEEN", StringComparison.OrdinalIgnoreCase))
        {
            var parts = where.Split(new[] { "BETWEEN", "AND" }, 
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 3)
            {
                column = parts[0];
                rangeStart = parts[1].Trim('\'', '"');
                rangeEnd = parts[2].Trim('\'', '"');
                return true;
            }
        }
        
        return false;
    }
    
    private static string GetMaxValueForType(string column)
    {
        return "9999999999"; // Large number for numeric types
    }
    
    private static string GetMinValueForType(string column)
    {
        return "0";
    }
    
    private static object? ParseValueForBTreeLookup(string value, DataType type)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        
        try
        {
            return type switch
            {
                DataType.Integer => int.Parse(value),
                DataType.Long => long.Parse(value),
                DataType.Real => double.Parse(value),
                DataType.Decimal => decimal.Parse(value),
                DataType.String => value,
                DataType.DateTime => DateTime.Parse(value),
                _ => value
            };
        }
        catch
        {
            return null;
        }
    }
}
```

#### Stap 3: GEEN CHANGES aan Table.CRUD.cs
**BELANGRIJK**: We gaan Table.CRUD.cs NIET meer editen omdat het te groot is en eerdere pogingen mislukten.

In plaats daarvan maken we een **NIEUW BESTAND**: `Table.BTreeSupport.cs`

```csharp
// Table.BTreeSupport.cs - NIEUW BESTAND
namespace SharpCoreDB.DataStructures;

public partial class Table
{
    /// <summary>
    /// Tries to use B-tree index for range query optimization.
    /// Returns null if B-tree cannot be used, otherwise returns results.
    /// </summary>
    private List<Dictionary<string, object>>? TryBTreeRangeScan(
        string where, 
        string? orderBy, 
        bool asc)
    {
        if (string.IsNullOrEmpty(where))
            return null;
        
        // Try to parse as range query
        if (!TryParseRangeWhereClause(where, out var col, out var start, out var end))
            return null;
        
        // Check if B-tree index exists
        if (!HasBTreeIndex(col))
            return null;
        
        var colIdx = Columns.IndexOf(col);
        if (colIdx < 0)
            return null;
        
        try
        {
            var results = new List<Dictionary<string, object>>();
            var engine = GetOrCreateStorageEngine();
            var colType = ColumnTypes[colIdx];
            
            // Get B-tree index (dynamically typed)
            var btreeIndex = _btreeIndexes[col];
            
            // Parse range values
            var startKey = ParseValueForBTreeLookup(start, colType);
            var endKey = ParseValueForBTreeLookup(end, colType);
            
            if (startKey == null || endKey == null)
                return null;
            
            // Call FindRange via reflection (since type is dynamic)
            var findRangeMethod = btreeIndex.GetType().GetMethod("FindRange");
            if (findRangeMethod == null)
                return null;
            
            var positions = (IEnumerable<long>)findRangeMethod.Invoke(
                btreeIndex, new[] { startKey, endKey })!;
            
            // Read rows from positions
            foreach (var pos in positions)
            {
                var data = engine.Read(Name, pos);
                if (data != null)
                {
                    var row = DeserializeRow(data);
                    if (row != null && IsCurrentVersion(row, pos))
                    {
                        results.Add(row);
                    }
                }
            }
            
            // Remove duplicates and apply ordering
            results = DeduplicateByPrimaryKey(results);
            return ApplyOrdering(results, orderBy, asc);
        }
        catch
        {
            // If B-tree fails, return null to fall back to full scan
            return null;
        }
    }
    
    private bool IsCurrentVersion(Dictionary<string, object> row, long position)
    {
        if (PrimaryKeyIndex < 0)
            return true;
        
        var pkCol = Columns[PrimaryKeyIndex];
        if (!row.TryGetValue(pkCol, out var pkVal) || pkVal == null)
            return true;
        
        var pkStr = pkVal.ToString() ?? string.Empty;
        var search = Index.Search(pkStr);
        return search.Found && search.Value == position;
    }
}
```

### Testing
Na implementatie testen met:
```csharp
db.Execute("CREATE TABLE users (id INT, age INT)");
db.Execute("CREATE INDEX idx_age ON users(age) USING BTREE");

for (int i = 0; i < 10000; i++)
    db.Execute($"INSERT INTO users VALUES ({i}, {20 + i % 50})");

var sw = Stopwatch.StartNew();
var results = db.ExecuteQuery("SELECT * FROM users WHERE age > 30");
sw.Stop();

Console.WriteLine($"Time: {sw.ElapsedMilliseconds}ms"); // Expected: ~10ms
```

### Verwachte Resultaten
- Voor fix: 28ms (full scan + index overhead)
- Na fix: ~10ms (O(log n + k) range scan)
- Speedup: 2.8x ✅

### Kritieke Waarschuwingen
1. ❌ **NIET** `Table.CRUD.cs` direkt editen - te groot, breaks partial class
2. ✅ **WEL** nieuwe partial class file maken (`Table.BTreeSupport.cs`)
3. ✅ Gebruik reflection voor dynamische B-tree type handling
4. ✅ Fail gracefully - return null om terug te vallen op full scan

### Volgorde van Implementatie
1. ✅ Maak `Table.BTreeSupport.cs` (nieuw bestand)
2. ✅ Voeg B-tree storage toe aan `Table.Indexing.cs`
3. ✅ Voeg helpers toe aan `Table.QueryHelpers.cs`
4. ✅ Update `ClearAllIndexes()` in `Table.Indexing.cs` om ook B-tree te clearen
5. ✅ Test met benchmark

**STATUS**: Ready to implement safely without breaking existing code!
