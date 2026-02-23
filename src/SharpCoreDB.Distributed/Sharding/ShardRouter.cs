// <copyright file="ShardRouter.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Distributed.Sharding;

/// <summary>
/// Routes queries to appropriate shards based on shard key values.
/// C# 14: Primary constructors, pattern matching, collection expressions.
/// </summary>
public sealed class ShardRouter(ShardManager shardManager, ShardKey shardKey)
{
    private readonly ShardManager _shardManager = shardManager ?? throw new ArgumentNullException(nameof(shardManager));
    private readonly ShardKey _shardKey = shardKey ?? throw new ArgumentNullException(nameof(shardKey));

    /// <summary>
    /// Routes a query to the appropriate shard based on the shard key value.
    /// </summary>
    /// <param name="query">The SQL query to route.</param>
    /// <param name="parameters">Query parameters that may contain the shard key value.</param>
    /// <returns>The shard ID to route to, or null if no specific shard can be determined.</returns>
    public string? RouteQuery(string query, IReadOnlyDictionary<string, object?>? parameters = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        // Extract shard key value from query or parameters
        var shardKeyValue = ExtractShardKeyValue(query, parameters);

        if (shardKeyValue is null)
        {
            return null; // Cannot determine shard
        }

        return GetShardForValue(shardKeyValue);
    }

    /// <summary>
    /// Routes a query to the appropriate shard based on an explicit shard key value.
    /// </summary>
    /// <param name="shardKeyValue">The value of the shard key.</param>
    /// <returns>The shard ID to route to.</returns>
    public string GetShardForValue(object? shardKeyValue)
    {
        var onlineShards = _shardManager.GetOnlineShards();
        if (onlineShards.Count == 0)
        {
            throw new InvalidOperationException("No online shards available.");
        }

        var shardIndex = _shardKey.GetShardIndex(shardKeyValue, onlineShards.Count);

        // Map shard index to actual shard ID
        var orderedShards = onlineShards.OrderBy(s => s.ShardId).ToList();
        if (shardIndex < orderedShards.Count)
        {
            return orderedShards[shardIndex].ShardId;
        }

        // Fallback to first shard if index is out of range
        return orderedShards[0].ShardId;
    }

    /// <summary>
    /// Routes a query that needs to access multiple shards.
    /// </summary>
    /// <param name="query">The SQL query that may span multiple shards.</param>
    /// <param name="parameters">Query parameters.</param>
    /// <returns>Collection of shard IDs that need to be queried.</returns>
    public IReadOnlyCollection<string> RouteMultiShardQuery(string query, IReadOnlyDictionary<string, object?>? parameters = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        // Check if this is a cross-shard query (JOIN, UNION, etc.)
        if (IsCrossShardQuery(query))
        {
            return GetAllOnlineShardIds();
        }

        // For single-shard queries, try to route specifically
        var singleShard = RouteQuery(query, parameters);
        if (singleShard is not null)
        {
            return [singleShard];
        }

        // If we can't determine the specific shard, query all shards
        return GetAllOnlineShardIds();
    }

    /// <summary>
    /// Gets all online shard IDs for broadcast operations.
    /// </summary>
    /// <returns>Collection of all online shard IDs.</returns>
    public IReadOnlyCollection<string> GetAllOnlineShardIds()
    {
        return [.. _shardManager.GetOnlineShards().Select(s => s.ShardId)];
    }

