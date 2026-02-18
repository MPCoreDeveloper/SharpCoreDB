// <copyright file="CustomHeuristicFunction.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Graph.Heuristics;

using System.Collections.Generic;

/// <summary>
/// Delegate for custom A* heuristic functions.
/// ✅ GraphRAG Phase 6.2: User-defined guidance for pathfinding optimization.
/// </summary>
/// <param name="currentNode">The current node being evaluated.</param>
/// <param name="goalNode">The target/goal node.</param>
/// <param name="currentDepth">Current traversal depth from start node.</param>
/// <param name="maxDepth">Maximum allowed traversal depth.</param>
/// <param name="context">Optional context data (e.g., node positions, weights).</param>
/// <returns>Estimated cost from current node to goal (lower = better).</returns>
/// <remarks>
/// <para><strong>Guidelines for writing efficient heuristics:</strong></para>
/// <list type="bullet">
/// <item><description><strong>Admissible:</strong> h(n) should never overestimate the actual cost (for optimal paths).</description></item>
/// <item><description><strong>Fast:</strong> Heuristic is called frequently; keep computation under 1ms.</description></item>
/// <item><description><strong>Consistent:</strong> h(n) ≤ cost(n, n') + h(n') for neighboring nodes.</description></item>
/// <item><description><strong>Domain-Specific:</strong> Use graph properties (positions, weights, business logic).</description></item>
/// </list>
/// <para><strong>Example (Manhattan Distance for grid graphs):</strong></para>
/// <code>
/// CustomHeuristicFunction manhattanHeuristic = (current, goal, depth, maxDepth, context) =>
/// {
///     var positions = (Dictionary&lt;long, (int X, int Y)&gt;)context["positions"];
///     var (x1, y1) = positions[current];
///     var (x2, y2) = positions[goal];
///     return Math.Abs(x1 - x2) + Math.Abs(y1 - y2);
/// };
/// </code>
/// </remarks>
public delegate double CustomHeuristicFunction(
    long currentNode,
    long goalNode,
    int currentDepth,
    int maxDepth,
    IReadOnlyDictionary<string, object> context);
