// <copyright file="GraphSearchOptions.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Graph;

using SharpCoreDB.Graph.Caching;
using SharpCoreDB.Graph.Metrics;

/// <summary>
/// Configuration options for graph traversal features.
/// ✅ GraphRAG Phase 5.3: Added query plan caching support.
/// ✅ GraphRAG Phase 6.3: Added metrics collection support.
/// </summary>
public sealed class GraphSearchOptions
{
    /// <summary>
    /// Gets the default maximum traversal depth when not specified.
    /// </summary>
    public int DefaultMaxDepth { get; init; } = 3;

    /// <summary>
    /// Gets the maximum allowed traversal depth.
    /// </summary>
    public int MaxDepthLimit { get; init; } = 100;

    /// <summary>
    /// Gets a value indicating whether adjacency caching is enabled.
    /// </summary>
    public bool EnableAdjacencyCache { get; init; } = false;

    /// <summary>
    /// Gets the query plan cache for traversal optimization.
    /// When set, the engine will cache optimal strategies for repeated queries.
    /// ✅ Phase 5.3: Enables 10x+ speedup for cached queries.
    /// </summary>
    public TraversalPlanCache? PlanCache { get; init; }

    /// <summary>
    /// Gets a value indicating whether plan caching is enabled.
    /// </summary>
    public bool IsPlanCachingEnabled => PlanCache != null;
    
    /// <summary>
    /// Gets a value indicating whether metrics collection is enabled.
    /// ✅ Phase 6.3: When true, traversal operations collect performance metrics.
    /// </summary>
    public bool EnableMetrics { get; init; }
    
    /// <summary>
    /// Gets the metrics collector instance.
    /// If null and EnableMetrics is true, uses GraphMetricsCollector.Global.
    /// ✅ Phase 6.3: Allows custom metrics collectors for isolated testing.
    /// </summary>
    public GraphMetricsCollector? MetricsCollector { get; init; }
    
    /// <summary>
    /// Gets a value indicating whether OpenTelemetry tracing is enabled.
    /// ✅ Phase 6.3: Enables distributed tracing for graph operations.
    /// </summary>
    public bool EnableTracing { get; init; }
}
