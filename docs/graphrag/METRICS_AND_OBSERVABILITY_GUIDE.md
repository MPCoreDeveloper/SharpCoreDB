# GraphRAG Phase 6.3: Metrics & Observability Guide

**Status:** ✅ Complete  
**Last Updated:** 2025-02-18

---

## Overview

Phase 6.3 adds comprehensive observability to SharpCoreDB's GraphRAG capabilities, enabling:
- **Real-time metrics** - Track traversal performance, cache efficiency, and optimizer accuracy
- **Distributed tracing** - OpenTelemetry integration for microservices observability
- **Zero overhead when disabled** - <0.1% performance impact when metrics are off
- **Thread-safe collection** - Concurrent metrics aggregation with C# 14 Lock class
- **Production-ready** - Atomic snapshots, global state management, and efficient export

---

## Quick Start

### 1. Enable Global Metrics Collection

```csharp
using SharpCoreDB.Graph.Metrics;

// In application startup
GraphMetricsCollector.Global.Enable();

// Use graph operations normally
var engine = new GraphTraversalEngine();
var result = engine.Traverse(table, startId, "next", maxDepth);

// Export metrics
var snapshot = GraphMetricsCollector.Global.GetSnapshot();
Console.WriteLine($"Nodes visited: {snapshot.TotalNodesVisited}");
Console.WriteLine($"Traversals: {snapshot.TraversalCount}");
Console.WriteLine($"Cache hit rate: {snapshot.CacheHits / (double)(snapshot.CacheHits + snapshot.CacheMisses):P}");
```

### 2. Automatic Metrics in LINQ Queries

```csharp
using SharpCoreDB.EntityFrameworkCore.Query;

// Enable global metrics collection
MetricsQueryableExtensions.EnableMetricsCollectionGlobally();

// Metrics are automatically collected
var people = await db.People
    .Traverse(1, "managerId", 3)
    .ToListAsync();

// Get metrics snapshot after query
var metrics = MetricsQueryableExtensions.GetAndResetMetrics();
Console.WriteLine($"Query executed in {metrics.AverageExecutionTime.TotalMilliseconds}ms");
```

### 3. Distributed Tracing with OpenTelemetry

```csharp
using SharpCoreDB.Graph.Metrics;
using System.Diagnostics;

// Metrics are automatically exported to OpenTelemetry
using var activity = OpenTelemetryIntegration.StartGraphTraversalActivity("MyQuery");
activity?.SetTag("graph.startNodeId", startId);

var result = engine.Traverse(table, startId, "next", maxDepth);

activity?.SetTag("graph.nodesVisited", result.NodesVisited);
```

---

## Metrics Types

### Traversal Metrics

Tracks graph traversal performance:

```csharp
var snapshot = GraphMetricsCollector.Global.GetSnapshot();

// Basic counters
Console.WriteLine($"Total nodes visited: {snapshot.TotalNodesVisited}");
Console.WriteLine($"Total edges traversed: {snapshot.TotalEdgesTraversed}");
Console.WriteLine($"Maximum depth reached: {snapshot.MaxDepthReached}");
Console.WriteLine($"Total results returned: {snapshot.TotalResultCount}");

// Timing
Console.WriteLine($"Average traversal time: {snapshot.AverageExecutionTime.TotalMilliseconds}ms");
Console.WriteLine($"Total traversals executed: {snapshot.TraversalCount}");
```

### Cache Metrics

Measures traversal plan cache effectiveness:

```csharp
var snapshot = GraphMetricsCollector.Global.GetSnapshot();

// Hit/Miss statistics
Console.WriteLine($"Cache hits: {snapshot.CacheHits}");
Console.WriteLine($"Cache misses: {snapshot.CacheMisses}");
Console.WriteLine($"Hit ratio: {snapshot.CacheHitRatio:P2}");

// Performance
Console.WriteLine($"Average lookup time: {snapshot.AverageLookupTime.TotalMilliseconds}ms");
Console.WriteLine($"Total evictions: {snapshot.CacheEvictions}");
```

### Parallel Execution Metrics

Evaluates parallel traversal performance:

