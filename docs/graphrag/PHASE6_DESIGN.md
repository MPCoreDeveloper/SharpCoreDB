# Phase 6: Advanced Graph Optimizations - Design Document

**Version:** 1.0  
**Date:** 2025-02-16  
**Status:** 🚧 In Progress  
**Target Completion:** 2025-02-28

---

## 📋 Overview

Phase 6 introduces advanced optimization features to SharpCoreDB's GraphRAG implementation, focusing on:

1. **Parallel Traversal** - Multi-core graph exploration
2. **Custom Heuristics** - User-defined A* guidance functions
3. **Graph Statistics** - Automatic metadata collection
4. **Smart Cache Invalidation** - Incremental updates without full cache clear
5. **Performance Monitoring** - Built-in metrics and observability

---

## 🎯 Goals

### Primary Objectives
- ✅ Enable multi-threaded graph traversal for large graphs
- ✅ Support custom A* heuristics for domain-specific optimization
- ✅ Provide automatic graph statistics gathering
- ✅ Implement intelligent cache invalidation
- ✅ Add comprehensive performance monitoring

### Success Criteria
- **Parallel Speedup:** 2-4x faster on 4+ core systems
- **Custom Heuristics:** 10-50% better pathfinding vs generic heuristics
- **Smart Invalidation:** <10% of cache cleared on updates (vs 100% currently)
- **Zero Regressions:** All 97 existing tests must pass
- **Production Ready:** Complete documentation and examples

---

## 🏗️ Architecture

### 1. Parallel Traversal

**Design:** Work-stealing parallel BFS using `System.Threading.Channels`

```csharp
public sealed class ParallelGraphTraversalEngine
{
    private readonly int _degreeOfParallelism;
    private readonly Channel<TraversalWorkItem> _workQueue;

    public async Task<IReadOnlyCollection<long>> TraverseBfsParallelAsync(
        ITable table,
        long startNodeId,
        string relationshipColumn,
        int maxDepth,
        CancellationToken cancellationToken = default)
    {
        // 1. Create bounded channel for work items
        // 2. Spawn worker tasks (degree of parallelism)
        // 3. Each worker processes nodes from queue
        // 4. Workers add discovered neighbors to queue
        // 5. Coordinate completion via ConcurrentHashSet
        // 6. Return all discovered nodes
    }
}
```

**Key Features:**
- Thread-safe visited set using `ConcurrentDictionary<long, byte>`
- Work-stealing via `Channel<T>` for load balancing
- Configurable degree of parallelism
- Graceful degradation to sequential for small graphs

**Performance Target:** 2-4x speedup on 8-core systems for graphs with >10K nodes

---

### 2. Custom Heuristics

**Design:** Delegate-based A* heuristic system

```csharp
public delegate double CustomHeuristicFunction(
    long currentNode,
    long goalNode,
    int currentDepth,
    int maxDepth,
    IReadOnlyDictionary<string, object> context);

public sealed class CustomAStarPathfinder
{
    private readonly CustomHeuristicFunction _heuristic;

    public CustomAStarPathfinder(CustomHeuristicFunction heuristic)
    {
        _heuristic = heuristic ?? throw new ArgumentNullException(nameof(heuristic));
    }

    public AStarPathResult FindPath(/* ... */)
    {
        // Use custom heuristic instead of built-in
        var h = _heuristic(currentNode, goalNode, depth, maxDepth, context);
        // ...
    }
}
```

**Built-In Heuristics (for reference):**
- **Depth Heuristic:** `h(n) = maxDepth - currentDepth`
- **Uniform Heuristic:** `h(n) = 0` (Dijkstra)
- **Manhattan Distance:** `h(n) = |x1 - x2| + |y1 - y2|` (for spatial graphs)
- **Euclidean Distance:** `h(n) = sqrt((x1-x2)² + (y1-y2)²)`

**Use Cases:**
1. **Spatial Graphs:** Use geographic distance as heuristic
2. **Weighted Graphs:** Use edge weight estimates
3. **Domain-Specific:** Use business logic (e.g., priority, cost)

---

### 3. Graph Statistics Collector

**Design:** Automatic metadata gathering for optimization