    /// <summary>
    /// Routes DDL operations that affect all shards.
    /// </summary>
    /// <param name="query">The DDL query (CREATE TABLE, ALTER TABLE, etc.).</param>
    /// <returns>Collection of all online shard IDs.</returns>
    public IReadOnlyCollection<string> RouteDdlQuery(string query)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        // DDL operations typically need to be executed on all shards
        return GetAllOnlineShardIds();
    }

    /// <summary>
    /// Validates that a query can be routed with the current shard configuration.
    /// </summary>
    /// <param name="query">The query to validate.</param>
    /// <param name="parameters">Query parameters.</param>
    /// <returns>True if the query can be routed.</returns>
    public bool CanRouteQuery(string query, IReadOnlyDictionary<string, object?>? parameters = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        try
        {
            var shardKeyValue = ExtractShardKeyValue(query, parameters);
            return shardKeyValue is not null || IsCrossShardQuery(query);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Extracts the shard key value from a query and its parameters.
    /// </summary>
    /// <param name="query">The SQL query.</param>
    /// <param name="parameters">Query parameters.</param>
    /// <returns>The shard key value, or null if not found.</returns>
    private object? ExtractShardKeyValue(string query, IReadOnlyDictionary<string, object?>? parameters)
    {
        // Look for the shard key column in WHERE clauses
        var whereClause = ExtractWhereClause(query);
        if (string.IsNullOrEmpty(whereClause))
        {
            return null;
        }

        // Parse WHERE clause for shard key conditions
        return ParseShardKeyCondition(whereClause, parameters);
    }

    /// <summary>
    /// Extracts the WHERE clause from a SQL query.
    /// </summary>
    /// <param name="query">The SQL query.</param>
    /// <returns>The WHERE clause, or null if not found.</returns>
    private static string? ExtractWhereClause(string query)
    {
        var upperQuery = query.ToUpperInvariant();
        var whereIndex = upperQuery.IndexOf("WHERE", StringComparison.Ordinal);

        if (whereIndex < 0)
        {
            return null;
        }

        // Find the end of the WHERE clause (before ORDER BY, GROUP BY, etc.)
        var endKeywords = new[] { "ORDER BY", "GROUP BY", "HAVING", "LIMIT", "UNION" };
        var endIndex = query.Length;

        foreach (var keyword in endKeywords)
        {
            var keywordIndex = upperQuery.IndexOf(keyword, whereIndex, StringComparison.Ordinal);
            if (keywordIndex >= 0 && keywordIndex < endIndex)
            {
                endIndex = keywordIndex;
            }
        }

        return query[whereIndex..endIndex].Trim();
    }

    /// <summary>
    /// Parses a WHERE clause to find shard key conditions.
    /// </summary>
    /// <param name="whereClause">The WHERE clause to parse.</param>
    /// <param name="parameters">Query parameters.</param>
    /// <returns>The shard key value, or null if not found.</returns>
    private object? ParseShardKeyCondition(string whereClause, IReadOnlyDictionary<string, object?>? parameters)
    {
        // Simple parsing for conditions like: id = 123, id = @param, id = 'value'
        var conditions = whereClause.Split(new[] { " AND ", " OR " }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var condition in conditions)
        {
            var trimmed = condition.Trim();
            if (trimmed.StartsWith($"{_shardKey.ColumnName} =", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith($"{_shardKey.ColumnName}=", StringComparison.OrdinalIgnoreCase))
            {
                var valuePart = trimmed[(trimmed.IndexOf('=') + 1)..].Trim();

                // Handle parameter placeholders
                if (valuePart.StartsWith('@') && parameters is not null)
                {
                    var paramName = valuePart[1..];
                    if (parameters.TryGetValue(paramName, out var paramValue))
                    {
                        return paramValue;
                    }
                }
                // Handle literal values
                else if (TryParseLiteralValue(valuePart, out var literalValue))
                {
                    return literalValue;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Attempts to parse a literal value from a SQL string.
    /// </summary>
    /// <param name="valueStr">The string representation of the value.</param>
    /// <param name="value">The parsed value.</param>
    /// <returns>True if parsing succeeded.</returns>
    private static bool TryParseLiteralValue(string valueStr, out object? value)
    {
        value = null;

        // String literals
        if (valueStr.StartsWith('\'') && valueStr.EndsWith('\''))
        {
            value = valueStr[1..^1];
            return true;
        }

        // Numeric literals
        if (int.TryParse(valueStr, out var intValue))
        {
            value = intValue;
            return true;
        }

        if (long.TryParse(valueStr, out var longValue))
        {
            value = longValue;
            return true;
        }

        if (decimal.TryParse(valueStr, out var decimalValue))
        {
            value = decimalValue;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Determines if a query requires access to multiple shards.
    /// </summary>
    /// <param name="query">The SQL query.</param>
    /// <returns>True if the query is cross-shard.</returns>
    private static bool IsCrossShardQuery(string query)
    {
        var upperQuery = query.ToUpperInvariant();

        // Queries that typically span multiple shards
        return upperQuery.Contains(" JOIN ", StringComparison.Ordinal) ||
               upperQuery.Contains(" UNION ", StringComparison.Ordinal) ||
               upperQuery.Contains(" INTERSECT ", StringComparison.Ordinal) ||
               upperQuery.Contains(" EXCEPT ", StringComparison.Ordinal) ||
               upperQuery.Contains(" IN (SELECT ", StringComparison.Ordinal) ||
               upperQuery.Contains(" EXISTS (SELECT ", StringComparison.Ordinal) ||
               upperQuery.Contains(" ALL ", StringComparison.Ordinal) ||
               upperQuery.Contains(" ANY ", StringComparison.Ordinal);
    }

    /// <summary>
    /// Gets routing statistics for monitoring.
    /// </summary>
    /// <returns>Routing statistics.</returns>
    public ShardRoutingStats GetRoutingStats()
    {
        var onlineShards = _shardManager.GetOnlineShards();
        var offlineShards = _shardManager.GetOfflineShards();

        return new ShardRoutingStats
        {
            TotalShards = _shardManager.ShardCount,
            OnlineShards = onlineShards.Count,
            OfflineShards = offlineShards.Count,
            ShardKeyColumn = _shardKey.ColumnName,
            ShardKeyStrategy = _shardKey.Strategy
        };
    }
}

/// <summary>
/// Statistics for shard routing operations.
/// </summary>
public class ShardRoutingStats
{
    /// <summary>Gets the total number of configured shards.</summary>
    public int TotalShards { get; init; }

    /// <summary>Gets the number of online shards.</summary>
    public int OnlineShards { get; init; }

    /// <summary>Gets the number of offline shards.</summary>
    public int OfflineShards { get; init; }

    /// <summary>Gets the shard key column name.</summary>
    public string ShardKeyColumn { get; init; } = string.Empty;

    /// <summary>Gets the shard key strategy.</summary>
    public ShardKeyStrategy ShardKeyStrategy { get; init; }
}
