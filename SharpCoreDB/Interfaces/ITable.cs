// <copyright file="ITable.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Interfaces;

/// <summary>
/// Interface for a database table.
/// </summary>
public interface ITable
{
    /// <summary>
    /// Gets the table name.
    /// </summary>
    string Name { get; set; }

    /// <summary>
    /// Gets the column names.
    /// </summary>
    List<string> Columns { get; }

    /// <summary>
    /// Gets the column types.
    /// </summary>
    List<DataType> ColumnTypes { get; }

    /// <summary>
    /// Gets the data file path.
    /// </summary>
    string DataFile { get; set; }

    /// <summary>
    /// Gets the primary key column index.
    /// </summary>
    int PrimaryKeyIndex { get; }

    /// <summary>
    /// Gets whether columns are auto-generated.
    /// </summary>
    List<bool> IsAuto { get; }

    /// <summary>
    /// Gets whether columns are NOT NULL.
    /// </summary>
    List<bool> IsNotNull { get; }

    /// <summary>
    /// Gets the default values for columns.
    /// </summary>
    List<object?> DefaultValues { get; }

    /// <summary>
    /// Gets the foreign key constraints.
    /// </summary>
    List<ForeignKeyConstraint> ForeignKeys { get; }

    /// <summary>
    /// Gets the unique constraints (column names or composite).
    /// </summary>
    List<List<string>> UniqueConstraints { get; }

    /// <summary>
    /// Inserts a row into the table.
    /// </summary>
    /// <param name="row">The row data.</param>
    void Insert(Dictionary<string, object> row);

    /// <summary>
    /// Inserts multiple rows into the table in a single batch operation.
    /// CRITICAL PERFORMANCE: Uses AppendBytesMultiple for 5-10x faster bulk inserts.
    /// </summary>
    /// <param name="rows">The list of rows to insert.</param>
    /// <returns>Array of file positions where each row was written.</returns>
    long[] InsertBatch(List<Dictionary<string, object>> rows);

    /// <summary>
    /// ✅ NEW: Inserts multiple rows from binary-encoded buffer (zero-allocation path).
    /// Uses StreamingRowEncoder format to avoid Dictionary materialization.
    /// Expected: 40-60% faster than InsertBatch() for large batches (10K+ rows).
    /// </summary>
    /// <param name="encodedData">Binary-encoded row data from StreamingRowEncoder.</param>
    /// <param name="rowCount">Number of rows encoded in the buffer.</param>
    /// <returns>Array of file positions where each row was written.</returns>
    long[] InsertBatchFromBuffer(ReadOnlySpan<byte> encodedData, int rowCount);

    /// <summary>
    /// Selects rows from the table with optional filtering and ordering.
    /// </summary>
    /// <param name="where">The where clause string.</param>
    /// <param name="orderBy">The column to order by.</param>
    /// <param name="asc">Whether to order ascending.</param>
    /// <returns>The selected rows.</returns>
    List<Dictionary<string, object>> Select(string? where = null, string? orderBy = null, bool asc = true);

    /// <summary>
    /// Selects rows from the table with optional filtering, ordering, and encryption bypass.
    /// </summary>
    /// <param name="where">The where clause string.</param>
    /// <param name="orderBy">The column to order by.</param>
    /// <param name="asc">Whether to order ascending.</param>
    /// <param name="noEncrypt">If true, bypasses encryption for this select.</param>
    /// <returns>The selected rows.</returns>
    List<Dictionary<string, object>> Select(string? where, string? orderBy, bool asc, bool noEncrypt);

    /// <summary>
    /// Updates rows in the table.
    /// </summary>
    /// <param name="where">The where clause string.</param>
    /// <param name="updates">The updates to apply.</param>
    void Update(string? where, Dictionary<string, object> updates);

    /// <summary>
    /// Deletes rows from the table.
    /// </summary>
    /// <param name="where">The where clause string.</param>
    void Delete(string? where);

    /// <summary>
    /// Creates a hash index on the specified column for fast WHERE clause lookups.
    /// </summary>
    /// <param name="columnName">The column name to index.</param>
    void CreateHashIndex(string columnName);