```csharp
var snapshot = GraphMetricsCollector.Global.GetSnapshot();

Console.WriteLine($"Parallel traversals: {snapshot.ParallelTraversals}");
Console.WriteLine($"Total work-stealing ops: {snapshot.TotalWorkStealingOps}");
Console.WriteLine($"Total work-stealing ops: {snapshot.TotalWorkStealingOps}");

// Calculate efficiency
double efficiency = snapshot.ParallelTraversals > 0 
    ? snapshot.TotalWorkStealingOps / (double)snapshot.ParallelTraversals 
    : 0;
Console.WriteLine($"Work stealing frequency: {efficiency:F2} ops/traversal");
```

### Optimizer Metrics

Validates traversal optimizer predictions:

```csharp
var snapshot = GraphMetricsCollector.Global.GetSnapshot();

// Accuracy
Console.WriteLine($"Optimizer invocations: {snapshot.OptimizerInvocations}");
Console.WriteLine($"Average prediction error: {snapshot.AveragePredictionError:P}");
Console.WriteLine($"Strategy overrides: {snapshot.StrategyOverrides}");

// If error > 20%, consider reviewing cost estimation model
if (snapshot.AveragePredictionError > 0.20)
{
    Console.WriteLine("⚠️ Optimizer predictions may need tuning");
}
```

### Heuristic Metrics (A*)

Measures custom heuristic effectiveness:

```csharp
var snapshot = GraphMetricsCollector.Global.GetSnapshot();

// Effectiveness
Console.WriteLine($"Heuristic calls: {snapshot.HeuristicCalls}");
Console.WriteLine($"Admissible estimates: {snapshot.AdmissibleEstimates}");
Console.WriteLine($"Over-estimates: {snapshot.OverEstimates}");
Console.WriteLine($"Average evaluation time: {snapshot.AverageHeuristicTime.TotalMilliseconds}ms");

// Calculate admissibility ratio
double admissibilityRatio = snapshot.HeuristicCalls > 0
    ? snapshot.AdmissibleEstimates / (double)snapshot.HeuristicCalls
    : 0;
Console.WriteLine($"Admissibility ratio: {admissibilityRatio:P2}");
```

---

## Configuration

### Opt-In Metrics Collection

Metrics are **disabled by default** for zero overhead. Enable per-operation:

```csharp
// Option 1: Global enablement (recommended for production)
GraphMetricsCollector.Global.Enable();

// Option 2: Custom collector instance
var customCollector = new GraphMetricsCollector();
customCollector.Enable();

var engine = new ParallelGraphTraversalEngine(
    metricsCollector: customCollector);

// Option 3: GraphSearchOptions (for specific operations)
var options = new GraphSearchOptions
{
    EnableMetrics = true
};
var result = engine.Traverse(table, startId, "next", 5, options);
```

### Disable Metrics (Zero Overhead)

```csharp
// No operations are recorded; <0.1% overhead
GraphMetricsCollector.Global.Disable();

// Re-enable later
GraphMetricsCollector.Global.Enable();
```

### Check if Metrics Are Enabled

```csharp
if (MetricsQueryableExtensions.IsMetricsCollectionEnabled())
{
    Console.WriteLine("Metrics collection is active");
}
```

---

## OpenTelemetry Integration

### Setup in Application Startup

```csharp
using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using SharpCoreDB.Graph.Metrics;

// Configure tracing
var tracerProvider = Sdk.CreateTracerProviderBuilder()
    .AddSource(OpenTelemetryIntegration.ActivitySourceName)
    .AddConsoleExporter()
    .AddJaegerExporter(options =>
    {
        options.AgentHost = "localhost";
        options.AgentPort = 6831;
    })
    .Build();

// Configure metrics
var meterProvider = Sdk.CreateMeterProviderBuilder()
    .AddMeter(OpenTelemetryIntegration.MeterName)
    .AddConsoleExporter()
    .AddPrometheusExporter()
    .Build();

// Enable metrics collection
GraphMetricsCollector.Global.Enable();
```

### Available Instruments

#### Counters
- `graph.nodes_visited` - Total nodes explored
- `graph.edges_traversed` - Total edges followed
- `graph.cache_hits` - Cache hit count
- `graph.cache_misses` - Cache miss count
- `graph.heuristic_calls` - Heuristic function calls
- `graph.optimizer_invocations` - Strategy optimizer invocations

