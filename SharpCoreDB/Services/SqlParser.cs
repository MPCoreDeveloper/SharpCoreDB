// <copyright file="SqlParser.cs" company="MPCoreDeveloper">
// Copyright (c) 2024-2025 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Services;

using SharpCoreDB;
using SharpCoreDB.Constants;
using SharpCoreDB.DataStructures;
using SharpCoreDB.Interfaces;

/// <summary>
/// Simple SQL parser and executor.
// 
/// SECURITY WARNING: This parser has basic SQL injection protections but is NOT fully safe.
/// Always use parameterized queries for untrusted input. Never use string concatenation or interpolation.
/// 
/// SAFE PATTERNS:
///   - ExecuteSQL(sql, parameters) with Dictionary parameters
///   - Use @paramName or ? placeholders in SQL
/// 
/// UNSAFE PATTERNS (DO NOT USE):
///   - String interpolation: $"SELECT * FROM users WHERE name = '{userName}'"
///   - String concatenation: "DELETE FROM users WHERE id = " + userId
/// 
/// See SECURITY.md for detailed security guidelines.
/// 
/// ENHANCED PARSER:
/// This parser now includes enhanced parsing capabilities through EnhancedSqlParser:
///   - Support for RIGHT JOIN, FULL OUTER JOIN
///   - Subqueries in FROM and WHERE clauses
///   - Advanced error recovery
///   - Multiple SQL dialect support
/// Use ParseWithEnhancedParser() for complex queries.
/// </summary>
public class SqlParser : ISqlParser
{
    private readonly Dictionary<string, ITable> tables;
    private readonly IWAL wal;
    private readonly string dbPath;
    private readonly IStorage storage;
    private readonly bool isReadOnly;
    private readonly QueryCache? queryCache;
    private readonly bool noEncrypt;
    private readonly EnhancedSqlParser? enhancedParser;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqlParser"/> class.
    /// Simple SQL parser and executor.
    /// </summary>
    public SqlParser(Dictionary<string, ITable> tables, IWAL wal, string dbPath, IStorage storage, bool isReadOnly = false, QueryCache? queryCache = null, bool noEncrypt = false)
    {
        this.tables = tables;
        this.wal = wal;
        this.dbPath = dbPath;
        this.storage = storage;
        this.isReadOnly = isReadOnly;
        this.queryCache = queryCache;
        this.noEncrypt = noEncrypt;
    }

    /// <inheritdoc />
    public void Execute(string sql, IWAL? wal = null)
    {
        this.Execute(sql, null, wal);
    }

    /// <inheritdoc />
    public void Execute(string sql, Dictionary<string, object?> parameters, IWAL? wal = null)
    {
        string? originalSql = null;
        if (parameters != null && parameters.Count > 0)
        {
            originalSql = sql;
            sql = this.BindParameters(sql, parameters);
        }
        else
        {
            // SECURITY WARNING: Fallback to string interpolation is UNSAFE
            // This warning alerts developers to use parameterized queries
            Console.WriteLine("⚠️  SECURITY WARNING: Executing SQL without parameters. This is unsafe for untrusted input. Use parameterized queries.");
            // Sanitize inputs automatically (basic sanitization - NOT SUFFICIENT for security)
            sql = this.SanitizeSql(sql);
        }

        // Use query cache if available
        string cacheKey = originalSql ?? sql;
        string[] parts;
        
        if (this.queryCache != null)
        {
            var cached = this.queryCache.GetOrAdd(cacheKey, key =>
            {
                var parsedParts = sql.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                return new QueryCache.CachedQuery
                {
                    Sql = key,
                    Parts = parsedParts,
                    CachedAt = DateTime.UtcNow
                };
            });
            parts = cached.Parts;
        }
        else
        {
            parts = sql.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        }
        
        this.ExecuteInternal(sql, parts, wal, cacheKey);
    }

    /// <summary>
    /// Executes a prepared statement with the cached query plan.
    /// </summary>
    /// <param name="plan">The cached query plan.</param>
    /// <param name="parameters">The parameters to bind.</param>
    /// <param name="wal">The WAL instance.</param>
    public void Execute(CachedQueryPlan plan, Dictionary<string, object?> parameters, IWAL? wal = null)
    {
        var sql = plan.Sql;
        if (parameters != null && parameters.Count > 0)
        {
            sql = this.BindParameters(sql, parameters);
        }
        else
        {
            sql = this.SanitizeSql(sql);
        }

        this.ExecuteInternal(sql, plan.Parts, wal, plan.Sql);
    }

    /// <summary>
    /// Executes a query and returns the results.
    /// </summary>
    /// <param name="sql">The SQL query.</param>
    /// <param name="parameters">The parameters.</param>
    /// <returns>The query results.</returns>
    public List<Dictionary<string, object>> ExecuteQuery(string sql, Dictionary<string, object?> parameters = null)
    {
        if (parameters != null && parameters.Count > 0)
        {
            sql = this.BindParameters(sql, parameters);
        }
        else
        {
            sql = this.SanitizeSql(sql);
        }

        var parts = sql.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return this.ExecuteQueryInternal(sql, parts);
    }

