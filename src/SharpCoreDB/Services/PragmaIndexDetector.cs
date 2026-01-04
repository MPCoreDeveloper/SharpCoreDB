// <copyright file="PragmaIndexDetector.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Services;

using SharpCoreDB.Interfaces;
using System.Text.RegularExpressions;

/// <summary>
/// Detects and analyzes indexes using PRAGMA-like introspection.
/// Provides SQLite-compatible PRAGMA commands for index discovery.
/// </summary>
public sealed partial class PragmaIndexDetector
{
    private readonly Dictionary<string, TableIndexInfo> _tableIndexes = [];
    
    /// <summary>
    /// Information about indexes on a table.
    /// </summary>
    public sealed record TableIndexInfo
    {
        /// <summary>Gets or initializes the table name.</summary>
        public required string TableName { get; init; }
        
        /// <summary>Gets or initializes the list of indexes on this table.</summary>
        public required List<IndexInfo> Indexes { get; init; }
        
        /// <summary>Gets or initializes the column statistics for index recommendations.</summary>
        public required Dictionary<string, ColumnStats> ColumnStatistics { get; init; }
    }
    
    /// <summary>
    /// Information about a single index.
    /// </summary>
    public sealed record IndexInfo
    {
        /// <summary>Gets or initializes the index name.</summary>
        public required string Name { get; init; }
        
        /// <summary>Gets or initializes the column name that is indexed.</summary>
        public required string ColumnName { get; init; }
        
        /// <summary>Gets or initializes the index type (Hash, BTree, etc.).</summary>
        public required IndexType Type { get; init; }
        
        /// <summary>Gets or initializes a value indicating whether the index enforces uniqueness.</summary>
        public required bool IsUnique { get; init; }
        
        /// <summary>Gets or initializes a value indicating whether the index was automatically created.</summary>
        public required bool IsAutoCreated { get; init; }
        
        /// <summary>Gets or initializes the index statistics.</summary>
        public required IndexStatistics Statistics { get; init; }
    }
    
    /// <summary>
    /// Statistics for a column (used for auto-indexing decisions).
    /// </summary>
    public sealed record ColumnStats
    {
        /// <summary>Gets or initializes the column name.</summary>
        public required string ColumnName { get; init; }
        
        /// <summary>Gets or initializes the total number of rows in the table.</summary>
        public required int TotalRows { get; init; }
        
        /// <summary>Gets or initializes the number of unique values in the column.</summary>
        public required int UniqueValues { get; init; }
        
        /// <summary>Gets or initializes the number of null values in the column.</summary>
        public required int NullCount { get; init; }
        
        /// <summary>Gets or initializes the selectivity of the column (unique values / total rows).</summary>
        public required double Selectivity { get; init; }
        
        /// <summary>Gets or initializes the number of times this column has been queried in WHERE clauses.</summary>
        public required int QueryCount { get; init; }
        
        /// <summary>Gets or initializes a value indicating whether an index is recommended for this column.</summary>
        public required bool ShouldIndex { get; init; }
    }

    /// <summary>
    /// Analyzes table structure and recommends indexes.
    /// Similar to SQLite's PRAGMA index_list and PRAGMA table_info.
    /// </summary>
    public TableIndexInfo AnalyzeTable(string tableName, IEnumerable<Dictionary<string, object?>> rows)
    {
        var columnStats = AnalyzeColumns(rows);
        var indexes = RecommendIndexes(tableName, columnStats);
        
        var info = new TableIndexInfo
        {
            TableName = tableName,
            Indexes = indexes,
            ColumnStatistics = columnStats
        };
        
        _tableIndexes[tableName] = info;
        return info;
    }

    /// <summary>
    /// Analyzes column statistics to determine index candidates.
    /// </summary>
    private Dictionary<string, ColumnStats> AnalyzeColumns(IEnumerable<Dictionary<string, object?>> rows)
    {
        var rowList = rows.ToList();
        var totalRows = rowList.Count;
        
        if (totalRows == 0)
            return [];

        var stats = new Dictionary<string, ColumnStats>();
        var firstRow = rowList[0];

        foreach (var column in firstRow.Keys)
        {
            var uniqueValues = new HashSet<object?>();
            var nullCount = 0;

            foreach (var row in rowList)
            {
                var value = row.TryGetValue(column, out var v) ? v : null;
                if (value == null)
                    nullCount++;
                else
                    uniqueValues.Add(value);
            }

            var uniqueCount = uniqueValues.Count;
            var selectivity = totalRows > 0 ? (double)uniqueCount / totalRows : 0;
            
            // Auto-index recommendation rules:
            // 1. High selectivity (> 0.5) = good for indexing
            // 2. Primary keys (unique) = always index
            // 3. Low selectivity (< 0.1) = skip (not selective enough)
            var shouldIndex = selectivity > 0.5 || uniqueCount == totalRows;

            stats[column] = new ColumnStats
            {
                ColumnName = column,
                TotalRows = totalRows,
                UniqueValues = uniqueCount,
                NullCount = nullCount,
                Selectivity = selectivity,
                QueryCount = 0, // Will be updated by query tracking
                ShouldIndex = shouldIndex
            };
        }

        return stats;
    }

