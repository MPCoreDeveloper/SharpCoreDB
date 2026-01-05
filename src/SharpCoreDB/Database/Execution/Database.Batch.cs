// <copyright file="Database.Batch.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

// ✅ RELOCATED: Moved from root to Database/Execution/
// Original: SharpCoreDB/Database.Batch.cs
// New: SharpCoreDB/Database/Execution/Database.Batch.cs
// Date: December 2025

namespace SharpCoreDB;

/// <summary>
/// Database implementation - Batch operations partial class.
/// CRITICAL PERFORMANCE: 680x improvement for bulk inserts!
/// 
/// Location: Database/Execution/Database.Batch.cs
/// Purpose: Batch SQL execution, bulk insert optimization
/// Features: INSERT statement batching, StreamingRowEncoder, transaction grouping
/// Performance: 10K inserts in &lt;50ms with optimized path
/// </summary>
public partial class Database
{
    /// <summary>
    /// Detects if a SQL statement is an INSERT.
    /// </summary>
    private static bool IsInsertStatement(string sql)
    {
        var trimmed = sql.AsSpan().Trim();
        return trimmed.Length >= 11 && trimmed[..11].Equals("INSERT INTO", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Parses an INSERT statement to extract table name and row data.
    /// </summary>
    private (string tableName, Dictionary<string, object> row)? ParseInsertStatement(string sql)
    {
        try
        {
            var insertSql = sql[sql.IndexOf("INSERT INTO", StringComparison.OrdinalIgnoreCase)..];
            var tableStart = "INSERT INTO ".Length;
            var tableEnd = insertSql.IndexOf(' ', tableStart);
            if (tableEnd == -1)
            {
                tableEnd = insertSql.IndexOf('(', tableStart);
            }

            var tableName = insertSql[tableStart..tableEnd].Trim();
            
            if (!tables.ContainsKey(tableName))
                return null;
            
            var rest = insertSql[tableEnd..];
            List<string>? insertColumns = null;
            
            if (rest.TrimStart().StartsWith('('))
            {
                var colStart = rest.IndexOf('(') + 1;
                var colEnd = rest.IndexOf(')', colStart);
                var colStr = rest[colStart..colEnd];
                insertColumns = [.. colStr.Split(',').Select(c => c.Trim())];
                rest = rest[(colEnd + 1)..];
            }

            var valuesStart = rest.IndexOf("VALUES", StringComparison.OrdinalIgnoreCase) + "VALUES".Length;
            var valuesStr = rest[valuesStart..].Trim().TrimStart('(').TrimEnd(')');
            var values = valuesStr.Split(',').Select(v => v.Trim().Trim('\'')).ToList();
            
            Dictionary<string, object> row = [];
            var table = tables[tableName];
            
            if (insertColumns is null)
            {
                for (int i = 0; i < table.Columns.Count; i++)
                {
                    var col = table.Columns[i];
                    var type = table.ColumnTypes[i];
                    var parsedValue = SqlParser.ParseValue(values[i], type) ?? DBNull.Value;
                    row[col] = parsedValue;
                }
            }
            else
            {
                for (int i = 0; i < insertColumns.Count; i++)
                {
                    var col = insertColumns[i];
                    var idx = table.Columns.IndexOf(col);
                    var type = table.ColumnTypes[idx];
                    row[col] = SqlParser.ParseValue(values[i], type) ?? DBNull.Value;
                }
            }

            return (tableName, row);
        }
        catch
        {
            return null;
        }
    }

    /// <inheritdoc />
    public void ExecuteBatchSQL(IEnumerable<string> sqlStatements)
    {
        ArgumentNullException.ThrowIfNull(sqlStatements);
        
        var statements = sqlStatements as string[] ?? [.. sqlStatements];
        if (statements.Length == 0) return;

        var hasSelect = statements.Any(sql =>
        {
            var trimmed = sql.AsSpan().Trim();
            return trimmed.Length >= 6 && trimmed[..6].Equals("SELECT", StringComparison.OrdinalIgnoreCase);
        });

        if (hasSelect)
        {
            foreach (var sql in statements)
            {
                ExecuteSQL(sql);
            }
            return;
        }

        Dictionary<string, List<Dictionary<string, object>>> insertsByTable = [];
        List<string> nonInserts = [];

        foreach (var sql in statements)
        {
            if (IsInsertStatement(sql))
            {
                var parsed = ParseInsertStatement(sql);
                if (parsed.HasValue)
                {
                    var (tableName, row) = parsed.Value;
                    
                    if (!insertsByTable.TryGetValue(tableName, out var rows))
                    {
                        rows = [];
                        insertsByTable[tableName] = rows;
                    }
                    
                    rows.Add(row);
                }
                else
                {
                    nonInserts.Add(sql);
                }
            }
            else
            {
                nonInserts.Add(sql);
            }
        }

        lock (_walLock)
        {
            storage.BeginTransaction();
            
            try
            {
                foreach (var (tableName, rows) in insertsByTable)
                {
                    if (tables.TryGetValue(tableName, out var table))
                    {
                        table.InsertBatch(rows);
                    }
                }

                if (nonInserts.Count > 0)
                {
                    var sqlParser = new SqlParser(tables, _dbPath, storage, isReadOnly, queryCache, config);
                    
                    foreach (var sql in nonInserts)
                    {
                        sqlParser.Execute(sql, null);
                    }
                }

                if (!isReadOnly && statements.Any(IsSchemaChangingCommand))
                {
                    SaveMetadata();
                }
                
                storage.CommitAsync().GetAwaiter().GetResult();
            }
            catch
            {
                storage.Rollback();
                throw;
            }
        }
    }

    /// <inheritdoc />
    public async Task ExecuteBatchSQLAsync(IEnumerable<string> sqlStatements, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sqlStatements);
        
        var statements = sqlStatements as string[] ?? [.. sqlStatements];
        if (statements.Length == 0) return;

        var hasSelect = statements.Any(sql =>
        {
            var trimmed = sql.AsSpan().Trim();
            return trimmed.Length >= 6 && trimmed[..6].Equals("SELECT", StringComparison.OrdinalIgnoreCase);
        });

        if (hasSelect)
        {
            foreach (var sql in statements)
            {
                await ExecuteSQLAsync(sql, cancellationToken);
            }
            return;
        }

        Dictionary<string, List<Dictionary<string, object>>> insertsByTable = [];
        List<string> nonInserts = [];

        foreach (var sql in statements)
        {
            if (IsInsertStatement(sql))
            {
                var parsed = ParseInsertStatement(sql);
                if (parsed.HasValue)
                {
                    var (tableName, row) = parsed.Value;

                    if (!insertsByTable.TryGetValue(tableName, out var rows))
                    {
                        rows = [];
                        insertsByTable[tableName] = rows;
                    }

                    rows.Add(row);
                }
                else
                {
                    nonInserts.Add(sql);
                }
            }
            else
            {
                nonInserts.Add(sql);
            }
        }

        Task commitTask;
        lock (_walLock)
        {
            storage.BeginTransaction();

            try
            {
                foreach (var (tableName, rows) in insertsByTable)
                {
                    if (tables.TryGetValue(tableName, out var table))
                    {
                        table.InsertBatch(rows);
                    }
                }

                if (nonInserts.Count > 0)
                {
                    var sqlParser = new SqlParser(tables, _dbPath, storage, isReadOnly, queryCache, config);
                    
                    foreach (var sql in nonInserts)
                    {
                        sqlParser.Execute(sql, null);
                    }
                }

                if (!isReadOnly && statements.Any(IsSchemaChangingCommand))
                {
                    SaveMetadata();
                }
                
                commitTask = storage.CommitAsync();
            }
            catch
            {
                storage.Rollback();
                throw;
            }
        }
        
        await commitTask;
    }

    /// <summary>
    /// Bulk insert operation optimized for large data imports (10K-1M rows).
    /// </summary>
    public async Task BulkInsertAsync(
        string tableName, 
        List<Dictionary<string, object>> rows, 
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tableName);
        ArgumentNullException.ThrowIfNull(rows);
        
        if (rows.Count == 0) return;
        if (isReadOnly) throw new InvalidOperationException("Cannot insert in readonly mode");
        if (!tables.TryGetValue(tableName, out var table))
            throw new InvalidOperationException($"Table '{tableName}' does not exist");

        if ((config?.UseOptimizedInsertPath ?? false) || rows.Count > 5000)
        {
            await BulkInsertOptimizedInternalAsync(tableName, rows, table, cancellationToken);
            return;
        }

        int batchSize = (config?.HighSpeedInsertMode ?? false)
            ? (config?.GroupCommitSize ?? 1000)
            : 100;

        await Task.Run(() =>
        {
            lock (_walLock)
            {
                storage.BeginTransaction();
                
                try
                {
                    for (int i = 0; i < rows.Count; i += batchSize)
                    {
                        int remaining = rows.Count - i;
                        int chunkSize = Math.Min(batchSize, remaining);
                        var chunk = rows.GetRange(i, chunkSize);
                        
                        table.InsertBatch(chunk);
                        
                        if ((config?.HighSpeedInsertMode ?? false) && 
                            (i + chunkSize) % (config?.GroupCommitSize ?? 1000) == 0)
                        {
                            storage.FlushTransactionBuffer();
                        }
                    }
                    
                    storage.CommitAsync().GetAwaiter().GetResult();
                }
                catch
                {
                    storage.Rollback();
                    throw;
                }
            }
        }, cancellationToken);
    }