    /// <summary>
    /// Executes a query and returns the results with optional encryption bypass.
    /// </summary>
    /// <param name="sql">The SQL query.</param>
    /// <param name="parameters">The parameters.</param>
    /// <param name="noEncrypt">If true, bypasses encryption for this query.</param>
    /// <returns>The query results.</returns>
    public List<Dictionary<string, object>> ExecuteQuery(string sql, Dictionary<string, object?> parameters, bool noEncrypt)
    {
        if (parameters != null && parameters.Count > 0)
        {
            sql = this.BindParameters(sql, parameters);
        }
        else
        {
            sql = this.SanitizeSql(sql);
        }

        var parts = sql.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return this.ExecuteQueryInternal(sql, parts, noEncrypt);
    }

    private void ExecuteInternal(string sql, string[] parts, IWAL? wal = null, string? cacheKey = null)
    {
        // Parts are already provided, no need to parse again

        if (parts[0].ToUpper() == SqlConstants.CREATE && parts[1].ToUpper() == SqlConstants.TABLE)
        {
            if (this.isReadOnly)
            {
                throw new InvalidOperationException("Cannot create table in readonly mode");
            }

            var tableName = parts[2];
            var colsStart = sql.IndexOf('(');
            var colsEnd = sql.LastIndexOf(')');
            var colsStr = sql.Substring(colsStart + 1, colsEnd - colsStart - 1);
            List<string> colDefs = colsStr.Split(',').Select(c => c.Trim()).ToList();
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
                var isPrimary = def.Contains(SqlConstants.PRIMARYKEY);
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
                    _ => DataType.String,
                });
                isAuto.Add(isAutoGen);
                if (isPrimary)
                {
                    primaryKeyIndex = i;
                }
            }

            var table = new Table(this.storage, this.isReadOnly)
            {
                Name = tableName,
                Columns = columns,
                ColumnTypes = columnTypes,
                IsAuto = isAuto,
                PrimaryKeyIndex = primaryKeyIndex,
                DataFile = Path.Combine(this.dbPath, tableName + PersistenceConstants.TableFileExtension),
            };
            this.tables[tableName] = table;
            wal?.Log(sql);
            // Auto-create hash index on primary key for faster lookups
            if (primaryKeyIndex >= 0)
            {
                table.CreateHashIndex(table.Columns[primaryKeyIndex]);
            }
            // Auto-create hash indexes on all columns for faster WHERE lookups
            for (int i = 0; i < columns.Count; i++)
            {
                if (i != primaryKeyIndex)
                {
                    table.CreateHashIndex(columns[i]);
                }
            }
        }
        else if (parts[0].ToUpper() == SqlConstants.CREATE && parts[1].ToUpper() == "INDEX")
        {
            if (this.isReadOnly)
            {
                throw new InvalidOperationException("Cannot create index in readonly mode");
            }

            // CREATE INDEX idx_name ON table_name (column_name)
            // or CREATE UNIQUE INDEX idx_name ON table_name (column_name)
            var indexNameIdx = parts[1].ToUpper() == "UNIQUE" ? 3 : 2;
            var onIdx = Array.IndexOf(parts.Select(p => p.ToUpper()).ToArray(), "ON");
            if (onIdx < 0)
            {
                throw new InvalidOperationException("CREATE INDEX requires ON clause");
            }

            var tableName = parts[onIdx + 1];
            if (!this.tables.ContainsKey(tableName))
            {
                throw new InvalidOperationException($"Table {tableName} does not exist");
            }

            // Extract column name from parentheses
            var columnStart = sql.IndexOf('(');
            var columnEnd = sql.IndexOf(')');
            if (columnStart < 0 || columnEnd < 0)
            {
                throw new InvalidOperationException("CREATE INDEX requires column name in parentheses");
            }

            var columnName = sql.Substring(columnStart + 1, columnEnd - columnStart - 1).Trim();

            // Create hash index on the table
            this.tables[tableName].CreateHashIndex(columnName);
            wal?.Log(sql);
        }
        else if (parts[0].ToUpper() == SqlConstants.INSERT && parts[1].ToUpper() == SqlConstants.INTO)
        {
            if (this.isReadOnly)
            {
                throw new InvalidOperationException("Cannot insert in readonly mode");
            }

            var insertSql = sql[sql.IndexOf("INSERT INTO")..];
            var tableStart = "INSERT INTO ".Length;
            var tableEnd = insertSql.IndexOf(' ', tableStart);
            if (tableEnd == -1)
            {
                tableEnd = insertSql.IndexOf('(', tableStart);
            }

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
            List<string> values = valuesStr.Split(',').Select(v => v.Trim().Trim('\'')).ToList();
            var row = new Dictionary<string, object>();
            if (insertColumns == null)
            {
                // All columns
                for (int i = 0; i < this.tables[tableName].Columns.Count; i++)
                {
                    var col = this.tables[tableName].Columns[i];
                    var type = this.tables[tableName].ColumnTypes[i];
                    row[col] = this.ParseValue(values[i], type);
                }
            }
            else
            {
                // Specified columns
                for (int i = 0; i < insertColumns.Count; i++)
                {
                    var col = insertColumns[i];
                    var idx = this.tables[tableName].Columns.IndexOf(col);
                    var type = this.tables[tableName].ColumnTypes[idx];
                    row[col] = this.ParseValue(values[i], type);
                }

                // For auto columns not specified
                for (int i = 0; i < this.tables[tableName].Columns.Count; i++)
                {
                    var col = this.tables[tableName].Columns[i];
                    if (!row.ContainsKey(col) && this.tables[tableName].IsAuto[i])
                    {
                        row[col] = this.GenerateAutoValue(this.tables[tableName].ColumnTypes[i]);
                    }
                }
            }

            this.tables[tableName].Insert(row);
            wal?.Log(sql);
        }
        else if (parts[0].ToUpper() == "EXPLAIN")
        {
            if (parts.Length < 2 || parts[1].ToUpper() != "SELECT")
            {
                throw new InvalidOperationException("EXPLAIN only supports SELECT queries");
            }
            var selectParts = parts.Skip(1).ToArray();
            var fromIdx = Array.IndexOf(selectParts, SqlConstants.FROM);
            if (fromIdx < 0)
            {
                throw new InvalidOperationException("Invalid SELECT query for EXPLAIN");
            }
            var tableName = selectParts[fromIdx + 1];
            if (!this.tables.ContainsKey(tableName))
            {
                throw new InvalidOperationException($"Table {tableName} does not exist");
            }
            var whereIdx = Array.IndexOf(selectParts, SqlConstants.WHERE);
            string plan = "Full table scan";
            if (whereIdx > 0)
            {
                var whereStr = string.Join(" ", selectParts.Skip(whereIdx + 1));
                var whereTokens = whereStr.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (whereTokens.Length >= 3 && whereTokens[1] == "=")
                {
                    var col = whereTokens[0];
                    if (this.tables[tableName].HasHashIndex(col))
                    {
                        plan = $"Hash index lookup on {col}";
                    }
                    else if (this.tables[tableName].PrimaryKeyIndex >= 0 && this.tables[tableName].Columns[this.tables[tableName].PrimaryKeyIndex] == col)
                    {
                        plan = $"Primary key lookup on {col}";
                    }
                    else
                    {
                        plan = $"Full table scan with WHERE on {col}";
                    }
                }
                else
                {
                    plan = "Full table scan with complex WHERE";
                }
            }
            Console.WriteLine($"Query Plan: {plan}");
        }
        else if (parts[0].ToUpper() == SqlConstants.SELECT)
        {
            var fromIdx = Array.IndexOf(parts, SqlConstants.FROM);
            string[] keywords = ["WHERE", "ORDER", "LIMIT"];
            var fromParts = parts.Skip(fromIdx + 1).TakeWhile(p => !keywords.Contains(p.ToUpper())).ToArray();
            var whereIdx = Array.IndexOf(parts, SqlConstants.WHERE);
            var orderIdx = Array.IndexOf(parts, SqlConstants.ORDER);
            var limitIdx = Array.IndexOf(parts, "LIMIT");
            string? whereStr = null;
            if (whereIdx > 0)
            {
                var endIdx = orderIdx > 0 ? orderIdx : limitIdx > 0 ? limitIdx : parts.Length;
                whereStr = string.Join(" ", parts.Skip(whereIdx + 1).Take(endIdx - whereIdx - 1));
            }

            string? orderBy = null;
            bool asc = true;
            if (orderIdx > 0 && parts.Length > orderIdx + 3 && parts[orderIdx + 1].ToUpper() == SqlConstants.BY)
            {
                orderBy = parts[orderIdx + 2];
                asc = parts[orderIdx + 3].ToUpper() != SqlConstants.DESC;
            }

            int? limit = null;
            int? offset = null;
            if (limitIdx > 0)
            {
                var limitParts = parts.Skip(limitIdx + 1).ToArray();
                if (limitParts.Length > 0)
                {
                    limit = int.Parse(limitParts[0]);
                    if (limitParts.Length > 2 && limitParts[1].ToUpper() == "OFFSET")
                    {
                        offset = int.Parse(limitParts[2]);
                    }
                }
            }

            List<Dictionary<string, object>> results;

            if (fromParts.Any(p => p.ToUpper() == "JOIN"))
            {
                // Parse JOIN
                var table1 = fromParts[0];
                var joinType = fromParts.Contains("LEFT") ? "LEFT" : "INNER";
                var joinIdx = Array.IndexOf(fromParts, joinType == "LEFT" ? "LEFT" : "JOIN");
                if (joinType == "LEFT")
                {
                    joinIdx = Array.IndexOf(fromParts, "JOIN");
                }

                var table2 = fromParts[joinIdx + 1];
                var onIdx = Array.IndexOf(fromParts, "ON");
                var onStr = string.Join(" ", fromParts.Skip(onIdx + 1));

                // Assume ON t1.col = t2.col
                var onParts = onStr.Split('=');
                var left = onParts[0].Trim().Split('.')[1];
                var right = onParts[1].Trim().Split('.')[1];
                var rows1 = this.tables[table1].Select();
                results = new List<Dictionary<string, object>>();
                foreach (var r1 in rows1)
                {
                    var joinKey = r1[left];
                    string whereForTable2 = $"{right} = {this.FormatValue(joinKey)}";
                    var matchingRows = this.tables[table2].Select(whereForTable2);
                    if (matchingRows.Any())
                    {
                        foreach (var r2 in matchingRows)
                        {
                            var combined = new Dictionary<string, object>();
                            foreach (var kv in r1)
                            {
                                combined[table1 + "." + kv.Key] = kv.Value;
                            }

                            foreach (var kv in r2)
                            {
                                combined[table2 + "." + kv.Key] = kv.Value;
                            }

                            results.Add(combined);
                        }
                    }
                    else if (joinType == "LEFT")
                    {
                        var combined = new Dictionary<string, object>();
                        foreach (var kv in r1)
                        {
                            combined[table1 + "." + kv.Key] = kv.Value;
                        }

                        // Null for table2
                        foreach (var col in this.tables[table2].Columns)
                        {
                            combined[table2 + "." + col] = null;
                        }

                        results.Add(combined);
                    }
                }

                // Apply where
                if (!string.IsNullOrEmpty(whereStr))
                {
                    results = results.Where(r => this.EvaluateJoinWhere(r, whereStr)).ToList();
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

                // Apply limit and offset
                if (offset.HasValue)
                {
                    results = results.Skip(offset.Value).ToList();
                }

                if (limit.HasValue)
                {
                    results = results.Take(limit.Value).ToList();
                }

                foreach (var row in results)
                {
                    Console.WriteLine(string.Join(", ", row.Select(kv => $"{kv.Key}: {kv.Value ?? "NULL"}")));
                }
            }
            else
            {
                var tableName = fromParts[0];
                if (!string.IsNullOrEmpty(whereStr))
                {
                    var columns = this.ParseWhereColumns(whereStr);
                    foreach (var col in columns)
                    {
                        if (this.tables.ContainsKey(tableName) && this.tables[tableName].Columns.Contains(col))
                        {
                            this.tables[tableName].IncrementColumnUsage(col);
                        }
                    }
                }
                Console.WriteLine($"Query Plan: {GetQueryPlan(tableName, whereStr)}");
                results = this.tables[tableName].Select(whereStr, orderBy, asc);

                // Track column usage for SELECT * queries
                if (whereStr == null && orderBy == null && limit == null && offset == null)
                {
                    // Simple case: SELECT * FROM table
                    this.tables[tableName].TrackAllColumnsUsage();
                }
                else if (whereStr != null)
                {
                    // Analyze WHERE clause for used columns
                    var usedColumns = this.ParseWhereColumns(whereStr);
                    foreach (var column in usedColumns)
                    {
                        if (this.tables[tableName].Columns.Contains(column))
                        {
                            this.tables[tableName].TrackColumnUsage(column);
                        }
                    }
                }

                // Apply limit and offset
                if (offset.HasValue)
                {
                    results = results.Skip(offset.Value).ToList();
                }

                if (limit.HasValue)
                {
                    results = results.Take(limit.Value).ToList();
                }

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
                var idx = this.tables[tableName].Columns.IndexOf(col);
                var type = this.tables[tableName].ColumnTypes[idx];
                updates[col] = this.ParseValue(set.Value, type);
            }

            this.tables[tableName].Update(whereStr, updates);
            wal?.Log(sql);
        }
        else if (parts[0].ToUpper() == "DELETE" && parts[1].ToUpper() == "FROM")
        {
            var tableName = parts[2];
            var whereIdx = Array.IndexOf(parts, "WHERE");
            var whereStr = whereIdx > 0 ? string.Join(" ", parts.Skip(whereIdx + 1)) : null;
            this.tables[tableName].Delete(whereStr);
            wal?.Log(sql);
        }
        else if (parts[0].ToUpper() == "PRAGMA" && parts.Length > 1 && parts[1].ToUpper() == "STATS")
        {
            foreach (var kv in this.tables)
            {
                var table = kv.Value;
                Console.WriteLine($"Table {kv.Key}:");
                foreach (var colUsage in table.GetColumnUsage())
                {
                    Console.WriteLine($"  {colUsage.Key}: {colUsage.Value} usages");
                    // Auto-create index if usage > 10 and no index
                    if (colUsage.Value > 10 && !table.HasHashIndex(colUsage.Key))
                    {
                        table.CreateHashIndex(colUsage.Key);
                        Console.WriteLine($"    Auto-created hash index on {colUsage.Key}");
                    }
                }
            }

            // Log query cache statistics
            if (this.queryCache != null)
            {
                var cacheStats = this.queryCache.GetStatistics();
                Console.WriteLine($"Query Cache Stats:");
                Console.WriteLine($"  Hits: {cacheStats.Hits}");
                Console.WriteLine($"  Misses: {cacheStats.Misses}");
                Console.WriteLine($"  Hit Rate: {cacheStats.HitRate:P2}");
                Console.WriteLine($"  Cached Queries: {cacheStats.Count}");
            }
            else
            {
                Console.WriteLine("Query Cache: Disabled");
            }
        }
        else
        {
        }
    }

    /// <summary>
    /// Parses SQL using the enhanced parser with full dialect support and error recovery.
    /// </summary>
    /// <param name="sql">The SQL statement to parse.</param>
    /// <param name="dialect">The SQL dialect to use (defaults to SharpCoreDB).</param>
    /// <returns>The parsed AST node, or null if parsing failed.</returns>
    public SqlNode? ParseWithEnhancedParser(string sql, ISqlDialect? dialect = null)
    {
        var parser = new EnhancedSqlParser(dialect ?? SqlDialectFactory.Default);
        var ast = parser.Parse(sql);

        if (parser.HasErrors)
        {
            Console.WriteLine("⚠️  SQL Parser Warnings:");
            foreach (var error in parser.Errors)
            {
                Console.WriteLine($"  - {error}");
            }
        }

        return ast;
    }

    /// <summary>
    /// Converts an AST node back to SQL string for debugging.
    /// </summary>
    /// <param name="node">The AST node to convert.</param>
    /// <param name="dialect">The SQL dialect to use.</param>
    /// <returns>The SQL string representation.</returns>
    public string? AstToSql(SqlNode node, ISqlDialect? dialect = null)
    {
        var visitor = new SqlToStringVisitor(dialect ?? SqlDialectFactory.Default);
        return node.Accept(visitor)?.ToString();
    }

    /// <summary>
    /// Validates SQL syntax without executing it.
    /// </summary>
    /// <param name="sql">The SQL to validate.</param>
    /// <param name="errors">Output list of validation errors.</param>
    /// <returns>True if SQL is valid, false otherwise.</returns>
    public bool ValidateSql(string sql, out List<string> errors)
    {
        var parser = new EnhancedSqlParser();
        var ast = parser.Parse(sql);
        errors = new List<string>(parser.Errors);
        return ast != null && !parser.HasErrors;
    }

    private object ParseValue(string val, DataType type)
    {
        if (val == "NULL")
        {
            return null;
        }

        try
        {
            if (type == DataType.Boolean)
            {
                var lower = val.ToLower();
                if (lower == "1" || lower == "true") return true;
                if (lower == "0" || lower == "false") return false;
                // Accept numeric strings: non-zero => true, zero => false
                if (int.TryParse(val, out var intBool))
                {
                    return intBool != 0;
                }
                return bool.Parse(val);
            }

            return type switch
            {
                DataType.Integer => int.Parse(val),
                DataType.String => val,
                DataType.Real => double.Parse(val),
                DataType.Blob => Convert.FromBase64String(val),
                DataType.DateTime => DateTime.Parse(val),
                DataType.Long => long.Parse(val),
                DataType.Decimal => decimal.Parse(val),
                DataType.Ulid => Ulid.Parse(val),
                DataType.Guid => Guid.Parse(val),
                _ => val,
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
        _ => throw new InvalidOperationException($"Auto generation not supported for type {type}"),
    };

    private bool EvaluateJoinWhere(Dictionary<string, object> row, string where)
    {
        if (string.IsNullOrEmpty(where))
        {
            return true;
        }

        var parts = where.Split(' ');
        if (parts.Length <= 3)
        {
            // Simple case: key op value
            var key = parts[0].Trim();
            var op = parts[1].Trim();
            var value = parts[2].Trim().Trim('\'');

            // Support for null values
            if (value.Equals("NULL", StringComparison.OrdinalIgnoreCase))
            {
                value = null;
            }

            if (row.ContainsKey(key))
            {
                var rowValue = row[key];

                return op switch
                {
                    "=" => rowValue?.ToString() == value,
                    "!=" => rowValue?.ToString() != value,
                    "<" => Comparer<object>.Default.Compare(rowValue, value) < 0,
                    "<=" => Comparer<object>.Default.Compare(rowValue, value) <= 0,
                    ">" => Comparer<object>.Default.Compare(rowValue, value) > 0,
                    ">=" => Comparer<object>.Default.Compare(rowValue, value) >= 0,
                    "LIKE" => rowValue?.ToString().Contains(value.Replace("%", string.Empty).Replace("_", string.Empty)) == true,
                    "NOT LIKE" => rowValue?.ToString().Contains(value.Replace("%", string.Empty).Replace("_", string.Empty)) != true,
                    "IN" => value.Split(',').Select(v => v.Trim().Trim('\'')).Contains(rowValue?.ToString()),
                    "NOT IN" => !value.Split(',').Select(v => v.Trim().Trim('\'')).Contains(rowValue?.ToString()),
                    _ => throw new InvalidOperationException($"Unsupported operator {op}"),
                };
            }

            return false;
        }
        else
        {
            // Complex case: multiple conditions
            var subConditions = new List<bool>();
            for (int i = 0; i < parts.Length; i += 4)
            {
                var key = parts[i].Trim();
                var op = parts[i + 1].Trim();
                var value = parts[i + 2].Trim().Trim('\'');

                // Support for null values
                if (value.Equals("NULL", StringComparison.OrdinalIgnoreCase))
                {
                    value = null;
                }

                if (row.ContainsKey(key))
                {
                    var rowValue = row[key];

                    subConditions.Add(op switch
                    {
                        "=" => rowValue?.ToString() == value,
                        "!=" => rowValue?.ToString() != value,
                        "<" => Comparer<object>.Default.Compare(rowValue, value) < 0,
                        "<=" => Comparer<object>.Default.Compare(rowValue, value) <= 0,
                        ">" => Comparer<object>.Default.Compare(rowValue, value) > 0,
                        ">=" => Comparer<object>.Default.Compare(rowValue, value) >= 0,
                        "LIKE" => rowValue?.ToString().Contains(value.Replace("%", string.Empty).Replace("_", string.Empty)) == true,
                        "NOT LIKE" => rowValue?.ToString().Contains(value.Replace("%", string.Empty).Replace("_", string.Empty)) != true,
                        "IN" => value.Split(',').Select(v => v.Trim().Trim('\'')).Contains(rowValue?.ToString()),
                        "NOT IN" => !value.Split(',').Select(v => v.Trim().Trim('\'')).Contains(rowValue?.ToString()),
                        _ => throw new InvalidOperationException($"Unsupported operator {op}"),
                    });
                }
            }

            // Combine sub-conditions with AND
            return subConditions.All(c => c);
        }
    }

    private string BindParameters(string sql, Dictionary<string, object?> parameters)
    {
        var result = sql;
        
        // Handle named parameters (@paramName or @param0, @param1, etc.)
        foreach (var param in parameters)
        {
            var paramName = param.Key;
            var valueStr = this.FormatValue(param.Value);
            
            // Try matching with @ prefix
            if (paramName.StartsWith("@"))
            {
                result = result.Replace(paramName, valueStr);
            }
            else
            {
                // Also try with @ prefix added
                result = result.Replace("@" + paramName, valueStr);
            }
        }
        
        // Handle positional parameters (?)
        var paramIndex = 0;
        var index = 0;
        while ((index = result.IndexOf('?', index)) != -1)
        {
            if (paramIndex >= parameters.Count)
            {
                throw new InvalidOperationException("Not enough parameters provided for SQL query.");
            }

            var paramKey = paramIndex.ToString();
            if (!parameters.TryGetValue(paramKey, out var value))
            {
                // Try finding any unused parameter
                var unusedParam = parameters.FirstOrDefault(p => !result.Contains(p.Key));
                if (unusedParam.Key != null)
                {
                    value = unusedParam.Value;
                }
                else
                {
                    throw new InvalidOperationException($"Parameter '{paramKey}' not found.");
                }
            }

            var valueStr = this.FormatValue(value);
            result = result.Remove(index, 1).Insert(index, valueStr);
            index += valueStr.Length; // Skip past the inserted value
            paramIndex++;
        }

        return result;
    }

    private string SanitizeSql(string sql)
    {
        // Basic sanitization: escape single quotes
        return sql.Replace("'", "''");
    }

    private string FormatValue(object? value)
    {
        return value switch
        {
            null => "NULL",
            string s => $"'{s.Replace("'", "''")}'",
            int i => i.ToString(System.Globalization.CultureInfo.InvariantCulture),
            long l => l.ToString(System.Globalization.CultureInfo.InvariantCulture),
            double d => d.ToString(System.Globalization.CultureInfo.InvariantCulture),
            bool b => b ? "1" : "0",
            DateTime dt => $"'{dt:yyyy-MM-dd HH:mm:ss}'",
            decimal m => m.ToString(System.Globalization.CultureInfo.InvariantCulture),
            _ => $"'{value.ToString()?.Replace("'", "''")}'",
        };
    }

    private List<Dictionary<string, object>> ExecuteQueryInternal(string sql, string[] parts, bool noEncrypt = false)
    {
        List<Dictionary<string, object>> results = new List<Dictionary<string, object>>();

        if (parts[0].ToUpper() == SqlConstants.SELECT)
        {
            var fromIdx = Array.IndexOf(parts, SqlConstants.FROM);
            string[] keywords = ["WHERE", "ORDER", "LIMIT"];
            var fromParts = parts.Skip(fromIdx + 1).TakeWhile(p => !keywords.Contains(p.ToUpper())).ToArray();
            var whereIdx = Array.IndexOf(parts, SqlConstants.WHERE);
            var orderIdx = Array.IndexOf(parts, SqlConstants.ORDER);
            var limitIdx = Array.IndexOf(parts, "LIMIT");
            string? whereStr = null;
            if (whereIdx > 0)
            {
                var endIdx = orderIdx > 0 ? orderIdx : limitIdx > 0 ? limitIdx : parts.Length;
                whereStr = string.Join(" ", parts.Skip(whereIdx + 1).Take(endIdx - whereIdx - 1));
            }

            string? orderBy = null;
            bool asc = true;
            if (orderIdx > 0 && parts.Length > orderIdx + 3 && parts[orderIdx + 1].ToUpper() == SqlConstants.BY)
            {
                orderBy = parts[orderIdx + 2];
                asc = parts[orderIdx + 3].ToUpper() != SqlConstants.DESC;
            }

            int? limit = null;
            int? offset = null;
            if (limitIdx > 0)
            {
                var limitParts = parts.Skip(limitIdx + 1).ToArray();
                if (limitParts.Length > 0)
                {
                    limit = int.Parse(limitParts[0]);
                    if (limitParts.Length > 2 && limitParts[1].ToUpper() == "OFFSET")
                    {
                        offset = int.Parse(limitParts[2]);
                    }
                }
            }

            if (fromParts.Any(p => p.ToUpper() == "JOIN"))
            {
                // Parse JOIN
                var table1 = fromParts[0];
                var joinType = fromParts.Contains("LEFT") ? "LEFT" : "INNER";
                var joinIdx = Array.IndexOf(fromParts, joinType == "LEFT" ? "LEFT" : "JOIN");
                if (joinType == "LEFT")
                {
                    joinIdx = Array.IndexOf(fromParts, "JOIN");
                }

                var table2 = fromParts[joinIdx + 1];
                var onIdx = Array.IndexOf(fromParts, "ON");
                var onStr = string.Join(" ", fromParts.Skip(onIdx + 1));

                // Assume ON t1.col = t2.col
                var onParts = onStr.Split('=');
                var left = onParts[0].Trim().Split('.')[1];
                var right = onParts[1].Trim().Split('.')[1];
                var rows1 = this.tables[table1].Select(null, null, true, noEncrypt);
                results = new List<Dictionary<string, object>>();
                foreach (var r1 in rows1)
                {
                    var joinKey = r1[left];
                    string whereForTable2 = $"{right} = {this.FormatValue(joinKey)}";
                    var matchingRows = this.tables[table2].Select(whereForTable2, null, true, noEncrypt);
                    if (matchingRows.Any())
                    {
                        foreach (var r2 in matchingRows)
                        {
                            var combined = new Dictionary<string, object>();
                            foreach (var kv in r1)
                            {
                                combined[table1 + "." + kv.Key] = kv.Value;
                            }

                            foreach (var kv in r2)
                            {
                                combined[table2 + "." + kv.Key] = kv.Value;
                            }

                            results.Add(combined);
                        }
                    }
                    else if (joinType == "LEFT")
                    {
                        var combined = new Dictionary<string, object>();
                        foreach (var kv in r1)
                        {
                            combined[table1 + "." + kv.Key] = kv.Value;
                        }

                        // Null for table2
                        foreach (var col in this.tables[table2].Columns)
                        {
                            combined[table2 + "." + col] = null;
                        }

                        results.Add(combined);
                    }
                }

                // Apply where
                if (!string.IsNullOrEmpty(whereStr))
                {
                    results = results.Where(r => this.EvaluateJoinWhere(r, whereStr)).ToList();
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

                // Apply limit and offset
                if (offset.HasValue)
                {
                    results = results.Skip(offset.Value).ToList();
                }

                if (limit.HasValue)
                {
                    results = results.Take(limit.Value).ToList();
                }

                foreach (var row in results)
                {
                    Console.WriteLine(string.Join(", ", row.Select(kv => $"{kv.Key}: {kv.Value ?? "NULL"}")));
                }
            }
            else
            {
                var tableName = fromParts[0];
                if (!string.IsNullOrEmpty(whereStr))
                {
                    var columns = this.ParseWhereColumns(whereStr);
                    foreach (var col in columns)
                    {
                        if (this.tables.ContainsKey(tableName) && this.tables[tableName].Columns.Contains(col))
                        {
                            this.tables[tableName].IncrementColumnUsage(col);
                        }
                    }
                }
                Console.WriteLine($"Query Plan: {GetQueryPlan(tableName, whereStr)}");
                results = this.tables[tableName].Select(whereStr, orderBy, asc, noEncrypt);

                // Track column usage for SELECT * queries
                if (whereStr == null && orderBy == null && limit == null && offset == null)
                {
                    // Simple case: SELECT * FROM table
                    this.tables[tableName].TrackAllColumnsUsage();
                }
                else if (whereStr != null)
                {
                    // Analyze WHERE clause for used columns
                    var usedColumns = this.ParseWhereColumns(whereStr);
                    foreach (var column in usedColumns)
                    {
                        if (this.tables[tableName].Columns.Contains(column))
                        {
                            this.tables[tableName].TrackColumnUsage(column);
                        }
                    }
                }

                // Apply limit and offset
                if (offset.HasValue)
                {
                    results = results.Skip(offset.Value).ToList();
                }

                if (limit.HasValue)
                {
                    results = results.Take(limit.Value).ToList();
                }

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
                var idx = this.tables[tableName].Columns.IndexOf(col);
                var type = this.tables[tableName].ColumnTypes[idx];
                updates[col] = this.ParseValue(set.Value, type);
            }

            this.tables[tableName].Update(whereStr, updates);
            wal?.Log(sql);
        }
        else if (parts[0].ToUpper() == "DELETE" && parts[1].ToUpper() == "FROM")
        {
            var tableName = parts[2];
            var whereIdx = Array.IndexOf(parts, "WHERE");
            var whereStr = whereIdx > 0 ? string.Join(" ", parts.Skip(whereIdx + 1)) : null;
            this.tables[tableName].Delete(whereStr);
            wal?.Log(sql);
        }
        else if (parts[0].ToUpper() == "PRAGMA" && parts.Length > 1 && parts[1].ToUpper() == "STATS")
        {
            foreach (var kv in this.tables)
            {
                var table = kv.Value;
                Console.WriteLine($"Table {kv.Key}:");
                foreach (var colUsage in table.GetColumnUsage())
                {
                    Console.WriteLine($"  {colUsage.Key}: {colUsage.Value} usages");
                    // Auto-create index if usage > 10 and no index
                    if (colUsage.Value > 10 && !table.HasHashIndex(colUsage.Key))
                    {
                        table.CreateHashIndex(colUsage.Key);
                        Console.WriteLine($"    Auto-created hash index on {colUsage.Key}");
                    }
                }
            }

            // Log query cache statistics
            if (this.queryCache != null)
            {
                var cacheStats = this.queryCache.GetStatistics();
                Console.WriteLine($"Query Cache Stats:");
                Console.WriteLine($"  Hits: {cacheStats.Hits}");
                Console.WriteLine($"  Misses: {cacheStats.Misses}");
                Console.WriteLine($"  Hit Rate: {cacheStats.HitRate:P2}");
                Console.WriteLine($"  Cached Queries: {cacheStats.Count}");
            }
            else
            {
                Console.WriteLine("Query Cache: Disabled");
            }
        }
        else
        {
        }

        return results;
    }

    private string GetQueryPlan(string tableName, string? whereStr)
    {
        string plan = "Full table scan";
        if (!string.IsNullOrEmpty(whereStr))
        {
            var whereTokens = whereStr.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (whereTokens.Length >= 3 && whereTokens[1] == "=")
            {
                var col = whereTokens[0];
                if (this.tables[tableName].HasHashIndex(col))
                {
                    plan = $"Hash index lookup on {col}";
                }
                else if (this.tables[tableName].PrimaryKeyIndex >= 0 && this.tables[tableName].Columns[this.tables[tableName].PrimaryKeyIndex] == col)
                {
                    plan = $"Primary key lookup on {col}";
                }
                else
                {
                    plan = $"Full table scan with WHERE on {col}";
                }
            }
            else
            {
                plan = "Full table scan with complex WHERE";
            }
        }
        return plan;
    }

    private List<string> ParseWhereColumns(string where)
    {
        var columns = new List<string>();
        var tokens = where.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i];
            if (i % 4 == 0 || (i > 0 && tokens[i-1] == "AND" || tokens[i-1] == "OR"))
            {
                // Assume column name
                if (!string.IsNullOrEmpty(token) && char.IsLetter(token[0]))
                {
                    columns.Add(token);
                }
            }
        }
        return columns.Distinct().ToList();
    }
}