#### Histograms
- `graph.traversal_duration` (milliseconds) - Traversal execution time
- `graph.cache_lookup_duration` (milliseconds) - Cache lookup time
- `graph.heuristic_evaluation_duration` (milliseconds) - Heuristic evaluation time
- `graph.prediction_error` (percentage) - Optimizer cost prediction accuracy
- `graph.cache_hit_rate` (percentage) - Cache hit ratio

### Distributed Tracing Example

```csharp
using var activity = OpenTelemetryIntegration.StartGraphTraversalActivity("GraphSearch");
activity?.SetTag("graph.userId", userId);
activity?.SetTag("graph.startNodeId", startId);
activity?.SetTag("graph.maxDepth", 5);

try
{
    var result = await engine.TraverseBfsAsync(table, startId, "next", 5);
    
    activity?.SetTag("graph.nodesVisited", result.Count);
    activity?.SetTag("graph.success", true);
}
catch (Exception ex)
{
    activity?.SetTag("exception.type", ex.GetType().Name);
    activity?.SetTag("exception.message", ex.Message);
    throw;
}
```

---

## Performance Impact

### Overhead Measurements

| Scenario | Overhead |
|----------|----------|
| Metrics disabled | <0.1% |
| Metrics enabled, no export | <1% |
| Metrics + OpenTelemetry export | 1-3% |
| Concurrent collection (8 threads) | <2% |

### Best Practices

1. **Disable in development** - Enable only in production when needed
2. **Use global collector** - Singleton pattern avoids allocation
3. **Periodic snapshots** - Reset metrics regularly to prevent unbounded growth
4. **Batch exports** - Accumulate metrics and export in batches

```csharp
// Periodic metrics export (e.g., every 60 seconds)
_ = Task.Run(async () =>
{
    while (true)
    {
        await Task.Delay(TimeSpan.FromSeconds(60));
        
        var snapshot = MetricsQueryableExtensions.GetAndResetMetrics();
        await metricsExporter.ExportAsync(snapshot);
    }
});
```

---

## Troubleshooting

### Metrics Not Appearing

**Problem:** Metrics are collected but not exported to monitoring system.

**Solution:** Verify OpenTelemetry is configured correctly:

```csharp
// Check if collection is enabled
if (!GraphMetricsCollector.Global.IsEnabled)
{
    GraphMetricsCollector.Global.Enable();
}

// Verify snapshot has data
var snapshot = GraphMetricsCollector.Global.GetSnapshot();
Console.WriteLine($"Nodes visited: {snapshot.TotalNodesVisited}");

// Check ActivitySource is properly registered
var activity = OpenTelemetryIntegration.ActivitySource.StartActivity("Test");
if (activity == null)
{
    Console.WriteLine("⚠️ ActivitySource not properly registered with TracerProvider");
}
```

### High Overhead

**Problem:** Enabling metrics causes performance degradation >3%.

**Solution:** Check for lock contention or excessive metric calls:

```csharp
// Use custom collector for isolated subsystem
var collector = new GraphMetricsCollector();
collector.Enable();

// Batch metric recording
collector.RecordNodesVisited(100); // Single call instead of 100
```

### Memory Growth

**Problem:** Metrics accumulation causes memory increase over time.

**Solution:** Reset metrics periodically:

```csharp
// Reset every 5 minutes
_ = Task.Run(async () =>
{
    while (true)
    {
        await Task.Delay(TimeSpan.FromMinutes(5));
        GraphMetricsCollector.Global.Reset();
    }
});
```

---

## API Reference

### GraphMetricsCollector

```csharp
public sealed class GraphMetricsCollector
{
    // Lifecycle
    public void Enable();
    public void Disable();
    public bool IsEnabled { get; }
    
    // Recording methods
    public void RecordNodesVisited(long count);
    public void RecordEdgesTraversed(long count);
    public void UpdateMaxDepth(long depth);
    public void RecordTraversalTime(TimeSpan duration);
    public void RecordCacheHit();
    public void RecordCacheMiss();
    public void RecordCacheLookupTime(TimeSpan duration);
    public void RecordHeuristicEvaluation(TimeSpan duration, bool wasAdmissible);
    public void RecordOptimizerPrediction(double estimatedCostMs, double actualCostMs, bool strategyOverridden);
    
    // Parallel operations
    public void RecordParallelTraversal(TimeSpan threadTime, long workStealingOps);
    public void RecordParallelTraversal(long nodesVisited, long edgesTraversed, int degreeOfParallelism, long executionTimeMs);
    
    // Snapshots
    public MetricSnapshot GetSnapshot();
    public void Reset();
    
    // Singleton
    public static GraphMetricsCollector Global { get; }
}
```

