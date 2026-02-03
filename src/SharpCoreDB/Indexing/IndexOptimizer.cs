// <copyright file="IndexOptimizer.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Indexing;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Index optimizer for composite index strategies.
/// C# 14: Record types, modern patterns, selectivity estimation.
/// 
/// ✅ SCDB Phase 9: Index Enhancements
/// 
/// Purpose:
/// - Optimize multi-column index column ordering
/// - Estimate index selectivity
/// - Choose best index for a query
/// - Provide index selection guidance
/// </summary>
public static class IndexOptimizer
{
    /// <summary>
    /// Recommends optimal column ordering for a composite index.
    /// </summary>
    public static List<string> RecommendColumnOrder(
        List<string> columns,
        Dictionary<string, ColumnStatistics> statistics)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(statistics);

        // Order by selectivity (most selective first)
        return columns
            .OrderByDescending(col => GetSelectivity(col, statistics))
            .ToList();
    }

    /// <summary>
    /// Chooses the best index for a query.
    /// </summary>
    public static IndexSelectionResult SelectBestIndex(
        List<string> queryColumns,
        List<IndexDefinition> availableIndexes,
        Dictionary<string, ColumnStatistics> statistics,
        IndexHintCollection? hints = null)
    {
        ArgumentNullException.ThrowIfNull(queryColumns);
        ArgumentNullException.ThrowIfNull(availableIndexes);

        if (availableIndexes.Count == 0)
        {
            return new IndexSelectionResult
            {
                SelectedIndex = null,
                Reason = "No indexes available",
                EstimatedSelectivity = 1.0
            };
        }

        // Apply hints if provided
        if (hints != null && availableIndexes.Count > 0)
        {
            var tableName = availableIndexes[0].TableName;
            var forcedIndexes = hints.GetForcedIndexes(tableName).ToList();

            if (forcedIndexes.Count > 0)
            {
                var forcedIndex = availableIndexes.FirstOrDefault(idx => forcedIndexes.Contains(idx.Name));
                if (forcedIndex != null)
                {
                    return new IndexSelectionResult
                    {
                        SelectedIndex = forcedIndex,
                        Reason = "Index forced by hint",
                        EstimatedSelectivity = CalculateSelectivity(forcedIndex, queryColumns, statistics)
                    };
                }
            }
        }

        // Score each index
        var scoredIndexes = availableIndexes
            .Select(idx => new
            {
                Index = idx,
                Score = ScoreIndex(idx, queryColumns, statistics, hints)
            })
            .OrderByDescending(x => x.Score)
            .ToList();

        var best = scoredIndexes.FirstOrDefault();

        if (best == null)
        {
            return new IndexSelectionResult
            {
                SelectedIndex = null,
                Reason = "No suitable index found",
                EstimatedSelectivity = 1.0
            };
        }

        return new IndexSelectionResult
        {
            SelectedIndex = best.Index,
            Reason = $"Best match with score {best.Score:F2}",
            EstimatedSelectivity = CalculateSelectivity(best.Index, queryColumns, statistics)
        };
    }

    /// <summary>
    /// Estimates the selectivity of a column.
    /// </summary>
    public static double EstimateColumnSelectivity(
        string columnName,
        Dictionary<string, ColumnStatistics> statistics)
    {
        if (statistics.TryGetValue(columnName, out var stats))
        {
            // Selectivity = distinct values / total rows
            return stats.TotalRows > 0 ? (double)stats.DistinctValues / stats.TotalRows : 0;
        }

        return 0.1; // Default low selectivity
    }

    /// <summary>
    /// Validates index design and provides recommendations.
    /// </summary>
    public static IndexDesignAnalysis AnalyzeIndexDesign(
        IndexDefinition index,
        Dictionary<string, ColumnStatistics> statistics)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(statistics);

        var issues = new List<string>();
        var recommendations = new List<string>();

        // Check column count
        if (index.Columns.Count == 0)
        {
            issues.Add("Index has no columns");
        }
        else if (index.Columns.Count > 5)
        {
            recommendations.Add("Consider reducing column count (>5 columns may impact performance)");
        }

        // Check column ordering
        if (index.Columns.Count > 1)
        {
            var optimalOrder = RecommendColumnOrder(index.Columns, statistics);
            if (!index.Columns.SequenceEqual(optimalOrder))
            {
                recommendations.Add($"Consider reordering columns to: {string.Join(", ", optimalOrder)}");
            }
        }

        // Check selectivity
        foreach (var column in index.Columns)
        {
            var selectivity = EstimateColumnSelectivity(column, statistics);
            if (selectivity < 0.01)
            {
                recommendations.Add($"Column '{column}' has very low selectivity ({selectivity:P2})");
            }
        }

        return new IndexDesignAnalysis
        {
            IndexName = index.Name,
            Issues = issues,
            Recommendations = recommendations,
            OverallScore = CalculateDesignScore(index, statistics)
        };
    }

    // Private helpers

    private static double GetSelectivity(string column, Dictionary<string, ColumnStatistics> statistics)
    {
        if (statistics.TryGetValue(column, out var stats))
        {
            return stats.TotalRows > 0 ? (double)stats.DistinctValues / stats.TotalRows : 0;
        }

        return 0.1; // Default
    }

    private static double ScoreIndex(
        IndexDefinition index,
        List<string> queryColumns,
        Dictionary<string, ColumnStatistics> statistics,
        IndexHintCollection? hints)
    {
        double score = 0;

        // Check if hints prefer or avoid this index
        if (hints != null)
        {
            var preferredOrder = hints.GetPreferredOrder(index.TableName, [index.Name]).ToList();
            var preferredIndex = preferredOrder.IndexOf(index.Name);

            if (preferredIndex >= 0)
            {
                // Preferred indexes get bonus
                score += 100 - preferredIndex * 10;
            }
        }

        // Matching columns (prefix matching)
        int matchingPrefix = 0;
        for (int i = 0; i < Math.Min(index.Columns.Count, queryColumns.Count); i++)
        {
            if (index.Columns[i].Equals(queryColumns[i], StringComparison.OrdinalIgnoreCase))
            {
                matchingPrefix++;
                score += 20 * (queryColumns.Count - i); // Earlier columns worth more
            }
            else
            {
                break; // Prefix must be continuous
            }
        }

        // Selectivity bonus
        if (index.Columns.Count > 0)
        {
            var firstColSelectivity = GetSelectivity(index.Columns[0], statistics);
            score += firstColSelectivity * 50;
        }

        // Penalty for extra columns
        if (index.Columns.Count > queryColumns.Count)
        {
            score -= (index.Columns.Count - queryColumns.Count) * 5;
        }

        return score;
    }

    private static double CalculateSelectivity(
        IndexDefinition index,
        List<string> queryColumns,
        Dictionary<string, ColumnStatistics> statistics)
    {
        // Combined selectivity for matching columns
        double combinedSelectivity = 1.0;

        var matchingColumns = index.Columns.Take(queryColumns.Count).ToList();

        foreach (var column in matchingColumns)
        {
            if (statistics.TryGetValue(column, out var stats))
            {
                var colSelectivity = stats.TotalRows > 0
                    ? (double)stats.DistinctValues / stats.TotalRows
                    : 0.1;

                combinedSelectivity *= colSelectivity;
            }
        }

        return Math.Max(0.001, combinedSelectivity);
    }

    private static double CalculateDesignScore(
        IndexDefinition index,
        Dictionary<string, ColumnStatistics> statistics)
    {
        double score = 100;

        // Deduct for too many columns
        if (index.Columns.Count > 5)
        {
            score -= (index.Columns.Count - 5) * 10;
        }

        // Check column ordering
        if (index.Columns.Count > 1)
        {
            var optimalOrder = RecommendColumnOrder(index.Columns, statistics);
            if (!index.Columns.SequenceEqual(optimalOrder))
            {
                score -= 20;
            }
        }

        // Check first column selectivity
        if (index.Columns.Count > 0)
        {
            var firstColSelectivity = GetSelectivity(index.Columns[0], statistics);
            if (firstColSelectivity < 0.01)
            {
                score -= 30;
            }
        }

        return Math.Max(0, score);
    }
}