    /// <summary>
    /// Creates a named hash index on the specified column.
    /// Supports SQL syntax: CREATE INDEX idx_name ON table(column).
    /// </summary>
    /// <param name="indexName">The index name (e.g., "idx_email").</param>
    /// <param name="columnName">The column name to index (e.g., "email").</param>
    void CreateHashIndex(string indexName, string columnName);

    /// <summary>
    /// Checks if a hash index exists for the specified column.
    /// </summary>
    /// <param name="columnName">The column name.</param>
    /// <returns>True if index exists.</returns>
    bool HasHashIndex(string columnName);

    /// <summary>
    /// Gets hash index statistics for a column.
    /// </summary>
    /// <param name="columnName">The column name.</param>
    /// <returns>Index statistics or null if no index exists.</returns>
    (int UniqueKeys, int TotalRows, double AvgRowsPerKey)? GetHashIndexStatistics(string columnName);

    /// <summary>
    /// Increments the usage count for a column in WHERE clauses.
    /// </summary>
    /// <param name="columnName">The column name.</param>
    void IncrementColumnUsage(string columnName);

    /// <summary>
    /// Gets the column usage statistics.
    /// </summary>
    /// <returns>Dictionary of column names to usage counts.</returns>
    IReadOnlyDictionary<string, long> GetColumnUsage();

    /// <summary>
    /// Tracks usage for all columns (e.g., SELECT *).
    /// </summary>
    void TrackAllColumnsUsage();

    /// <summary>
    /// Tracks usage for a specific column.
    /// </summary>
    /// <param name="columnName">The column name.</param>
    void TrackColumnUsage(string columnName);

    /// <summary>
    /// Removes a hash index for the specified column.
    /// </summary>
    /// <param name="columnName">The column name.</param>
    /// <returns>True if index was removed, false if it didn't exist.</returns>
    bool RemoveHashIndex(string columnName);

    /// <summary>
    /// Clears all indexes (hash indexes, registrations, and state).
    /// Used when table is dropped or recreated to ensure complete cleanup.
    /// This prevents stale/corrupt index data from being read after DDL operations.
    /// </summary>
    void ClearAllIndexes();
    
    /// <summary>
    /// Gets the cached row count (O(1) operation).
    /// Returns -1 if cache is not initialized.
    /// ✅ PERFORMANCE: Avoids expensive full table scan in GetDatabaseStatistics()
    /// </summary>
    /// <returns>Number of rows in the table, or -1 if not cached.</returns>
    long GetCachedRowCount();

    /// <summary>
    /// Refreshes the cached row count by doing a full table scan.
    /// Call this once after loading the table from disk.
    /// </summary>
    void RefreshRowCount();

    /// <summary>
    /// Creates a B-tree index on the specified column for range queries.
    /// Supports: WHERE column &gt; value, WHERE column BETWEEN x AND y, ORDER BY column.
    /// </summary>
    /// <param name="columnName">The column name to index.</param>
    void CreateBTreeIndex(string columnName);

    /// <summary>
    /// Creates a named B-tree index on the specified column.
    /// Supports SQL syntax: CREATE INDEX idx_name ON table(column) USING BTREE.
    /// </summary>
    /// <param name="indexName">The index name (e.g., "idx_age_btree").</param>
    /// <param name="columnName">The column name to index (e.g., "age").</param>
    void CreateBTreeIndex(string indexName, string columnName);

    /// <summary>
    /// Checks if a B-tree index exists for the specified column.
    /// </summary>
    /// <param name="columnName">The column name.</param>
    /// <returns>True if B-tree index exists.</returns>
    bool HasBTreeIndex(string columnName);
    
    /// <summary>
    /// Flushes all pending writes to disk.
    /// Ensures INSERT/UPDATE/DELETE operations are persisted.
    /// </summary>
    void Flush();

    /// <summary>
    /// Adds a new column to the table schema.
    /// Used for ALTER TABLE ADD COLUMN operations.
    /// </summary>
    /// <param name="columnDef">The column definition to add.</param>
    void AddColumn(ColumnDefinition columnDef);
}
