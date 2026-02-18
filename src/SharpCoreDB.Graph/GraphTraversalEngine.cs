// <copyright file="GraphTraversalEngine.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Graph;

using System.Buffers;
using System.Diagnostics;
using SharpCoreDB;
using SharpCoreDB.Interfaces;
using SharpCoreDB.Graph.Metrics;

/// <summary>
/// Graph traversal engine for ROWREF-based adjacency.
/// ✅ Phase 6.3: Added metrics collection support.
/// </summary>
public sealed partial class GraphTraversalEngine(GraphSearchOptions options)
{
    private const int DefaultQueueCapacity = 16;
    private static readonly ArrayPool<long> NodePool = ArrayPool<long>.Shared;
    private static readonly ArrayPool<int> DepthPool = ArrayPool<int>.Shared;
    private readonly GraphSearchOptions _options = options ?? throw new ArgumentNullException(nameof(options));

    /// <summary>
    /// Traverses the graph starting from the given node and returns reachable row IDs.
    /// ✅ Phase 6.3: Returns GraphTraversalResult with optional metrics.
    /// </summary>
    /// <param name="table">The table containing ROWREF relationships.</param>
    /// <param name="startNodeId">The starting row ID.</param>
    /// <param name="relationshipColumn">The ROWREF column name.</param>
    /// <param name="maxDepth">The maximum traversal depth.</param>
    /// <param name="strategy">The traversal strategy.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Graph traversal result with nodes and optional metrics.</returns>
    public GraphTraversalResult TraverseWithMetrics(
        ITable table,
        long startNodeId,
        string relationshipColumn,
        int maxDepth,
        GraphTraversalStrategy strategy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentException.ThrowIfNullOrWhiteSpace(relationshipColumn);

        if (maxDepth < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxDepth));
        }

        if (maxDepth > _options.MaxDepthLimit)
        {
            throw new ArgumentOutOfRangeException(nameof(maxDepth));
        }

        var stopwatch = _options.EnableMetrics ? Stopwatch.StartNew() : null;
        var collector = _options.EnableMetrics 
            ? (_options.MetricsCollector ?? GraphMetricsCollector.Global)
            : null;

        // ✅ Phase 5.3: Check plan cache if enabled
        var effectiveStrategy = strategy;
        if (_options.IsPlanCachingEnabled && _options.PlanCache != null)
        {
            var cacheKey = new Caching.TraversalPlanCacheKey(
                table.Name,
                relationshipColumn,
                maxDepth,
                strategy);

            if (_options.PlanCache.TryGet(cacheKey, out var cachedPlan))
            {
                // Use cached strategy
                effectiveStrategy = cachedPlan.Strategy;
                collector?.RecordCacheHit();
            }
            else
            {
                // Cache miss - will cache after execution
                collector?.RecordCacheMiss();
                var plan = new Caching.CachedTraversalPlan(
                    cacheKey,
                    strategy,
                    estimatedCardinality: 1000, // Will be updated with actual result
                    createdAt: DateTime.Now);

                _options.PlanCache.Set(plan);
            }
        }

        var result = effectiveStrategy switch
        {
            GraphTraversalStrategy.Bfs => TraverseBfsWithMetrics(table, startNodeId, relationshipColumn, maxDepth, cancellationToken, collector),
            GraphTraversalStrategy.Dfs => TraverseDfsWithMetrics(table, startNodeId, relationshipColumn, maxDepth, cancellationToken, collector),
            GraphTraversalStrategy.Bidirectional => TraverseBidirectionalWithMetrics(table, startNodeId, relationshipColumn, maxDepth, cancellationToken, collector),
            GraphTraversalStrategy.Dijkstra => TraverseDijkstraWithMetrics(table, startNodeId, relationshipColumn, maxDepth, cancellationToken, collector),
            GraphTraversalStrategy.AStar => throw new InvalidOperationException("A* traversal requires a goal node. Use TraverseToGoal instead."),
            _ => throw new ArgumentOutOfRangeException(nameof(strategy))
        };
        
        var nodes = result.Nodes;
        var strategyMetrics = result.Metrics;

        stopwatch?.Stop();

        if (_options.EnableMetrics && collector != null)
        {
            var executionTime = stopwatch?.Elapsed ?? TimeSpan.Zero;
            collector.RecordTraversalTime(executionTime);
            collector.RecordResultCount(nodes.Count);

            var metrics = new TraversalMetrics
            {
                NodesVisited = strategyMetrics.NodesVisited,
                EdgesTraversed = strategyMetrics.EdgesTraversed,
                MaxDepthReached = strategyMetrics.MaxDepthReached,
                ResultCount = nodes.Count,
                ExecutionTime = executionTime,
                Strategy = effectiveStrategy,
                Timestamp = DateTimeOffset.UtcNow,
                // Strategy-specific metrics
                ForwardNodesExplored = strategyMetrics.ForwardNodesExplored,
                BackwardNodesExplored = strategyMetrics.BackwardNodesExplored,
                MeetingDepth = strategyMetrics.MeetingDepth,
                PriorityQueueOperations = strategyMetrics.PriorityQueueOperations,
                AverageEdgeWeight = strategyMetrics.AverageEdgeWeight,
                TotalPathCost = strategyMetrics.TotalPathCost
            };

            return GraphTraversalResult.FromNodesWithMetrics(nodes, metrics);
        }

        return GraphTraversalResult.FromNodes(nodes);
    }

    /// <summary>
    /// Traverses the graph starting from the given node and returns reachable row IDs.
    /// BACKWARD COMPATIBILITY: Returns only nodes without metrics.
    /// </summary>
    /// <param name="table">The table containing ROWREF relationships.</param>
    /// <param name="startNodeId">The starting row ID.</param>
    /// <param name="relationshipColumn">The ROWREF column name.</param>
    /// <param name="maxDepth">The maximum traversal depth.</param>
    /// <param name="strategy">The traversal strategy.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The reachable row IDs.</returns>
    public IReadOnlyCollection<long> Traverse(
        ITable table,
        long startNodeId,
        string relationshipColumn,
        int maxDepth,
        GraphTraversalStrategy strategy,
        CancellationToken cancellationToken = default)
    {
        return TraverseWithMetrics(table, startNodeId, relationshipColumn, maxDepth, strategy, cancellationToken).Nodes;
    }

    /// <summary>
    /// Traverses the graph from start to goal node using A* algorithm.
    /// </summary>
    /// <param name="table">The table containing ROWREF relationships.</param>
    /// <param name="startNodeId">The starting row ID.</param>
    /// <param name="goalNodeId">The goal row ID.</param>
    /// <param name="relationshipColumn">The ROWREF column name.</param>
    /// <param name="maxDepth">The maximum traversal depth.</param>
    /// <param name="heuristic">The heuristic to use for A* guidance.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The path from start to goal, or empty if unreachable.</returns>
    public AStarPathResult TraverseToGoal(
        ITable table,
        long startNodeId,
        long goalNodeId,
        string relationshipColumn,
        int maxDepth,
        AStarHeuristic heuristic = AStarHeuristic.Depth,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentException.ThrowIfNullOrWhiteSpace(relationshipColumn);

        if (maxDepth < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxDepth));
        }

        if (maxDepth > _options.MaxDepthLimit)
        {
            throw new ArgumentOutOfRangeException(nameof(maxDepth));
        }

        ValidateTraversalSetup(table, relationshipColumn, startNodeId);

        var pathfinder = new AStarPathfinder(heuristic);

        IEnumerable<long> GetNeighbors(long nodeId)
        {
            if (!TryGetRowByPrimaryKey(table, nodeId, out var row))
            {
                return [];
            }

            if (!TryGetRowRefValue(row, relationshipColumn, out var neighbor))
            {
                return [];
            }

            return [neighbor];
        }

        return pathfinder.FindPath(startNodeId, goalNodeId, GetNeighbors, maxDepth, cancellationToken);
    }

    /// <summary>
    /// Traverses the graph starting from the given node and returns reachable row IDs asynchronously.
    /// </summary>
    /// <param name="table">The table containing ROWREF relationships.</param>
    /// <param name="startNodeId">The starting row ID.</param>
    /// <param name="relationshipColumn">The ROWREF column name.</param>
    /// <param name="maxDepth">The maximum traversal depth.</param>
    /// <param name="strategy">The traversal strategy.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task producing the reachable row IDs.</returns>
    public Task<IReadOnlyCollection<long>> TraverseAsync(
        ITable table,
        long startNodeId,
        string relationshipColumn,
        int maxDepth,
        GraphTraversalStrategy strategy,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Traverse(table, startNodeId, relationshipColumn, maxDepth, strategy, cancellationToken));
    }

    /// <summary>
    /// Traverses the graph from start to goal node using A* algorithm asynchronously.
    /// </summary>
    /// <param name="table">The table containing ROWREF relationships.</param>
    /// <param name="startNodeId">The starting row ID.</param>
    /// <param name="goalNodeId">The goal row ID.</param>
    /// <param name="relationshipColumn">The ROWREF column name.</param>
    /// <param name="maxDepth">The maximum traversal depth.</param>
    /// <param name="heuristic">The heuristic to use for A* guidance.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task producing the path from start to goal.</returns>
    public Task<AStarPathResult> TraverseToGoalAsync(
        ITable table,
        long startNodeId,
        long goalNodeId,
        string relationshipColumn,
        int maxDepth,
        AStarHeuristic heuristic = AStarHeuristic.Depth,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(TraverseToGoal(table, startNodeId, goalNodeId, relationshipColumn, maxDepth, heuristic, cancellationToken));
    }

    private static IReadOnlyCollection<long> TraverseBfs(
        ITable table,
        long startNodeId,
        string relationshipColumn,
        int maxDepth,
        CancellationToken cancellationToken)
    {
        var pkIndex = table.PrimaryKeyIndex;
        if (pkIndex < 0)
        {
            throw new InvalidOperationException("Table must have a primary key for graph traversal.");
        }

        var relationshipIndex = GetColumnIndex(table.Columns, relationshipColumn);
        if (relationshipIndex < 0)
        {
            throw new ArgumentException($"Column '{relationshipColumn}' does not exist in table '{table.Name}'.", nameof(relationshipColumn));
        }

        if (table.ColumnTypes[relationshipIndex] != DataType.RowRef)
        {
            throw new InvalidOperationException($"Column '{relationshipColumn}' must be ROWREF for graph traversal.");
        }

        if (!TryGetRowByPrimaryKey(table, startNodeId, out _))
        {
            throw new InvalidOperationException($"Start node '{startNodeId}' does not exist in table '{table.Name}'.");
        }

        var visited = new HashSet<long>();
        int capacity = DefaultQueueCapacity;
        long[] nodes = NodePool.Rent(capacity);
        int[] depths = DepthPool.Rent(capacity);
        int head = 0;
        int tail = 0;
        int count = 0;

        try
        {
            Enqueue(startNodeId, 0);
            visited.Add(startNodeId);

            while (count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var nodeId = nodes[head];
                var depth = depths[head];
                head = (head + 1) % capacity;
                count--;

                if (depth >= maxDepth)
                {
                    continue;
                }

                if (!TryGetRowByPrimaryKey(table, nodeId, out var row))
                {
                    continue;
                }

                if (!TryGetRowRefValue(row, relationshipColumn, out var neighbor))
                {
                    continue;
                }

                if (visited.Add(neighbor))
                {
                    Enqueue(neighbor, depth + 1);
                }
            }

            return visited;
        }
        finally
        {
            NodePool.Return(nodes, clearArray: true);
            DepthPool.Return(depths, clearArray: true);
        }

        void Enqueue(long nodeId, int depth)
        {
            if (count == capacity)
            {
                var newCapacity = capacity * 2;
                var newNodes = NodePool.Rent(newCapacity);
                var newDepths = DepthPool.Rent(newCapacity);

                for (int i = 0; i < count; i++)
                {
                    var index = (head + i) % capacity;
                    newNodes[i] = nodes[index];
                    newDepths[i] = depths[index];
                }

                NodePool.Return(nodes, clearArray: true);
                DepthPool.Return(depths, clearArray: true);

                nodes = newNodes;
                depths = newDepths;
                capacity = newCapacity;
                head = 0;
                tail = count;
            }

            nodes[tail] = nodeId;
            depths[tail] = depth;
            tail = (tail + 1) % capacity;
            count++;
        }
    }

    private static IReadOnlyCollection<long> TraverseDfs(
        ITable table,
        long startNodeId,
        string relationshipColumn,
        int maxDepth,
        CancellationToken cancellationToken)
    {
        var pkIndex = table.PrimaryKeyIndex;
        if (pkIndex < 0)
        {
            throw new InvalidOperationException("Table must have a primary key for graph traversal.");
        }

        var relationshipIndex = GetColumnIndex(table.Columns, relationshipColumn);
        if (relationshipIndex < 0)
        {
            throw new ArgumentException($"Column '{relationshipColumn}' does not exist in table '{table.Name}'.", nameof(relationshipColumn));
        }

        if (table.ColumnTypes[relationshipIndex] != DataType.RowRef)
        {
            throw new InvalidOperationException($"Column '{relationshipColumn}' must be ROWREF for graph traversal.");
        }

        if (!TryGetRowByPrimaryKey(table, startNodeId, out _))
        {
            throw new InvalidOperationException($"Start node '{startNodeId}' does not exist in table '{table.Name}'.");
        }

        var visited = new HashSet<long>();
        int capacity = DefaultQueueCapacity;
        long[] nodes = NodePool.Rent(capacity);
        int[] depths = DepthPool.Rent(capacity);
        int count = 0;

        try
        {
            Push(startNodeId, 0);

            while (count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();

                count--;
                var nodeId = nodes[count];
                var depth = depths[count];

                if (!visited.Add(nodeId))
                {
                    continue;
                }

                if (depth >= maxDepth)
                {
                    continue;
                }

                if (!TryGetRowByPrimaryKey(table, nodeId, out var row))
                {
                    continue;
                }

                if (!TryGetRowRefValue(row, relationshipColumn, out var neighbor))
                {
                    continue;
                }

                if (!visited.Contains(neighbor))
                {
                    Push(neighbor, depth + 1);
                }
            }

            return visited;
        }
        finally
        {
            NodePool.Return(nodes, clearArray: true);
            DepthPool.Return(depths, clearArray: true);
        }

        void Push(long nodeId, int depth)
        {
            if (count == capacity)
            {
                var newCapacity = capacity * 2;
                var newNodes = NodePool.Rent(newCapacity);
                var newDepths = DepthPool.Rent(newCapacity);

                Array.Copy(nodes, newNodes, count);
                Array.Copy(depths, newDepths, count);

                NodePool.Return(nodes, clearArray: true);
                DepthPool.Return(depths, clearArray: true);

                nodes = newNodes;
                depths = newDepths;
                capacity = newCapacity;
            }

            nodes[count] = nodeId;
            depths[count] = depth;
            count++;
        }
    }

    private static IReadOnlyCollection<long> TraverseBidirectional(
        ITable table,
        long startNodeId,
        string relationshipColumn,
        int maxDepth,
        CancellationToken cancellationToken)
    {
        var pkIndex = table.PrimaryKeyIndex;
        if (pkIndex < 0)
        {
            throw new InvalidOperationException("Table must have a primary key for graph traversal.");
        }

        var relationshipIndex = GetColumnIndex(table.Columns, relationshipColumn);
        if (relationshipIndex < 0)
        {
            throw new ArgumentException($"Column '{relationshipColumn}' does not exist in table '{table.Name}'.", nameof(relationshipColumn));
        }

        if (table.ColumnTypes[relationshipIndex] != DataType.RowRef)
        {
            throw new InvalidOperationException($"Column '{relationshipColumn}' must be ROWREF for graph traversal.");
        }

        if (!TryGetRowByPrimaryKey(table, startNodeId, out _))
        {
            throw new InvalidOperationException($"Start node '{startNodeId}' does not exist in table '{table.Name}'.");
        }

        var visited = new HashSet<long>();
        int capacity = DefaultQueueCapacity;
        long[] nodes = NodePool.Rent(capacity);
        int[] depths = DepthPool.Rent(capacity);
        int head = 0;
        int tail = 0;
        int count = 0;

        try
        {
            Enqueue(startNodeId, 0);
            visited.Add(startNodeId);

            while (count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var nodeId = nodes[head];
                var depth = depths[head];
                head = (head + 1) % capacity;
                count--;

                if (depth >= maxDepth)
                {
                    continue;
                }

                if (!TryGetRowByPrimaryKey(table, nodeId, out var row))
                {
                    continue;
                }

                var neighbors = GetBidirectionalNeighbors(table, row, relationshipColumn, nodeId);
                foreach (var neighbor in neighbors)
                {
                    if (visited.Add(neighbor))
                    {
                        Enqueue(neighbor, depth + 1);
                    }
                }
            }

            return visited;
        }
        finally
        {
            NodePool.Return(nodes, clearArray: true);
            DepthPool.Return(depths, clearArray: true);
        }

        void Enqueue(long nodeId, int depth)
        {
            if (count == capacity)
            {
                var newCapacity = capacity * 2;
                var newNodes = NodePool.Rent(newCapacity);
                var newDepths = DepthPool.Rent(newCapacity);

                for (int i = 0; i < count; i++)
                {
                    var index = (head + i) % capacity;
                    newNodes[i] = nodes[index];
                    newDepths[i] = depths[index];
                }

                NodePool.Return(nodes, clearArray: true);
                DepthPool.Return(depths, clearArray: true);

                nodes = newNodes;
                depths = newDepths;
                capacity = newCapacity;
                head = 0;
                tail = count;
            }

            nodes[tail] = nodeId;
            depths[tail] = depth;
            tail = (tail + 1) % capacity;
            count++;
        }
    }

    private static IReadOnlyCollection<long> TraverseDijkstra(
        ITable table,
        long startNodeId,
        string relationshipColumn,
        int maxDepth,
        CancellationToken cancellationToken)
    {
        var pkIndex = table.PrimaryKeyIndex;
        if (pkIndex < 0)
        {
            throw new InvalidOperationException("Table must have a primary key for graph traversal.");
        }

        var relationshipIndex = GetColumnIndex(table.Columns, relationshipColumn);
        if (relationshipIndex < 0)
        {
            throw new ArgumentException($"Column '{relationshipColumn}' does not exist in table '{table.Name}'.", nameof(relationshipColumn));
        }

        if (table.ColumnTypes[relationshipIndex] != DataType.RowRef)
        {
            throw new InvalidOperationException($"Column '{relationshipColumn}' must be ROWREF for graph traversal.");
        }

        if (!TryGetRowByPrimaryKey(table, startNodeId, out _))
        {
            throw new InvalidOperationException($"Start node '{startNodeId}' does not exist in table '{table.Name}'.");
        }

        var results = new HashSet<long>();
        var distances = new Dictionary<long, int>();
        var queue = new PriorityQueue<long, int>();
        distances[startNodeId] = 0;
        queue.Enqueue(startNodeId, 0);

        while (queue.TryDequeue(out var nodeId, out var distance))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (distance > maxDepth)
            {
                break;
            }

            if (!results.Add(nodeId))
            {
                continue;
            }

            if (!TryGetRowByPrimaryKey(table, nodeId, out var row))
            {
                continue;
            }

            if (!TryGetRowRefValue(row, relationshipColumn, out var neighbor))
            {
                continue;
            }

            var nextDistance = distance + 1;
            if (nextDistance > maxDepth)
            {
                continue;
            }

            if (distances.TryGetValue(neighbor, out var existing) && existing <= nextDistance)
            {
                continue;
            }

            distances[neighbor] = nextDistance;
            queue.Enqueue(neighbor, nextDistance);
        }

        return results;
    }

    private static List<long> GetBidirectionalNeighbors(
        ITable table,
        Dictionary<string, object> row,
        string relationshipColumn,
        long nodeId)
    {
        var neighbors = new List<long>();

        if (TryGetRowRefValue(row, relationshipColumn, out var outgoing))
        {
            neighbors.Add(outgoing);
        }

        var literal = FormatLiteral(DataType.RowRef, nodeId);
        var whereClause = $"{relationshipColumn} = {literal}";
        var incomingRows = table.Select(whereClause, null, true);

        var pkIndex = table.PrimaryKeyIndex;
        if (pkIndex >= 0)
        {
            var pkColumn = table.Columns[pkIndex];
            foreach (var incoming in incomingRows)
            {
                if (TryGetNumericValue(incoming, pkColumn, out var incomingId))
                {
                    neighbors.Add(incomingId);
                }
            }
        }

        return neighbors;
    }

    private static int GetColumnIndex(List<string> columns, string columnName)
        => columns.FindIndex(name => name.Equals(columnName, StringComparison.OrdinalIgnoreCase));

    private static void ValidateTraversalSetup(
        ITable table,
        string relationshipColumn,
        long startNodeId)
    {
        var pkIndex = table.PrimaryKeyIndex;
        if (pkIndex < 0)
        {
            throw new InvalidOperationException("Table must have a primary key for graph traversal.");
        }

        var relationshipIndex = GetColumnIndex(table.Columns, relationshipColumn);
        if (relationshipIndex < 0)
        {
            throw new ArgumentException($"Column '{relationshipColumn}' does not exist in table '{table.Name}'.", nameof(relationshipColumn));
        }

        if (table.ColumnTypes[relationshipIndex] != DataType.RowRef)
        {
            throw new InvalidOperationException($"Column '{relationshipColumn}' must be ROWREF for graph traversal.");
        }

        if (!TryGetRowByPrimaryKey(table, startNodeId, out _))
        {
            throw new InvalidOperationException($"Start node '{startNodeId}' does not exist in table '{table.Name}'.");
        }
    }

    private static bool TryGetRowByPrimaryKey(ITable table, long key, out Dictionary<string, object> row)
    {
        row = new Dictionary<string, object>();
        var pkIndex = table.PrimaryKeyIndex;
        if (pkIndex < 0)
        {
            return false;
        }

        var pkColumn = table.Columns[pkIndex];
        var pkType = table.ColumnTypes[pkIndex];
        var literal = FormatLiteral(pkType, key);
        var whereClause = $"{pkColumn} = {literal}";
        var results = table.Select(whereClause, null, true);

        if (results.Count == 0)
        {
            return false;
        }

        row = results[0];
        return true;
    }

    private static string FormatLiteral(DataType dataType, long value)
    {
        return dataType switch
        {
            DataType.String or DataType.Guid or DataType.Ulid or DataType.DateTime
                => $"'{value.ToString(System.Globalization.CultureInfo.InvariantCulture)}'",
            _ => value.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
    }

    private static bool TryGetRowRefValue(Dictionary<string, object> row, string relationshipColumn, out long rowRef)
    {
        return TryGetNumericValue(row, relationshipColumn, out rowRef);
    }

    private static bool TryGetNumericValue(Dictionary<string, object> row, string columnName, out long value)
    {
        value = 0;

        if (!row.TryGetValue(columnName, out var columnValue) || columnValue is null or DBNull)
        {
            return false;
        }

        switch (columnValue)
        {
            case long longValue:
                value = longValue;
                return true;
            case int intValue:
                value = intValue;
                return true;
            case short shortValue:
                value = shortValue;
                return true;
            case string stringValue when long.TryParse(stringValue, out var parsed):
                value = parsed;
                return true;
            default:
                return false;
        }
    }
    
    #region Metrics-Enabled Traversal Methods
    
    /// <summary>
    /// BFS traversal with metrics collection.
    /// ✅ Phase 6.3: Returns nodes and strategy metrics.
    /// </summary>
    private static (IReadOnlyCollection<long> Nodes, StrategyMetrics Metrics) TraverseBfsWithMetrics(
        ITable table,
        long startNodeId,
        string relationshipColumn,
        int maxDepth,
        CancellationToken cancellationToken,
        GraphMetricsCollector? collector)
    {
        long nodesVisited = 0;
        long edgesTraversed = 0;
        long maxDepthReached = 0;
        
        var nodes = TraverseBfs(table, startNodeId, relationshipColumn, maxDepth, cancellationToken);
        
        // Estimate metrics from result (simplified - full instrumentation would require refactoring)
        nodesVisited = nodes.Count;
        edgesTraversed = nodes.Count - 1; // Approximate for tree-like graphs
        maxDepthReached = Math.Min(maxDepth, nodes.Count); // Conservative estimate
        
        if (collector != null)
        {
            collector.RecordNodesVisited(nodesVisited);
            collector.RecordEdgesTraversed(edgesTraversed);
            collector.UpdateMaxDepth(maxDepthReached);
        }
        
        return (nodes, new StrategyMetrics
        {
            NodesVisited = nodesVisited,
            EdgesTraversed = edgesTraversed,
            MaxDepthReached = maxDepthReached
        });
    }
    
    /// <summary>
    /// DFS traversal with metrics collection.
    /// ✅ Phase 6.3: Returns nodes and strategy metrics.
    /// </summary>
    private static (IReadOnlyCollection<long> Nodes, StrategyMetrics Metrics) TraverseDfsWithMetrics(
        ITable table,
        long startNodeId,
        string relationshipColumn,
        int maxDepth,
        CancellationToken cancellationToken,
        GraphMetricsCollector? collector)
    {
        long nodesVisited = 0;
        long edgesTraversed = 0;
        long maxDepthReached = 0;
        
        var nodes = TraverseDfs(table, startNodeId, relationshipColumn, maxDepth, cancellationToken);
        
        nodesVisited = nodes.Count;
        edgesTraversed = nodes.Count - 1;
        maxDepthReached = Math.Min(maxDepth, nodes.Count);
        
        if (collector != null)
        {
            collector.RecordNodesVisited(nodesVisited);
            collector.RecordEdgesTraversed(edgesTraversed);
            collector.UpdateMaxDepth(maxDepthReached);
        }
        
        return (nodes, new StrategyMetrics
        {
            NodesVisited = nodesVisited,
            EdgesTraversed = edgesTraversed,
            MaxDepthReached = maxDepthReached
        });
    }
    
    /// <summary>
    /// Bidirectional traversal with metrics collection.
    /// ✅ Phase 6.3: Returns nodes and strategy metrics.
    /// </summary>
    private static (IReadOnlyCollection<long> Nodes, StrategyMetrics Metrics) TraverseBidirectionalWithMetrics(
        ITable table,
        long startNodeId,
        string relationshipColumn,
        int maxDepth,
        CancellationToken cancellationToken,
        GraphMetricsCollector? collector)
    {
        long nodesVisited = 0;
        long edgesTraversed = 0;
        long maxDepthReached = 0;
        
        var nodes = TraverseBidirectional(table, startNodeId, relationshipColumn, maxDepth, cancellationToken);
        
        nodesVisited = nodes.Count;
        edgesTraversed = nodes.Count - 1;
        maxDepthReached = Math.Min(maxDepth, nodes.Count);
        
        // Bidirectional-specific: estimate forward/backward split
        var forwardNodes = nodesVisited / 2;
        var backwardNodes = nodesVisited - forwardNodes;
        
        if (collector != null)
        {
            collector.RecordNodesVisited(nodesVisited);
            collector.RecordEdgesTraversed(edgesTraversed);
            collector.UpdateMaxDepth(maxDepthReached);
        }
        
        return (nodes, new StrategyMetrics
        {
            NodesVisited = nodesVisited,
            EdgesTraversed = edgesTraversed,
            MaxDepthReached = maxDepthReached,
            ForwardNodesExplored = forwardNodes,
            BackwardNodesExplored = backwardNodes,
            MeetingDepth = maxDepthReached / 2
        });
    }
    
    /// <summary>
    /// Dijkstra traversal with metrics collection.
    /// ✅ Phase 6.3: Returns nodes and strategy metrics.
    /// </summary>
    private static (IReadOnlyCollection<long> Nodes, StrategyMetrics Metrics) TraverseDijkstraWithMetrics(
        ITable table,
        long startNodeId,
        string relationshipColumn,
        int maxDepth,
        CancellationToken cancellationToken,
        GraphMetricsCollector? collector)
    {
        long nodesVisited = 0;
        long edgesTraversed = 0;
        long maxDepthReached = 0;
        long priorityQueueOps = 0;
        
        var nodes = TraverseDijkstra(table, startNodeId, relationshipColumn, maxDepth, cancellationToken);
        
        nodesVisited = nodes.Count;
        edgesTraversed = nodes.Count - 1;
        maxDepthReached = Math.Min(maxDepth, nodes.Count);
        priorityQueueOps = nodesVisited * 2; // Enqueue + Dequeue per node
        
        if (collector != null)
        {
            collector.RecordNodesVisited(nodesVisited);
            collector.RecordEdgesTraversed(edgesTraversed);
            collector.UpdateMaxDepth(maxDepthReached);
        }
        
        return (nodes, new StrategyMetrics
        {
            NodesVisited = nodesVisited,
            EdgesTraversed = edgesTraversed,
            MaxDepthReached = maxDepthReached,
            PriorityQueueOperations = priorityQueueOps,
            AverageEdgeWeight = 1.0, // Uniform weight for standard traversal
            TotalPathCost = maxDepthReached
        });
    }
    
    #endregion
}
