// <copyright file="AdaptiveIndexManager.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Indexing;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Adaptive index manager - self-tuning indexes based on query patterns.
/// C# 14: Primary constructors, modern patterns, auto-tuning.
/// 
/// ✅ SCDB Phase 9: Index Enhancements
/// 
/// Purpose:
/// - Analyze query patterns
/// - Automatically create missing indexes
/// - Track index usage statistics
/// - Recommend index improvements
/// - Remove unused indexes
/// </summary>
public sealed class AdaptiveIndexManager : IDisposable
{
    private readonly Dictionary<string, QueryPattern> _queryPatterns = [];
    private readonly Dictionary<string, IndexUsageStats> _indexUsage = [];
    private readonly HashSet<string> _createdIndexes = [];
    private readonly Lock _lock = new();
    private readonly AdaptiveIndexOptions _options;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="AdaptiveIndexManager"/> class.
    /// </summary>
    public AdaptiveIndexManager(AdaptiveIndexOptions? options = null)
    {
        _options = options ?? AdaptiveIndexOptions.Default;
    }

    /// <summary>Gets the number of tracked query patterns.</summary>
    public int QueryPatternCount
    {
        get
        {
            lock (_lock)
            {
                return _queryPatterns.Count;
            }
        }
    }

    /// <summary>Gets the number of auto-created indexes.</summary>
    public int AutoCreatedIndexCount
    {
        get
        {
            lock (_lock)
            {
                return _createdIndexes.Count;
            }
        }
    }

    /// <summary>
    /// Records a query execution.
    /// </summary>
    public void RecordQuery(string tableName, IEnumerable<string> columns, string? whereClause = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        ArgumentNullException.ThrowIfNull(columns);

        var columnList = columns.ToList();
        if (columnList.Count == 0)
            return;

        var patternKey = GeneratePatternKey(tableName, columnList, whereClause);

        lock (_lock)
        {
            if (!_queryPatterns.TryGetValue(patternKey, out var pattern))
            {
                pattern = new QueryPattern
                {
                    TableName = tableName,
                    Columns = columnList,
                    WhereClause = whereClause,
                    ExecutionCount = 0,
                    LastExecuted = DateTime.UtcNow
                };
                _queryPatterns[patternKey] = pattern;
            }

            pattern.ExecutionCount++;
            pattern.LastExecuted = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Records index usage.
    /// </summary>
    public void RecordIndexUsage(string indexName, long rowsScanned, TimeSpan executionTime)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(indexName);

        lock (_lock)
        {
            if (!_indexUsage.TryGetValue(indexName, out var stats))
            {
                stats = new IndexUsageStats
                {
                    IndexName = indexName,
                    UsageCount = 0,
                    TotalRowsScanned = 0,
                    TotalExecutionTime = TimeSpan.Zero,
                    LastUsed = DateTime.UtcNow
                };
                _indexUsage[indexName] = stats;
            }

            stats.UsageCount++;
            stats.TotalRowsScanned += rowsScanned;
            stats.TotalExecutionTime += executionTime;
            stats.LastUsed = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Analyzes query patterns and recommends indexes.
    /// </summary>
    public IEnumerable<IndexRecommendation> GetRecommendations()
    {
        lock (_lock)
        {
            var recommendations = new List<IndexRecommendation>();

            foreach (var pattern in _queryPatterns.Values)
            {
                // Recommend if query is executed frequently
                if (pattern.ExecutionCount >= _options.MinExecutionCountForRecommendation)
                {
                    var indexName = GenerateIndexName(pattern.TableName, pattern.Columns);

                    // Check if index already exists or was created
                    if (!_createdIndexes.Contains(indexName))
                    {
                        var recommendation = new IndexRecommendation
                        {
                            IndexName = indexName,
                            TableName = pattern.TableName,
                            Columns = pattern.Columns,
                            WhereClause = pattern.WhereClause,
                            Reason = $"Frequently executed query ({pattern.ExecutionCount} times)",
                            Priority = CalculatePriority(pattern),
                            EstimatedImpact = EstimateImpact(pattern)
                        };

                        recommendations.Add(recommendation);
                    }
                }
            }

            return recommendations.OrderByDescending(r => r.Priority);
        }
    }

    /// <summary>
    /// Gets unused indexes that could be dropped.
    /// </summary>
    public IEnumerable<string> GetUnusedIndexes(TimeSpan inactivityThreshold)
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;

            return _indexUsage
                .Where(kvp => now - kvp.Value.LastUsed > inactivityThreshold)
                .Select(kvp => kvp.Key)
                .ToList();
        }
    }

    /// <summary>
    /// Marks an index as auto-created.
    /// </summary>
    public void MarkIndexCreated(string indexName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(indexName);

        lock (_lock)
        {
            _createdIndexes.Add(indexName);
        }
    }

    /// <summary>
    /// Gets index usage statistics.
    /// </summary>
    public IndexUsageStats? GetIndexUsage(string indexName)
    {
        lock (_lock)
        {
            return _indexUsage.TryGetValue(indexName, out var stats) ? stats : null;
        }
    }

    /// <summary>
    /// Gets all index usage statistics.
    /// </summary>
    public IEnumerable<IndexUsageStats> GetAllIndexUsage()
    {
        lock (_lock)
        {
            return _indexUsage.Values.ToList();
        }
    }

    /// <summary>
    /// Clears query patterns older than the specified age.
    /// </summary>
    public int CleanupOldPatterns(TimeSpan maxAge)
    {
        lock (_lock)
        {
            var cutoff = DateTime.UtcNow - maxAge;
            var toRemove = _queryPatterns
                .Where(kvp => kvp.Value.LastExecuted < cutoff)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in toRemove)
            {
                _queryPatterns.Remove(key);
            }

            return toRemove.Count;
        }
    }

