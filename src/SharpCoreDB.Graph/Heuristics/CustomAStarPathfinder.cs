// <copyright file="CustomAStarPathfinder.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Graph.Heuristics;

using System;
using System.Collections.Generic;
using System.Linq;
using SharpCoreDB.Interfaces;

/// <summary>
/// A* pathfinding with custom heuristic functions.
/// ✅ GraphRAG Phase 6.2: User-defined guidance for optimal pathfinding.
/// </summary>
/// <remarks>
/// <para>
/// Allows users to provide domain-specific heuristics to guide A* pathfinding
/// for better performance and more accurate path discovery.
/// </para>
/// <para><strong>Performance Impact:</strong></para>
/// <list type="bullet">
/// <item><description><strong>Good heuristics:</strong> 10-50% fewer nodes explored vs generic</description></item>
/// <item><description><strong>Bad heuristics:</strong> May find suboptimal paths or be slower</description></item>
/// <item><description><strong>Admissible heuristics:</strong> Guarantee optimal paths</description></item>
/// </list>
/// <para><strong>Example (Manhattan distance for grid graph):</strong></para>
/// <code>
/// var positions = new Dictionary&lt;long, (int X, int Y)&gt;
/// {
///     [1] = (0, 0),
///     [2] = (3, 4),
///     [3] = (6, 8)
/// };
/// var context = new HeuristicContext { ["positions"] = positions };
/// var heuristic = BuiltInHeuristics.ManhattanDistance();
/// var pathfinder = new CustomAStarPathfinder(heuristic);
/// 
/// var result = pathfinder.FindPath(
///     table: myTable,
///     startNodeId: 1,
///     goalNodeId: 3,
///     relationshipColumn: "next",
///     maxDepth: 10,
///     context: context);
/// 
/// Console.WriteLine($"Path: {string.Join(" -> ", result.Path)}");
/// Console.WriteLine($"Cost: {result.TotalCost}");
/// Console.WriteLine($"Nodes explored: {result.NodesExplored}");
/// </code>
/// </remarks>
public sealed class CustomAStarPathfinder
{
    private readonly CustomHeuristicFunction _heuristic;

    /// <summary>
    /// Initializes a new A* pathfinder with a custom heuristic.
    /// </summary>
    /// <param name="heuristic">The heuristic function to guide pathfinding.</param>
    /// <exception cref="ArgumentNullException">If heuristic is null.</exception>
    public CustomAStarPathfinder(CustomHeuristicFunction heuristic)
    {
        _heuristic = heuristic ?? throw new ArgumentNullException(nameof(heuristic));
    }

    /// <summary>
    /// Finds the shortest path using A* with custom heuristic.
    /// </summary>
    /// <param name="table">The table containing ROWREF relationships.</param>
    /// <param name="startNodeId">The starting row ID.</param>
    /// <param name="goalNodeId">The goal row ID.</param>
    /// <param name="relationshipColumn">The ROWREF column name.</param>
    /// <param name="maxDepth">The maximum search depth.</param>
    /// <param name="context">Optional heuristic context data.</param>
    /// <returns>The pathfinding result with path, cost, and statistics.</returns>
    /// <exception cref="ArgumentNullException">If table is null.</exception>
    /// <exception cref="ArgumentException">If relationshipColumn is null or whitespace.</exception>
    /// <exception cref="ArgumentOutOfRangeException">If maxDepth is negative.</exception>
    public CustomAStarResult FindPath(
        ITable table,
        long startNodeId,
        long goalNodeId,
        string relationshipColumn,
        int maxDepth,
        HeuristicContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentException.ThrowIfNullOrWhiteSpace(relationshipColumn);

        if (maxDepth < 0)
            throw new ArgumentOutOfRangeException(nameof(maxDepth), "Max depth must be non-negative");

        context ??= new HeuristicContext();

        var openSet = new PriorityQueue<long, double>();
        var cameFrom = new Dictionary<long, long>();
        var gScore = new Dictionary<long, double> { [startNodeId] = 0 };
        var fScore = new Dictionary<long, double> { [startNodeId] = _heuristic(startNodeId, goalNodeId, 0, maxDepth, context) };
        var depths = new Dictionary<long, int> { [startNodeId] = 0 };

        openSet.Enqueue(startNodeId, fScore[startNodeId]);

        int nodesExplored = 0;

        while (openSet.Count > 0)
        {
            var current = openSet.Dequeue();
            nodesExplored++;

            // Goal reached
            if (current == goalNodeId)
            {
                var path = ReconstructPath(cameFrom, current);
                var totalCost = gScore[current];
                return new CustomAStarResult(path, totalCost, nodesExplored, true);
            }

            var currentDepth = depths[current];

            // Max depth reached
            if (currentDepth >= maxDepth)
                continue;

            var neighbors = GetNeighbors(table, current, relationshipColumn);

            foreach (var neighbor in neighbors)
            {
                var tentativeGScore = gScore[current] + 1.0; // Edge cost = 1

                if (!gScore.TryGetValue(neighbor, out var neighborGScore) || tentativeGScore < neighborGScore)
                {
                    // This path is better
                    cameFrom[neighbor] = current;
                    gScore[neighbor] = tentativeGScore;
                    depths[neighbor] = currentDepth + 1;

                    var h = _heuristic(neighbor, goalNodeId, currentDepth + 1, maxDepth, context);
                    var f = tentativeGScore + h;
                    fScore[neighbor] = f;

                    openSet.Enqueue(neighbor, f);
                }
            }
        }

        // No path found
        return new CustomAStarResult([], 0, nodesExplored, false);
    }

