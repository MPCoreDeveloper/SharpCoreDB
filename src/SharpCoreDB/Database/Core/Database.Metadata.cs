// <copyright file="Database.Metadata.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

// ✅ RELOCATED: Moved from root to Database/Core/
// Original: SharpCoreDB/Database.Metadata.cs
// New: SharpCoreDB/Database/Core/Database.Metadata.cs
// Date: December 2025

namespace SharpCoreDB;

using SharpCoreDB.Interfaces;

/// <summary>
/// Database metadata implementation for schema discovery (IMetadataProvider).
/// 
/// Location: Database/Core/Database.Metadata.cs
/// Purpose: Implements IMetadataProvider for table/column metadata discovery
/// Used by: ADO.NET provider, Entity Framework provider, schema tools
/// </summary>
public partial class Database : IMetadataProvider
{
    /// <inheritdoc />
    public bool TryGetTable(string tableName, out ITable table)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        return tables.TryGetValue(tableName, out table!);
    }

    /// <inheritdoc />
    public IReadOnlyList<TableInfo> GetTables()
    {
        if (tables is null || tables.Count == 0)
        {
            return [];  // ✅ C# 14: collection expression
        }

        List<TableInfo> list = new(tables.Count);  // ✅ C# 14: target-typed new
        foreach (var kvp in tables)
        {
            list.Add(new TableInfo
            {
                Name = kvp.Key,
                Type = "TABLE"
            });
        }
        return list;
    }

    /// <inheritdoc />
    public IReadOnlyList<ColumnInfo> GetColumns(string tableName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);

        if (!tables.TryGetValue(tableName, out var table))
        {
            return [];  // ✅ C# 14: collection expression
        }

        var columns = table.Columns;
        var types = table.ColumnTypes;
        var collations = table.ColumnCollations;
        List<ColumnInfo> list = new(columns.Count);  // ✅ C# 14: target-typed new

        for (int i = 0; i < columns.Count; i++)
        {
            // ✅ COLLATE Phase 1: Resolve collation, default to Binary if list is short
            var collation = i < collations.Count ? collations[i] : CollationType.Binary;

            list.Add(new ColumnInfo
            {
                Table = tableName,
                Name = columns[i],
                DataType = types[i].ToString(),
                Ordinal = i,
                IsNullable = true,
                Collation = collation == CollationType.Binary ? null : collation.ToString().ToUpperInvariant()
            });
        }

        return list;
    }

    /// <summary>
    /// Gets the collation type for a specific column in a table.
    /// ✅ COLLATE Phase 3: Helper method for query execution collation resolution.
    /// </summary>
    /// <param name="tableName">The table name.</param>
    /// <param name="columnName">The column name.</param>
    /// <returns>The collation type for the column, or Binary if not found.</returns>
    public CollationType GetColumnCollation(string tableName, string columnName)
    {
        if (!tables.TryGetValue(tableName, out var table))
            return CollationType.Binary;

        var columnIndex = table.Columns.IndexOf(columnName);
        if (columnIndex < 0 || columnIndex >= table.ColumnCollations.Count)
            return CollationType.Binary;

        return table.ColumnCollations[columnIndex];
    }
}
