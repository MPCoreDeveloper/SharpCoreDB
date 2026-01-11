// <copyright file="SqlParser.Statistics.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Services;

using SharpCoreDB.Interfaces;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// SqlParser partial class - Table and Column Statistics.
/// ✅ C# 14: Modern patterns, readonly collections, collection expressions.
/// 
/// This file contains statistics tracking and retrieval:
/// - Column usage tracking (for query optimization)
/// - Row count management
/// - Index statistics
/// - Future: Histogram generation, cardinality estimation
/// 
/// Statistics are used by the query optimizer to choose efficient execution plans.
/// </summary>
public partial class SqlParser
{
    /// <summary>
    /// Tracks usage for all columns in a table.
    /// Called when SELECT * is used.
    /// </summary>
    /// <param name="tableName">The table name.</param>
    public void TrackAllColumnsUsage(string tableName)
    {
        if (!this.tables.TryGetValue(tableName, out var table))
            return;

        table.TrackAllColumnsUsage();
    }

    /// <summary>
    /// Gets column usage statistics for a table.
    /// Returns a dictionary mapping column names to usage counts.
    /// ✅ C# 14: Readonly collection return type.
    /// </summary>
    /// <param name="tableName">The table name.</param>
    /// <returns>Column usage statistics, or empty if table doesn't exist.</returns>
    public IReadOnlyDictionary<string, long> GetColumnUsage(string tableName)
    {
        if (!this.tables.TryGetValue(tableName, out var table))
            return new Dictionary<string, long>();

        return table.GetColumnUsage();
    }

    /// <summary>
    /// Prints column usage statistics for a table.
    /// Useful for identifying candidates for indexing.
    /// </summary>
    /// <param name="tableName">The table name.</param>
    public void PrintColumnUsageStatistics(string tableName)
    {
        var usage = GetColumnUsage(tableName);
        
        if (usage.Count == 0)
        {
            Console.WriteLine($"No usage statistics available for table '{tableName}'");
            return;
        }

        Console.WriteLine($"\n═══ Column Usage Statistics: {tableName} ═══");
        
        // ✅ C# 14: LINQ with modern patterns
        var sorted = usage.OrderByDescending(kv => kv.Value).ToList();
        
        foreach (var (column, count) in sorted)
        {
            Console.WriteLine($"  {column,-30} {count,10:N0} queries");
        }
        
        Console.WriteLine();
    }

    /// <summary>
    /// Gets the cached row count for a table.
    /// Returns cached value for performance.
    /// </summary>
    /// <param name="tableName">The table name.</param>
    /// <returns>The row count, or 0 if table doesn't exist.</returns>
    public long GetCachedRowCount(string tableName)
    {
        if (!this.tables.TryGetValue(tableName, out var table))
            return 0;

        return table.GetCachedRowCount();
    }

    /// <summary>
    /// Refreshes the row count cache for a table.
    /// Should be called after bulk operations.
    /// </summary>
    /// <param name="tableName">The table name.</param>
    public void RefreshRowCount(string tableName)
    {
        if (!this.tables.TryGetValue(tableName, out var table))
            return;

        table.RefreshRowCount();
    }

    /// <summary>
    /// Gets comprehensive table statistics.
    /// ✅ C# 14: Tuple return with named elements.
    /// </summary>
    /// <param name="tableName">The table name.</param>
    /// <returns>Table statistics tuple, or null if table doesn't exist.</returns>
    public (long RowCount, int ColumnCount, long TotalBytes, int IndexCount)? GetTableStatistics(string tableName)
    {
        if (!this.tables.TryGetValue(tableName, out var table))
            return null;

        long rowCount = table.GetCachedRowCount();
        int columnCount = table.Columns.Count;
        int indexCount = 0;

        // Count indexes
        foreach (var column in table.Columns)
        {
            if (table.HasHashIndex(column))
                indexCount++;
            if (table.HasBTreeIndex(column))
                indexCount++;
        }

        // Estimate total bytes (rough approximation)
        // This would be more accurate with actual storage engine integration
        long estimatedBytes = rowCount * columnCount * 50; // Assume ~50 bytes per cell average

        return (rowCount, columnCount, estimatedBytes, indexCount);
    }

    /// <summary>
    /// Prints comprehensive table statistics.
    /// </summary>
    /// <param name="tableName">The table name.</param>
    public void PrintTableStatistics(string tableName)
    {
        var stats = GetTableStatistics(tableName);
        
        if (stats is null)
        {
            Console.WriteLine($"Table '{tableName}' does not exist");
            return;
        }

        var (rowCount, columnCount, totalBytes, indexCount) = stats.Value;

        Console.WriteLine($"\n═══ Table Statistics: {tableName} ═══");
        Console.WriteLine($"  Rows:       {rowCount,15:N0}");
        Console.WriteLine($"  Columns:    {columnCount,15:N0}");
        Console.WriteLine($"  Est. Size:  {FormatBytes(totalBytes),15}");
        Console.WriteLine($"  Indexes:    {indexCount,15:N0}");
        Console.WriteLine();
    }

    /// <summary>
    /// Formats byte count for human-readable display.
    /// ✅ C# 14: Pattern matching with when clauses.
    /// </summary>
    private static string FormatBytes(long bytes)
    {
        return bytes switch
        {
            >= 1_099_511_627_776 => $"{bytes / 1_099_511_627_776.0:F2} TB",
            >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F2} GB",
            >= 1_048_576 => $"{bytes / 1_048_576.0:F2} MB",
            >= 1_024 => $"{bytes / 1_024.0:F2} KB",
            _ => $"{bytes} bytes"
        };
    }
}