```csharp
public sealed class GraphStatisticsCollector
{
    public async Task<GraphStatistics> CollectAsync(
        ITable table,
        string relationshipColumn,
        CancellationToken cancellationToken = default)
    {
        // 1. Count total nodes (table row count)
        // 2. Sample relationships to estimate edges
        // 3. Calculate average degree
        // 4. Detect graph density
        // 5. Identify highly connected nodes
        // 6. Return comprehensive statistics
    }
}

public sealed class GraphStatistics
{
    public long TotalNodes { get; init; }
    public long TotalEdges { get; init; }
    public double AverageDegree { get; init; }
    public double Density { get; init; } // edges / (nodes * (nodes-1))
    public int MaxDegree { get; init; }
    public IReadOnlyList<long> HighlyConnectedNodes { get; init; } // Top 1%
    public DateTime CollectedAt { get; init; }
}
```

**Usage:**
```csharp
var collector = new GraphStatisticsCollector();
var stats = await collector.CollectAsync(myTable, "References");

// Use for auto-strategy selection
var optimizer = new TraversalStrategyOptimizer(stats);
var strategy = optimizer.RecommendStrategy(maxDepth: 5);
```

---

### 4. Smart Cache Invalidation

**Design:** Track dependencies and invalidate only affected entries

```csharp
public sealed class IncrementalCacheInvalidator
{
    private readonly TraversalPlanCache _cache;
    private readonly ConcurrentDictionary<string, HashSet<TraversalPlanCacheKey>> _tableIndex;

    public void InvalidateTable(string tableName)
    {
        // Invalidate only plans for this table
        if (_tableIndex.TryGetValue(tableName, out var keys))
        {
            foreach (var key in keys)
            {
                _cache.Remove(key);
            }
        }
    }

    public void InvalidateColumn(string tableName, string columnName)
    {
        // Invalidate only plans using this relationship column
        var keysToRemove = _tableIndex[tableName]
            .Where(k => k.RelationshipColumn == columnName)
            .ToList();

        foreach (var key in keysToRemove)
        {
            _cache.Remove(key);
        }
    }

    public void InvalidateDepth(string tableName, int minDepth)
    {
        // Invalidate plans with depth >= minDepth
        // (for graphs where deep queries need refresh)
    }
}
```

**Invalidation Strategies:**
1. **Table-Level:** Clear all plans for a table (after bulk insert)
2. **Column-Level:** Clear plans for specific relationship (after FK changes)
3. **Depth-Level:** Clear deep traversals (after graph restructuring)
4. **Smart Sampling:** Invalidate based on change percentage

---

### 5. Performance Monitoring

**Design:** Built-in metrics collection

```csharp
public sealed class GraphMetrics
{
    private long _totalTraversals;
    private long _totalNodesVisited;
    private double _averageTraversalTimeMs;
    private readonly ConcurrentDictionary<GraphTraversalStrategy, long> _strategyUsage;

    public void RecordTraversal(
        GraphTraversalStrategy strategy,
        int nodesVisited,
        double elapsedMs)
    {
        Interlocked.Increment(ref _totalTraversals);
        Interlocked.Add(ref _totalNodesVisited, nodesVisited);
        
        // Update running average
        var oldAvg = _averageTraversalTimeMs;
        var newAvg = (oldAvg * (_totalTraversals - 1) + elapsedMs) / _totalTraversals;
        _averageTraversalTimeMs = newAvg;

        _strategyUsage.AddOrUpdate(strategy, 1, (_, count) => count + 1);
    }

    public MetricsSnapshot GetSnapshot()
    {
        return new MetricsSnapshot
        {
            TotalTraversals = _totalTraversals,
            TotalNodesVisited = _totalNodesVisited,
            AverageTraversalTimeMs = _averageTraversalTimeMs,
            StrategyUsage = _strategyUsage.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
            CapturedAt = DateTime.Now
        };
    }
}
```

**Metrics Tracked:**
- Total traversals executed
- Total nodes visited
- Average traversal time
- Strategy usage distribution
- Cache hit/miss ratio
- Parallel vs sequential execution ratio

---

## 📊 Implementation Plan

### Week 1: Parallel Traversal (Feb 17-21)
**Goal:** Multi-threaded BFS with 2-4x speedup

**Tasks:**
1. Implement `ParallelGraphTraversalEngine`
2. Add work-stealing via `Channel<T>`
3. Thread-safe visited tracking
4. Configurable parallelism
5. Benchmark vs sequential BFS
6. Add 8 comprehensive tests

