// <copyright file="Database.Execution.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

// ✅ RELOCATED: This file was moved from root SharpCoreDB/ to Database/Execution/
// Original path: SharpCoreDB/Database.Execution.cs
// New path: SharpCoreDB/Database/Execution/Database.Execution.cs
// Date: December 2025

namespace SharpCoreDB;

using System.Text.Json;

/// <summary>
/// Database implementation - Execution partial class.
/// Handles SQL execution with modern C# 14 patterns and async support.
/// 
/// Location: Database/Execution/Database.Execution.cs
/// Purpose: SQL command execution (sync + async), query execution, compiled queries
/// Features: Group commit WAL, query plan caching, parameter validation
/// Dependencies: SqlParser, QueryPlanCache, GroupCommitWAL
/// </summary>
public partial class Database
{
    /// <inheritdoc />
    public void ExecuteSQL(string sql)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);
        
        SqlQueryValidator.ValidateQuery(
            sql, 
            null, 
            config?.SqlValidationMode ?? SqlQueryValidator.ValidationMode.Lenient,
            config?.StrictParameterValidation ?? true);
        
        var parts = sql.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts[0].Equals(SqlConstants.SELECT, StringComparison.OrdinalIgnoreCase))
        {
            ExecuteSelectQuery(sql, null);
            return;
        }

        // ✅ Cache plans for DML: INSERT, UPDATE, DELETE
        if (parts[0].Equals("INSERT", StringComparison.OrdinalIgnoreCase))
        {
            GetOrAddPlan(sql, null, SqlCommandType.INSERT);
        }
        else if (parts[0].Equals("UPDATE", StringComparison.OrdinalIgnoreCase))
        {
            GetOrAddPlan(sql, null, SqlCommandType.UPDATE);
        }
        else if (parts[0].Equals("DELETE", StringComparison.OrdinalIgnoreCase))
        {
            GetOrAddPlan(sql, null, SqlCommandType.DELETE);
        }

        // ✅ OPTIMIZATION: Skip WAL for DELETE/UPDATE with PageBased storage
        bool isDeleteOrUpdate = parts[0].Equals("DELETE", StringComparison.OrdinalIgnoreCase) ||
                               parts[0].Equals("UPDATE", StringComparison.OrdinalIgnoreCase);
        
        bool useWal = groupCommitWal is not null && !isDeleteOrUpdate;

        if (useWal)
        {
            ExecuteSQLWithGroupCommit(sql).GetAwaiter().GetResult();
        }
        else
        {
            lock (_walLock)
            {
                var sqlParser = new SqlParser(tables, null!, _dbPath, storage, isReadOnly, queryCache, false, config);
                sqlParser.Execute(sql, null);
                
                if (!isReadOnly && IsSchemaChangingCommand(sql))
                {
                    SaveMetadata();
                    ApplyColumnarCompactionThresholdToTables();
                    _metadataDirty = true;
                }
                else if (!isReadOnly)
                {
                    _metadataDirty = true;
                }
            }
        }
    }

    /// <summary>
    /// Executes a parameterized SQL command.
    /// </summary>
    /// <param name="sql">The SQL command with parameter placeholders.</param>
    /// <param name="parameters">The parameters to bind.</param>
    public void ExecuteSQL(string sql, Dictionary<string, object?> parameters)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);
        ArgumentNullException.ThrowIfNull(parameters);
        
        SqlQueryValidator.ValidateQuery(
            sql, 
            parameters, 
            config?.SqlValidationMode ?? SqlQueryValidator.ValidationMode.Lenient,
            config?.StrictParameterValidation ?? true);
        
        var parts = sql.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts[0].Equals(SqlConstants.SELECT, StringComparison.OrdinalIgnoreCase))
        {
            ExecuteSelectQuery(sql, parameters);
            return;
        }

        // ✅ Cache plans for DML: INSERT, UPDATE, DELETE
        if (parts[0].Equals("INSERT", StringComparison.OrdinalIgnoreCase))
        {
            GetOrAddPlan(sql, parameters, SqlCommandType.INSERT);
        }
        else if (parts[0].Equals("UPDATE", StringComparison.OrdinalIgnoreCase))
        {
            GetOrAddPlan(sql, parameters, SqlCommandType.UPDATE);
        }
        else if (parts[0].Equals("DELETE", StringComparison.OrdinalIgnoreCase))
        {
            GetOrAddPlan(sql, parameters, SqlCommandType.DELETE);
        }

        bool isDeleteOrUpdate = parts[0].Equals("DELETE", StringComparison.OrdinalIgnoreCase) ||
                               parts[0].Equals("UPDATE", StringComparison.OrdinalIgnoreCase);
        
        bool useWal = groupCommitWal is not null && !isDeleteOrUpdate;

        if (useWal)
        {
            ExecuteSQLWithGroupCommit(sql, parameters).GetAwaiter().GetResult();
        }
        else
        {
            lock (_walLock)
            {
                var sqlParser = new SqlParser(tables, null!, _dbPath, storage, isReadOnly, queryCache, false, config);
                sqlParser.Execute(sql, parameters, null);
                
                if (!isReadOnly && IsSchemaChangingCommand(sql))
                {
                    SaveMetadata();
                    ApplyColumnarCompactionThresholdToTables();
                    _metadataDirty = true;
                }
                else if (!isReadOnly)
                {
                    _metadataDirty = true;
                }
            }
        }
    }

    /// <summary>
    /// Executes a parameterized SQL command with positional parameters.
    /// </summary>
    /// <param name="sql">The SQL statement with ? placeholders.</param>
    /// <param name="parameters">Parameters in order of ? placeholders.</param>
    public void ExecuteSQL(string sql, params object?[] parameters)
    {
        if (parameters is null || parameters.Length == 0)
        {
            ExecuteSQL(sql);
            return;
        }

        Dictionary<string, object?> paramDict = [];  // ✅ C# 14: Collection expression
        for (int i = 0; i < parameters.Length; i++)
        {
            paramDict[$"@p{i}"] = parameters[i];
        }

        ExecuteSQL(sql, paramDict);
    }

    /// <inheritdoc />
    public async Task ExecuteSQLAsync(string sql, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);
        
        var parts = sql.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts[0].Equals(SqlConstants.SELECT, StringComparison.OrdinalIgnoreCase))
        {
            await ExecuteSelectQueryAsync(sql, null, cancellationToken).ConfigureAwait(false);
            return;
        }

        // ✅ Cache plans for DML: INSERT, UPDATE, DELETE
        if (parts[0].Equals("INSERT", StringComparison.OrdinalIgnoreCase))
        {
            GetOrAddPlan(sql, null, SqlCommandType.INSERT);
        }
        else if (parts[0].Equals("UPDATE", StringComparison.OrdinalIgnoreCase))
        {
            GetOrAddPlan(sql, null, SqlCommandType.UPDATE);
        }
        else if (parts[0].Equals("DELETE", StringComparison.OrdinalIgnoreCase))
        {
            GetOrAddPlan(sql, null, SqlCommandType.DELETE);
        }

        if (groupCommitWal is not null)
        {
            await ExecuteSQLWithGroupCommit(sql, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await Task.Run(() => ExecuteSQL(sql), cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Executes a parameterized SQL command asynchronously.
    /// </summary>
    /// <param name="sql">The SQL command with parameter placeholders.</param>
    /// <param name="parameters">The parameters to bind.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task ExecuteSQLAsync(string sql, Dictionary<string, object?> parameters, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);
        ArgumentNullException.ThrowIfNull(parameters);
        
        var parts = sql.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts[0].Equals(SqlConstants.SELECT, StringComparison.OrdinalIgnoreCase))
        {
            await ExecuteSelectQueryAsync(sql, parameters, cancellationToken).ConfigureAwait(false);
            return;
        }

        // ✅ Cache plans for DML: INSERT, UPDATE, DELETE
        if (parts[0].Equals("INSERT", StringComparison.OrdinalIgnoreCase))
        {
            GetOrAddPlan(sql, parameters, SqlCommandType.INSERT);
        }
        else if (parts[0].Equals("UPDATE", StringComparison.OrdinalIgnoreCase))
        {
            GetOrAddPlan(sql, parameters, SqlCommandType.UPDATE);
        }
        else if (parts[0].Equals("DELETE", StringComparison.OrdinalIgnoreCase))
        {
            GetOrAddPlan(sql, parameters, SqlCommandType.DELETE);
        }

        if (groupCommitWal is not null)
        {
            await ExecuteSQLWithGroupCommit(sql, parameters, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await Task.Run(() => ExecuteSQL(sql, parameters), cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Executes SELECT query with plan caching.
    /// </summary>
    private void ExecuteSelectQuery(string sql, Dictionary<string, object?>? parameters)
    {
        var sqlParser = new SqlParser(tables, null!, _dbPath, storage, isReadOnly, queryCache, false, config);
        sqlParser.Execute(sql, parameters ?? new Dictionary<string, object?>());
    }

    /// <summary>
    /// Executes SELECT query asynchronously with plan caching.
    /// </summary>
    private async Task ExecuteSelectQueryAsync(string sql, Dictionary<string, object?>? parameters, CancellationToken cancellationToken)
    {
        await Task.Run(() =>
        {
            var sqlParser = new SqlParser(tables, null!, _dbPath, storage, isReadOnly, queryCache, false, config);
            sqlParser.Execute(sql, parameters ?? new Dictionary<string, object?>());
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Executes SQL with group commit WAL for improved write performance.
    /// </summary>
    private async Task ExecuteSQLWithGroupCommit(string sql, CancellationToken cancellationToken = default)
    {
        lock (_walLock)
        {
            var sqlParser = new SqlParser(tables, null!, _dbPath, storage, isReadOnly, queryCache, false, config);
            sqlParser.Execute(sql, null);
            
            if (!isReadOnly && IsSchemaChangingCommand(sql))
            {
                SaveMetadata();
                ApplyColumnarCompactionThresholdToTables();
            }
        }
        
        byte[] walData = Encoding.UTF8.GetBytes(sql);
        await groupCommitWal!.CommitAsync(walData, cancellationToken);
    }

    /// <summary>
    /// Executes parameterized SQL with group commit WAL.
    /// </summary>
    private async Task ExecuteSQLWithGroupCommit(string sql, Dictionary<string, object?> parameters, CancellationToken cancellationToken = default)
    {
        lock (_walLock)
        {
            var sqlParser = new SqlParser(tables, null!, _dbPath, storage, isReadOnly, queryCache, false, config);
            sqlParser.Execute(sql, parameters, null);
            
            if (!isReadOnly && IsSchemaChangingCommand(sql))
            {
                SaveMetadata();
                ApplyColumnarCompactionThresholdToTables();
            }
        }
        
        var walEntry = new { Sql = sql, Parameters = parameters };
        byte[] walData = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(walEntry));
        await groupCommitWal!.CommitAsync(walData, cancellationToken);
    }

    /// <summary>
    /// Executes a query and returns the results.
    /// </summary>
    /// <param name="sql">The SQL query.</param>
    /// <param name="parameters">Optional query parameters.</param>
    /// <returns>The query results.</returns>
    public List<Dictionary<string, object>> ExecuteQuery(string sql, Dictionary<string, object?>? parameters = null)
    {
        var entry = GetOrAddPlan(sql, parameters, SqlCommandType.SELECT);
        
        if (entry is not null && entry.CompiledPlan is not null)
        {
            var sqlParserCompiled = new SqlParser(tables, null, _dbPath, storage, isReadOnly, queryCache, false, config);
            return sqlParserCompiled.ExecuteQuery(entry.CachedPlan, parameters ?? []);
        }

        var sqlParser = new SqlParser(tables, null, _dbPath, storage, isReadOnly, queryCache, false, config);
        return entry is not null 
            ? sqlParser.ExecuteQuery(entry.CachedPlan, parameters ?? [])
            : sqlParser.ExecuteQuery(sql, parameters ?? []);
    }

    /// <summary>
    /// Executes a query with optional encryption bypass.
    /// </summary>
    /// <param name="sql">The SQL query.</param>
    /// <param name="parameters">Optional query parameters.</param>
    /// <param name="noEncrypt">If true, bypasses encryption for this query.</param>
    /// <returns>The query results.</returns>
    public List<Dictionary<string, object>> ExecuteQuery(string sql, Dictionary<string, object?>? parameters, bool noEncrypt)
    {
        var sqlParser = new SqlParser(tables, null, _dbPath, storage, isReadOnly, queryCache, noEncrypt, config);
        return sqlParser.ExecuteQuery(sql, parameters ?? [], noEncrypt);
    }

    /// <summary>
    /// Executes a compiled query plan (zero parsing overhead).
    /// Expected: 5-10x faster than ExecuteQuery for repeated queries.
    /// </summary>
    /// <param name="plan">The compiled query plan.</param>
    /// <param name="parameters">Optional query parameters.</param>
    /// <returns>The query results.</returns>
    public List<Dictionary<string, object>> ExecuteCompiled(CompiledQueryPlan plan, Dictionary<string, object?>? parameters = null)
    {
        var cached = new CachedQueryPlan(plan.Sql, plan.Sql.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        var sqlParser = new SqlParser(tables, null, _dbPath, storage, isReadOnly, queryCache, false, config);
        return sqlParser.ExecuteQuery(cached, parameters ?? []);
    }

    /// <summary>
    /// Executes a prepared statement with compiled query optimization.
    /// </summary>
    /// <param name="stmt">The prepared statement.</param>
    /// <param name="parameters">Optional query parameters.</param>
    /// <returns>The query results.</returns>
    public List<Dictionary<string, object>> ExecuteCompiledQuery(DataStructures.PreparedStatement stmt, Dictionary<string, object?>? parameters = null)
    {
        ArgumentNullException.ThrowIfNull(stmt);
        var sqlParser = new SqlParser(tables, null, _dbPath, storage, isReadOnly, queryCache, false, config);
        return sqlParser.ExecuteQuery(stmt.Plan, parameters ?? []);
    }

    /// <summary>
    /// Applies the configured columnar auto-compaction threshold to all tables.
    /// Called after schema changes to ensure new tables have correct settings.
    /// </summary>
    private void ApplyColumnarCompactionThresholdToTables()
    {
        if (this.config is null)
            return;

        var threshold = this.config.ColumnarAutoCompactionThreshold;
        foreach (var table in tables.Values)
        {
            if (table is Table concrete)
            {
                concrete.SetCompactionThreshold(threshold);
            }
        }
    }
}
