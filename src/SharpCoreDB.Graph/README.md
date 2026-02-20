# SharpCoreDB.Graph

**Version:** 1.3.5 (Phase 6.2 Complete)  
**Status:** ✅ Production Ready

**Target Framework:** .NET 10 / C# 14  
**Package:** `SharpCoreDB.Graph` v1.3.5

---

## Overview

`SharpCoreDB.Graph` provides high-performance graph traversal and pathfinding algorithms for SharpCoreDB:

- ✅ **Phase 6.2**: A* Pathfinding with 30-50% performance improvement ✅ **NEW**
- ✅ **Phase 3**: BFS, DFS, Dijkstra, Bidirectional traversal
- ✅ **Hybrid Queries**: Combine graph + vector semantic search
- ✅ **Cost Estimation**: Automatic strategy selection
- ✅ **LINQ Extensions**: Fluent API for graph queries
- ✅ **Zero Dependencies**: Pure C# 14, NativeAOT compatible

### Performance Highlights (Phase 6.2)

| Operation | Performance | Improvement |
|-----------|-------------|-------------|
| **A* Pathfinding** | 30-50% faster | vs baseline algorithms |
| **Node Traversal (1M nodes)** | <100ms | BFS/DFS optimized |
| **Memory Usage** | Ultra-low | Streaming API |

---

## Quick Start

### Installation

```bash
dotnet add package SharpCoreDB.Graph --version 1.3.5
```

### Basic Usage

```csharp
using SharpCoreDB.Graph;

var services = new ServiceCollection();
services.AddSharpCoreDB().AddGraphSupport();
var database = services.BuildServiceProvider().GetRequiredService<IDatabase>();

// Create graph tables
await database.ExecuteAsync(@"
    CREATE TABLE nodes (
        id INT PRIMARY KEY,
        name TEXT,
        data TEXT
    )
");

await database.ExecuteAsync(@"
    CREATE TABLE edges (
        source_id INT,
        target_id INT,
        weight FLOAT,
        PRIMARY KEY (source_id, target_id)
    )
");

// Find shortest path using A* (Phase 6.2)
var path = await database.QueryAsync(@"
    SELECT GRAPH_ASTAR(1, 10, 'edges', 'weight') as node_id
");

foreach (var row in path)
{
    Console.WriteLine($"Node: {row["node_id"]}");
}
```

---

## Pathfinding Algorithms

### A* Pathfinding (Phase 6.2) - RECOMMENDED
**30-50% faster with custom heuristics**

```csharp
// Find shortest path from node 1 to node 100
var path = await database.QueryAsync(@"
    SELECT GRAPH_ASTAR(1, 100, 'edges', 'weight', 'euclidean_heuristic') as node_id
    FROM dual
    ORDER BY path_order
");

// A* with heuristic function (Euclidean distance)
var pathWithHeuristic = await database.QueryAsync(@"
    SELECT GRAPH_ASTAR_HEURISTIC(
        start_node => 1,
        end_node => 100,
        edges_table => 'edges',
        weight_column => 'weight',
        heuristic => 'custom_h(source, target)'
    ) as node_id
");
```

**Key Advantages:**
- Combines Dijkstra's optimality with BFS's speed
- Custom heuristics for domain-specific optimization
- Guaranteed shortest path (if heuristic is admissible)
- 30-50% faster than pure Dijkstra

### Dijkstra - Weighted Graphs
**Shortest path with edge weights**

```csharp
// Weighted shortest path
var path = await database.QueryAsync(@"
    SELECT GRAPH_TRAVERSE(
        start_node => 1,
        target_node => 100,
        edges_table => 'edges',
        weight_column => 'weight',
        strategy => 'dijkstra'
    ) as node_id
");
```

### Bidirectional Search
**Meet-in-the-middle for faster paths**

```csharp
var path = await database.QueryAsync(@"
    SELECT GRAPH_TRAVERSE(
        start_node => 1,
        target_node => 100,
        edges_table => 'edges',
        strategy => 'bidirectional'
    ) as node_id
");
```

