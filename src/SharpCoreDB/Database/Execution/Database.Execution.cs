// <copyright file="Database.Execution.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

// ✅ RELOCATED: This file was moved from root SharpCoreDB/ to Database/Execution/
// Original path: SharpCoreDB/Database.Execution.cs
// New path: SharpCoreDB/Database/Execution/Database.Execution.cs
// Date: December 2025

namespace SharpCoreDB;

using System.Runtime.CompilerServices;
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
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private SqlParser GetSharedSqlParser()
    {
        if (_sharedSqlParser is null)
        {
            _sharedSqlParser = new SqlParser(tables, _dbPath, storage, isReadOnly, queryCache, config);
            _sharedSqlParser.Database = this;

            // v2: resolve the optional GRAPH_RAG provider once and cache it for the parser
            // lifetime. Previously this DI lookup ran on every ExecuteSQL/ExecuteQuery call.
            if (_cachedGraphRagProvider is null)
            {
                _cachedGraphRagProvider = _serviceProvider.GetService(typeof(IGraphRagProvider)) as IGraphRagProvider;
            }

            if (_cachedGraphRagProvider is not null)
            {
                _sharedSqlParser.SetGraphRagProvider(_cachedGraphRagProvider);
            }
        }

        return _sharedSqlParser;
    }


    /// <summary>
    /// Returns the first whitespace-delimited token of a SQL statement as a span
    /// without allocating — replaces the hot-path <c>Trim().Split(' ')[0]</c> verb
    /// dispatch (which allocated a Trim substring, a string[] and one string per
    /// token on every ExecuteSQL/ExecuteNonQuery call). Matches Trim() semantics:
    /// leading whitespace is skipped, then the token runs until the next whitespace.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ReadOnlySpan<char> FirstToken(ReadOnlySpan<char> sql)
    {
        int i = 0;
        while (i < sql.Length && char.IsWhiteSpace(sql[i]))
        {
            i++;
        }

        int start = i;
        while (i < sql.Length && !char.IsWhiteSpace(sql[i]))
        {
            i++;
        }

        return sql[start..i];
    }

    /// <inheritdoc />
    public void ExecuteSQL(string sql)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);

        // §2 instrumentation (2026-09-15): on the standalone-statement shape the storage write measures 0.8 µs
        // and the WAL 0.02 µs, so ~75 % of a warm ~60 µs/statement is unattributed and lives in the statement
        // machinery below the table. This is one of the two phases that had no stamp at all.
        long statementValidateStart = SharpCoreDB.Diagnostics.WritePathProfiler.Stamp();
        SqlQueryValidator.ValidateQuery(
            sql, 
            null, 
            config?.SqlValidationMode ?? SqlQueryValidator.ValidationMode.Lenient,
            config?.StrictParameterValidation ?? true);
        SharpCoreDB.Diagnostics.WritePathProfiler.Add(
            SharpCoreDB.Diagnostics.WritePathProfiler.Stage.StatementValidate, statementValidateStart);

        if (FirstToken(sql).Equals(SqlConstants.SELECT.AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            // ✅ CRITICAL FIX: Flush dirty data BEFORE SELECT
            // This ensures SELECT sees all uncommitted inserts/updates/deletes
            // Without this, SELECTs run against stale in-memory state
            if (_metadataDirty || _batchUpdateActive)
            {
                Flush();
            }
            
            ExecuteSelectQuery(sql, null);
            return;
        }

        // ⚠️ REMOVED (2026-09-15): this block warmed the plan cache for DML
        // (`GetOrAddPlan(sql, null, SqlCommandType.INSERT/UPDATE/DELETE)`) and was pure waste on this path. Two
        // facts, both verified rather than assumed:
        //   * the return value is DISCARDED here, and nothing anywhere reads a DML plan entry — `CachedPlan` is
        //     consumed in exactly two places (`Database.Core.cs:939`, the struct-query path, and `ExecuteQuery`
        //     below), both SELECT paths, and the only reader, `TryGetCachedPlan`, has no callers at all;
        //   * the key is built from the normalized SQL *including literal values* (`GetNormalizedSql` collapses
        //     whitespace only), so statements that differ just in their literals — a benchmark of 20,000 distinct
        //     INSERTs, or any application that inlines values — miss every time regardless.
        // So every statement paid a normalized copy, a cache key, a `Split` of the whole statement, a
        // `CachedQueryPlan`, a cache insert and `DateTime.UtcNow` for a plan nothing reads, while the cache
        // accumulated one unusable entry per distinct statement. Measured at **~21 µs of a warm
        // 59.64 µs statement** (`--multirowinsert`, 1 row/statement, median of 5), attributed by the write-path
        // profiler as `parse`. The statement classification that fed it is gone with it — it had no other use.
        // The SELECT path keeps its cache, which is consumed and does hit (`Database.Core.cs:934`). Making DML
        // plans *useful* would be a feature, not this change: the DML path would have to accept a plan argument,
        // because `sqlParser.Execute(sql, …)` takes raw SQL.

        // ✅ UNIFIED: Use IStorageEngine for all DML operations
        // StorageEngine handles WAL, transactions, and batching consistently
        // No more separate GroupCommitWAL logic - it's integrated into the engine
        // §2 instrumentation (2026-09-15): outer stamp for the last unattributed bucket on this shape — lock
        // acquisition, the shared-parser fetch and the hand-off. `parse` nests inside, so the delta is the cost.
        long dispatchStart = SharpCoreDB.Diagnostics.WritePathProfiler.Stamp();
        lock (_walLock)
        {
            var sqlParser = GetSharedSqlParser();
            sqlParser.Execute(sql, null);
            SharpCoreDB.Diagnostics.WritePathProfiler.Add(
                SharpCoreDB.Diagnostics.WritePathProfiler.Stage.Dispatch, dispatchStart);
            
            if (!isReadOnly && IsSchemaChangingCommand(sql))
            {
                ForceSave();
                ApplyColumnarCompactionThresholdToTables();
            }
            else if (!isReadOnly)
            {
                _metadataDirty = true;
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

        if (FirstToken(sql).Equals(SqlConstants.SELECT.AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            // ✅ CRITICAL FIX: Flush dirty data BEFORE SELECT
            // This ensures SELECT sees all uncommitted inserts/updates/deletes
            // Without this, SELECTs run against stale in-memory state
            if (_metadataDirty || _batchUpdateActive)
            {
                Flush();
            }
            
            ExecuteSelectQuery(sql, parameters);
            return;
        }

        // ⚠️ REMOVED (2026-09-15): the identical dead plan-cache warm-up for DML — same discarded return, same
        // absence of any reader of DML plan entries (full reasoning in the non-parameterized overload above).
        // Repeated parameterized DML would have hit this cache, but nothing reads the entries, so the whole
        // chain was cost without effect.

        // ✅ UNIFIED: Use IStorageEngine for all DML operations
        // StorageEngine handles WAL, transactions, and batching consistently
        // No more separate GroupCommitWAL logic - it's integrated into the engine
        lock (_walLock)
        {
            var sqlParser = GetSharedSqlParser();
            sqlParser.Execute(sql, parameters, null);
            
            if (!isReadOnly && IsSchemaChangingCommand(sql))
            {
                ForceSave();
                ApplyColumnarCompactionThresholdToTables();
            }
            else if (!isReadOnly)
            {
                _metadataDirty = true;
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

        if (FirstToken(sql).Equals(SqlConstants.SELECT.AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            await ExecuteSelectQueryAsync(sql, null, cancellationToken).ConfigureAwait(false);
            return;
        }

        // ✅ Cache plans for DML: INSERT, UPDATE, DELETE
        if (FirstToken(sql).Equals(SqlConstants.INSERT.AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            GetOrAddPlan(sql, null, SqlCommandType.INSERT);
        }
        else if (FirstToken(sql).Equals(SqlConstants.UPDATE.AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            GetOrAddPlan(sql, null, SqlCommandType.UPDATE);
        }
        else if (FirstToken(sql).Equals(SqlConstants.DELETE.AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            GetOrAddPlan(sql, null, SqlCommandType.DELETE);
        }

        // ✅ UNIFIED: Use IStorageEngine for all DML operations
        // StorageEngine handles WAL, transactions, and batching consistently
        // No more separate GroupCommitWAL logic - it's integrated into the engine
        lock (_walLock)
        {
            var sqlParser = GetSharedSqlParser();
            sqlParser.Execute(sql, null);
            
            if (!isReadOnly && IsSchemaChangingCommand(sql))
            {
                ForceSave();
                ApplyColumnarCompactionThresholdToTables();
            }
            else if (!isReadOnly)
            {
                _metadataDirty = true;
            }
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

        if (FirstToken(sql).Equals(SqlConstants.SELECT.AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            await ExecuteSelectQueryAsync(sql, parameters, cancellationToken).ConfigureAwait(false);
            return;
        }

        // ✅ Cache plans for DML: INSERT, UPDATE, DELETE
        if (FirstToken(sql).Equals(SqlConstants.INSERT.AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            GetOrAddPlan(sql, parameters, SqlCommandType.INSERT);
        }
        else if (FirstToken(sql).Equals(SqlConstants.UPDATE.AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            GetOrAddPlan(sql, parameters, SqlCommandType.UPDATE);
        }
        else if (FirstToken(sql).Equals(SqlConstants.DELETE.AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            GetOrAddPlan(sql, parameters, SqlCommandType.DELETE);
        }

        // ✅ UNIFIED: Use IStorageEngine for all DML operations
        // StorageEngine handles WAL, transactions, and batching consistently
        // No more separate GroupCommitWAL logic - it's integrated into the engine
        lock (_walLock)
        {
            var sqlParser = GetSharedSqlParser();
            sqlParser.Execute(sql, parameters, null);
            
            if (!isReadOnly && IsSchemaChangingCommand(sql))
            {
                ForceSave();
                ApplyColumnarCompactionThresholdToTables();
            }
            else if (!isReadOnly)
            {
                _metadataDirty = true;
            }
        }
    }

    /// <summary>
    /// Executes SELECT query with plan caching.
    /// </summary>
    private void ExecuteSelectQuery(string sql, Dictionary<string, object?>? parameters)
    {
        var sqlParser = GetSharedSqlParser();
        sqlParser.Execute(sql, parameters ?? new Dictionary<string, object?>());
    }

    /// <summary>
    /// Executes SELECT query asynchronously with plan caching.
    /// </summary>
    private async Task ExecuteSelectQueryAsync(string sql, Dictionary<string, object?>? parameters, CancellationToken cancellationToken)
    {
        await Task.Run(() =>
        {
            var sqlParser = GetSharedSqlParser();
            sqlParser.Execute(sql, parameters ?? new Dictionary<string, object?>());
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Executes a query and returns the results.
    /// </summary>
    /// <param name="sql">The SQL query.</param>
    /// <param name="parameters">Optional query parameters.</param>
    /// <returns>The query results.</returns>
    public List<Dictionary<string, object>> ExecuteQuery(string sql, Dictionary<string, object?>? parameters = null)
    {
        // ✅ FIX (Known Issue 4): Flush pending BATCH (ITable bulk/transaction) writes before
        // executing the query so they are visible without an explicit database.Flush().
        // NOTE: We deliberately do NOT flush on plain _metadataDirty — SQL INSERTs are already
        // visible in-memory on the default engine, and a blanket metadata flush here breaks
        // read-after-write for the page-based engine (regression observed in the full suite).
        if (_batchUpdateActive)
        {
            Flush();
        }

        var entry = GetOrAddPlan(sql, parameters, SqlCommandType.SELECT);
        var sqlParser = GetSharedSqlParser();
        if (entry is not null)
            return sqlParser.ExecuteQuery(entry.CachedPlan, parameters);
        return sqlParser.ExecuteQuery(sql, parameters);
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
        // ✅ FIX (Known Issue 4): Flush pending BATCH writes so they are visible without an
        // explicit database.Flush() (see the parameterless overload for the _metadataDirty note).
        if (_batchUpdateActive)
        {
            Flush();
        }

        var sqlParser = GetSharedSqlParser();
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
        var sqlParser = GetSharedSqlParser();
        return sqlParser.ExecuteQuery(cached, parameters);
    }

    /// <summary>
    /// Executes a prepared statement with compiled query optimization.
    /// ✅ PERFORMANCE FIX: Use CompiledQueryExecutor for compiled plans, fallback to SqlParser.
    /// This provides true zero-parsing execution for compiled queries.
    /// Reduces execution time from ~12,793ms to ~8ms for 1000 compiled queries (1600x faster).
    /// </summary>
    /// <param name="stmt">The prepared statement.</param>
    /// <param name="parameters">Optional query parameters.</param>
    /// <returns>The query results.</returns>
    public List<Dictionary<string, object>> ExecuteCompiledQuery(DataStructures.PreparedStatement stmt, Dictionary<string, object?>? parameters = null)
    {
        ArgumentNullException.ThrowIfNull(stmt);
        
        // ✅ CRITICAL FIX: Flush dirty data BEFORE executing compiled query
        // This ensures the compiled query executor sees all uncommitted inserts/updates/deletes
        // Without this, ExecuteCompiledQuery reads stale in-memory state (the "141/200 mystery")
        // NOTE: If this flush causes performance regression in compiled queries, investigate
        // optimizing the flush mechanism or caching table state differently. The flush is
        // necessary because ExecuteSQL writes to storage but in-memory tables may be stale.
        // See Database.Execution.cs ExecuteSQL() SELECT path for the same pattern.
        if (_metadataDirty || _batchUpdateActive)
        {
            Flush();
        }
        
        // ✅ Use CompiledQueryExecutor if available for maximum performance
        if (stmt.CompiledPlan is not null)
        {
            var executor = new Services.CompiledQueryExecutor(tables);
            return executor.Execute(stmt.CompiledPlan, parameters);
        }
        
        // ✅ Fallback: Lazy-initialize shared SqlParser (thread-safe via Interlocked)
        if (_sharedSqlParser is null)
        {
            _sharedSqlParser = new SqlParser(tables, _dbPath, storage, isReadOnly, queryCache, config);
            _sharedSqlParser.Database = this;
        }

        return _sharedSqlParser.ExecuteQuery(stmt.Plan, parameters);
    }

    /// <inheritdoc />
    public Dictionary<string, object>? FindByPrimaryKey(string tableName, object key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        ArgumentNullException.ThrowIfNull(key);

        if (!tables.TryGetValue(tableName, out var table))
            return null;

        return table.FindByPrimaryKey(key);
    }

    /// <inheritdoc />
    public List<Dictionary<string, object>> FindByIndex(string tableName, string column, object value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        ArgumentException.ThrowIfNullOrWhiteSpace(column);
        ArgumentNullException.ThrowIfNull(value);

        if (!tables.TryGetValue(tableName, out var table))
            return [];

        return table.FindByIndex(column, value);
    }

    /// <inheritdoc />
    public bool UpdateByPrimaryKey(string tableName, object key, Dictionary<string, object> updates)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(updates);

        if (!tables.TryGetValue(tableName, out var table))
            return false;

        lock (_walLock)
        {
            bool result = table.UpdateByPrimaryKey(key, updates);
            if (result && !isReadOnly)
            {
                _metadataDirty = true;
            }

            return result;
        }
    }

    /// <inheritdoc />
    public bool DeleteByPrimaryKey(string tableName, object key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        ArgumentNullException.ThrowIfNull(key);

        if (!tables.TryGetValue(tableName, out var table))
            return false;

        lock (_walLock)
        {
            bool result = table.DeleteByPrimaryKey(key);
            if (result && !isReadOnly)
            {
                _metadataDirty = true;
            }

            return result;
        }
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

    /// <summary>
    /// Flushes pending data to disk synchronously.
    /// ✅ UNIFIED: Uses the storage engine's flush mechanism instead of GroupCommitWAL
    /// Used for testing and explicit durability control.
    /// </summary>
    public void FlushPendingWalStatements()
    {
        // ✅ Delegate to unified storage engine flush
        Flush();
    }
}