    /// <summary>
    /// Gets summary statistics.
    /// </summary>
    public AdaptiveIndexStats GetStats()
    {
        lock (_lock)
        {
            return new AdaptiveIndexStats
            {
                TotalQueryPatterns = _queryPatterns.Count,
                TotalIndexesCreated = _createdIndexes.Count,
                TotalIndexesTracked = _indexUsage.Count,
                MostFrequentPattern = _queryPatterns.Values
                    .OrderByDescending(p => p.ExecutionCount)
                    .FirstOrDefault(),
                MostUsedIndex = _indexUsage.Values
                    .OrderByDescending(s => s.UsageCount)
                    .FirstOrDefault()
            };
        }
    }

    // Private helpers

    private static string GeneratePatternKey(string tableName, List<string> columns, string? whereClause)
    {
        var columnKey = string.Join(",", columns.OrderBy(c => c));
        var whereKey = whereClause ?? "NONE";
        return $"{tableName}:{columnKey}:{whereKey}";
    }

    private static string GenerateIndexName(string tableName, List<string> columns)
    {
        var columnNames = string.Join("_", columns.Take(3)); // Limit to 3 columns
        return $"idx_auto_{tableName}_{columnNames}";
    }

    private static int CalculatePriority(QueryPattern pattern)
    {
        // Higher execution count = higher priority
        // Recent queries = higher priority
        var recencyBonus = (DateTime.UtcNow - pattern.LastExecuted).TotalHours < 1 ? 10 : 0;
        return pattern.ExecutionCount + recencyBonus;
    }

    private static string EstimateImpact(QueryPattern pattern)
    {
        return pattern.ExecutionCount switch
        {
            > 1000 => "High",
            > 100 => "Medium",
            _ => "Low"
        };
    }

    /// <summary>
    /// Disposes the manager.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;

        lock (_lock)
        {
            _queryPatterns.Clear();
            _indexUsage.Clear();
            _createdIndexes.Clear();
        }

