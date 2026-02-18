// <copyright file="AStarPathfinding.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Graph;

using SharpCoreDB.Interfaces;
using System;
using System.Collections.Generic;

/// <summary>
/// A* pathfinding node with cost information.
/// </summary>
internal struct AStarNode
{
    /// <summary>
    /// Gets or sets the node ID in the graph.
    /// </summary>
    public long NodeId { get; set; }

    /// <summary>
    /// Gets or sets the parent node ID (for path reconstruction).
    /// </summary>
    public long ParentId { get; set; }

    /// <summary>
    /// Gets or sets the actual cost from start node (g-cost).
    /// </summary>
    public int GCost { get; set; }

    /// <summary>
    /// Gets or sets the estimated cost to goal (h-cost from heuristic).
    /// </summary>
    public int HCost { get; set; }

    /// <summary>
    /// Gets the total estimated cost (f-cost = g-cost + h-cost).
    /// </summary>
    public int FCost => GCost + HCost;

    /// <summary>
    /// Gets or sets the depth in the traversal (for depth-based heuristics).
    /// </summary>
    public int Depth { get; set; }
}

/// <summary>
/// Result of A* pathfinding.
/// </summary>
public record AStarPathResult
{
    /// <summary>
    /// Gets the path as a list of node IDs from start to goal.
    /// Empty if goal is unreachable.
    /// </summary>
    public required List<long> Path { get; init; }

    /// <summary>
    /// Gets the actual cost of the path (g-cost).
    /// </summary>
    public required int PathCost { get; init; }

    /// <summary>
    /// Gets the number of nodes expanded during search.
    /// </summary>
    public required long NodesExpanded { get; init; }

    /// <summary>
    /// Gets a value indicating whether the goal was reached.
    /// </summary>
    public bool GoalReached => Path.Count > 0;

    /// <summary>
    /// Gets the depth of the found path.
    /// </summary>
    public int PathDepth => Path.Count - 1;
}

/// <summary>
/// A* pathfinding implementation for graph traversal.
/// ✅ GraphRAG Phase 4: Optimal shortest-path finding with heuristic guidance.
/// </summary>
public sealed class AStarPathfinder
{
    /// <summary>
    /// Initializes a new A* pathfinder with specified heuristic.
    /// </summary>
    /// <param name="heuristic">The heuristic function to use.</param>
    public AStarPathfinder(AStarHeuristic heuristic = AStarHeuristic.Depth)
    {
        Heuristic = heuristic;
    }

    /// <summary>
    /// Gets the heuristic being used.
    /// </summary>
    public AStarHeuristic Heuristic { get; }

    /// <summary>
    /// Finds the shortest path from start to goal using A* algorithm.
    /// </summary>
    /// <param name="startNodeId">The starting node ID.</param>
    /// <param name="goalNodeId">The goal node ID.</param>
    /// <param name="getNeighbors">Function to get neighbors of a node.</param>
    /// <param name="maxDepth">Maximum search depth.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A* path result containing path and metadata.</returns>
    public AStarPathResult FindPath(
        long startNodeId,
        long goalNodeId,
        Func<long, IEnumerable<long>> getNeighbors,
        int maxDepth,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(getNeighbors);

        if (maxDepth < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxDepth));
        }

        // Quick path: start equals goal
        if (startNodeId == goalNodeId)
        {
            return new AStarPathResult
            {
                Path = [startNodeId],
                PathCost = 0,
                NodesExpanded = 1,
            };
        }

        var openSet = new PriorityQueue<AStarNode, int>();
        var closedSet = new HashSet<long>();
        var gCosts = new Dictionary<long, int> { { startNodeId, 0 } };
        var parents = new Dictionary<long, long>();

        var startHeuristic = CalculateHeuristic(startNodeId, goalNodeId, 0, maxDepth);
        var startNode = new AStarNode
        {
            NodeId = startNodeId,
            ParentId = -1,
            GCost = 0,
            HCost = startHeuristic,
            Depth = 0,
        };

        openSet.Enqueue(startNode, startNode.FCost);
        long nodesExpanded = 0;

        while (openSet.TryDequeue(out var current, out _))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (current.NodeId == goalNodeId)
            {
                return ReconstructPath(parents, goalNodeId, current.GCost, nodesExpanded);
            }

            if (closedSet.Add(current.NodeId))
            {
                nodesExpanded++;
            }
            else
            {
                continue;
            }

            // Explore neighbors
            if (current.Depth < maxDepth)
            {
                var neighbors = getNeighbors(current.NodeId);
                foreach (var neighbor in neighbors)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (closedSet.Contains(neighbor))
                    {
                        continue;
                    }

                    var tentativeGCost = current.GCost + 1;
                    var hasNeighbor = gCosts.TryGetValue(neighbor, out var neighborGCost);

                    if (!hasNeighbor || tentativeGCost < neighborGCost)
                    {
                        parents[neighbor] = current.NodeId;
                        gCosts[neighbor] = tentativeGCost;

                        var hCost = CalculateHeuristic(neighbor, goalNodeId, current.Depth + 1, maxDepth);
                        var neighborNode = new AStarNode
                        {
                            NodeId = neighbor,
                            ParentId = current.NodeId,
                            GCost = tentativeGCost,
                            HCost = hCost,
                            Depth = current.Depth + 1,
                        };

                        openSet.Enqueue(neighborNode, neighborNode.FCost);
                    }
                }
            }
        }

        // Goal unreachable
        return new AStarPathResult
        {
            Path = [],
            PathCost = int.MaxValue,
            NodesExpanded = nodesExpanded,
        };
    }

    /// <summary>
    /// Calculates the heuristic cost from current node to goal.
    /// </summary>
    /// <param name="current">Current node ID (unused for most heuristics).</param>
    /// <param name="goal">Goal node ID (unused for most heuristics).</param>
    /// <param name="currentDepth">Current depth in traversal.</param>
    /// <param name="maxDepth">Maximum allowed depth.</param>
    /// <returns>Estimated cost to goal.</returns>
    private int CalculateHeuristic(long current, long goal, int currentDepth, int maxDepth)
    {
        return Heuristic switch
        {
            AStarHeuristic.Depth => Math.Max(0, maxDepth - currentDepth),
            AStarHeuristic.Uniform => 0,
            _ => 0,
        };
    }

    /// <summary>
    /// Reconstructs the path from start to goal using parent pointers.
    /// </summary>
    /// <param name="parents">Parent map built during search.</param>
    /// <param name="goal">Goal node ID.</param>
    /// <param name="pathCost">Cost of the path.</param>
    /// <param name="nodesExpanded">Number of nodes expanded.</param>
    /// <returns>The reconstructed path and metadata.</returns>
    private AStarPathResult ReconstructPath(
        Dictionary<long, long> parents,
        long goal,
        int pathCost,
        long nodesExpanded)
    {
        var path = new List<long>();
        var current = goal;

        while (current != -1)
        {
            path.Add(current);
            if (!parents.TryGetValue(current, out var parent))
            {
                break;
            }
            current = parent;
        }

        path.Reverse();

        return new AStarPathResult
        {
            Path = path,
            PathCost = pathCost,
            NodesExpanded = nodesExpanded,
        };
    }
}
