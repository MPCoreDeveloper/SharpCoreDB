using SharpCoreDB.DataStructures;
using SharpCoreDB.Interfaces;

namespace SharpCoreDB.Services;

/// <summary>
/// Extensions to SqlParser for advanced SQL features.
/// </summary>
public static class SqlParserExtensions
{
    /// <summary>
    /// Executes UPSERT operations (INSERT OR REPLACE).
    /// </summary>
    public static void ExecuteUpsert(
        Dictionary<string, ITable> tables,
        string sql,
        string dbPath,
        IStorage storage,
        bool isReadOnly,
        IWAL? wal)
    {
        if (isReadOnly)
            throw new InvalidOperationException("Cannot upsert in readonly mode");

        // Parse INSERT OR REPLACE or INSERT ON CONFLICT
        var isInsertOrReplace = sql.Contains("INSERT OR REPLACE", StringComparison.OrdinalIgnoreCase);
        var isOnConflict = sql.Contains("ON CONFLICT", StringComparison.OrdinalIgnoreCase);

        if (!isInsertOrReplace && !isOnConflict)
            return;

        // Extract table name and values (simplified parsing)
        var tableName = ExtractTableName(sql);
        var values = ExtractValues(sql);
        var columns = ExtractColumns(sql);

        if (!tables.ContainsKey(tableName))
            throw new InvalidOperationException($"Table {tableName} does not exist");

        var table = tables[tableName];
        
        // For UPSERT, we need to check if row exists and update or insert
        // This is a simplified implementation
        var row = new Dictionary<string, object>();
        
        if (columns != null && columns.Count > 0)
        {
            for (int i = 0; i < columns.Count && i < values.Count; i++)
            {
                row[columns[i]] = values[i];
            }
        }

        // In a real implementation, we would check for primary key conflicts
        // and either update or insert accordingly
        table.Insert(row);
        wal?.Log(sql);
    }

    /// <summary>
    /// Executes CREATE INDEX statement.
    /// </summary>
    public static DatabaseIndex ExecuteCreateIndex(
        string sql,
        Dictionary<string, ITable> tables,
        Dictionary<string, DatabaseIndex> indexes,
        bool isReadOnly,
        IWAL? wal)
    {
        if (isReadOnly)
            throw new InvalidOperationException("Cannot create index in readonly mode");

        // Parse: CREATE INDEX idx_name ON table_name (column_name)
        var parts = sql.Split(new[] { ' ', '(', ')' }, StringSplitOptions.RemoveEmptyEntries);
        
        var indexIdx = Array.FindIndex(parts, p => p.Equals("INDEX", StringComparison.OrdinalIgnoreCase));
        var onIdx = Array.FindIndex(parts, p => p.Equals("ON", StringComparison.OrdinalIgnoreCase));
        
        if (indexIdx < 0 || onIdx < 0)
            throw new ArgumentException("Invalid CREATE INDEX syntax");

        var indexName = parts[indexIdx + 1];
        var tableName = parts[onIdx + 1];
        var columnName = parts[onIdx + 2];

        if (!tables.ContainsKey(tableName))
            throw new InvalidOperationException($"Table {tableName} does not exist");

        var isUnique = sql.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase);
        var index = new DatabaseIndex(indexName, tableName, columnName, isUnique);
        
        // Build index from existing data
        // Note: This would need access to table rows
        indexes[indexName] = index;
        wal?.Log(sql);