**Deliverables:**
- `ParallelGraphTraversalEngine.cs`
- `ParallelTraversalTests.cs` (8 tests)
- `PARALLEL_TRAVERSAL_GUIDE.md`

---

### Week 2: Custom Heuristics (Feb 22-24)
**Goal:** User-defined A* heuristics

**Tasks:**
1. Design `CustomHeuristicFunction` delegate
2. Implement `CustomAStarPathfinder`
3. Add spatial heuristics (Manhattan, Euclidean)
4. Add weighted graph heuristics
5. Add 6 comprehensive tests

**Deliverables:**
- `CustomHeuristic.cs`
- `CustomAStarPathfinder.cs`
- `SpatialHeuristics.cs`
- `CustomHeuristicTests.cs` (6 tests)
- `CUSTOM_HEURISTICS_GUIDE.md`

---

### Week 3: Statistics & Monitoring (Feb 25-28)
**Goal:** Observability and smart optimization

**Tasks:**
1. Implement `GraphStatisticsCollector`
2. Implement `GraphMetrics`
3. Implement `IncrementalCacheInvalidator`
4. Integrate metrics with `GraphTraversalEngine`
5. Add 10 comprehensive tests

**Deliverables:**
- `GraphStatisticsCollector.cs`
- `GraphMetrics.cs`
- `IncrementalCacheInvalidator.cs`
- `GraphStatisticsTests.cs` (10 tests)
- `GRAPH_METRICS_GUIDE.md`

---

## 🎯 Success Metrics

### Performance Targets

| Feature | Baseline | Target | Measurement |
|---------|----------|--------|-------------|
| Parallel BFS (8 cores) | 1x | **3x faster** | 10K node graph |
| Custom Heuristics | Generic | **20% fewer nodes** | A* pathfinding |
| Smart Invalidation | 100% cache clear | **<10% cleared** | Table update |
| Metrics Overhead | N/A | **<5% slowdown** | With monitoring |

### Quality Targets

| Metric | Target |
|--------|--------|
| Test Coverage | 100% (all new code) |
| Documentation | Complete API reference |
| Code Compliance | 100% C# 14 |
| Zero Regressions | All 97 existing tests pass |

---

## 🔧 API Examples

### Example 1: Parallel Traversal

```csharp
var parallelEngine = new ParallelGraphTraversalEngine(
    degreeOfParallelism: Environment.ProcessorCount);

var result = await parallelEngine.TraverseBfsParallelAsync(
    table: myTable,
    startNodeId: 1,
    relationshipColumn: "References",
    maxDepth: 5);

// 3x faster on 8-core system! ⚡
```

### Example 2: Custom Heuristic (Spatial)

```csharp
// Define spatial heuristic
CustomHeuristicFunction spatialHeuristic = (current, goal, depth, maxDepth, context) =>
{
    var currentPos = (Point)context["positions"][current];
    var goalPos = (Point)context["positions"][goal];
    return Math.Abs(currentPos.X - goalPos.X) + Math.Abs(currentPos.Y - goalPos.Y);
};

var pathfinder = new CustomAStarPathfinder(spatialHeuristic);
var path = pathfinder.FindPath(start, goal, GetNeighbors, maxDepth: 100, context);

// 50% fewer nodes explored! ⚡
```

### Example 3: Graph Statistics

```csharp
var collector = new GraphStatisticsCollector();
var stats = await collector.CollectAsync(myTable, "References");

Console.WriteLine($"""
    Graph Statistics:
    - Nodes: {stats.TotalNodes}
    - Edges: {stats.TotalEdges}
    - Avg Degree: {stats.AverageDegree:F2}
    - Density: {stats.Density:P2}
    """);

// Use for automatic strategy selection
var optimizer = new TraversalStrategyOptimizer(stats);
var recommendedStrategy = optimizer.RecommendStrategy(maxDepth: 5);
```

### Example 4: Smart Cache Invalidation

```csharp
var invalidator = new IncrementalCacheInvalidator(cache);

// After updating references in documents table
await myTable.UpdateAsync(/* ... */);

// Only invalidate affected plans (not entire cache!)
invalidator.InvalidateColumn("documents", "References");

Console.WriteLine($"Invalidated {invalidator.LastInvalidationCount} entries");
// vs full cache clear (1000+ entries)
```

