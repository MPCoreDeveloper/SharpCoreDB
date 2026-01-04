// <copyright file="IDatabase.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Interfaces;

using SharpCoreDB.DataStructures;
using SharpCoreDB.Storage;

/// <summary>
/// Interface for the database engine.
/// ✅ NEW: Compiled query support for zero-parse execution (5-10x faster).
/// ✅ NEW: VACUUM support for single-file storage defragmentation.
/// </summary>
public interface IDatabase
{
    /// <summary>
    /// Initializes the database with a master password.
    /// </summary>
    /// <param name="dbPath">The database path.</param>
    /// <param name="masterPassword">The master password.</param>
    /// <returns>The initialized database instance.</returns>
    IDatabase Initialize(string dbPath, string masterPassword);

    /// <summary>
    /// Executes a SQL command.
    /// </summary>
    /// <param name="sql">The SQL command.</param>
    void ExecuteSQL(string sql);

    /// <summary>
    /// Executes a parameterized SQL command.
    /// </summary>
    /// <param name="sql">The SQL command with ? placeholders.</param>
    /// <param name="parameters">The parameters to bind.</param>
    void ExecuteSQL(string sql, Dictionary<string, object?> parameters);

    /// <summary>
    /// Executes a SQL command asynchronously.
    /// </summary>
    /// <param name="sql">The SQL command.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task ExecuteSQLAsync(string sql, CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a parameterized SQL command asynchronously.
    /// </summary>
    /// <param name="sql">The SQL command with ? placeholders.</param>
    /// <param name="parameters">The parameters to bind.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task ExecuteSQLAsync(string sql, Dictionary<string, object?> parameters, CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes multiple SQL commands in a batch for improved performance.
    /// </summary>
    /// <param name="sqlStatements">Collection of SQL statements to execute.</param>
    void ExecuteBatchSQL(IEnumerable<string> sqlStatements);

    /// <summary>
    /// Executes multiple SQL commands in a batch asynchronously.
    /// </summary>
    /// <param name="sqlStatements">Collection of SQL statements to execute.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task ExecuteBatchSQLAsync(IEnumerable<string> sqlStatements, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a user.
    /// </summary>
    /// <param name="username">The username.</param>
    /// <param name="password">The password.</param>
    void CreateUser(string username, string password);

    /// <summary>
    /// Logs in a user.
    /// </summary>
    /// <param name="username">The username.</param>
    /// <param name="password">The password.</param>
    /// <returns>True if login successful.</returns>
    bool Login(string username, string password);

    /// <summary>
    /// Gets query cache statistics.
    /// </summary>
    /// <returns>A tuple containing cache hits, misses, hit rate, and total cached queries.</returns>
    (long Hits, long Misses, double HitRate, int Count) GetQueryCacheStatistics();

    /// <summary>
    /// Clears the query cache.
    /// </summary>
    void ClearQueryCache();

    /// <summary>
    /// Prepares a SQL statement for efficient repeated execution.
    /// </summary>
    /// <param name="sql">The SQL statement to prepare.</param>
    /// <returns>A prepared statement instance.</returns>
    SharpCoreDB.DataStructures.PreparedStatement Prepare(string sql);

    /// <summary>
    /// Executes a prepared statement with parameters.
    /// </summary>
    /// <param name="stmt">The prepared statement.</param>
    /// <param name="parameters">The parameters to bind.</param>
    void ExecutePrepared(SharpCoreDB.DataStructures.PreparedStatement stmt, Dictionary<string, object?> parameters);

    /// <summary>
    /// Executes a prepared statement asynchronously with parameters.
    /// </summary>
    /// <param name="stmt">The prepared statement.</param>
    /// <param name="parameters">The parameters to bind.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task ExecutePreparedAsync(SharpCoreDB.DataStructures.PreparedStatement stmt, Dictionary<string, object?> parameters, CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a query and returns the results.
    /// </summary>
    /// <param name="sql">The SQL query.</param>
    /// <param name="parameters">The parameters.</param>
    /// <returns>The query results.</returns>
    List<Dictionary<string, object>> ExecuteQuery(string sql, Dictionary<string, object?>? parameters = null);

    /// <summary>
    /// Executes a query and returns the results with optional encryption bypass.
    /// </summary>
    /// <param name="sql">The SQL query.</param>
    /// <param name="parameters">The parameters.</param>
    /// <param name="noEncrypt">If true, bypasses encryption for this query.</param>
    /// <returns>The query results.</returns>
    List<Dictionary<string, object>> ExecuteQuery(string sql, Dictionary<string, object?> parameters, bool noEncrypt);

    /// <summary>
    /// Gets whether a batch UPDATE transaction is currently active.
    /// </summary>
    bool IsBatchUpdateActive { get; }

    /// <summary>
    /// Begins a batch UPDATE transaction for improved performance.
    /// All index updates are deferred until EndBatchUpdate() is called.
    /// </summary>
    void BeginBatchUpdate();

    /// <summary>
    /// Ends the batch UPDATE transaction and commits changes.
    /// All deferred indexes are rebuilt and WAL is flushed.
    /// </summary>
    void EndBatchUpdate();

    /// <summary>
    /// Cancels the active batch UPDATE transaction (rollback).
    /// </summary>
    void CancelBatchUpdate();

    /// <summary>
    /// Executes a compiled query plan (zero parsing overhead).
    /// Expected performance: 5-10x faster than ExecuteQuery for repeated queries.
    /// Target: 1000 identical SELECTs in less than 8ms total.
    /// </summary>
    /// <param name="plan">The compiled query plan.</param>
    /// <param name="parameters">The query parameters.</param>
    /// <returns>The query results.</returns>
    List<Dictionary<string, object>> ExecuteCompiled(CompiledQueryPlan plan, Dictionary<string, object?>? parameters = null);

    /// <summary>
    /// Executes a prepared statement with compiled query optimization.
    /// Uses zero-parse execution for SELECT queries with compiled plans.
    /// </summary>
    /// <param name="stmt">The prepared statement.</param>
    /// <param name="parameters">The query parameters.</param>
    /// <returns>The query results.</returns>
    List<Dictionary<string, object>> ExecuteCompiledQuery(DataStructures.PreparedStatement stmt, Dictionary<string, object?>? parameters = null);

    /// <summary>
    /// Flushes all pending changes to disk and saves metadata.
    /// This ensures all CREATE TABLE, INSERT, UPDATE, DELETE operations are persisted.
    /// Call this after batch operations or before closing the connection if you want to guarantee persistence.
    /// </summary>
    void Flush();
    
    /// <summary>
    /// Forces metadata to be saved to disk, ignoring the dirty flag.
    /// Used internally by the provider to ensure persistence on connection close.
    /// </summary>
    void ForceSave();

    /// <summary>
    /// Performs VACUUM operation on single-file databases to reclaim space and reduce fragmentation.
    /// For directory-based databases, this operation is a no-op.
    /// </summary>
    /// <param name="mode">VACUUM mode (Quick, Incremental, or Full)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>VACUUM result with statistics</returns>
    Task<VacuumResult> VacuumAsync(VacuumMode mode = VacuumMode.Quick, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the database storage mode (Directory or SingleFile).
    /// </summary>
    StorageMode StorageMode { get; }

    /// <summary>
    /// Gets storage statistics (file size, fragmentation, block count, etc.).
    /// </summary>
    /// <returns>Storage statistics</returns>
    StorageStatistics GetStorageStatistics();
}
