// <copyright file="SqlParser.BTreeIndex.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Services;

using SharpCoreDB.Interfaces;
using System.Collections.Generic;

/// <summary>
/// SqlParser partial class - B-Tree Index operations.
/// ✅ C# 14: Modern patterns, required properties, collection expressions.
/// 
/// This file contains all B-Tree index-related functionality:
/// - Creating B-Tree indexes (single and named)
/// - Checking B-Tree index existence
/// - Range query support (&gt;, &lt;, &gt;=, &lt;=, BETWEEN)
/// 
/// B-Tree indexes are ideal for:
/// - Range queries (e.g., WHERE age > 18)
/// - Sorted access (ORDER BY optimization)
/// - Prefix searches (e.g., WHERE name LIKE 'John%')
/// </summary>
public partial class SqlParser
{
    /// <summary>
    /// Creates a B-Tree index on the specified column of a table.
    /// B-Tree indexes provide O(log n) lookup and support range queries.
    /// </summary>
    /// <param name="tableName">The table name.</param>
    /// <param name="columnName">The column to index.</param>
    public void CreateBTreeIndex(string tableName, string columnName)
    {
        if (!this.tables.TryGetValue(tableName, out var table))
            throw new InvalidOperationException($"Table {tableName} does not exist");

        if (!table.Columns.Contains(columnName))
            throw new InvalidOperationException($"Column {columnName} does not exist in table {tableName}");

        table.CreateBTreeIndex(columnName);
        Console.WriteLine($"✓ B-Tree index created on {tableName}.{columnName}");
    }

    /// <summary>
    /// Creates a named B-Tree index on the specified column.
    /// Named indexes can be referenced explicitly in queries or optimization hints.
    /// </summary>
    /// <param name="indexName">The name of the index.</param>
    /// <param name="tableName">The table name.</param>
    /// <param name="columnName">The column to index.</param>
    public void CreateBTreeIndex(string indexName, string tableName, string columnName)
    {
        if (!this.tables.TryGetValue(tableName, out var table))
            throw new InvalidOperationException($"Table {tableName} does not exist");

        if (!table.Columns.Contains(columnName))
            throw new InvalidOperationException($"Column {columnName} does not exist in table {tableName}");

        table.CreateBTreeIndex(indexName, columnName);
        Console.WriteLine($"✓ B-Tree index '{indexName}' created on {tableName}.{columnName}");
    }

    /// <summary>
    /// Creates a named B-Tree index with unique constraint.
    /// Unique B-Tree indexes enforce uniqueness and support range queries on unique columns.
    /// ✅ C# 14: Modern parameter patterns.
    /// </summary>
    /// <param name="indexName">The name of the index.</param>
    /// <param name="tableName">The table name.</param>
    /// <param name="columnName">The column to index.</param>
    /// <param name="unique">Whether the index should enforce uniqueness.</param>
    public void CreateBTreeIndex(string indexName, string tableName, string columnName, bool unique)
    {
        if (!this.tables.TryGetValue(tableName, out var table))
            throw new InvalidOperationException($"Table {tableName} does not exist");

        if (!table.Columns.Contains(columnName))
            throw new InvalidOperationException($"Column {columnName} does not exist in table {tableName}");

        table.CreateBTreeIndex(indexName, columnName, unique);
        
        var uniqueStr = unique ? " (unique)" : string.Empty;
        Console.WriteLine($"✓ B-Tree index '{indexName}' created on {tableName}.{columnName}{uniqueStr}");
    }

    /// <summary>
    /// Checks if a B-Tree index exists on the specified column.
    /// </summary>
    /// <param name="tableName">The table name.</param>
    /// <param name="columnName">The column name.</param>
    /// <returns>True if a B-Tree index exists on the column, false otherwise.</returns>
    public bool HasBTreeIndex(string tableName, string columnName)
    {
        if (!this.tables.TryGetValue(tableName, out var table))
            return false;

        return table.HasBTreeIndex(columnName);
    }

    /// <summary>
    /// Removes all indexes (both hash and B-Tree) from a table.
    /// Use this for cleanup or before dropping a table.
    /// </summary>
    /// <param name="tableName">The table name.</param>
    public void ClearAllIndexes(string tableName)
    {
        if (!this.tables.TryGetValue(tableName, out var table))
            throw new InvalidOperationException($"Table {tableName} does not exist");

        table.ClearAllIndexes();
        Console.WriteLine($"✓ All indexes cleared from {tableName}");
    }
}