### Example 5: Performance Monitoring

```csharp
var metrics = new GraphMetrics();
var engine = new GraphTraversalEngine(options, metrics);

// Use normally - metrics collected automatically
var result = engine.Traverse(table, 1, "next", 5, GraphTraversalStrategy.Bfs);

// Check metrics
var snapshot = metrics.GetSnapshot();
Console.WriteLine($"""
    Performance Metrics:
    - Total Traversals: {snapshot.TotalTraversals}
    - Avg Time: {snapshot.AverageTraversalTimeMs:F2}ms
    - Most Used Strategy: {snapshot.StrategyUsage.OrderByDescending(x => x.Value).First().Key}
    """);
```

---

## 🚧 Implementation Phases

### Phase 6.1: Parallel Traversal ✅ In Progress
- Week 1 (Feb 17-21)
- Focus: Multi-threaded BFS

### Phase 6.2: Custom Heuristics
- Week 2 (Feb 22-24)
- Focus: User-defined A* guidance

### Phase 6.3: Observability
- Week 3 (Feb 25-28)
- Focus: Statistics, metrics, monitoring

---

## 🎓 Design Decisions

### 1. Why Parallel BFS, Not DFS?

**Decision:** Implement parallel BFS first

**Rationale:**
- BFS is naturally parallelizable (level-by-level)
- DFS is inherently sequential (backtracking)
- BFS is more commonly used for graph queries
- BFS benefits more from parallelization

### 2. Why Delegate for Custom Heuristics?

**Decision:** Use `delegate` instead of interface

**Rationale:**
- Simpler for users (inline lambdas)
- Better performance (no virtual dispatch)
- More flexible (closures for context)
- Consistent with C# conventions (`Func<T>`, `Action<T>`)

### 3. Why Sampling for Statistics?

**Decision:** Sample relationships instead of full scan

**Rationale:**
- Full scan is expensive (O(n))
- Sampling gives 95% accurate estimate
- Configurable sample size (default 10%)
- Better for large graphs (1M+ nodes)

### 4. Why Index-Based Invalidation?

**Decision:** Build secondary index for cache invalidation

**Rationale:**
- Fast lookup by table/column
- Small memory overhead (~1% of cache)
- Enables surgical invalidation
- Critical for production systems

---

## 🔒 Risks & Mitigation

### Risk 1: Parallel Overhead

**Risk:** Parallel BFS slower for small graphs

**Mitigation:**
- Auto-detect graph size
- Use sequential for <1000 nodes
- Benchmark threshold empirically

### Risk 2: Heuristic Complexity

**Risk:** Users write inefficient heuristics

**Mitigation:**
- Provide built-in efficient heuristics
- Document performance guidelines
- Add timeout protection
- Warn on slow heuristics

### Risk 3: Statistics Staleness

**Risk:** Statistics become outdated

**Mitigation:**
- Add TTL to statistics (default 1 hour)
- Automatic refresh on large changes
- Manual refresh API
- Stale warning in logs

---

## 📝 Documentation Plan

### User Guides
1. **PARALLEL_TRAVERSAL_GUIDE.md** - How to use parallel BFS
2. **CUSTOM_HEURISTICS_GUIDE.md** - Writing custom heuristics
3. **GRAPH_METRICS_GUIDE.md** - Monitoring and observability

### API Reference
- XML comments on all public APIs
- Code examples for each feature
- Performance guidelines

### Migration Guide
- How to adopt parallel traversal
- How to add custom heuristics
- How to enable monitoring

---

## ✅ Acceptance Criteria

**Phase 6 is complete when:**

1. ✅ Parallel BFS achieves 2-4x speedup (8+ cores)
2. ✅ Custom heuristics reduce A* node exploration by 20%+
3. ✅ Smart invalidation clears <10% of cache on updates
4. ✅ All 24+ new tests passing (100%)
5. ✅ All 97 existing tests still passing (zero regressions)
6. ✅ Complete documentation with examples
7. ✅ Performance benchmarks validated
8. ✅ Production deployment guide complete

---

**Status:** 🚧 **Phase 6.1 In Progress**  
**Next Milestone:** Parallel BFS implementation (Feb 21)

---

**Version:** 1.0  
**Last Updated:** 2025-02-16  
**Author:** SharpCoreDB Team
