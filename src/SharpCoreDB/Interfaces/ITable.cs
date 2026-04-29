// <copyright file="ITable.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Interfaces;

using SharpCoreDB.Storage.Hybrid;

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
    /// Gets whether this table has an auto-generated internal <c>_rowid</c> column.
    /// When true, the table was created without an explicit PRIMARY KEY and SharpCoreDB
    /// automatically injected a ULID-based <c>_rowid</c> column as the primary key.
    /// The <c>_rowid</c> column is hidden from <c>SELECT *</c> results but can be
    /// queried explicitly via <c>SELECT _rowid, ...</c>.
    /// </summary>
    /// <remarks>
    /// This follows the SQLite rowid pattern: every table has a unique row identifier,
    /// but it is only visible when explicitly requested. Unlike SQLite's integer rowid,
    /// SharpCoreDB uses ULID which is globally unique and lexicographically sortable.
    /// </remarks>
    bool HasInternalRowId { get; }

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
    /// Gets the per-column collation types.
    /// Follows the same per-column list pattern as <see cref="IsAuto"/>, <see cref="IsNotNull"/>, etc.
    /// Defaults to <see cref="CollationType.Binary"/> for all columns.
    /// </summary>
    List<CollationType> ColumnCollations { get; }

    /// <summary>
    /// Gets the per-column locale names for <see cref="CollationType.Locale"/> columns.
    /// Null entries indicate no locale (non-Locale collation types).
    /// ✅ Phase 9: Parallel list to <see cref="ColumnCollations"/>.
    /// </summary>
    List<string?> ColumnLocaleNames { get; }

    // ── DDL lifecycle members ───────────────────────────────────────────────
    // These have default implementations so existing test fakes that implement
    // ITable for read-only purposes do not need to be updated.

    /// <summary>
    /// Gets or sets the storage engine mode for this table.
    /// Directory-mode tables use <see cref="StorageMode.Columnar"/> or <see cref="StorageMode.PageBased"/>.
    /// Single-file tables default to <see cref="StorageMode.Columnar"/> and the value is schema-only.
    /// </summary>
    StorageMode StorageMode { get => StorageMode.Columnar; set { } }

    /// <summary>
    /// Gets or sets the primary key B-tree index.
    /// Directory-mode tables use a full BTree; single-file tables use a no-op implementation.
    /// </summary>
    IIndex<string, long> Index { get => NullIndex.Instance; set { } }

    /// <summary>
    /// Gets or sets per-column DEFAULT expressions (SQL expression strings).
    /// </summary>
    List<string?> DefaultExpressions { get => []; set { } }

    /// <summary>
    /// Gets or sets per-column CHECK constraint expressions.
    /// </summary>
    List<string?> ColumnCheckExpressions { get => []; set { } }

    /// <summary>
    /// Gets or sets table-level CHECK constraint expressions.
    /// </summary>
    List<string> TableCheckConstraints { get => []; set { } }

    /// <summary>
    /// Initializes (or re-initializes) the underlying storage engine for this table.
    /// For directory-mode tables this opens or creates the columnar/page-based file.
    /// For single-file tables this is a no-op (storage is managed by the provider).
    /// </summary>
    void InitializeStorageEngine() { }

    /// <summary>
    /// Returns true if an index (by name or column name) exists on this table.
    /// Used by <c>CREATE INDEX IF NOT EXISTS</c> to avoid duplicate creation.
    /// Single-file tables without a real index registry always return false.
    /// </summary>
    /// <param name="nameOrColumn">Index name or column name to check.</param>
    /// <returns>True if the index exists.</returns>
    bool HasIndex(string nameOrColumn) => false;

    /// <summary>
    /// Applies the given <paramref name="schema"/> definition to this table instance.
    /// Called by <c>SqlParser.ExecuteCreateTable</c> after the factory creates the table
    /// object but before it is registered or its storage is initialised.
    /// The default implementation is a no-op; concrete classes override this to populate
    /// their internal schema fields without requiring individual property setters on the interface.
    /// </summary>
    /// <param name="schema">The full schema definition produced by DDL parsing.</param>
    void ApplySchema(TableSchemaDefinition schema) { }

    // ── Insert ──────────────────────────────────────────────────────────────

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
    /// Finds a single row by primary key value, bypassing SQL parsing.
    /// Returns null if not found.
    /// </summary>
    /// <param name="key">The primary key value.</param>
    /// <returns>The matching row or null.</returns>
    Dictionary<string, object>? FindByPrimaryKey(object key);

    /// <summary>
    /// Finds rows matching a value in the specified indexed column, bypassing SQL parsing.
    /// Requires a hash index on the column.
    /// </summary>
    /// <param name="column">The indexed column name.</param>
    /// <param name="value">The value to search for.</param>
    /// <returns>Matching rows.</returns>
    List<Dictionary<string, object>> FindByIndex(string column, object value);

    /// <summary>
    /// Updates a single row identified by primary key, bypassing SQL parsing.
    /// Returns true if a row was found and updated.
    /// </summary>
    /// <param name="key">The primary key value.</param>
    /// <param name="updates">The column updates to apply.</param>
    /// <returns>True if a row was updated.</returns>
    bool UpdateByPrimaryKey(object key, Dictionary<string, object> updates);

    /// <summary>
    /// Deletes a single row identified by primary key, bypassing SQL parsing.
    /// Returns true if a row was found and deleted.
    /// </summary>
    /// <param name="key">The primary key value.</param>
    /// <returns>True if a row was deleted.</returns>
    bool DeleteByPrimaryKey(object key);

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
    /// <param name="isUnique">Whether to enforce uniqueness (default: false).</param>
    void CreateHashIndex(string indexName, string columnName, bool isUnique = false);

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
    /// <param name="isUnique">Whether to enforce uniqueness (default: false).</param>
    void CreateBTreeIndex(string indexName, string columnName, bool isUnique = false);

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

    /// <summary>
    /// Drops a column from the table schema and removes its data from all rows.
    /// Used for ALTER TABLE DROP COLUMN operations.
    /// </summary>
    /// <param name="columnName">The name of the column to drop (case-insensitive).</param>
    void DropColumn(string columnName);

    /// <summary>
    /// Renames a column in the table schema.
    /// Used for ALTER TABLE RENAME COLUMN operations.
    /// </summary>
    /// <param name="oldName">The current column name (case-insensitive).</param>
    /// <param name="newName">The new column name.</param>
    void RenameColumn(string oldName, string newName);

    /// <summary>
    /// Sets an extensible metadata value on this table.
    /// Used by optional features such as vector indexes.
    /// </summary>
    /// <param name="key">The metadata key (case-insensitive).</param>
    /// <param name="value">The metadata value.</param>
    void SetMetadata(string key, object value);

    /// <summary>
    /// Gets an extensible metadata value, or null if not found.
    /// </summary>
    /// <param name="key">The metadata key (case-insensitive).</param>
    /// <returns>The metadata value, or null.</returns>
    object? GetMetadata(string key);

    /// <summary>
    /// Removes an extensible metadata entry.
    /// </summary>
    /// <param name="key">The metadata key.</param>
    /// <returns>True if the entry was removed.</returns>
    bool RemoveMetadata(string key);

    // ── Private helper singleton ─────────────────────────────────────────────

    /// <summary>
    /// No-op <see cref="IIndex{TKey,TValue}"/> singleton returned by the default
    /// <see cref="Index"/> property implementation.
    /// </summary>
    private sealed class NullIndex : IIndex<string, long>
    {
        internal static readonly NullIndex Instance = new();
        public void Insert(string key, long value) { }
        public (bool Found, long Value) Search(string key) => (false, 0);
        public bool Delete(string key) => false;
        public void Clear() { }
    }
}
