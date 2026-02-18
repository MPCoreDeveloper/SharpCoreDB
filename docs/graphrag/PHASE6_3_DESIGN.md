# Phase 6.3: Observability & Metrics — Design Document

**Status:** 🚧 In Progress  
**Target Release:** Phase 6.3  
**Created:** 2025-02-16  
**Last Updated:** 2025-02-16

---

## Overview

Phase 6.3 adds comprehensive observability and metrics to SharpCoreDB's GraphRAG capabilities, enabling:
- **Performance monitoring** - Track traversal times, cache efficiency, parallel speedup
- **Diagnostics** - Identify bottlenecks and optimization opportunities
- **Cost validation** - Verify optimizer predictions vs actual execution
- **Distributed tracing** - OpenTelemetry integration for microservices
- **Production insights** - Real-time metrics for operational monitoring

---

## Goals

### Primary Objectives
1. **Non-intrusive metrics** - Zero allocation in hot paths, minimal overhead (<1%)
2. **Comprehensive coverage** - All graph operations instrumented
3. **Standards-based** - OpenTelemetry-compatible for ecosystem integration
4. **Developer-friendly** - Simple APIs, automatic collection with LINQ
5. **Production-ready** - Thread-safe, high-performance, opt-in

### Success Criteria
- ✅ <1% performance overhead when metrics enabled
- ✅ <0.1% overhead when metrics disabled
- ✅ Thread-safe concurrent metric collection
- ✅ OpenTelemetry export for standard observability stacks
- ✅ 90%+ test coverage for metrics infrastructure

---

## Metrics Taxonomy

### 1. Traversal Metrics

#### Basic Counters
```csharp
public sealed class TraversalMetrics
{
    public long NodesVisited { get; init; }           // Total nodes explored
    public long EdgesTraversed { get; init; }         // Total edges followed
    public long MaxDepthReached { get; init; }        // Deepest level explored
    public long ResultCount { get; init; }            // Nodes in result set
    public TimeSpan ExecutionTime { get; init; }      // Wall-clock time
    public GraphTraversalStrategy Strategy { get; init; } // BFS/DFS/etc
}
```

#### Strategy-Specific Metrics
```csharp
// Bidirectional search
public long ForwardNodesExplored { get; init; }
public long BackwardNodesExplored { get; init; }
public long MeetingDepth { get; init; }

// Dijkstra/A*
public long PriorityQueuePeeks { get; init; }
public double AverageEdgeWeight { get; init; }
public double TotalPathCost { get; init; }
```

---

### 2. Cache Metrics

```csharp
public sealed class CacheMetrics
{
    // Hit/Miss
    public long Hits { get; init; }
    public long Misses { get; init; }
    public double HitRate => Hits / (double)(Hits + Misses);
    
    // Performance
    public TimeSpan AverageLookupTime { get; init; }
    public TimeSpan CacheConstructionTime { get; init; }
    
    // Capacity
    public int CurrentSize { get; init; }
    public int MaxSize { get; init; }
    public long Evictions { get; init; }
    
    // Memory
    public long EstimatedMemoryBytes { get; init; }
}
```

---

### 3. Parallel Execution Metrics

```csharp
public sealed class ParallelTraversalMetrics
{
    // Threading
    public int DegreeOfParallelism { get; init; }
    public int ActiveThreads { get; init; }
    public TimeSpan TotalThreadTime { get; init; }
    
    // Efficiency
    public double ParallelSpeedup { get; init; }      // Sequential time / Parallel time
    public double ParallelEfficiency { get; init; }   // Speedup / DOP
    
    // Work Distribution
    public long[] NodesPerThread { get; init; }
    public long WorkStealingOps { get; init; }
    public long IdleTimeMs { get; init; }
}
```

---

### 4. Optimizer Metrics

