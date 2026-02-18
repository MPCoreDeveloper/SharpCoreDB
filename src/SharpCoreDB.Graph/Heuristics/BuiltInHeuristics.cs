// <copyright file="BuiltInHeuristics.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Graph.Heuristics;

using System;
using System.Collections.Generic;

/// <summary>
/// Built-in heuristic functions for common graph types.
/// ✅ GraphRAG Phase 6.2: Pre-optimized heuristics for spatial and weighted graphs.
/// </summary>
public static class BuiltInHeuristics
{
    /// <summary>
    /// Uniform cost heuristic (h(n) = 0) - equivalent to Dijkstra's algorithm.
    /// </summary>
    /// <remarks>
    /// <para><strong>Use when:</strong></para>
    /// <list type="bullet">
    /// <item><description>You need optimal paths without domain knowledge</description></item>
    /// <item><description>Graph has no spatial/weight information</description></item>
    /// <item><description>Exploring all possible paths equally</description></item>
    /// </list>
    /// <para><strong>Performance:</strong> Slowest but always finds optimal path.</para>
    /// </remarks>
    public static CustomHeuristicFunction UniformCost { get; } = (current, goal, depth, maxDepth, context) => 0.0;

    /// <summary>
    /// Depth-based heuristic (h(n) = maxDepth - currentDepth).
    /// </summary>
    /// <remarks>
    /// <para><strong>Use when:</strong></para>
    /// <list type="bullet">
    /// <item><description>Preferring shorter paths</description></item>
    /// <item><description>No spatial information available</description></item>
    /// <item><description>Default fallback heuristic</description></item>
    /// </list>
    /// <para><strong>Performance:</strong> Fast but non-admissible (may not find optimal path).</para>
    /// </remarks>
    public static CustomHeuristicFunction DepthBased { get; } = (current, goal, depth, maxDepth, context) => maxDepth - depth;

    /// <summary>
    /// Manhattan distance heuristic for grid-based graphs.
    /// </summary>
    /// <param name="positionsKey">Context key for node positions (default: "positions").</param>
    /// <returns>Manhattan distance heuristic function.</returns>
    /// <remarks>
    /// <para><strong>Use when:</strong></para>
    /// <list type="bullet">
    /// <item><description>Graph represents a grid (city streets, tile maps)</description></item>
    /// <item><description>Movement is restricted to orthogonal directions</description></item>
    /// <item><description>Each node has 2D integer coordinates</description></item>
    /// </list>
    /// <para><strong>Requirements:</strong></para>
    /// <list type="number">
    /// <item><description>Context must contain <c>Dictionary&lt;long, (int X, int Y)&gt;</c> at specified key</description></item>
    /// <item><description>All nodes must have position data</description></item>
    /// </list>
    /// <para><strong>Formula:</strong> <c>|x1 - x2| + |y1 - y2|</c></para>
    /// <para><strong>Example:</strong></para>
    /// <code>
    /// var context = new HeuristicContext
    /// {
    ///     ["positions"] = new Dictionary&lt;long, (int X, int Y)&gt;
    ///     {
    ///         [1] = (0, 0),
    ///         [2] = (3, 4)
    ///     }
    /// };
    /// var heuristic = BuiltInHeuristics.ManhattanDistance();
    /// var distance = heuristic(1, 2, 0, 10, context); // Returns 7
    /// </code>
    /// </remarks>
    public static CustomHeuristicFunction ManhattanDistance(string positionsKey = "positions")
    {
        return (current, goal, depth, maxDepth, context) =>
        {
            if (!context.TryGetValue(positionsKey, out var positionsObj))
                throw new ArgumentException($"Context must contain '{positionsKey}' key with node positions", nameof(context));

            var positions = (Dictionary<long, (int X, int Y)>)positionsObj;

            if (!positions.TryGetValue(current, out var currentPos))
                throw new ArgumentException($"Position not found for node {current}", nameof(current));

            if (!positions.TryGetValue(goal, out var goalPos))
                throw new ArgumentException($"Position not found for node {goal}", nameof(goal));

            return Math.Abs(currentPos.X - goalPos.X) + Math.Abs(currentPos.Y - goalPos.Y);
        };
    }

