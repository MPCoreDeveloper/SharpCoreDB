# SharpCoreDB.Graph

**Status:** ✅ Phase 3 complete (Phase 4 prototype)  
**Target Framework:** .NET 10 / C# 14  
**Package:** `SharpCoreDB.Graph`  
**Test Status:** Tests available (run `dotnet test` locally)

---

## Overview

`SharpCoreDB.Graph` provides complete graph capabilities for SharpCoreDB:

- ✅ **Phase 1:** ROWREF index-free adjacency + serialization
- ✅ **Phase 2:** BFS/DFS/Bidirectional/Dijkstra traversal
- ✅ **Phase 3:** Traversal optimizer + hybrid graph+vector queries
- 🟡 **Phase 4:** Advanced optimization (prototype)

### Phase 3: What's New

**TraversalStrategyOptimizer** — Automatic strategy selection based on cost estimation
- Evaluates all 4 strategies (BFS, DFS, Bidirectional, Dijkstra)
- Provides cost breakdown and cardinality estimates
- Supports custom graph statistics for refined predictions

**Enhanced HybridGraphVectorOptimizer** — Cost-aware hybrid query optimization
- Detects graph + vector operations in WHERE clauses
- Estimates cost of each operation
- Recommends execution order (graph first or vector first)
- Provides detailed rationale for recommendations

**LINQ Extensions** — Hybrid query API
- `.WithVectorSimilarity()` - Filter by vector distance
- `.OrderByVectorDistance()` - Rank by semantic relevance
- `.WithHybridScoring()` - Combine graph + vector scores

---

## Key Features

- **Automatic Strategy Selection:** Choose optimal traversal based on graph topology
- **Cost Estimation:** Cardinality and execution cost prediction
- **Hybrid Queries:** Combine structural (graph) + semantic (vector) search
- **Vector Metrics:** Cosine, Euclidean, Manhattan, Inner Product
- **Zero Dependencies:** Pure managed C# 14, NativeAOT compatible
- **Comprehensive Tests:** 60+ test cases for optimizer and hybrid queries

---

## Quick Start

### Option 1: Raw SQL
```sql
SELECT GRAPH_TRAVERSE(1, 'nextId', 3, 0) -- BFS from node 1, 3 hops
```

### Option 2: EF Core LINQ (Recommended)
```csharp
var nodeIds = await context.Nodes
    .Traverse(1, "nextId", 3, GraphTraversalStrategy.Bfs)
    .ToListAsync();
```

### Option 3: Programmatic API
```csharp
var provider = new GraphTraversalProvider(table);
var result = await provider.TraverseAsync(
    startNodeId: 1,
    relationshipColumn: "nextId",
    maxDepth: 3,
    strategy: GraphTraversalStrategy.Bfs,
    cancellationToken: ct);
```

---

## Traversal Strategies

### BFS (Breadth-First) — Breadth emphasis
- Shortest paths guaranteed
- Level-based exploration
- Higher memory usage for wide graphs

### DFS (Depth-First) — Depth emphasis
- Memory-efficient stack-based
- Good for hierarchies
- Can be slow on wide graphs

### Bidirectional — Both directions
- Explores outgoing + incoming edges
- Finds all connected nodes
- Higher edge access cost

### Dijkstra — Weighted shortest paths
- Uses optional edge `weight` column
- Best for weighted graphs
- Priority queue overhead

---

## SQL Integration

### GRAPH_TRAVERSE Function
```sql
SELECT GRAPH_TRAVERSE(startNodeId, relationshipColumn, maxDepth, strategy)

-- Examples:
SELECT GRAPH_TRAVERSE(1, 'nextId', 3, 0)         -- BFS
SELECT GRAPH_TRAVERSE(5, 'parentId', 10, 1)      -- DFS
```

---

## EF Core Integration

### LINQ Extension Methods
- `.Traverse<T>()` - Graph traversal
- `.WhereIn<T>()` - Filter by results
- `.TraverseWhere<T>()` - Combined traversal + WHERE
- `.Distinct<T>()` - Remove duplicates
- `.Take<T>()` - Limit results
- `.WithVectorSimilarity()` - Filter by vector distance
- `.OrderByVectorDistance()` - Rank by semantic relevance
- `.WithHybridScoring()` - Combine graph + vector scores

### Usage
```csharp
var orders = await context.Orders
    .Where(o => context.Suppliers
        .Traverse(supplierId, "parentId", 3, GraphTraversalStrategy.Bfs)
        .Contains(o.SupplierId))
    .Where(o => o.Amount > 100)
    .ToListAsync();
```

---

## Project Status

### ✅ Complete
- Phase 1: ROWREF + serialization
- Phase 2: Traversal engine (all 4 strategies)
- Phase 3: Optimizer + hybrid queries

### 🟡 In Progress / Planned
- Phase 4: Multi-hop index optimization
- Advanced statistics collection
- Real-time graph analytics

---

## Testing

### Test Coverage
- `GraphTraversalEngineTests.cs` - Core engine tests
- `GraphFunctionProviderTests.cs` - SQL function tests
- `GraphTraversalIntegrationTests.cs` - Integration tests
- `HybridGraphVectorQueryTests.cs` - Vector + Graph tests
- `GraphTraversalEFCoreTests.cs` - EF Core integration
- `GraphTraversalQueryableExtensionsTests.cs` - Extension tests
- `TraversalStrategyOptimizerTests` - Strategy selection validation
- `HybridGraphVectorOptimizerTests` - Cost-based optimization tests

Run `dotnet test` to validate status in your environment.

---

## Documentation

- [LINQ API Guide](../../docs/graphrag/LINQ_API_GUIDE.md) - Complete API reference
- [EF Core Complete Guide](../../docs/graphrag/EF_CORE_COMPLETE_GUIDE.md) - Usage patterns
- [Integration Summary](../../docs/graphrag/EF_CORE_INTEGRATION_SUMMARY.md) - Architecture
- [Start Here](../../docs/graphrag/00_START_HERE.md) - Quick navigation
