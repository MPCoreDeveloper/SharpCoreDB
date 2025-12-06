// <copyright file="IDatabase.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace SharpCoreDB.Interfaces;

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
    /// Executes a SQL command asynchronously.
    /// </summary>
    /// <param name="sql">The SQL command.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task ExecuteSQLAsync(string sql, CancellationToken cancellationToken = default);

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
    /// Executes a parameterized SQL command.
    /// </summary>
    /// <param name="sql">The SQL command with ? placeholders.</param>
    /// <param name="parameters">The parameters to bind.</param>
    void ExecuteSQL(string sql, Dictionary<string, object?> parameters);

    /// <summary>
    /// Executes a parameterized SQL command asynchronously.
    /// </summary>
    /// <param name="sql">The SQL command with ? placeholders.</param>
    /// <param name="parameters">The parameters to bind.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task ExecuteSQLAsync(string sql, Dictionary<string, object?> parameters, CancellationToken cancellationToken = default);
}
