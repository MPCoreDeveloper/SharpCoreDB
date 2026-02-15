# GraphRAG + EF Core Integration Guide

## Overview

This guide demonstrates how to use the **GraphRAG LINQ extensions** with Entity Framework Core to query graph relationships with a fluent, type-safe API.

> **Phase Status**: ✅ Complete - GraphRAG Phase 2 EF Core Integration

## Key Features

- **LINQ Graph Queries**: `.Traverse()` method for fluent graph exploration
- **Strategy Support**: BFS, DFS, Bidirectional, and Dijkstra traversal
- **Depth Control**: Set maximum traversal depth
- **Filtering**: Combine graph traversal with WHERE predicates  
- **Performance**: Translates to native `GRAPH_TRAVERSE()` SQL functions (zero overhead)
- **Type-Safe**: Full IntelliSense support in Visual Studio

## Quick Start

### 1. Basic Traversal

```csharp
// Traverse from node 1, following the "next" relationship
var nodeIds = await db.Nodes
    .Traverse(startNodeId: 1, relationshipColumn: "next", 
              maxDepth: 3, strategy: GraphTraversalStrategy.Bfs)
    .ToListAsync();

// Result: IEnumerable<long> containing all reachable node IDs
```

### 2. Filtering by Traversal Results

```csharp
// Find all orders linked to nodes within 5 hops of node 1
var traversalIds = new List<long> { 1, 2, 3, 4, 5 };

var orders = await db.Orders
    .WhereIn(traversalIds)
    .ToListAsync();

// Alternative: Use TraverseWhere for combined query
var orders = await db.Orders
    .TraverseWhere(
        startNodeId: 1,
        relationshipColumn: "NodeId",
        maxDepth: 5,
        strategy: GraphTraversalStrategy.Dfs,
        predicate: o => o.Amount > 100)
    .ToListAsync();
```

### 3. Complex Queries

```csharp
// Multi-step graph exploration with filtering
var result = await db.Orders
    .Where(o => db.Nodes
        .Traverse(startNodeId: o.LinkedNodeId, 
                 relationshipColumn: "parent", 
                 maxDepth: 3, 
                 strategy: GraphTraversalStrategy.Bfs)
        .Contains(o.RootNodeId))
    .Where(o => o.Status == "Active")
    .OrderBy(o => o.Amount)
    .ToListAsync();
```

## API Reference

### IQueryable&lt;T&gt; Extensions

#### Traverse()
Traverses a graph and returns reachable node IDs.

```csharp
public static IQueryable<long> Traverse<TEntity>(
    this IQueryable<TEntity> source,
    long startNodeId,
    string relationshipColumn,
    int maxDepth,
    GraphTraversalStrategy strategy)
```

**Parameters:**
- `startNodeId`: The row ID to start traversal from
- `relationshipColumn`: Name of the ROWREF column containing edge relationships
- `maxDepth`: Maximum number of hops (0 = only start node, 1 = start + neighbors, etc.)
- `strategy`: `GraphTraversalStrategy.Bfs`, `Dfs`, `Bidirectional`, or `Dijkstra`

**Returns:** `IQueryable<long>` - Database-evaluated traversal results

**Example:**
```csharp
var reachable = db.Nodes
    .Traverse(1, "nextId", 3, GraphTraversalStrategy.Bfs)
    .ToListAsync();
```

---

#### WhereIn()
Filters entities by checking if their ID is in the traversal result set.

```csharp
public static IQueryable<TEntity> WhereIn<TEntity>(
    this IQueryable<TEntity> source,
    IEnumerable<long> traversalIds)
```

**Parameters:**
- `traversalIds`: Collection of IDs to filter by

**Returns:** Filtered `IQueryable<TEntity>`

**Example:**
```csharp
var nodes = new List<long> { 1, 2, 3, 4, 5 };
var entities = await db.MyEntities
    .WhereIn(nodes)
    .ToListAsync();
```

---

#### TraverseWhere()
Combines graph traversal with an additional WHERE predicate in a single query.

```csharp
public static IQueryable<TEntity> TraverseWhere<TEntity>(
    this IQueryable<TEntity> source,
    long startNodeId,
    string relationshipColumn,
    int maxDepth,
    GraphTraversalStrategy strategy,
    Expression<Func<TEntity, bool>> predicate)
```

