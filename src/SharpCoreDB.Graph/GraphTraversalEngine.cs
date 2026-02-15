// <copyright file="GraphTraversalEngine.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Graph;

using System.Buffers;
using SharpCoreDB;
using SharpCoreDB.Interfaces;

/// <summary>
/// Graph traversal engine for ROWREF-based adjacency.
/// </summary>
public sealed partial class GraphTraversalEngine(GraphSearchOptions options)
{
    private const int DefaultQueueCapacity = 16;
    private static readonly ArrayPool<long> NodePool = ArrayPool<long>.Shared;
    private static readonly ArrayPool<int> DepthPool = ArrayPool<int>.Shared;
    private readonly GraphSearchOptions _options = options ?? throw new ArgumentNullException(nameof(options));

    /// <summary>
    /// Traverses the graph starting from the given node and returns reachable row IDs.
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

        return strategy switch
        {
            GraphTraversalStrategy.Bfs => TraverseBfs(table, startNodeId, relationshipColumn, maxDepth, cancellationToken),
            GraphTraversalStrategy.Dfs => TraverseDfs(table, startNodeId, relationshipColumn, maxDepth, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(strategy))
        };
    }

    /// <summary>
    /// Traverses the graph starting from the given node and returns reachable row IDs.
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

    private static int GetColumnIndex(List<string> columns, string columnName)
        => columns.FindIndex(name => name.Equals(columnName, StringComparison.OrdinalIgnoreCase));

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
        rowRef = 0;

        if (!row.TryGetValue(relationshipColumn, out var value) || value is null or DBNull)
        {
            return false;
        }

        switch (value)
        {
            case long longValue:
                rowRef = longValue;
                return true;
            case int intValue:
                rowRef = intValue;
                return true;
            case short shortValue:
                rowRef = shortValue;
                return true;
            case string stringValue when long.TryParse(stringValue, out var parsed):
                rowRef = parsed;
                return true;
            default:
                return false;
        }
    }
}
