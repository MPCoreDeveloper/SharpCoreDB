// <copyright file="ITable.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
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
    string Name { get; }

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
    string DataFile { get; }

    /// <summary>
    /// Gets the primary key column index.
    /// </summary>
    int PrimaryKeyIndex { get; }

    /// <summary>
    /// Gets whether columns are auto-generated.
    /// </summary>
    List<bool> IsAuto { get; }

    /// <summary>
    /// Inserts a row into the table.
    /// </summary>
    /// <param name="row">The row data.</param>
    void Insert(Dictionary<string, object> row);

    /// <summary>
    /// Selects rows from the table with optional filtering and ordering.
    /// </summary>
    /// <param name="where">The where clause string.</param>
    /// <param name="orderBy">The column to order by.</param>
    /// <param name="asc">Whether to order ascending.</param>
    /// <returns>The selected rows.</returns>
    List<Dictionary<string, object>> Select(string? where = null, string? orderBy = null, bool asc = true);

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
}