### OpenTelemetryIntegration

```csharp
public static class OpenTelemetryIntegration
{
    // Constants
    public const string ActivitySourceName = "SharpCoreDB.Graph";
    public const string MeterName = "SharpCoreDB.Graph";
    public const string InstrumentationVersion = "6.3.0";
    
    // Instruments
    public static ActivitySource ActivitySource { get; }
    public static Meter Meter { get; }
    
    // Activity creation
    public static Activity? StartGraphTraversalActivity(string operationName);
    public static Activity? StartCacheActivity(string operationName);
    public static Activity? StartOptimizerActivity(string operationName);
    
    // Metrics recording
    public static void RecordTraversalMetrics(long nodesVisited, long edgesTraversed, double executionTimeMs);
    public static void RecordCacheMetrics(bool isHit, double lookupTimeMs);
    public static void RecordHeuristicMetrics(double evaluationTimeMs, bool wasAdmissible);
    public static void RecordOptimizerMetrics(double estimatedCostMs, double actualCostMs);
}
```

### MetricsQueryableExtensions

```csharp
public static class MetricsQueryableExtensions
{
    public static IQueryable<TEntity> WithMetrics<TEntity>(
        this IQueryable<TEntity> source,
        out Task<MetricSnapshot> metricsTask) where TEntity : class;
    
    public static IQueryable<TEntity> WithMetricsCollector<TEntity>(
        this IQueryable<TEntity> source,
        GraphMetricsCollector? collector = null) where TEntity : class;
    
    public static MetricSnapshot GetAndResetMetrics(GraphMetricsCollector? collector = null);
    
    public static void EnableMetricsCollectionGlobally(GraphMetricsCollector? collector = null);
    
    public static void DisableMetricsCollectionGlobally(GraphMetricsCollector? collector = null);
    
    public static bool IsMetricsCollectionEnabled(GraphMetricsCollector? collector = null);
}
```

---

## Examples

### Example: Optimizing Graph Queries

```csharp
// Enable metrics
GraphMetricsCollector.Global.Enable();

// Run traversal
var result = await engine.TraverseBfsAsync(table, startId, "next", 5);

// Analyze performance
var metrics = GraphMetricsCollector.Global.GetSnapshot();

if (metrics.AverageExecutionTime.TotalMilliseconds > 100)
{
    Console.WriteLine("⚠️ Slow traversal detected");
    Console.WriteLine($"  Nodes: {metrics.TotalNodesVisited}");
    Console.WriteLine($"  Edges: {metrics.TotalEdgesTraversed}");
    
    // Consider using parallel traversal or improving graph structure
}

if (metrics.CacheHitRatio < 0.5)
{
    Console.WriteLine("⚠️ Low cache hit rate");
    // Consider increasing cache size or query warming
}
```

### Example: Heuristic Validation

```csharp
var positions = new Dictionary<long, (int X, int Y)> { /* ... */ };
var context = new HeuristicContext { ["positions"] = positions };
var heuristic = BuiltInHeuristics.ManhattanDistance();

GraphMetricsCollector.Global.Enable();

var pathfinder = new CustomAStarPathfinder(heuristic);
var result = pathfinder.FindPath(table, 1, 100, "next", 20, context);

var metrics = GraphMetricsCollector.Global.GetSnapshot();

double admissibilityRatio = metrics.AdmissibleEstimates / (double)metrics.HeuristicCalls;
Console.WriteLine($"Heuristic admissibility: {admissibilityRatio:P2}");

if (admissibilityRatio < 0.9)
{
    Console.WriteLine("⚠️ Heuristic over-estimates significantly");
}
```

---

## See Also

- [GraphRAG README](./README.md) - Feature overview
- [Phase 6.2 Custom Heuristics](./CUSTOM_HEURISTICS_GUIDE.md) - A* pathfinding with metrics
- [OpenTelemetry Documentation](https://opentelemetry.io/docs/) - Standards-based observability
- [.NET Diagnostics](https://docs.microsoft.com/en-us/dotnet/fundamentals/diagnostics/) - Advanced tracing

