// <copyright file="IDatabase.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Interfaces;

using SharpCoreDB.DataStructures;

/// <summary>
/// Interface for the database engine.
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
}