**Example:**
```csharp
var expensiveOrders = await db.Orders
    .TraverseWhere(
        startNodeId: 1,
        relationshipColumn: "supplierNodeId",
        maxDepth: 4,
        strategy: GraphTraversalStrategy.Bfs,
        predicate: o => o.Amount > 1000)
    .ToListAsync();
```

---

#### Distinct()
Removes duplicate IDs from traversal results.

```csharp
var unique = await db.Nodes
    .Traverse(1, "next", 5, GraphTraversalStrategy.Bfs)
    .Distinct()
    .ToListAsync();
```

---

#### Take()
Limits the number of traversal results returned.

```csharp
var first10 = await db.Nodes
    .Traverse(1, "next", 10, GraphTraversalStrategy.Bfs)
    .Take(10)
    .ToListAsync();
```

---

## Traversal Strategies

### BFS (Breadth-First Search)
Explores nodes level by level, guaranteeing shortest paths.

```csharp
.Traverse(1, "next", 5, GraphTraversalStrategy.Bfs)
```

**Best for:**
- Finding nearest neighbors
- Shortest path analysis
- Level-based exploration
- Knowledge graphs

### DFS (Depth-First Search)
Explores as far as possible along each branch before backtracking.

```csharp
.Traverse(1, "parent", 5, GraphTraversalStrategy.Dfs)
```

**Best for:**
- Tree-like hierarchies
- Deep relationship chains
- Memory-efficient exploration
- Hierarchical data

### Bidirectional
Searches from both start and end nodes simultaneously.

```csharp
.Traverse(startId: 1, relationshipColumn: "related", maxDepth: 3,
         strategy: GraphTraversalStrategy.Bidirectional)
```

**Best for:**
- Finding connections between two specific nodes
- Reducing search space for long-distance queries
- Social network recommendations

### Dijkstra
Weighted shortest path traversal (requires edge weights).

```csharp
.Traverse(1, "weightedNext", 10, GraphTraversalStrategy.Dijkstra)
```

**Best for:**
- Cost-optimized paths
- Weighted graph analysis
- Network routing

---

## Generated SQL

The LINQ methods compile to efficient SQL using the `GRAPH_TRAVERSE()` function:

### Example 1: Simple Traversal
```csharp
var result = db.Nodes
    .Traverse(1, "nextId", 3, GraphTraversalStrategy.Bfs)
    .ToListAsync();
```

**Generates SQL:**
```sql
SELECT GRAPH_TRAVERSE(1, 'nextId', 3, 0)
```

### Example 2: With WHERE Clause
```csharp
var result = db.Orders
    .Where(o => db.Nodes
        .Traverse(1, "supplierId", 5, GraphTraversalStrategy.Bfs)
        .Contains(o.NodeId))
    .Where(o => o.Amount > 100)
    .ToListAsync();
```

**Generates SQL:**
```sql
SELECT * FROM Orders 
WHERE NodeId IN (GRAPH_TRAVERSE(1, 'supplierId', 5, 0))
  AND Amount > 100
```

### Example 3: Multiple Filters
```csharp
var result = db.Orders
    .WhereIn(traversalIds)
    .Where(o => o.Status == "Active")
    .OrderBy(o => o.CreatedDate)
    .ToListAsync();
```

**Generates SQL:**
```sql
SELECT * FROM Orders
WHERE Id IN (traversal_ids)
  AND Status = 'Active'
ORDER BY CreatedDate ASC
```

---

## Performance Considerations

### Query Optimization
- **Database Evaluation**: All traversal logic runs in the database via `GRAPH_TRAVERSE()` SQL function
- **No Network Overhead**: Results streamed directly from database
- **Index Utilization**: Native SQL queries leverage existing indexes
- **Lazy Evaluation**: LINQ queries are not executed until `.ToList()` or `.ToListAsync()`

### Best Practices

1. **Use `.ToListAsync()` in async contexts**
   ```csharp
   var results = await query.ToListAsync(); // ✅ Correct
   var results = query.ToList().Result;      // ❌ Blocking
   ```

2. **Limit depth for large graphs**
   ```csharp
   .Traverse(1, "next", 5, ...) // ✅ Reasonable
   .Traverse(1, "next", 1000, ...) // ❌ Expensive
   ```

