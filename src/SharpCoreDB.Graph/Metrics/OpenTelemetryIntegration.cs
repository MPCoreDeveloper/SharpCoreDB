// <copyright file="OpenTelemetryIntegration.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Graph.Metrics;

using System.Diagnostics;
using System.Diagnostics.Metrics;

/// <summary>
/// OpenTelemetry integration for GraphRAG metrics and tracing.
/// ✅ GraphRAG Phase 6.3: Standards-based observability for distributed systems.
/// </summary>
/// <remarks>
/// <para>
/// Provides ActivitySource for distributed tracing and Meter for metrics export.
/// Integrates seamlessly with OpenTelemetry collectors and observability platforms.
/// </para>
/// <para><strong>Usage Example:</strong></para>
/// <code>
/// // Initialize OpenTelemetry in application startup
/// var tracingProvider = new TracerProvider()
///     .AddSource(OpenTelemetryIntegration.ActivitySourceName)
///     .AddConsoleExporter();
///
/// var meterProvider = new MeterProvider()
///     .AddMeter(OpenTelemetryIntegration.MeterName)
///     .AddConsoleExporter();
///
/// // Activities and Meters are automatically instrumented in graph operations
/// </code>
/// </remarks>
public static class OpenTelemetryIntegration
{
    /// <summary>OpenTelemetry Activity Source name for graph operations.</summary>
    public const string ActivitySourceName = "SharpCoreDB.Graph";

    /// <summary>OpenTelemetry Meter name for graph metrics.</summary>
    public const string MeterName = "SharpCoreDB.Graph";

    /// <summary>Instrumentation version.</summary>
    public const string InstrumentationVersion = "6.3.0";

    /// <summary>Activity Source for distributed tracing of graph operations.</summary>
    public static readonly ActivitySource ActivitySource = new(ActivitySourceName, InstrumentationVersion);

    /// <summary>Meter for metrics export and collection.</summary>
    public static readonly Meter Meter = new(MeterName, InstrumentationVersion);

    // Counters
    /// <summary>Counter for total nodes visited across all traversals.</summary>
    public static readonly Counter<long> NodesVisitedCounter = Meter.CreateCounter<long>(
        "graph.nodes_visited",
        "nodes",
        "Total nodes explored in graph traversals");

    /// <summary>Counter for total edges traversed.</summary>
    public static readonly Counter<long> EdgesTraversedCounter = Meter.CreateCounter<long>(
        "graph.edges_traversed",
        "edges",
        "Total edges followed during traversals");

    /// <summary>Counter for total cache hits.</summary>
    public static readonly Counter<long> CacheHitsCounter = Meter.CreateCounter<long>(
        "graph.cache_hits",
        "operations",
        "Total cache hits for traversal plans");

    /// <summary>Counter for total cache misses.</summary>
    public static readonly Counter<long> CacheMissesCounter = Meter.CreateCounter<long>(
        "graph.cache_misses",
        "operations",
        "Total cache misses for traversal plans");

    /// <summary>Counter for total heuristic evaluations.</summary>
    public static readonly Counter<long> HeuristicCallsCounter = Meter.CreateCounter<long>(
        "graph.heuristic_calls",
        "calls",
        "Total heuristic function evaluations");

    /// <summary>Counter for optimizer invocations.</summary>
    public static readonly Counter<long> OptimizerInvocationsCounter = Meter.CreateCounter<long>(
        "graph.optimizer_invocations",
        "invocations",
        "Total traversal strategy optimizer invocations");

    // Histograms
    /// <summary>Histogram for traversal execution duration.</summary>
    public static readonly Histogram<double> TraversalDurationHistogram = Meter.CreateHistogram<double>(
        "graph.traversal_duration",
        "milliseconds",
        "Execution time for graph traversal operations");