    /// <summary>
    /// Finds the shortest path using A* with custom heuristic and edge costs.
    /// </summary>
    /// <param name="table">The table containing ROWREF relationships.</param>
    /// <param name="startNodeId">The starting row ID.</param>
    /// <param name="goalNodeId">The goal row ID.</param>
    /// <param name="relationshipColumn">The ROWREF column name.</param>
    /// <param name="costColumn">The column containing edge costs (default: uniform cost 1.0).</param>
    /// <param name="maxDepth">The maximum search depth.</param>
    /// <param name="context">Optional heuristic context data.</param>
    /// <returns>The pathfinding result with path, cost, and statistics.</returns>
    public CustomAStarResult FindPathWithCosts(
        ITable table,
        long startNodeId,
        long goalNodeId,
        string relationshipColumn,
        string costColumn,
        int maxDepth,
        HeuristicContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentException.ThrowIfNullOrWhiteSpace(relationshipColumn);
        ArgumentException.ThrowIfNullOrWhiteSpace(costColumn);

        if (maxDepth < 0)
            throw new ArgumentOutOfRangeException(nameof(maxDepth), "Max depth must be non-negative");

        context ??= new HeuristicContext();

        var openSet = new PriorityQueue<long, double>();
        var cameFrom = new Dictionary<long, long>();
        var gScore = new Dictionary<long, double> { [startNodeId] = 0 };
        var fScore = new Dictionary<long, double> { [startNodeId] = _heuristic(startNodeId, goalNodeId, 0, maxDepth, context) };
        var depths = new Dictionary<long, int> { [startNodeId] = 0 };

        openSet.Enqueue(startNodeId, fScore[startNodeId]);

        int nodesExplored = 0;

        while (openSet.Count > 0)
        {
            var current = openSet.Dequeue();
            nodesExplored++;

            if (current == goalNodeId)
            {
                var path = ReconstructPath(cameFrom, current);
                var totalCost = gScore[current];
                return new CustomAStarResult(path, totalCost, nodesExplored, true);
            }

            var currentDepth = depths[current];
            if (currentDepth >= maxDepth)
                continue;

            var neighbors = GetNeighborsWithCosts(table, current, relationshipColumn, costColumn);

            foreach (var (neighbor, cost) in neighbors)
            {
                var tentativeGScore = gScore[current] + cost;

                if (!gScore.TryGetValue(neighbor, out var neighborGScore) || tentativeGScore < neighborGScore)
                {
                    cameFrom[neighbor] = current;
                    gScore[neighbor] = tentativeGScore;
                    depths[neighbor] = currentDepth + 1;

                    var h = _heuristic(neighbor, goalNodeId, currentDepth + 1, maxDepth, context);
                    var f = tentativeGScore + h;
                    fScore[neighbor] = f;

                    openSet.Enqueue(neighbor, f);
                }
            }
        }

        return new CustomAStarResult([], 0, nodesExplored, false);
    }

    private static List<long> ReconstructPath(Dictionary<long, long> cameFrom, long current)
    {
        var path = new List<long> { current };

        while (cameFrom.ContainsKey(current))
        {
            current = cameFrom[current];
            path.Insert(0, current);
        }

        return path;
    }

    private static List<long> GetNeighbors(ITable table, long nodeId, string relationshipColumn)
    {
        var neighbors = new List<long>();

        try
        {
            var rows = table.Select($"id={nodeId}");
            if (rows.Count == 0)
                return neighbors;

            var row = rows[0];
            if (row.TryGetValue(relationshipColumn, out var value))
            {
                if (value is long neighborId && neighborId > 0)
                {
                    neighbors.Add(neighborId);
                }
                else if (value != DBNull.Value && value != null && long.TryParse(value.ToString(), out var parsed))
                {
                    neighbors.Add(parsed);
                }
            }
        }
        catch
        {
            // Node not found or invalid
        }

        return neighbors;
    }

    private static List<(long NeighborId, double Cost)> GetNeighborsWithCosts(
        ITable table,
        long nodeId,
        string relationshipColumn,
        string costColumn)
    {
        var neighbors = new List<(long, double)>();

        try
        {
            var rows = table.Select($"id={nodeId}");
            if (rows.Count == 0)
                return neighbors;

            var row = rows[0];

            if (!row.TryGetValue(relationshipColumn, out var relationshipValue))
                return neighbors;

            long neighborId = 0;
            if (relationshipValue is long id && id > 0)
            {
                neighborId = id;
            }
            else if (relationshipValue != DBNull.Value && relationshipValue != null && long.TryParse(relationshipValue.ToString(), out var parsed))
            {
                neighborId = parsed;
            }
            else
            {
                return neighbors;
            }

            // Get cost
            double cost = 1.0;
            if (row.TryGetValue(costColumn, out var costValue) && costValue != DBNull.Value && costValue != null)
            {
                if (costValue is double d)
                {
                    cost = d;
                }
                else if (double.TryParse(costValue.ToString(), out var parsedCost))
                {
                    cost = parsedCost;
                }
            }

            neighbors.Add((neighborId, cost));
        }
        catch
        {
            // Node not found or invalid
        }

        return neighbors;
    }
}

/// <summary>
/// Result of a custom A* pathfinding operation.
/// </summary>
/// <param name="Path">The discovered path (empty if no path found).</param>
/// <param name="TotalCost">The total cost of the path.</param>
/// <param name="NodesExplored">Number of nodes explored during search.</param>
/// <param name="Success">Whether a path was found.</param>
public sealed record CustomAStarResult(
    IReadOnlyList<long> Path,
    double TotalCost,
    int NodesExplored,
    bool Success);