3. **Apply filtering early**
   ```csharp
   db.Orders
       .Where(o => o.Status == "Active")     // ✅ Filter first
       .WhereIn(traversalIds)                // Then traverse
   ```

4. **Reuse traversal results**
   ```csharp
   var ids = await query.ToListAsync();
   // Reuse ids for multiple subsequent queries
   ```

---

## Error Handling

### Invalid Parameters
```csharp
// ❌ Negative max depth
db.Nodes.Traverse(1, "next", -1, GraphTraversalStrategy.Bfs);
// Throws: ArgumentOutOfRangeException

// ❌ Null relationship column
db.Nodes.Traverse(1, null, 3, GraphTraversalStrategy.Bfs);
// Throws: ArgumentException

// ❌ Null source
IQueryable<Node> source = null;
source.Traverse(1, "next", 3, GraphTraversalStrategy.Bfs);
// Throws: ArgumentNullException
```

### Safe Usage
```csharp
try
{
    var result = await db.Nodes
        .Traverse(startNodeId, relationshipColumn, maxDepth, strategy)
        .ToListAsync(cancellationToken);
}
catch (ArgumentException ex)
{
    // Handle invalid parameters
    logger.LogError(ex, "Invalid traversal parameters");
}
catch (OperationCanceledException)
{
    // Handle cancellation
    logger.LogInformation("Traversal cancelled");
}
```

---

## Advanced Examples

### 1. Find All Descendants in a Tree
```csharp
// Get all employees under a manager (hierarchical structure)
var subordinates = await db.Employees
    .Where(e => db.Employees
        .Traverse(startNodeId: managerId, 
                 relationshipColumn: "supervisorId",
                 maxDepth: 10, // Max organizational depth
                 strategy: GraphTraversalStrategy.Bfs)
        .Contains(e.Id))
    .ToListAsync();
```

### 2. Find Reachable Products in Supply Chain
```csharp
// Locate all products obtainable from a supplier
var products = await db.Products
    .Where(p => db.SupplyChain
        .Traverse(startNodeId: supplierId,
                 relationshipColumn: "sourceId",
                 maxDepth: 5,
                 strategy: GraphTraversalStrategy.Bfs)
        .Contains(p.SourceNodeId))
    .Where(p => p.InStock)
    .OrderBy(p => p.Price)
    .ToListAsync();
```

### 3. Social Network Friend Recommendations
```csharp
// Find friends of friends (degree-2 network)
var potentialFriends = await db.Users
    .Where(u => db.Friendships
        .Traverse(startNodeId: userId,
                 relationshipColumn: "friendId",
                 maxDepth: 2,
                 strategy: GraphTraversalStrategy.Bfs)
        .Contains(u.Id))
    .Where(u => !db.BlockedUsers.Any(b => b.UserId == userId && b.BlockedId == u.Id))
    .OrderByDescending(u => u.MutualFriendCount)
    .Take(20)
    .ToListAsync();
```

### 4. Knowledge Graph Query
```csharp
// Find all related concepts
var relatedConcepts = await db.Concepts
    .Where(c => db.ConceptGraph
        .Traverse(startNodeId: conceptId,
                 relationshipColumn: "relatedConceptId",
                 maxDepth: 3,
                 strategy: GraphTraversalStrategy.Dijkstra) // Use weighted edges
        .Contains(c.Id))
    .OrderBy(c => c.Relevance)
    .ToListAsync();
```

---

## Troubleshooting

### Issue: "GRAPH_TRAVERSE is not recognized"
**Cause**: Database provider not correctly configured  
**Solution**: Ensure `DbContextOptions` uses `.UseSharpCoreDB(connectionString)`

### Issue: "Column does not exist"
**Cause**: Wrong relationship column name  
**Solution**: Verify ROWREF column name in table schema

### Issue: Slow Queries
**Cause**: Large max depth or missing indexes  
**Solution**: 
- Reduce `maxDepth` parameter
- Ensure ROWREF column is indexed
- Use DFS instead of BFS for deep graphs

---

## See Also

- [GraphRAG Architecture](../GRAPHRAG_PROPOSAL_ANALYSIS.md)
- [SharpCoreDB EF Core Provider](../SharpCoreDB.EntityFrameworkCore/README.md)
- [Graph Traversal Engine](../../SharpCoreDB.Graph/README.md)
- [Performance Benchmarks](./GraphTraversalPerformanceBenchmarks.cs)