/// <summary>
/// Index definition.
/// </summary>
public sealed record IndexDefinition
{
    /// <summary>Index name.</summary>
    public required string Name { get; init; }

    /// <summary>Table name.</summary>
    public required string TableName { get; init; }

    /// <summary>Indexed columns (in order).</summary>
    public required List<string> Columns { get; init; }

    /// <summary>Whether the index is unique.</summary>
    public bool IsUnique { get; init; }

    /// <summary>Whether the index is clustered.</summary>
    public bool IsClustered { get; init; }
}

/// <summary>
/// Column statistics for selectivity estimation.
/// </summary>
public sealed record ColumnStatistics
{
    /// <summary>Total rows in table.</summary>
    public required long TotalRows { get; init; }

    /// <summary>Number of distinct values.</summary>
    public required long DistinctValues { get; init; }

    /// <summary>Number of null values.</summary>
    public long NullCount { get; init; }

    /// <summary>Average value length (for strings).</summary>
    public int? AverageLength { get; init; }
}

/// <summary>
/// Index selection result.
/// </summary>
public sealed record IndexSelectionResult
{
    /// <summary>Selected index (or null if none).</summary>
    public IndexDefinition? SelectedIndex { get; init; }

    /// <summary>Reason for selection.</summary>
    public required string Reason { get; init; }

    /// <summary>Estimated selectivity (0-1).</summary>
    public required double EstimatedSelectivity { get; init; }
}

/// <summary>
/// Index design analysis result.
/// </summary>
public sealed record IndexDesignAnalysis
{
    /// <summary>Index name.</summary>
    public required string IndexName { get; init; }

    /// <summary>Critical issues.</summary>
    public required List<string> Issues { get; init; }

    /// <summary>Improvement recommendations.</summary>
    public required List<string> Recommendations { get; init; }

    /// <summary>Overall design score (0-100).</summary>
    public required double OverallScore { get; init; }
}
