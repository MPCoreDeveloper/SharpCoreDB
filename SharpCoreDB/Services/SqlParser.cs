using SharpCoreDB.Interfaces;
using SharpCoreDB.DataStructures;
using SharpCoreDB;
using SharpCoreDB.Constants;

namespace SharpCoreDB.Services;

/// <summary>
/// Simple SQL parser and executor.
/// </summary>
public class SqlParser : ISqlParser
{
    private readonly Dictionary<string, ITable> _tables;
    private readonly IWAL _wal;
    private readonly string _dbPath;
    private readonly IStorage _storage;
    private readonly bool _isReadOnly;
    private readonly QueryCache? _queryCache;

    /// <summary>
    /// Simple SQL parser and executor.
    /// </summary>
    public SqlParser(Dictionary<string, ITable> tables, IWAL wal, string dbPath, IStorage storage, bool isReadOnly = false, QueryCache? queryCache = null)
    {
        _tables = tables;
        _wal = wal;
        _dbPath = dbPath;
        _storage = storage;
        _isReadOnly = isReadOnly;
        _queryCache = queryCache;
    }

    /// <inheritdoc />
    public void Execute(string sql, IWAL? wal = null)
    {
        // Use query cache for parsed SQL parts if available
        string[] parts;
        if (_queryCache != null)
        {
            var cached = _queryCache.GetOrAdd(sql, s => new QueryCache.CachedQuery
            {
                Sql = s,
                Parts = s.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries),
                CachedAt = DateTime.UtcNow,
                AccessCount = 1
            });
            parts = cached.Parts;
        }
        else
        {
            parts = sql.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        }
        if (parts[0].ToUpper() == SqlConstants.CREATE && parts[1].ToUpper() == SqlConstants.TABLE)
        {
            if (_isReadOnly) throw new InvalidOperationException("Cannot create table in readonly mode");
            var tableName = parts[2];
            var colsStart = sql.IndexOf('(');
            var colsEnd = sql.LastIndexOf(')');
            var colsStr = sql.Substring(colsStart + 1, colsEnd - colsStart - 1);
            var colDefs = colsStr.Split(',').Select(c => c.Trim()).ToList();
            var columns = new List<string>();
            var columnTypes = new List<DataType>();
            var isAuto = new List<bool>();
            var primaryKeyIndex = -1;
            for (int i = 0; i < colDefs.Count; i++)
            {
                var def = colDefs[i];
                var partsDef = def.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var colName = partsDef[0];
                var typeStr = partsDef[1].ToUpper();
                var isPrimary = def.Contains(SqlConstants.PRIMARY_KEY);
                var isAutoGen = def.Contains("AUTO");
                columns.Add(colName);
                columnTypes.Add(typeStr switch
                {
                    "INTEGER" => DataType.Integer,
                    "TEXT" => DataType.String,
                    "REAL" => DataType.Real,
                    "BLOB" => DataType.Blob,
                    "BOOLEAN" => DataType.Boolean,
                    "DATETIME" => DataType.DateTime,
                    "LONG" => DataType.Long,
                    "DECIMAL" => DataType.Decimal,
                    "ULID" => DataType.Ulid,
                    "GUID" => DataType.Guid,
                    _ => DataType.String
                });
                isAuto.Add(isAutoGen);
                if (isPrimary) primaryKeyIndex = i;
            }
            var table = new Table(_storage, _isReadOnly)
            {
                Name = tableName,
                Columns = columns,
                ColumnTypes = columnTypes,
                IsAuto = isAuto,
                PrimaryKeyIndex = primaryKeyIndex,
                DataFile = Path.Combine(_dbPath, tableName + PersistenceConstants.TableFileExtension)
            };
            _tables[tableName] = table;
            wal?.Log(sql);
        }
        else if (parts[0].ToUpper() == SqlConstants.CREATE && parts[1].ToUpper() == "INDEX")
        {
            if (_isReadOnly) throw new InvalidOperationException("Cannot create index in readonly mode");
            // CREATE INDEX idx_name ON table_name (column_name)
            // or CREATE UNIQUE INDEX idx_name ON table_name (column_name)
            
            var indexNameIdx = parts[1].ToUpper() == "UNIQUE" ? 3 : 2;
            var onIdx = Array.IndexOf(parts.Select(p => p.ToUpper()).ToArray(), "ON");
            if (onIdx < 0)
                throw new InvalidOperationException("CREATE INDEX requires ON clause");
            
            var tableName = parts[onIdx + 1];
            if (!_tables.ContainsKey(tableName))
                throw new InvalidOperationException($"Table {tableName} does not exist");
            
            // Extract column name from parentheses
            var columnStart = sql.IndexOf('(');
            var columnEnd = sql.IndexOf(')');
            if (columnStart < 0 || columnEnd < 0)
                throw new InvalidOperationException("CREATE INDEX requires column name in parentheses");
            
            var columnName = sql.Substring(columnStart + 1, columnEnd - columnStart - 1).Trim();
            
            // Create hash index on the table
            _tables[tableName].CreateHashIndex(columnName);
            wal?.Log(sql);
        }
        else if (parts[0].ToUpper() == SqlConstants.INSERT && parts[1].ToUpper() == SqlConstants.INTO)
        {
            if (_isReadOnly) throw new InvalidOperationException("Cannot insert in readonly mode");
            var insertSql = sql[sql.IndexOf("INSERT INTO")..];
            var tableStart = "INSERT INTO ".Length;
            var tableEnd = insertSql.IndexOf(' ', tableStart);
            if (tableEnd == -1) tableEnd = insertSql.IndexOf('(', tableStart);
            var tableName = insertSql[tableStart..tableEnd].Trim();
            var rest = insertSql[tableEnd..];
            List<string> insertColumns = null;
            if (rest.TrimStart().StartsWith("("))
            {
                var colStart = rest.IndexOf('(') + 1;
                var colEnd = rest.IndexOf(')', colStart);
                var colStr = rest[colStart..colEnd];
                insertColumns = colStr.Split(',').Select(c => c.Trim()).ToList();
                rest = rest[(colEnd + 1)..];
            }
            var valuesStart = rest.IndexOf(SqlConstants.VALUES) + SqlConstants.VALUES.Length;
            var valuesStr = rest[valuesStart..].Trim().TrimStart('(').TrimEnd(')');
            var values = valuesStr.Split(',').Select(v => v.Trim().Trim('\'')).ToList();
            var row = new Dictionary<string, object>();
            if (insertColumns == null)
            {
                // All columns
                for (int i = 0; i < _tables[tableName].Columns.Count; i++)
                {
                    var col = _tables[tableName].Columns[i];
                    var type = _tables[tableName].ColumnTypes[i];
                    row[col] = ParseValue(values[i], type);
                }
            }
            else
            {
                // Specified columns
                for (int i = 0; i < insertColumns.Count; i++)
                {
                    var col = insertColumns[i];
                    var idx = _tables[tableName].Columns.IndexOf(col);
                    var type = _tables[tableName].ColumnTypes[idx];
                    row[col] = ParseValue(values[i], type);
                }
                // For auto columns not specified
                for (int i = 0; i < _tables[tableName].Columns.Count; i++)
                {
                    var col = _tables[tableName].Columns[i];
                    if (!row.ContainsKey(col) && _tables[tableName].IsAuto[i])
                    {
                        row[col] = GenerateAutoValue(_tables[tableName].ColumnTypes[i]);
                    }
                }
            }
            _tables[tableName].Insert(row);
            wal?.Log(sql);
        }
        else if (parts[0].ToUpper() == SqlConstants.SELECT)
        {
            var fromIdx = Array.IndexOf(parts, SqlConstants.FROM);
            string[] keywords = ["WHERE", "ORDER"];
            var fromParts = parts.Skip(fromIdx + 1).TakeWhile(p => !keywords.Contains(p.ToUpper())).ToArray();
            var whereIdx = Array.IndexOf(parts, SqlConstants.WHERE);
            var orderIdx = Array.IndexOf(parts, SqlConstants.ORDER);
            string? whereStr = null;
            if (whereIdx > 0)
            {
                var endIdx = orderIdx > 0 ? orderIdx : parts.Length;
                whereStr = string.Join(" ", parts.Skip(whereIdx + 1).Take(endIdx - whereIdx - 1));
            }
            string? orderBy = null;
            bool asc = true;
            if (orderIdx > 0 && parts.Length > orderIdx + 3 && parts[orderIdx + 1].ToUpper() == SqlConstants.BY)
            {
                orderBy = parts[orderIdx + 2];
                asc = parts[orderIdx + 3].ToUpper() != SqlConstants.DESC;
            }
            if (fromParts.Any(p => p.ToUpper() == "JOIN"))
            {
                // Parse JOIN
                var table1 = fromParts[0];
                var joinType = fromParts.Contains("LEFT") ? "LEFT" : "INNER";
                var joinIdx = Array.IndexOf(fromParts, joinType == "LEFT" ? "LEFT" : "JOIN");
                if (joinType == "LEFT") joinIdx = Array.IndexOf(fromParts, "JOIN");
                var table2 = fromParts[joinIdx + 1];
                var onIdx = Array.IndexOf(fromParts, "ON");
                var onStr = string.Join(" ", fromParts.Skip(onIdx + 1));
                // Assume ON t1.col = t2.col
                var onParts = onStr.Split('=');
                var left = onParts[0].Trim().Split('.')[1];
                var right = onParts[1].Trim().Split('.')[1];
                var rows1 = _tables[table1].Select();
                var rows2 = _tables[table2].Select();
                // Create dict for fast lookup
                var dict2 = new Dictionary<object, List<Dictionary<string, object>>>();
                foreach (var r2 in rows2)
                {
                    var key = r2[right];
                    if (!dict2.ContainsKey(key ?? new object())) dict2[key ?? new object()] = new List<Dictionary<string, object>>();
                    dict2[key ?? new object()].Add(r2);
                }
                var results = new List<Dictionary<string, object>>();
                foreach (var r1 in rows1)
                {
                    var key = r1[left];
                    if (dict2.ContainsKey(key ?? new object()))
                    {
                        foreach (var r2 in dict2[key ?? new object()])
                        {
                            var combined = new Dictionary<string, object>();
                            foreach (var kv in r1) combined[table1 + "." + kv.Key] = kv.Value;
                            foreach (var kv in r2) combined[table2 + "." + kv.Key] = kv.Value;
                            results.Add(combined);
                        }
                    }
                    else if (joinType == "LEFT")
                    {
                        var combined = new Dictionary<string, object>();
                        foreach (var kv in r1) combined[table1 + "." + kv.Key] = kv.Value;
                        // Null for table2
                        foreach (var col in _tables[table2].Columns)
                        {
                            combined[table2 + "." + col] = null;
                        }
                        results.Add(combined);
                    }
                }
                // Apply where
                if (!string.IsNullOrEmpty(whereStr))
                {
                    results = results.Where(r => EvaluateJoinWhere(r, whereStr)).ToList();
                }
                // Order
                if (orderBy != null)
                {
                    var key = results.FirstOrDefault()?.Keys.FirstOrDefault(k => k.Contains(orderBy));
                    if (key != null)
                    {
                        results = asc ? [.. results.OrderBy(r => r[key])] : [.. results.OrderByDescending(r => r[key])];
                    }
                }
                foreach (var row in results)
                {
                    Console.WriteLine(string.Join(", ", row.Select(kv => $"{kv.Key}: {kv.Value ?? "NULL"}")));
                }
            }
            else
            {
                var tableName = fromParts[0];
                var results = _tables[tableName].Select(whereStr, orderBy, asc);
                foreach (var row in results)
                {
                    Console.WriteLine(string.Join(", ", row.Select(kv => $"{kv.Key}: {kv.Value ?? "NULL"}")));
                }
            }
        }
        else if (parts[0].ToUpper() == "UPDATE")
        {
            var tableName = parts[1];
            var setIdx = Array.IndexOf(parts, "SET");
            var whereIdx = Array.IndexOf(parts, "WHERE");
            var setStr = string.Join(" ", parts.Skip(setIdx + 1).Take(whereIdx - setIdx - 1));
            var whereStr = whereIdx > 0 ? string.Join(" ", parts.Skip(whereIdx + 1)) : null;
            var sets = setStr.Split(',').Select(s => s.Trim()).ToDictionary(s => s.Split('=')[0].Trim(), s => s.Split('=')[1].Trim().Trim('\''));
            var updates = new Dictionary<string, object>();
            foreach (var set in sets)
            {
                var col = set.Key;
                var idx = _tables[tableName].Columns.IndexOf(col);
                var type = _tables[tableName].ColumnTypes[idx];
                updates[col] = ParseValue(set.Value, type);
            }
            _tables[tableName].Update(whereStr, updates);
            wal?.Log(sql);
        }
        else if (parts[0].ToUpper() == "DELETE" && parts[1].ToUpper() == "FROM")
        {
            var tableName = parts[2];
            var whereIdx = Array.IndexOf(parts, "WHERE");
            var whereStr = whereIdx > 0 ? string.Join(" ", parts.Skip(whereIdx + 1)) : null;
            _tables[tableName].Delete(whereStr);
            wal?.Log(sql);
        }
    }

    private object ParseValue(string val, DataType type)
    {
        if (val == "NULL") return null;
        try
        {
            return type switch
            {
                DataType.Integer => int.Parse(val),
                DataType.String => val,
                DataType.Real => double.Parse(val),
                DataType.Blob => Convert.FromBase64String(val),
                DataType.Boolean => bool.Parse(val),
                DataType.DateTime => DateTime.Parse(val),
                DataType.Long => long.Parse(val),
                DataType.Decimal => decimal.Parse(val),
                DataType.Ulid => Ulid.Parse(val),
                DataType.Guid => Guid.Parse(val),
                _ => val
            };
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Invalid value '{val}' for data type {type}: {ex.Message}");
        }
    }

    private object GenerateAutoValue(DataType type) => type switch
    {
        DataType.Ulid => Ulid.NewUlid(),
        DataType.Guid => Guid.NewGuid(),
        _ => throw new InvalidOperationException($"Auto generation not supported for type {type}")
    };

    private bool EvaluateJoinWhere(Dictionary<string, object> row, string where)
    {
        if (string.IsNullOrEmpty(where)) return true;
        var parts = where.Split(' ');
        if (parts.Length == 3 && parts[1] == "=")
        {
            var col = parts[0];
            var val = parts[2].Trim('\'');
            return row[col]?.ToString() == val;
        }
        return true;
    }
}