```csharp
public sealed class OptimizerMetrics
{
    // Cost Estimation
    public double EstimatedCostMs { get; init; }
    public double ActualCostMs { get; init; }
    public double PredictionError { get; init; }      // |Estimated - Actual| / Actual
    
    // Cardinality
    public long EstimatedCardinality { get; init; }
    public long ActualCardinality { get; init; }
    
    // Strategy Selection
    public GraphTraversalStrategy RecommendedStrategy { get; init; }
    public GraphTraversalStrategy ActualStrategy { get; init; }
    public bool StrategyOverridden { get; init; }
}
```

---

### 5. Heuristic Metrics (A*)

```csharp
public sealed class HeuristicMetrics
{
    // Effectiveness
    public long NodesExplored { get; init; }
    public long OptimalPathLength { get; init; }
    public double HeuristicEfficiency { get; init; }  // Optimal / Explored
    
    // Admissibility
    public long AdmissibleEstimates { get; init; }    // h(n) <= actual cost
    public long OverEstimates { get; init; }
    
    // Performance
    public TimeSpan HeuristicEvaluationTime { get; init; }
    public long HeuristicCalls { get; init; }
}
```

---

## Architecture

### Core Components

```
┌─────────────────────────────────────────────────────┐
│         GraphMetricsCollector (singleton)           │
│  - Thread-safe counters (Lock + Interlocked)       │
│  - Snapshot generation                              │
│  - Reset/Clear operations                           │
└─────────────────────────────────────────────────────┘
                        │
        ┌───────────────┼───────────────┐
        │               │               │
┌───────▼──────┐ ┌─────▼──────┐ ┌─────▼──────┐
│ Traversal    │ │ Cache      │ │ Parallel   │
│ Metrics      │ │ Metrics    │ │ Metrics    │
└──────────────┘ └────────────┘ └────────────┘
        │               │               │
        └───────────────┼───────────────┘
                        │
        ┌───────────────▼───────────────┐
        │  OpenTelemetry Integration    │
        │  - ActivitySource (tracing)   │
        │  - Meter (metrics)            │
        │  - Baggage (context)          │
        └───────────────────────────────┘
```

### Collection Strategy

#### 1. Opt-In via Options
```csharp
var options = new GraphSearchOptions
{
    EnableMetrics = true,  // Default: false (zero overhead)
    MetricsCollector = myCollector
};

var result = engine.Traverse(table, startId, "next", 5, options);
var metrics = result.Metrics; // TraversalMetrics
```

#### 2. LINQ Extension (Auto-Collection)
```csharp
var results = await db.People
    .Traverse(1, "managerId", 3)
    .WithMetrics(out var metricsTask)  // Captures metrics automatically
    .ToListAsync();

var metrics = await metricsTask;
Console.WriteLine($"Nodes visited: {metrics.NodesVisited}");
```

#### 3. Global Collector (Production)
```csharp
// Startup configuration
GraphMetricsCollector.Global.Enable();

// Metrics accumulate globally
var engine = new GraphTraversalEngine();
engine.Traverse(table, 1, "next", 5);  // Metrics auto-recorded

// Export to monitoring system
var snapshot = GraphMetricsCollector.Global.GetSnapshot();
await exporter.ExportAsync(snapshot);
```

---

## OpenTelemetry Integration

### Activity Source (Distributed Tracing)
```csharp
private static readonly ActivitySource ActivitySource = 
    new("SharpCoreDB.Graph", "6.3.0");

public GraphTraversalResult Traverse(...)
{
    using var activity = ActivitySource.StartActivity("GraphTraversal.BFS");
    activity?.SetTag("graph.startNodeId", startNodeId);
    activity?.SetTag("graph.maxDepth", maxDepth);
    
    var result = TraverseInternal(...);
    
    activity?.SetTag("graph.nodesVisited", result.Metrics.NodesVisited);
    activity?.SetTag("graph.resultCount", result.Metrics.ResultCount);
    
    return result;
}
```

