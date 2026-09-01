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
/// ✅ NEW: last_insert_rowid() support for SQLite compatibility.
/// </summary>
public interface IDatabase : IAsyncDisposable
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
    /// Executes a simple point-lookup SELECT and returns zero-allocation <see cref="SharpCoreDB.DataStructures.StructRow"/>
    /// results (the v2 fast-path API). The returned <see cref="SharpCoreDB.DataStructures.StructRowQueryEnumerable"/>
    /// is a struct — foreach on it is allocation-free. Avoids per-row Dictionary allocations and value boxing.
    /// Supports the simple "SELECT [*|col] FROM t [WHERE col = @param|'literal'] [LIMIT n]" shape;
    /// more complex queries throw <see cref="NotSupportedException"/>.
    /// Default implementation throws — <see cref="Database"/> overrides with the real implementation.
    /// </summary>
    /// <param name="sql">The SQL query.</param>
    /// <param name="parameters">The parameters.</param>
    /// <returns>Zero-allocation filtered enumeration of StructRow instances.</returns>
    SharpCoreDB.DataStructures.StructRowQueryEnumerable ExecuteQueryStruct(string sql, Dictionary<string, object?>? parameters = null)
        => throw new NotSupportedException("ExecuteQueryStruct is not supported by this IDatabase implementation.");

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
    /// Finds a single row by primary key, bypassing SQL parsing entirely.
    /// Returns null if the table or key is not found.
    /// </summary>
    /// <param name="tableName">The table name.</param>
    /// <param name="key">The primary key value.</param>
    /// <returns>The matching row or null.</returns>
    Dictionary<string, object>? FindByPrimaryKey(string tableName, object key);

    /// <summary>
    /// Finds rows by an indexed column value, bypassing SQL parsing entirely.
    /// Requires a hash index on the column.
    /// </summary>
    /// <param name="tableName">The table name.</param>
    /// <param name="column">The indexed column name.</param>
    /// <param name="value">The value to search for.</param>
    /// <returns>Matching rows.</returns>
    List<Dictionary<string, object>> FindByIndex(string tableName, string column, object value);

    /// <summary>
    /// Updates a row by primary key, bypassing SQL parsing entirely.
    /// Returns true if a row was found and updated.
    /// </summary>
    /// <param name="tableName">The table name.</param>
    /// <param name="key">The primary key value.</param>
    /// <param name="updates">Column updates to apply.</param>
    /// <returns>True if a row was updated.</returns>
    bool UpdateByPrimaryKey(string tableName, object key, Dictionary<string, object> updates);

    /// <summary>
    /// Deletes a row by primary key, bypassing SQL parsing entirely.
    /// Returns true if a row was found and deleted.
    /// </summary>
    /// <param name="tableName">The table name.</param>
    /// <param name="key">The primary key value.</param>
    /// <returns>True if a row was deleted.</returns>
    bool DeleteByPrimaryKey(string tableName, object key);
    
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
    /// Changes the encryption password of an encrypted single-file database (envelope-encryption
    /// mode). The data-encryption-key is unchanged — only the wrapped-DEK password bundle is
    /// re-wrapped, so this is an O(1) operation that does not rewrite any data.
    /// </summary>
    /// <param name="newPassword">The new password/passphrase.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Rotation result.</returns>
    Task<EncryptionRotationResult> ChangeEncryptionPasswordAsync(string newPassword, CancellationToken cancellationToken = default);

    /// <summary>
    /// Rotates the data-encryption-key of an encrypted single-file database (full re-key).
    /// Every block plus the block registry, free-space map and WAL are re-encrypted under a
    /// new key. Provide exactly one of <paramref name="newKey"/> (raw-key mode) or
    /// <paramref name="newPassword"/> (password mode).
    /// </summary>
    /// <param name="newKey">New raw 32-byte encryption key (raw-key mode).</param>
    /// <param name="newPassword">New password (password mode) — a fresh DEK is generated and wrapped.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Rotation result.</returns>
    Task<EncryptionRotationResult> RotateEncryptionKeyAsync(byte[]? newKey = null, string? newPassword = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the database storage mode (Directory or SingleFile).
    /// </summary>
    StorageMode StorageMode { get; }

    /// <summary>
    /// Gets the ROWID of the most recent successful INSERT operation.
    /// Returns the row position (file offset or page slot) of the last inserted row.
    /// Thread-safe: Each thread has its own last insert rowid.
    /// Compatible with SQLite's last_insert_rowid() function.
    /// </summary>
    /// <returns>The ROWID of the last inserted row, or 0 if no inserts have occurred.</returns>
    long GetLastInsertRowId();

    /// <summary>
    /// Gets the number of rows affected by the most recently executed DML statement
    /// (INSERT/UPDATE/DELETE). Returns 0 for DDL and for statements that affected no rows.
    /// Compatible with SQLite's changes() function.
    /// </summary>
    /// <returns>The affected-row count of the last DML statement, or 0.</returns>
    int GetLastChanges();

    /// <summary>
    /// Attempts to get a table by name.
    /// </summary>
    /// <param name="tableName">The table name.</param>
    /// <param name="table">The table instance if found.</param>
    /// <returns>True if the table exists.</returns>
    bool TryGetTable(string tableName, out ITable table);

    /// <summary>
    /// Gets table metadata for schema discovery (SQLite compatibility).
    /// </summary>
    /// <returns>List of tables in the database.</returns>
    IReadOnlyList<TableInfo> GetTables();

    /// <summary>
    /// Gets column metadata for a table (SQLite compatibility).
    /// </summary>
    /// <param name="tableName">The table name.</param>
    /// <returns>List of columns for the table.</returns>
    IReadOnlyList<ColumnInfo> GetColumns(string tableName);

    /// <summary>
    /// Gets storage statistics (file size, fragmentation, block count, etc.).
    /// </summary>
    /// <returns>Storage statistics</returns>
    StorageStatistics GetStorageStatistics();

    /// <summary>
    /// Gets whether this database was created before 1.9.5 and may contain ULIDs stored in the
    /// legacy (pre-spec) Base32 encoding that need to be converted with <see cref="MigrateLegacyUlids"/>.
    /// The database metadata records the ULID encoding generation it was created with, so a database
    /// created before 1.9.5 is detected automatically.
    /// </summary>
    /// <returns>True when the database predates 1.9.5 and should be migrated; otherwise false.</returns>
    bool NeedsLegacyUlidMigration();

    /// <summary>
    /// Converts every ULID value stored in this database from the legacy pre-1.9.5 Base32 encoding
    /// to the ULID-spec-compliant encoding. The 128-bit value (timestamp + randomness) of every ULID
    /// is preserved exactly — only the Base32 text changes — so existing <c>_rowid</c> values and
    /// ULID columns migrate one-to-one. After a successful migration the database is permanently
    /// marked as spec-compliant and further calls are no-ops.
    /// </summary>
    /// <returns>The number of rows whose ULID values were rewritten.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the database is read-only or a row
    /// cannot be located while migrating.</exception>
    int MigrateLegacyUlids();
}