### BFS (Breadth-First Search)
**Unweighted shortest path**

```csharp
// Shortest path (unweighted)
var path = await database.QueryAsync(@"
    SELECT GRAPH_TRAVERSE(
        start_node => 1,
        target_node => 100,
        edges_table => 'edges',
        strategy => 'bfs'
    ) as node_id
");
```

### DFS (Depth-First Search)
**Explore all paths**

```csharp
// Visit all connected nodes
var path = await database.QueryAsync(@"
    SELECT GRAPH_TRAVERSE(
        start_node => 1,
        max_depth => 10,
        edges_table => 'edges',
        strategy => 'dfs'
    ) as node_id
");
```

---

## Graph Traversal

### Basic Traversal

```csharp
// Traverse from node 1, up to 5 hops
var neighbors = await database.QueryAsync(@"
    SELECT node_id, depth
    FROM GRAPH_TRAVERSE(1, 'edges', 5, 'bfs')
");

foreach (var row in neighbors)
{
    Console.WriteLine($"Node: {row["node_id"]}, Depth: {row["depth"]}");
}
```

### Hybrid Graph + Vector Queries

```csharp
// Find nearby nodes AND similar embeddings
var results = await database.QueryAsync(@"
    SELECT 
        g.node_id,
        g.depth,
        v.distance
    FROM GRAPH_TRAVERSE(1, 'edges', 3, 'bfs') g
    INNER JOIN documents v ON g.node_id = v.id
    WHERE vec_distance_cosine(v.embedding, ?) < 0.1
    ORDER BY g.depth, v.distance
", [queryEmbedding]);
```

---

## Advanced Features

### Cost Estimation

```csharp
// Get cost estimate before executing
var estimate = await database.QueryAsync(@"
    EXPLAIN GRAPH_TRAVERSE(
        start => 1,
        target => 100,
        strategy => 'astar'
    )
");

// Returns: estimated_nodes, estimated_edges, recommended_strategy
```

### Strategy Selection

```csharp
// Automatic best strategy selection
var bestPath = await database.QueryAsync(@"
    SELECT GRAPH_TRAVERSE_AUTO(1, 100, 'edges', 'weight') as node_id
");

// System analyzes:
// - Graph density
// - Edge weights
// - Target distance
// - Selects optimal strategy (A*, Dijkstra, Bidirectional, BFS)
```

### Custom Heuristics (Phase 6.2)

```csharp
// Register custom heuristic function
await database.ExecuteAsync(@"
    CREATE FUNCTION euclidean_distance(x1 FLOAT, y1 FLOAT, x2 FLOAT, y2 FLOAT) 
    RETURNS FLOAT
    AS SQRT(POWER(x1 - x2, 2) + POWER(y1 - y2, 2))
");

// Use in A*
var path = await database.QueryAsync(@"
    SELECT GRAPH_ASTAR(
        start => 1,
        end => 100,
        edges_table => 'edges',
        weight_column => 'weight',
        heuristic => 'euclidean_distance(source_x, source_y, target_x, target_y)'
    ) as node_id
");
```

---

## Real-World Examples

### Social Network - Find Connections

```csharp
public async Task<List<(int UserId, int Degree)>> GetConnectionsAsync(int userId, int maxDegrees)
{
    var connections = await _database.QueryAsync(@"
        SELECT 
            node_id as user_id,
            depth as degree
        FROM GRAPH_TRAVERSE(?, 'follows', ?, 'bfs')
        WHERE depth > 0
        ORDER BY depth, node_id
    ", [userId, maxDegrees]);

    return connections
        .Cast<dynamic>()
        .Select(c => ((int)c["user_id"], (int)c["degree"]))
        .ToList();
}
```

### Route Planning - Shortest Path with Weights