### Meter (Metrics Export)
```csharp
private static readonly Meter Meter = new("SharpCoreDB.Graph", "6.3.0");

// Counters
private static readonly Counter<long> NodesVisitedCounter = 
    Meter.CreateCounter<long>("graph.nodes_visited", "nodes", "Total nodes explored");

// Histograms
private static readonly Histogram<double> TraversalDurationHistogram = 
    Meter.CreateHistogram<double>("graph.traversal_duration", "ms", "Traversal execution time");

// ObservableGauge (current cache size)
private static readonly ObservableGauge<int> CacheSizeGauge = 
    Meter.CreateObservableGauge("graph.cache_size", () => TraversalPlanCache.Instance.Count);
```

### Baggage (Context Propagation)
```csharp
// Propagate graph context across service boundaries
Baggage.SetBaggage("graph.queryId", queryId);
Baggage.SetBaggage("graph.userId", userId);

// Retrieved in downstream services
var queryId = Baggage.GetBaggage("graph.queryId");
```

---

## Implementation Plan

### Phase 6.3.1: Core Infrastructure (Week 1)
- [x] Design document (this file)
- [ ] `GraphMetricsCollector.cs` - Thread-safe metrics aggregation
- [ ] `MetricSnapshot.cs` - Immutable snapshot types
- [ ] `GraphMetricsOptions.cs` - Configuration
- [ ] Unit tests for collectors

### Phase 6.3.2: Component Integration (Week 1-2)
- [ ] `GraphTraversalEngine` metrics injection
- [ ] `TraversalPlanCache` metrics
- [ ] `ParallelGraphTraversalEngine` metrics
- [ ] `CustomAStarPathfinder` metrics
- [ ] Integration tests

### Phase 6.3.3: OpenTelemetry (Week 2)
- [ ] `GraphActivitySource.cs` - Tracing support
- [ ] `GraphMeter.cs` - Metrics export
- [ ] OpenTelemetry integration tests
- [ ] Jaeger/Prometheus examples

### Phase 6.3.4: EF Core Extensions (Week 2)
- [ ] `MetricsQueryableExtensions.cs` - `.WithMetrics()` API
- [ ] EF Core integration tests
- [ ] LINQ query metrics examples

### Phase 6.3.5: Documentation (Week 3)
- [ ] Observability guide (user-facing)
- [ ] Metrics reference (all available metrics)
- [ ] OpenTelemetry integration guide
- [ ] Production deployment best practices

---

## Performance Considerations

### Zero-Allocation Design
```csharp
// ❌ DON'T - allocates on every call
public void RecordTraversal(TraversalMetrics metrics)
{
    _metrics.Add(metrics); // List<T> allocation
}

// ✅ DO - update in-place with Interlocked
public void RecordNodesVisited(long count)
{
    Interlocked.Add(ref _nodesVisited, count);
}
```

### Lock Granularity
```csharp
// Use C# 14 Lock class for snapshot generation only
private readonly Lock _snapshotLock = new();

public MetricSnapshot GetSnapshot()
{
    lock (_snapshotLock)
    {
        // Read all counters atomically
        return new MetricSnapshot
        {
            NodesVisited = Interlocked.Read(ref _nodesVisited),
            EdgesTraversed = Interlocked.Read(ref _edgesTraversed),
            // ... snapshot all counters
        };
    }
}
```

### Conditional Compilation
```csharp
[MethodImpl(MethodImplOptions.AggressiveInlining)]
private void RecordMetric(long value)
{
    if (_options.EnableMetrics) // JIT optimizes away when false
    {
        Interlocked.Add(ref _counter, value);
    }
}
```

---

## Testing Strategy

### 1. Unit Tests
- Metric collection accuracy
- Thread-safety (concurrent updates)
- Snapshot consistency
- Reset/Clear operations

### 2. Integration Tests
- End-to-end metric flow
- OpenTelemetry export validation
- LINQ extension correctness
- Global collector isolation

### 3. Performance Tests
- Overhead measurement (<1% target)
- Disabled metrics overhead (<0.1%)
- Concurrent collection scalability

### 4. Benchmarks
```csharp
[Benchmark(Baseline = true)]
public void TraversalWithoutMetrics() { }

[Benchmark]
public void TraversalWithMetrics() { }

// Expected: <1% regression
```

