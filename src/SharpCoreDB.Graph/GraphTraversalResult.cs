// <copyright file="GraphTraversalResult.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Graph;

using SharpCoreDB.Graph.Metrics;

/// <summary>
/// Result of a graph traversal operation with optional metrics.
/// C# 14: Primary constructor with init-only properties.
/// ✅ Phase 6.3: Added metrics support for observability.
/// </summary>
public sealed class GraphTraversalResult
{
    /// <summary>
    /// Gets the reachable node IDs from the traversal.
    /// </summary>
    public required IReadOnlyCollection<long> Nodes { get; init; }
    
    /// <summary>
    /// Gets the traversal execution metrics (if metrics were enabled).
    /// </summary>
    public TraversalMetrics? Metrics { get; init; }
    
    /// <summary>
    /// Gets a value indicating whether metrics were collected.
    /// </summary>
    public bool HasMetrics => Metrics.HasValue;
    
    /// <summary>
    /// Creates a result without metrics (backward compatibility).
    /// </summary>
    public static GraphTraversalResult FromNodes(IReadOnlyCollection<long> nodes)
    {
        return new GraphTraversalResult { Nodes = nodes };
    }
    
    /// <summary>
    /// Creates a result with metrics.
    /// </summary>
    public static GraphTraversalResult FromNodesWithMetrics(
        IReadOnlyCollection<long> nodes, 
        TraversalMetrics metrics)
    {
        return new GraphTraversalResult 
        { 
            Nodes = nodes,
            Metrics = metrics
        };
    }
}