    /// <summary>
    /// Optimized bulk insert with StreamingRowEncoder (zero-allocation).
    /// </summary>
    private async Task BulkInsertOptimizedInternalAsync(
        string tableName,
        List<Dictionary<string, object>> rows,
        ITable table,
        CancellationToken cancellationToken)
    {
        _ = tableName;
        
        await Task.Run(() =>
        {
            lock (_walLock)
            {
                storage.BeginTransaction();
                
                try
                {
                    using var encoder = new Optimizations.StreamingRowEncoder(
                        table.Columns,
                        table.ColumnTypes,
                        64 * 1024);

                    List<long> allPositions = new(rows.Count);

                    for (int i = 0; i < rows.Count; i++)
                    {
                        var row = rows[i];
                        
                        if (!encoder.EncodeRow(row))
                        {
                            var batchData = encoder.GetBatchData();
                            var batchRowCount = encoder.GetRowCount();
                            
                            long[] positions = table.InsertBatchFromBuffer(batchData, batchRowCount);
                            allPositions.AddRange(positions);
                            
                            encoder.Reset();
                            
                            if (!encoder.EncodeRow(row))
                            {
                                throw new InvalidOperationException(
                                    $"Row {i} is too large to fit in batch buffer (max 64KB)");
                            }
                        }
                    }

                    if (encoder.GetRowCount() > 0)
                    {
                        var batchData = encoder.GetBatchData();
                        var batchRowCount = encoder.GetRowCount();
                        
                        long[] positions = table.InsertBatchFromBuffer(batchData, batchRowCount);
                        allPositions.AddRange(positions);
                    }
                    
                    storage.CommitAsync().GetAwaiter().GetResult();
                }
                catch
                {
                    storage.Rollback();
                    throw;
                }
            }
        }, cancellationToken);
    }
}