---

## Security & Privacy

### PII Handling
- ❌ **Never** log node IDs (could be user IDs)
- ❌ **Never** log edge data (could contain sensitive relationships)
- ✅ **Only** log aggregate statistics (counts, times, averages)

### Configuration
```csharp
var options = new GraphMetricsOptions
{
    EnableMetrics = true,
    SanitizeNodeIds = true,  // Hash IDs before export
    MaxBaggageSize = 1024,   // Prevent baggage bloat attacks
    AllowedBaggageKeys = ["graph.queryId"] // Whitelist
};
```

---

## Migration Path

### Existing Code (No Changes)
```csharp
// Works exactly as before - zero overhead
var result = engine.Traverse(table, 1, "next", 5);
```

### Opt-In Metrics
```csharp
// Enable for specific queries
var options = new GraphSearchOptions { EnableMetrics = true };
var result = engine.Traverse(table, 1, "next", 5, options);
Console.WriteLine($"Visited: {result.Metrics.NodesVisited}");
```

### Global Production Monitoring
```csharp
// Startup.cs
GraphMetricsCollector.Global.Enable();
services.AddOpenTelemetry()
    .WithMetrics(m => m.AddMeter("SharpCoreDB.Graph"));

// Automatic export to Prometheus/Datadog/etc
```

---

## Future Enhancements (Post-6.3)

1. **Query explain plans** - Visual query execution breakdown
2. **Adaptive optimization** - Use metrics to improve cost model
3. **Anomaly detection** - Detect unusual graph patterns
4. **Real-time dashboards** - Grafana/Kibana integration
5. **Machine learning** - Predict optimal strategies from historical metrics

---

## References

- **OpenTelemetry .NET:** https://opentelemetry.io/docs/languages/net/
- **System.Diagnostics.Metrics:** https://learn.microsoft.com/en-us/dotnet/core/diagnostics/metrics
- **C# 14 Lock Class:** https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/statements/lock
- **Interlocked Class:** https://learn.microsoft.com/en-us/dotnet/api/system.threading.interlocked

---

## Deliverables

### New Files
- `src/SharpCoreDB.Graph/Metrics/GraphMetricsCollector.cs`
- `src/SharpCoreDB.Graph/Metrics/TraversalMetrics.cs`
- `src/SharpCoreDB.Graph/Metrics/CacheMetrics.cs`
- `src/SharpCoreDB.Graph/Metrics/ParallelTraversalMetrics.cs`
- `src/SharpCoreDB.Graph/Metrics/OptimizerMetrics.cs`
- `src/SharpCoreDB.Graph/Metrics/HeuristicMetrics.cs`
- `src/SharpCoreDB.Graph/Metrics/MetricSnapshot.cs`
- `src/SharpCoreDB.Graph/Metrics/GraphMetricsOptions.cs`
- `src/SharpCoreDB.Graph/OpenTelemetry/GraphActivitySource.cs`
- `src/SharpCoreDB.Graph/OpenTelemetry/GraphMeter.cs`
- `src/SharpCoreDB.EntityFrameworkCore/Query/MetricsQueryableExtensions.cs`
- `tests/SharpCoreDB.Tests/Graph/Metrics/*.cs` (20+ test files)
- `docs/graphrag/OBSERVABILITY_GUIDE.md`
- `docs/graphrag/METRICS_REFERENCE.md`

### Modified Files
- `src/SharpCoreDB.Graph/GraphTraversalEngine.cs` - Add metrics injection points
- `src/SharpCoreDB.Graph/GraphSearchOptions.cs` - Add EnableMetrics property
- `src/SharpCoreDB.Graph/GraphTraversalResult.cs` - Add Metrics property
- `docs/graphrag/README.md` - Update Phase 6.3 status

---

**Status:** Design approved, ready for implementation  
**Estimated Effort:** 3 weeks  
**Dependencies:** None (builds on Phase 6.2)
