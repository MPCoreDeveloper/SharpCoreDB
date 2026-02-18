// <copyright file="TraversalPlanCacheKey.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Graph.Caching;

using SharpCoreDB.Interfaces;
using System;

/// <summary>
/// Cache key for traversal plans.
/// Uniquely identifies a traversal configuration for caching purposes.
/// ✅ GraphRAG Phase 5.2: Query plan caching for 10x speedup.
/// </summary>
public readonly record struct TraversalPlanCacheKey
{
    /// <summary>
    /// Initializes a new cache key.
    /// </summary>
    /// <param name="tableName">The table name being traversed.</param>
    /// <param name="relationshipColumn">The ROWREF column name.</param>
    /// <param name="maxDepth">Maximum traversal depth.</param>
    /// <param name="strategy">Traversal strategy.</param>
    /// <param name="heuristic">A* heuristic (if using A* strategy).</param>
    public TraversalPlanCacheKey(
        string tableName,
        string relationshipColumn,
        int maxDepth,
        GraphTraversalStrategy strategy,
        AStarHeuristic heuristic = AStarHeuristic.Depth)
    {
        TableName = tableName;
        RelationshipColumn = relationshipColumn;
        MaxDepth = maxDepth;
        Strategy = strategy;
        Heuristic = heuristic;
    }

    /// <summary>
    /// Gets the table name.
    /// </summary>
    public string TableName { get; }

    /// <summary>
    /// Gets the relationship column name.
    /// </summary>
    public string RelationshipColumn { get; }

    /// <summary>
    /// Gets the maximum traversal depth.
    /// </summary>
    public int MaxDepth { get; }

    /// <summary>
    /// Gets the traversal strategy.
    /// </summary>
    public GraphTraversalStrategy Strategy { get; }

    /// <summary>
    /// Gets the A* heuristic (only relevant for A* strategy).
    /// </summary>
    public AStarHeuristic Heuristic { get; }

    /// <summary>
    /// Converts the key to a string representation for debugging.
    /// </summary>
    /// <returns>String representation.</returns>
    public override string ToString()
    {
        var heuristicPart = Strategy == GraphTraversalStrategy.AStar
            ? $"|H:{Heuristic}"
            : string.Empty;

        return $"{TableName}|{RelationshipColumn}|{MaxDepth}|{Strategy}{heuristicPart}";
    }
}