        return index;
    }

    /// <summary>
    /// Executes PRAGMA commands.
    /// </summary>
    public static void ExecutePragma(
        string sql,
        Dictionary<string, ITable> tables,
        Dictionary<string, DatabaseIndex> indexes)
    {
        var parts = sql.Split(new[] { ' ', '(', ')' }, StringSplitOptions.RemoveEmptyEntries);
        
        if (parts.Length < 2)
            return;

        var pragmaCommand = parts[1].ToLowerInvariant();

        switch (pragmaCommand)
        {
            case "table_info":
                if (parts.Length >= 3)
                {
                    var tableName = parts[2];
                    ShowTableInfo(tables, tableName);
                }
                break;

            case "index_list":
                if (parts.Length >= 3)
                {
                    var tableName = parts[2];
                    ShowIndexList(indexes, tableName);
                }
                break;

            case "foreign_key_list":
                if (parts.Length >= 3)
                {
                    var tableName = parts[2];
                    ShowForeignKeyList(tableName);
                }
                break;

            default:
                Console.WriteLine($"PRAGMA {pragmaCommand} not implemented");
                break;
        }
    }

    /// <summary>
    /// Generates EXPLAIN query plan output.
    /// </summary>
    public static void ExecuteExplain(string sql, Dictionary<string, DatabaseIndex> indexes)
    {
        Console.WriteLine("QUERY PLAN");
        Console.WriteLine("==========");
        
        // Simplified query plan analysis
        if (sql.Contains("WHERE", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("SEARCH with WHERE clause");
            
            // Check if an index can be used
            var hasIndex = indexes.Values.Any(idx => 
                sql.Contains(idx.ColumnName, StringComparison.OrdinalIgnoreCase));
            
            if (hasIndex)
            {
                var index = indexes.Values.First(idx => 
                    sql.Contains(idx.ColumnName, StringComparison.OrdinalIgnoreCase));
                Console.WriteLine($"Using INDEX {index.Name} on {index.ColumnName}");
            }
            else
            {
                Console.WriteLine("SCAN (no index available)");
            }
        }
        else
        {
            Console.WriteLine("SCAN (full table scan)");
        }
    }

    private static void ShowTableInfo(Dictionary<string, ITable> tables, string tableName)
    {
        if (!tables.ContainsKey(tableName))
        {
            Console.WriteLine($"Table {tableName} not found");
            return;
        }

        var table = tables[tableName];
        Console.WriteLine($"Table: {tableName}");
        Console.WriteLine("Columns:");
        
        for (int i = 0; i < table.Columns.Count; i++)
        {
            var col = table.Columns[i];
            var type = table.ColumnTypes[i];
            var isPk = table.PrimaryKeyIndex == i ? " PRIMARY KEY" : "";
            Console.WriteLine($"  {i}: {col} {type}{isPk}");
        }
    }

    private static void ShowIndexList(Dictionary<string, DatabaseIndex> indexes, string tableName)
    {
        Console.WriteLine($"Indexes on table: {tableName}");
        
        var tableIndexes = indexes.Values.Where(idx => idx.TableName == tableName).ToList();
        
        if (tableIndexes.Count == 0)
        {
            Console.WriteLine("  (no indexes)");
            return;
        }

        foreach (var index in tableIndexes)
        {
            var unique = index.IsUnique ? "UNIQUE" : "";
            Console.WriteLine($"  {index.Name} on {index.ColumnName} {unique}");
        }
    }

    private static void ShowForeignKeyList(string tableName)
    {
        // Foreign keys not yet implemented
        Console.WriteLine($"Foreign keys on table: {tableName}");
        Console.WriteLine("  (not implemented)");
    }

    private static string ExtractTableName(string sql)
    {
        var parts = sql.Split(new[] { ' ', '(' }, StringSplitOptions.RemoveEmptyEntries);
        var intoIdx = Array.FindIndex(parts, p => p.Equals("INTO", StringComparison.OrdinalIgnoreCase));
        return intoIdx >= 0 && intoIdx + 1 < parts.Length ? parts[intoIdx + 1] : "";
    }

    private static List<string>? ExtractColumns(string sql)
    {
        var startIdx = sql.IndexOf('(');
        var endIdx = sql.IndexOf(')');
        
        if (startIdx < 0 || endIdx < 0 || startIdx > endIdx)
            return null;

        var columnsStr = sql.Substring(startIdx + 1, endIdx - startIdx - 1);
        var hasValues = sql.IndexOf("VALUES", StringComparison.OrdinalIgnoreCase);
        
        if (hasValues > 0 && hasValues < endIdx)
            return null;

        return columnsStr.Split(',')
            .Select(c => c.Trim())
            .Where(c => !string.IsNullOrEmpty(c))
            .ToList();
    }

    private static List<object> ExtractValues(string sql)
    {
        var valuesIdx = sql.IndexOf("VALUES", StringComparison.OrdinalIgnoreCase);
        if (valuesIdx < 0)
            return new List<object>();

        var remaining = sql.Substring(valuesIdx + 6).Trim();
        var startIdx = remaining.IndexOf('(');
        var endIdx = remaining.LastIndexOf(')');
        
        if (startIdx < 0 || endIdx < 0)
            return new List<object>();

        var valuesStr = remaining.Substring(startIdx + 1, endIdx - startIdx - 1);
        
        return valuesStr.Split(',')
            .Select(v => v.Trim().Trim('\'') as object)
            .ToList();
    }
}
