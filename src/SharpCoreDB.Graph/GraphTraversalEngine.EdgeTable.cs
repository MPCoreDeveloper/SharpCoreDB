// <copyright file="GraphTraversalEngine.EdgeTable.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Graph;

using System.Buffers;
using SharpCoreDB;
using SharpCoreDB.Interfaces;

/// <summary>
/// Edge table traversal support for GraphTraversalEngine.
/// ✅ GraphRAG Phase 2: Support for edge tables with source and target columns.
/// </summary>
public partial class GraphTraversalEngine
{
    /// <summary>
    /// Traverses a graph using an external edge table instead of ROWREF columns.
    /// </summary>
    /// <param name="nodeTable">The main table containing node properties.</param>
    /// <param name="edgeTable">The edge table with source and target node IDs.</param>
    /// <param name="startNodeId">The starting node ID.</param>
    /// <param name="maxDepth">The maximum traversal depth.</param>
    /// <param name="strategy">The traversal strategy (BFS/DFS).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The reachable node IDs.</returns>
    public IReadOnlyCollection<long> TraverseUsingEdgeTable(
        ITable nodeTable,
        ITable edgeTable,
        long startNodeId,
        int maxDepth,
        GraphTraversalStrategy strategy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(nodeTable);
        ArgumentNullException.ThrowIfNull(edgeTable);

        if (maxDepth < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxDepth));
        }

        if (maxDepth > _options.MaxDepthLimit)
        {
            throw new ArgumentOutOfRangeException(nameof(maxDepth));
        }

        if (!TryGetRowByPrimaryKey(nodeTable, startNodeId, out _))
        {
            throw new InvalidOperationException($"Start node '{startNodeId}' does not exist in node table.");
        }

        return strategy switch
        {
            GraphTraversalStrategy.Bfs => TraverseBfsEdgeTable(nodeTable, edgeTable, startNodeId, maxDepth, cancellationToken),
            GraphTraversalStrategy.Dfs => TraverseDfsEdgeTable(nodeTable, edgeTable, startNodeId, maxDepth, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(strategy))
        };
    }

    private IReadOnlyCollection<long> TraverseBfsEdgeTable(
        ITable nodeTable,
        ITable edgeTable,
        long startNodeId,
        int maxDepth,
        CancellationToken cancellationToken)
    {
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

                var neighbors = GetNeighborsFromEdgeTable(edgeTable, nodeId);
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

    private IReadOnlyCollection<long> TraverseDfsEdgeTable(
        ITable nodeTable,
        ITable edgeTable,
        long startNodeId,
        int maxDepth,
        CancellationToken cancellationToken)
    {
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

                var neighbors = GetNeighborsFromEdgeTable(edgeTable, nodeId);
                foreach (var neighbor in neighbors)
                {
                    if (!visited.Contains(neighbor))
                    {
                        Push(neighbor, depth + 1);
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

    private static List<long> GetNeighborsFromEdgeTable(ITable edgeTable, long sourceNodeId)
    {
        var neighbors = new List<long>();
        var literal = FormatLiteral(DataType.Long, sourceNodeId);
        var whereClause = $"source = {literal}";
        var edges = edgeTable.Select(whereClause, null, true);

        foreach (var edge in edges)
        {
            if (edge.TryGetValue("target", out var target) && target is long targetId)
            {
                neighbors.Add(targetId);
            }
        }

        return neighbors;
    }
}