    /// <summary>
    /// Euclidean distance heuristic for geographic/continuous graphs.
    /// </summary>
    /// <param name="positionsKey">Context key for node positions (default: "positions").</param>
    /// <returns>Euclidean distance heuristic function.</returns>
    /// <remarks>
    /// <para><strong>Use when:</strong></para>
    /// <list type="bullet">
    /// <item><description>Graph represents geographic locations</description></item>
    /// <item><description>Movement is unrestricted (straight-line distance)</description></item>
    /// <item><description>Each node has 2D floating-point coordinates</description></item>
    /// </list>
    /// <para><strong>Requirements:</strong></para>
    /// <list type="number">
    /// <item><description>Context must contain <c>Dictionary&lt;long, (double X, double Y)&gt;</c> at specified key</description></item>
    /// <item><description>All nodes must have position data</description></item>
    /// </list>
    /// <para><strong>Formula:</strong> <c>√((x1 - x2)² + (y1 - y2)²)</c></para>
    /// <para><strong>Example:</strong></para>
    /// <code>
    /// var context = new HeuristicContext
    /// {
    ///     ["positions"] = new Dictionary&lt;long, (double X, double Y)&gt;
    ///     {
    ///         [1] = (0.0, 0.0),
    ///         [2] = (3.0, 4.0)
    ///     }
    /// };
    /// var heuristic = BuiltInHeuristics.EuclideanDistance();
    /// var distance = heuristic(1, 2, 0, 10, context); // Returns 5.0
    /// </code>
    /// </remarks>
    public static CustomHeuristicFunction EuclideanDistance(string positionsKey = "positions")
    {
        return (current, goal, depth, maxDepth, context) =>
        {
            if (!context.TryGetValue(positionsKey, out var positionsObj))
                throw new ArgumentException($"Context must contain '{positionsKey}' key with node positions", nameof(context));

            var positions = (Dictionary<long, (double X, double Y)>)positionsObj;

            if (!positions.TryGetValue(current, out var currentPos))
                throw new ArgumentException($"Position not found for node {current}", nameof(current));

            if (!positions.TryGetValue(goal, out var goalPos))
                throw new ArgumentException($"Position not found for node {goal}", nameof(goal));

            var dx = currentPos.X - goalPos.X;
            var dy = currentPos.Y - goalPos.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        };
    }

    /// <summary>
    /// Weighted cost heuristic using edge weights.
    /// </summary>
    /// <param name="weightsKey">Context key for edge weights (default: "weights").</param>
    /// <param name="defaultWeight">Default weight if edge not found (default: 1.0).</param>
    /// <returns>Weighted cost heuristic function.</returns>
    /// <remarks>
    /// <para><strong>Use when:</strong></para>
    /// <list type="bullet">
    /// <item><description>Graph has weighted edges (cost, distance, time)</description></item>
    /// <item><description>Need to minimize total weight</description></item>
    /// <item><description>Each edge has an associated numeric cost</description></item>
    /// </list>
    /// <para><strong>Requirements:</strong></para>
    /// <list type="number">
    /// <item><description>Context must contain <c>Dictionary&lt;(long From, long To), double&gt;</c> at specified key</description></item>
    /// </list>
    /// <para><strong>Example:</strong></para>
    /// <code>
    /// var context = new HeuristicContext
    /// {
    ///     ["weights"] = new Dictionary&lt;(long, long), double&gt;
    ///     {
    ///         [(1, 2)] = 5.0,
    ///         [(2, 3)] = 3.0
    ///     }
    /// };
    /// var heuristic = BuiltInHeuristics.WeightedCost();
    /// </code>
    /// </remarks>
    public static CustomHeuristicFunction WeightedCost(string weightsKey = "weights", double defaultWeight = 1.0)
    {
        return (current, goal, depth, maxDepth, context) =>
        {
            if (!context.TryGetValue(weightsKey, out var weightsObj))
                return defaultWeight;

            var weights = (Dictionary<(long From, long To), double>)weightsObj;

            // Return weight of edge to goal if it exists
            if (weights.TryGetValue((current, goal), out var weight))
                return weight;

            return defaultWeight;
        };
    }
}
