// <copyright file="SqlParser.HashIndex.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Services;

using SharpCoreDB.Interfaces;
using System.Collections.Generic;

/// <summary>
/// SqlParser partial class - Hash Index operations.
/// ✅ C# 14: Modern patterns, required properties, collection expressions.
/// 
/// This file contains all hash index-related functionality:
/// - Creating hash indexes (single and named)
/// - Checking hash index existence
/// - Removing hash indexes
/// - Getting hash index statistics
/// </summary>
public partial class SqlParser
{
    /// <summary>
    /// Creates a hash index on the specified column of a table.
    /// Hash indexes provide O(1) lookup for equality searches.
    /// </summary>
    /// <param name="tableName">The table name.</param>
    /// <param name="columnName">The column to index.</param>
    public void CreateHashIndex(string tableName, string columnName)
    {
        if (!this.tables.TryGetValue(tableName, out var table))
            throw new InvalidOperationException($"Table {tableName} does not exist");

        if (!table.Columns.Contains(columnName))
            throw new InvalidOperationException($"Column {columnName} does not exist in table {tableName}");

        table.CreateHashIndex(columnName);
        Console.WriteLine($"✓ Hash index created on {tableName}.{columnName}");
    }

    /// <summary>
    /// Creates a named hash index on the specified column.
    /// Named indexes can be referenced explicitly in queries or optimization hints.
    /// </summary>
    /// <param name="indexName">The name of the index.</param>
    /// <param name="tableName">The table name.</param>
    /// <param name="columnName">The column to index.</param>
    public void CreateHashIndex(string indexName, string tableName, string columnName)
    {
        if (!this.tables.TryGetValue(tableName, out var table))
            throw new InvalidOperationException($"Table {tableName} does not exist");

        if (!table.Columns.Contains(columnName))
            throw new InvalidOperationException($"Column {columnName} does not exist in table {tableName}");

        table.CreateHashIndex(indexName, columnName);
        Console.WriteLine($"✓ Hash index '{indexName}' created on {tableName}.{columnName}");
    }

    /// <summary>
    /// Creates a named hash index with unique constraint.
    /// Unique hash indexes enforce uniqueness and provide O(1) duplicate checking.
    /// ✅ C# 14: Modern parameter patterns.
    /// </summary>
    /// <param name="indexName">The name of the index.</param>
    /// <param name="tableName">The table name.</param>
    /// <param name="columnName">The column to index.</param>
    /// <param name="unique">Whether the index should enforce uniqueness.</param>
    public void CreateHashIndex(string indexName, string tableName, string columnName, bool unique)
    {
        if (!this.tables.TryGetValue(tableName, out var table))
            throw new InvalidOperationException($"Table {tableName} does not exist");

        if (!table.Columns.Contains(columnName))
            throw new InvalidOperationException($"Column {columnName} does not exist in table {tableName}");

        table.CreateHashIndex(indexName, columnName, unique);
        
        var uniqueStr = unique ? " (unique)" : string.Empty;
        Console.WriteLine($"✓ Hash index '{indexName}' created on {tableName}.{columnName}{uniqueStr}");
    }

    /// <summary>
    /// Checks if a hash index exists on the specified column.
    /// </summary>
    /// <param name="tableName">The table name.</param>
    /// <param name="columnName">The column name.</param>
    /// <returns>True if a hash index exists on the column, false otherwise.</returns>
    public bool HasHashIndex(string tableName, string columnName)
    {
        if (!this.tables.TryGetValue(tableName, out var table))
            return false;

        return table.HasHashIndex(columnName);
    }

    /// <summary>
    /// Removes a hash index from the specified column.
    /// </summary>
    /// <param name="tableName">The table name.</param>
    /// <param name="columnName">The column name.</param>
    /// <returns>True if the index was removed, false if it didn't exist.</returns>
    public bool RemoveHashIndex(string tableName, string columnName)
    {
        if (!this.tables.TryGetValue(tableName, out var table))
            throw new InvalidOperationException($"Table {tableName} does not exist");

        bool removed = table.RemoveHashIndex(columnName);
        
        if (removed)
            Console.WriteLine($"✓ Hash index removed from {tableName}.{columnName}");
        else
            Console.WriteLine($"⚠ No hash index found on {tableName}.{columnName}");

        return removed;
    }

    /// <summary>
    /// Gets statistics about a hash index.
    /// Returns information about unique keys, total rows, and average rows per key.
    /// ✅ C# 14: Tuple return type with named elements.
    /// </summary>
    /// <param name="tableName">The table name.</param>
    /// <param name="columnName">The column name.</param>
    /// <returns>Index statistics, or null if no index exists.</returns>
    public (int UniqueKeys, int TotalRows, double AvgRowsPerKey)? GetHashIndexStatistics(string tableName, string columnName)
    {
        if (!this.tables.TryGetValue(tableName, out var table))
            return null;

        return table.GetHashIndexStatistics(columnName);
    }
}
