// <copyright file="Database.Metadata.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

// ✅ RELOCATED: Moved from root to Database/Core/
// Original: SharpCoreDB/Database.Metadata.cs
// New: SharpCoreDB/Database/Core/Database.Metadata.cs
// Date: December 2025

namespace SharpCoreDB;

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
        List<ColumnInfo> list = new(columns.Count);  // ✅ C# 14: target-typed new

        for (int i = 0; i < columns.Count; i++)
        {
            list.Add(new ColumnInfo
            {
                Table = tableName,
                Name = columns[i],
                DataType = types[i].ToString(),
                Ordinal = i,
                IsNullable = true
            });
        }

        return list;
    }
}
