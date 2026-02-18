// <copyright file="StrategyMetrics.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Graph;

/// <summary>
/// Internal metrics collected during traversal execution.
/// C# 14: Record struct for zero-allocation internal metrics.
/// </summary>
internal readonly record struct StrategyMetrics
{
    public long NodesVisited { get; init; }
    public long EdgesTraversed { get; init; }
    public long MaxDepthReached { get; init; }
    
    // Bidirectional-specific
    public long ForwardNodesExplored { get; init; }
    public long BackwardNodesExplored { get; init; }
    public long MeetingDepth { get; init; }
    
    // Dijkstra/A*-specific
    public long PriorityQueueOperations { get; init; }
    public double AverageEdgeWeight { get; init; }
    public double TotalPathCost { get; init; }
    
    public static StrategyMetrics Empty => new();
}