    /// <summary>
    /// Recommends indexes based on column statistics.
    /// </summary>
    private List<IndexInfo> RecommendIndexes(string tableName, Dictionary<string, ColumnStats> columnStats)
    {
        var indexes = new List<IndexInfo>();

        foreach (var (columnName, stats) in columnStats)
        {
            if (!stats.ShouldIndex)
                continue;

            var indexType = DetermineIndexType(stats);
            var isUnique = stats.UniqueValues == stats.TotalRows && stats.NullCount == 0;

            indexes.Add(new IndexInfo
            {
                Name = $"idx_{tableName}_{columnName}_auto",
                ColumnName = columnName,
                Type = indexType,
                IsUnique = isUnique,
                IsAutoCreated = true,
                Statistics = new IndexStatistics
                {
                    UniqueKeys = stats.UniqueValues,
                    TotalEntries = stats.TotalRows - stats.NullCount,
                    AverageEntriesPerKey = stats.UniqueValues > 0 ? 
                        (double)(stats.TotalRows - stats.NullCount) / stats.UniqueValues : 0,
                    Selectivity = stats.Selectivity,
                    MemoryUsageBytes = 0 // Will be calculated when index is created
                }
            });
        }

        return indexes;
    }

    /// <summary>
    /// Determines optimal index type based on column statistics and data type.
    /// </summary>
    private static IndexType DetermineIndexType(ColumnStats stats)
    {
        // High selectivity → Hash index (O(1) lookups)
        // Lower selectivity or need for ranges → B-Tree (O(log n) with range support)
        return stats.Selectivity > 0.8 ? IndexType.Hash : IndexType.BTree;
    }

    /// <summary>
    /// Records a query on a column (for tracking which columns are frequently queried).
    /// </summary>
    public void RecordQuery(string tableName, string columnName)
    {
        if (_tableIndexes.TryGetValue(tableName, out var tableInfo) &&
            tableInfo.ColumnStatistics.TryGetValue(columnName, out var stats))
        {
            // Update query count
            var newStats = stats with { QueryCount = stats.QueryCount + 1 };
            tableInfo.ColumnStatistics[columnName] = newStats;
            
            // If column is queried often but not indexed, recommend indexing
            if (newStats.QueryCount > 10 && !newStats.ShouldIndex)
            {
                tableInfo.ColumnStatistics[columnName] = newStats with { ShouldIndex = true };
            }
        }
    }

    /// <summary>
    /// Gets table information for a specific table.
    /// </summary>
    public TableIndexInfo? GetTableInfo(string tableName)
    {
        return _tableIndexes.TryGetValue(tableName, out var info) ? info : null;
    }

    /// <summary>
    /// Gets PRAGMA-style index information (SQLite compatible).
    /// </summary>
    public string GetPragmaIndexList(string tableName)
    {
        if (!_tableIndexes.TryGetValue(tableName, out var info))
            return $"-- No indexes found for table '{tableName}'";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"-- Index list for table '{tableName}'");
        foreach (var index in info.Indexes)
        {
            sb.Append($"-- {index.Name}: {index.ColumnName} ({index.Type}, ");
            sb.AppendLine($"unique={index.IsUnique}, auto={index.IsAutoCreated})");
        }
        return sb.ToString();
    }

    /// <summary>
    /// Gets PRAGMA-style table info with index recommendations.
    /// </summary>
    public string GetPragmaTableInfo(string tableName)
    {
        if (!_tableIndexes.TryGetValue(tableName, out var info))
            return $"-- No information found for table '{tableName}'";

        var result = $"-- Table info for '{tableName}'\n";
        foreach (var (columnName, stats) in info.ColumnStatistics)
        {
            result += $"-- {columnName}: {stats.UniqueValues}/{stats.TotalRows} unique " +
                     $"(selectivity={stats.Selectivity:F2}, queries={stats.QueryCount}, " +
                     $"index={stats.ShouldIndex})\n";
        }
        return result;
    }
}
