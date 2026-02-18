// <copyright file="IGraphTraversalProvider.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Interfaces;

/// <summary>
/// Optional extension point for graph traversal support.
/// Registered via DI — zero overhead when not registered.
/// </summary>
public interface IGraphTraversalProvider
{
    /// <summary>
    /// Determines whether this provider can traverse the specified table and relationship column.
    /// </summary>
    /// <param name="tableName">The table name.</param>
    /// <param name="relationshipColumn">The ROWREF column name.</param>
    /// <param name="maxDepth">The maximum traversal depth.</param>
    /// <returns>True if the provider supports the traversal request.</returns>
    bool CanTraverse(string tableName, string relationshipColumn, int maxDepth);

    /// <summary>
    /// Traverses the graph starting from the given node and returns reachable row IDs.
    /// </summary>
    /// <param name="table">The table containing ROWREF relationships.</param>
    /// <param name="tableName">The table name.</param>
    /// <param name="startNodeId">The starting row ID.</param>
    /// <param name="relationshipColumn">The ROWREF column name.</param>
    /// <param name="maxDepth">The maximum traversal depth.</param>
    /// <param name="strategy">The traversal strategy.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task producing the reachable row IDs.</returns>
    Task<IReadOnlyCollection<long>> TraverseAsync(
        ITable table,
        string tableName,
        long startNodeId,
        string relationshipColumn,
        int maxDepth,
        GraphTraversalStrategy strategy,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Traversal strategy for graph traversal providers.
/// </summary>
public enum GraphTraversalStrategy
{
    /// <summary>
    /// Breadth-first traversal.
    /// </summary>
    Bfs = 0,

    /// <summary>
    /// Depth-first traversal.
    /// </summary>
    Dfs = 1,

    /// <summary>
    /// Traversal that explores both outgoing and incoming relationships.
    /// </summary>
    Bidirectional = 2,

    /// <summary>
    /// Weighted shortest-path traversal using Dijkstra's algorithm.
    /// </summary>
    Dijkstra = 3,

    /// <summary>
    /// Optimal shortest-path traversal using A* algorithm with heuristic guidance.
    /// Requires a goal node and is significantly faster than Dijkstra when goal is known.
    /// </summary>
    AStar = 4,
}

/// <summary>
/// Heuristic type for A* pathfinding.
/// </summary>
public enum AStarHeuristic
{
    /// <summary>
    /// Depth-based heuristic: h(n) = max_depth - current_depth.
    /// Best for unweighted graphs or when goal depth is known.
    /// </summary>
    Depth = 0,

    /// <summary>
    /// Uniform heuristic: h(n) = 0 (degenerates to Dijkstra).
    /// Use when no heuristic information is available.
    /// </summary>
    Uniform = 1,
}