    /// <summary>Histogram for cache lookup time.</summary>
    public static readonly Histogram<double> CacheLookupTimeHistogram = Meter.CreateHistogram<double>(
        "graph.cache_lookup_duration",
        "milliseconds",
        "Time spent looking up entries in traversal plan cache");

    /// <summary>Histogram for heuristic evaluation time.</summary>
    public static readonly Histogram<double> HeuristicEvaluationHistogram = Meter.CreateHistogram<double>(
        "graph.heuristic_evaluation_duration",
        "milliseconds",
        "Time spent evaluating heuristic functions");

    /// <summary>Histogram for optimizer cost prediction error.</summary>
    public static readonly Histogram<double> PredictionErrorHistogram = Meter.CreateHistogram<double>(
        "graph.prediction_error",
        "percentage",
        "Cost prediction error percentage (optimizer accuracy)");

    /// <summary>Histogram for cache hit rate.</summary>
    public static readonly Histogram<double> CacheHitRateHistogram = Meter.CreateHistogram<double>(
        "graph.cache_hit_rate",
        "percentage",
        "Cache hit rate percentage");

    /// <summary>
    /// Create an Activity for a graph traversal operation.
    /// </summary>
    /// <param name="operationName">Name of the operation (e.g., "GraphTraversal.BFS").</param>
    /// <returns>An Activity that should be disposed when the operation completes.</returns>
    public static Activity? StartGraphTraversalActivity(string operationName)
    {
        var activity = ActivitySource.StartActivity(operationName);
        return activity;
    }

    /// <summary>
    /// Create an Activity for a cache operation.
    /// </summary>
    /// <param name="operationName">Name of the operation (e.g., "Cache.Lookup").</param>
    /// <returns>An Activity that should be disposed when the operation completes.</returns>
    public static Activity? StartCacheActivity(string operationName)
    {
        var activity = ActivitySource.StartActivity(operationName);
        return activity;
    }

    /// <summary>
    /// Create an Activity for an optimizer operation.
    /// </summary>
    /// <param name="operationName">Name of the operation (e.g., "Optimizer.SelectStrategy").</param>
    /// <returns>An Activity that should be disposed when the operation completes.</returns>
    public static Activity? StartOptimizerActivity(string operationName)
    {
        var activity = ActivitySource.StartActivity(operationName);
        return activity;
    }

    /// <summary>
    /// Record traversal operation to OpenTelemetry.
    /// </summary>
    public static void RecordTraversalMetrics(
        long nodesVisited,
        long edgesTraversed,
        double executionTimeMs)
    {
        NodesVisitedCounter.Add(nodesVisited);
        EdgesTraversedCounter.Add(edgesTraversed);
        TraversalDurationHistogram.Record(executionTimeMs);
    }

    /// <summary>
    /// Record cache operation to OpenTelemetry.
    /// </summary>
    public static void RecordCacheMetrics(
        bool isHit,
        double lookupTimeMs)
    {
        if (isHit)
        {
            CacheHitsCounter.Add(1);
        }
        else
        {
            CacheMissesCounter.Add(1);
        }

        CacheLookupTimeHistogram.Record(lookupTimeMs);
    }

    /// <summary>
    /// Record heuristic evaluation to OpenTelemetry.
    /// </summary>
    public static void RecordHeuristicMetrics(
        double evaluationTimeMs,
        bool wasAdmissible)
    {
        HeuristicCallsCounter.Add(1);
        HeuristicEvaluationHistogram.Record(evaluationTimeMs);
    }

    /// <summary>
    /// Record optimizer prediction to OpenTelemetry.
    /// </summary>
    public static void RecordOptimizerMetrics(
        double estimatedCostMs,
        double actualCostMs)
    {
        OptimizerInvocationsCounter.Add(1);

        if (actualCostMs > 0)
        {
            var errorPercentage = Math.Abs(estimatedCostMs - actualCostMs) / actualCostMs * 100;
            PredictionErrorHistogram.Record(errorPercentage);
        }
    }
}