```csharp
public async Task<List<int>> FindShortestRouteAsync(int startCity, int destCity)
{
    var route = await _database.QueryAsync(@"
        SELECT node_id
        FROM GRAPH_TRAVERSE(?, ?, 'roads', 'distance', 'dijkstra')
        ORDER BY path_order
    ", [startCity, destCity]);

    return route.Select(r => (int)r["node_id"]).ToList();
}
```

### Knowledge Graph - Semantic Search

```csharp
public async Task<List<string>> FindRelatedConceptsAsync(string concept)
{
    var related = await _database.QueryAsync(@"
        SELECT 
            g.concept_id,
            g.depth,
            v.semantic_distance
        FROM GRAPH_TRAVERSE(
            (SELECT id FROM concepts WHERE name = ?),
            'relationships',
            3,
            'bfs'
        ) g
        INNER JOIN concept_embeddings v ON g.concept_id = v.id
        WHERE vec_distance_cosine(v.embedding, ?) < 0.2
        ORDER BY v.semantic_distance ASC
        LIMIT 20
    ", [concept, conceptEmbedding]);

    return related.Select(r => (string)r["concept_name"]).ToList();
}
```

---

## Performance Tuning

### 1. Create Indexes on Graph Columns

```csharp
// Index for fast edge lookups
await database.ExecuteAsync(
    "CREATE INDEX idx_edges_source ON edges(source_id)"
);
await database.ExecuteAsync(
    "CREATE INDEX idx_edges_target ON edges(target_id)"
);
```

### 2. Use A* with Good Heuristics (Phase 6.2)

```csharp
// A* with Euclidean heuristic is 30-50% faster
var path = await database.QueryAsync(@"
    SELECT GRAPH_ASTAR(?, ?, 'edges', 'weight', 'euclidean') as node_id
", [start, end]);
```

### 3. Partition Large Graphs

```csharp
// For graphs with 100M+ nodes, partition by region
var path = await database.QueryAsync(@"
    SELECT GRAPH_TRAVERSE(?, ?, 'edges_region_1', 'weight', 'dijkstra')
    WHERE region = 'us-west'
");
```

### 4. Use Bidirectional Search for Long Paths

```csharp
// 2 searches from both ends meets faster
var path = await database.QueryAsync(@"
    SELECT GRAPH_TRAVERSE(?, ?, 'edges', 'weight', 'bidirectional')
");
```

---

## API Reference

### Functions

| Function | Purpose | Returns |
|----------|---------|---------|
| `GRAPH_TRAVERSE(start, target, edges_table, strategy)` | Traverse graph | node_id, depth |
| `GRAPH_ASTAR(start, target, edges_table, weight, heuristic)` | A* pathfinding (30-50% faster) | node_id, cost |
| `GRAPH_TRAVERSE_AUTO(start, target, edges_table, weight)` | Auto-select strategy | node_id, depth |

### Strategies

- `bfs` - Breadth-First Search
- `dfs` - Depth-First Search
- `dijkstra` - Weighted shortest path
- `bidirectional` - Meet-in-the-middle
- `astar` - A* with heuristics (Phase 6.2, **30-50% faster**)

---

## See Also

- **[Core SharpCoreDB](../SharpCoreDB/README.md)** - Database engine
- **[Vector Search](../SharpCoreDB.VectorSearch/README.md)** - Semantic search
- **[Analytics Engine](../SharpCoreDB.Analytics/README.md)** - Data analysis
- **[Main Documentation](../../docs/INDEX.md)** - Complete guide

---

## Testing

```bash
# Run graph tests
dotnet test tests/SharpCoreDB.Graph.Tests

# Run with coverage
dotnet-coverage collect -f cobertura -o coverage.xml dotnet test
```

**Test Coverage:** 17+ comprehensive test cases for Phase 6.2

---

## License

MIT License - See [LICENSE](../../LICENSE)

---

**Last Updated:** February 19, 2026 | Version 1.3.5 (Phase 6.2)
