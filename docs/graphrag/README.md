# GraphRAG — Lightweight Graph Capabilities for SharpCoreDB

**Status:** ✅ **Phase 3 Complete** (Phase 1 & 2 complete, Phase 4 prototype)  
**Target Release:** Roadmap item (schedule TBD)  
**Last Updated:** 2025-02-15

---

## Overview

GraphRAG Phase 3 adds traversal optimization and hybrid graph+vector query support to SharpCoreDB. The implementation includes:
- **TraversalStrategyOptimizer** - automatic strategy selection based on cardinality estimation
- **Enhanced HybridGraphVectorOptimizer** - cost-based execution ordering for hybrid queries
- **LINQ API Extensions** - WithVectorSimilarity, OrderByVectorDistance, WithHybridScoring

### Current Implementation Status

✅ **Phase 1:** ROWREF data type + storage serialization (complete)  
✅ **Phase 2:** Graph traversal (BFS/DFS/Bidirectional/Dijkstra) + SQL + EF Core (complete)  
✅ **Phase 3:** Traversal optimizer + Hybrid graph+vector queries (complete)  
🟡 **Phase 4:** Advanced features (planned)

---

## Phase 3 Features

### ✅ Traversal Strategy Optimizer
- Automatic strategy selection (BFS, DFS, Bidirectional, Dijkstra)
- Cost estimation based on graph statistics
- Cardinality prediction with degree-based heuristics
- Supports custom graph statistics for accurate estimates

### ✅ Hybrid Graph+Vector Optimization
- Cost-based predicate ordering (graph vs. vector filter first)
- Cardinality estimation for both operations
- Detailed execution hints with rationale
- Table statistics integration for better cost modeling

### ✅ LINQ Extensions (Hybrid Queries)
- `.WithVectorSimilarity()` - Filter by vector distance threshold
- `.OrderByVectorDistance()` - Rank by semantic relevance
- `.WithHybridScoring()` - Combine graph + vector scores
- Support for multiple distance metrics (cosine, euclidean, manhattan, inner product)

---

## API Reference

### Traversal Strategy Optimizer

```csharp
var optimizer = new TraversalStrategyOptimizer(table, "parentId", maxDepth: 3);
var recommendation = optimizer.RecommendStrategy();

Console.WriteLine($"Recommended: {recommendation.RecommendedStrategy}");
Console.WriteLine($"Estimated cost: {recommendation.Cost.TotalCost}ms");
Console.WriteLine($"Estimated cardinality: {recommendation.Cost.EstimatedCardinality}");
```

### Hybrid Graph+Vector Optimization

```csharp
var optimizer = new HybridGraphVectorOptimizer();
var tableStats = new TableStatistics 
{ 
    RowCount = 10000,
    EstimatedAverageDegree = 1.5,
    HasVectorIndex = true
};

var hint = optimizer.OptimizeQuery(selectNode, tableStats);

if (hint.HasGraphTraversal && hint.HasVectorSearch)
{
    Console.WriteLine($"Recommended order: {hint.RecommendedOrder}");
    Console.WriteLine($"Graph cost: {hint.GraphTraversalCost?.EstimatedCostMs}ms");
    Console.WriteLine($"Vector cost: {hint.VectorSearchCost?.EstimatedCostMs}ms");
}
```

### Hybrid LINQ Queries

```csharp
// Find documents related to doc 1 AND semantically similar to query embedding
var results = await db.Documents
    .Traverse(1, "relatedId", 3, GraphTraversalStrategy.Bfs)
    .WithVectorSimilarity(queryEmbedding, threshold: 0.8)
    .OrderByVectorDistance(queryEmbedding)
    .Take(10)
    .ToListAsync();

// Score by combining graph distance and vector similarity
var scored = results
    .AsEnumerable()
    .WithHybridScoring(graphWeight: 0.3, vectorWeight: 0.7)
    .OrderByDescending(x => x.HybridScore)
    .ToList();
```

---

## Phase 3 Deliverables

### New Files
- `src/SharpCoreDB.Graph/TraversalStrategyOptimizer.cs` - Automatic strategy selection
- `tests/SharpCoreDB.Tests/Graph/TraversalStrategyOptimizerTests.cs` - Optimizer tests
- `tests/SharpCoreDB.Tests/Graph/HybridGraphVectorOptimizerTests.cs` - Hybrid optimizer tests
- `tests/SharpCoreDB.EntityFrameworkCore.Tests/Query/HybridGraphVectorQueryTests.cs` - LINQ API tests

### Modified Files
- `src/SharpCoreDB.Graph/HybridGraphVectorOptimizer.cs` - Added cost-based logic (was prototype)
- `src/SharpCoreDB.EntityFrameworkCore/Query/GraphTraversalQueryableExtensions.cs` - Added hybrid query methods

---

## What's Implemented

✅ TraversalStrategyOptimizer with cost modeling  
✅ Graph statistics collection (cardinality estimation)  
✅ HybridGraphVectorOptimizer with cost-based ordering  
✅ LINQ API for hybrid queries  
✅ Vector distance metrics (cosine, euclidean, manhattan, inner product)  
✅ Hybrid scoring for composite ranking  
✅ Comprehensive tests (60+ test cases)

---

## Design

### Cost Model
```
GraphTraversalCost = EstimatedNodes × CostPerRowRef
VectorSearchCost = TableRows × CostPerVectorDistance

where:
  CostPerRowRef ≈ 0.001ms (1μs per ROWREF lookup)
  CostPerVectorDistance ≈ 0.01ms (10μs per distance calculation)
```

### Strategy Selection
The optimizer evaluates all 4 strategies and selects the one with the lowest estimated cost based on:
- Graph degree (fan-out)
- Traversal depth
- Estimated result cardinality
- Memory overhead

### Hybrid Query Ordering
For queries combining graph and vector operations:
- Apply the more selective filter first
- "More selective" = lower estimated cardinality
- Recommendation provided with detailed cost breakdown