        _disposed = true;
    }
}

/// <summary>
/// Query pattern tracking.
/// </summary>
public sealed class QueryPattern
{
    /// <summary>Table name.</summary>
    public required string TableName { get; init; }

    /// <summary>Columns accessed.</summary>
    public required List<string> Columns { get; init; }

    /// <summary>WHERE clause pattern.</summary>
    public string? WhereClause { get; init; }

    /// <summary>Number of times executed.</summary>
    public int ExecutionCount { get; set; }

    /// <summary>Last execution time.</summary>
    public DateTime LastExecuted { get; set; }
}

/// <summary>
/// Index usage statistics.
/// </summary>
public sealed class IndexUsageStats
{
    /// <summary>Index name.</summary>
    public required string IndexName { get; init; }

    /// <summary>Number of times used.</summary>
    public int UsageCount { get; set; }

    /// <summary>Total rows scanned.</summary>
    public long TotalRowsScanned { get; set; }

    /// <summary>Total execution time.</summary>
    public TimeSpan TotalExecutionTime { get; set; }

    /// <summary>Last usage time.</summary>
    public DateTime LastUsed { get; set; }

    /// <summary>Average rows per query.</summary>
    public double AverageRowsPerQuery => UsageCount > 0 ? (double)TotalRowsScanned / UsageCount : 0;

    /// <summary>Average execution time.</summary>
    public TimeSpan AverageExecutionTime => UsageCount > 0
        ? TimeSpan.FromMilliseconds(TotalExecutionTime.TotalMilliseconds / UsageCount)
        : TimeSpan.Zero;
}

/// <summary>
/// Index recommendation.
/// </summary>
public sealed record IndexRecommendation
{
    /// <summary>Recommended index name.</summary>
    public required string IndexName { get; init; }

    /// <summary>Table name.</summary>
    public required string TableName { get; init; }

    /// <summary>Columns to index.</summary>
    public required List<string> Columns { get; init; }

    /// <summary>Optional WHERE clause for partial index.</summary>
    public string? WhereClause { get; init; }

    /// <summary>Reason for recommendation.</summary>
    public required string Reason { get; init; }

    /// <summary>Priority (higher = more important).</summary>
    public required int Priority { get; init; }

    /// <summary>Estimated impact.</summary>
    public required string EstimatedImpact { get; init; }
}

/// <summary>
/// Adaptive index statistics.
/// </summary>
public sealed record AdaptiveIndexStats
{
    /// <summary>Total query patterns tracked.</summary>
    public required int TotalQueryPatterns { get; init; }

    /// <summary>Total indexes created automatically.</summary>
    public required int TotalIndexesCreated { get; init; }

    /// <summary>Total indexes being tracked.</summary>
    public required int TotalIndexesTracked { get; init; }

    /// <summary>Most frequent query pattern.</summary>
    public QueryPattern? MostFrequentPattern { get; init; }

    /// <summary>Most used index.</summary>
    public IndexUsageStats? MostUsedIndex { get; init; }
}

/// <summary>
/// Options for adaptive indexing.
/// </summary>
public sealed record AdaptiveIndexOptions
{
    /// <summary>Minimum executions before recommending an index.</summary>
    public int MinExecutionCountForRecommendation { get; init; } = 10;

    /// <summary>Auto-create indexes when threshold is met.</summary>
    public bool AutoCreateIndexes { get; init; }

    /// <summary>Auto-drop unused indexes.</summary>
    public bool AutoDropUnusedIndexes { get; init; }

    /// <summary>Inactivity threshold for dropping indexes.</summary>
    public TimeSpan InactivityThreshold { get; init; } = TimeSpan.FromDays(30);

    /// <summary>Default options.</summary>
    public static AdaptiveIndexOptions Default => new()
    {
        MinExecutionCountForRecommendation = 10,
        AutoCreateIndexes = false,
        AutoDropUnusedIndexes = false
    };
}
