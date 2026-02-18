# EF Core GraphRAG Integration - Complete Guide

## Overview

This document provides comprehensive guidance on using GraphRAG with Entity Framework Core in SharpCoreDB. It covers the LINQ query extensions, SQL translation, usage patterns, and best practices.

**Status:** ✅ Phase 5.1 complete  
**Last Updated:** 2025-02-16

---

## Table of Contents

1. [Quick Start](#quick-start)
2. [API Reference](#api-reference)
3. [SQL Translation](#sql-translation)
4. [Usage Patterns](#usage-patterns)
5. [Performance](#performance)
6. [Troubleshooting](#troubleshooting)
7. [Advanced Examples](#advanced-examples)

---

## Quick Start

### Installation & Setup

```csharp
// No special setup needed - included in SharpCoreDB.EntityFrameworkCore
using SharpCoreDB.EntityFrameworkCore.Query;
using SharpCoreDB.Interfaces;

var options = new DbContextOptionsBuilder<MyDbContext>()
    .UseSharpCoreDB("database.db")
    .Build();

using var context = new MyDbContext(options);
```

### 5-Minute Example

```csharp
// Find all products reachable from supplier 10 within 3 hops
var products = await context.Products
    .Where(p => context.Suppliers
        .Traverse(
            startNodeId: 10,
            relationshipColumn: "parentSupplierId",
            maxDepth: 3,
            strategy: GraphTraversalStrategy.Bfs)
        .Contains(p.SupplierId))
    .ToListAsync();
```

---

## API Reference

### Core Extension Methods

#### `IQueryable<T>.Traverse<T>()`

Returns all reachable node IDs via graph traversal.

```csharp
public static IQueryable<long> Traverse<TEntity>(
    this IQueryable<TEntity> source,
    long startNodeId,
    string relationshipColumn,
    int maxDepth,
    GraphTraversalStrategy strategy)
```

**Parameters:**
- `startNodeId` (long): Starting node row ID
- `relationshipColumn` (string): ROWREF column name containing edges
- `maxDepth` (int): Maximum traversal depth (0 = only start node)
- `strategy` (GraphTraversalStrategy): Traversal algorithm

**Returns:** `IQueryable<long>` of reachable node IDs

**Example:**
```csharp
var nodeIds = await context.Nodes
    .Traverse(1, "nextId", 5, GraphTraversalStrategy.Bfs)
    .ToListAsync();
// Returns: [1, 2, 3, 4, 5, 6] (all reachable from node 1)
```

---

### ✨ NEW: Fluent API (Phase 5)

#### `IQueryable<T>.GraphTraverse<T>()`

Creates a fluent graph traversal configuration with advanced strategy selection.

```csharp
public static GraphTraversalQueryable<TEntity> GraphTraverse<TEntity>(
    this IQueryable<TEntity> source,
    long startNodeId,
    string relationshipColumn,
    int maxDepth)
```

**Returns:** `GraphTraversalQueryable<TEntity>` for method chaining

**Fluent Methods:**

##### `.WithStrategy(GraphTraversalStrategy)`
Explicitly sets the traversal strategy.

```csharp
var results = await context.Documents
    .GraphTraverse(startId, "References", 5)
    .WithStrategy(GraphTraversalStrategy.AStar)
    .ToListAsync();
```

**Available Strategies:**
- `GraphTraversalStrategy.Bfs` - Breadth-first (default)
- `GraphTraversalStrategy.Dfs` - Depth-first
- `GraphTraversalStrategy.Bidirectional` - Both directions
- `GraphTraversalStrategy.Dijkstra` - Weighted shortest path
- `GraphTraversalStrategy.AStar` - Heuristic-guided shortest path

##### `.WithHeuristic(AStarHeuristic)`
Sets the A* heuristic function (only applies when using AStar strategy).

```csharp
var results = await context.Documents
    .GraphTraverse(startId, "References", 5)
    .WithStrategy(GraphTraversalStrategy.AStar)
    .WithHeuristic(AStarHeuristic.Depth)
    .ToListAsync();
```

**Available Heuristics:**
- `AStarHeuristic.Depth` - Depth-based heuristic (default, fastest)
- `AStarHeuristic.Uniform` - No heuristic (equivalent to Dijkstra)

##### `.WithAutoStrategy(GraphStatistics?)`
Automatically selects the optimal strategy based on graph characteristics.

```csharp
// Auto-select with default statistics
var results = await context.Documents
    .GraphTraverse(startId, "References", 5)
    .WithAutoStrategy()
    .ToListAsync();

// Auto-select with custom statistics
var stats = new GraphStatistics(
    totalNodes: 10000,
    totalEdges: 15000,
    estimatedDegree: 1.5);

var results = await context.Documents
    .GraphTraverse(startId, "References", 5)
    .WithAutoStrategy(stats)
    .ToListAsync();
```

**Example: Complete Fluent Chain**
```csharp
// Find related documents using A* with depth heuristic
var relatedDocs = await context.Documents
    .GraphTraverse(sourceDocId, "References", maxDepth: 3)
    .WithStrategy(GraphTraversalStrategy.AStar)
    .WithHeuristic(AStarHeuristic.Depth)
    .ToListAsync();

// Results: [1, 5, 12, 18, 23] (node IDs)
```

---

#### `IQueryable<T>.WhereIn<T>()`

Filters entities by checking if their ID is in a collection.

```csharp
public static IQueryable<TEntity> WhereIn<TEntity>(
    this IQueryable<TEntity> source,
    IEnumerable<long> traversalIds)
```

**Parameters:**
- `traversalIds` (IEnumerable<long>): Collection of IDs to filter by

**Returns:** Filtered `IQueryable<TEntity>`

**Example:**
```csharp
var traversalIds = new List<long> { 1, 2, 3, 4, 5 };
var entities = await context.Orders
    .WhereIn(traversalIds)
    .ToListAsync();
// Returns: Orders with IDs in traversalIds
```

---

#### `IQueryable<T>.TraverseWhere<T>()`

Combines traversal with WHERE filtering in a single query.

```csharp
public static IQueryable<TEntity> TraverseWhere<TEntity>(
    this IQueryable<TEntity> source,
    long startNodeId,
    string relationshipColumn,
    int maxDepth,
    GraphTraversalStrategy strategy,
    Expression<Func<TEntity, bool>> predicate)
```

**Parameters:**
- Same as `.Traverse()` plus:
- `predicate`: Additional WHERE condition

**Returns:** Filtered `IQueryable<TEntity>`

**Example:**
```csharp
var expensiveOrders = await context.Orders
    .TraverseWhere(
        startNodeId: 1,
        relationshipColumn: "supplierNodeId",
        maxDepth: 4,
        strategy: GraphTraversalStrategy.Bfs,
        predicate: o => o.Amount > 1000)
    .ToListAsync();
```

---

#### `IQueryable<T>.Distinct<T>()`

Removes duplicates from traversal results.

```csharp
var unique = await context.Nodes
    .Traverse(1, "nextId", 5, GraphTraversalStrategy.Bfs)
    .Distinct()
    .ToListAsync();
```

---

#### `IQueryable<T>.Take<T>()`

Limits traversal result count.

```csharp
var first10 = await context.Nodes
    .Traverse(1, "nextId", 10, GraphTraversalStrategy.Bfs)
    .Take(10)
    .ToListAsync();
```

---

## SQL Translation

### How LINQ Maps to SQL

The EF Core provider translates LINQ graph methods to native SQL function calls:

| LINQ Method | Generated SQL |
|---|---|
| `.Traverse(1, "next", 3, BFS)` | `GRAPH_TRAVERSE(1, 'next', 3, 0)` |
| `.Traverse(1, "next", 3, DFS)` | `GRAPH_TRAVERSE(1, 'next', 3, 1)` |
| `.WhereIn([1,2,3])` | `WHERE Id IN (1, 2, 3)` |

### Strategy Values

```csharp
enum GraphTraversalStrategy : int
{
    Bfs = 0,           // Breadth-first
    Dfs = 1,           // Depth-first
    Bidirectional = 2, // Outgoing + incoming traversal
    Dijkstra = 3       // Weighted shortest path
}
```

### SQL Examples

#### Simple Traversal
```csharp
var result = context.Nodes
    .Traverse(1, "nextId", 3, GraphTraversalStrategy.Bfs)
    .ToListAsync();
```

**Generated SQL:**
```sql
SELECT GRAPH_TRAVERSE(1, 'nextId', 3, 0)
```

---

#### With Filtering
```csharp
var result = context.Orders
    .Where(o => context.Nodes
        .Traverse(1, "supplierId", 5, GraphTraversalStrategy.Bfs)
        .Contains(o.NodeId))
    .Where(o => o.Amount > 100)
    .ToListAsync();
```

**Generated SQL:**
```sql
SELECT * FROM Orders 
WHERE NodeId IN (GRAPH_TRAVERSE(1, 'supplierId', 5, 0))
  AND Amount > 100
```

---

#### With Multiple Conditions
```csharp
var result = context.Orders
    .WhereIn(traversalIds)
    .Where(o => o.Status == "Active")
    .Where(o => o.CreatedDate >= DateTime.Now.AddDays(-30))
    .OrderByDescending(o => o.Amount)
    .ToListAsync();
```

**Generated SQL:**
```sql
SELECT * FROM Orders
WHERE Id IN (traversal_ids)
  AND Status = 'Active'
  AND CreatedDate >= DATETIME('now', '-30 days')
ORDER BY Amount DESC
```

---

## Usage Patterns

### Pattern 1: Simple Traversal

Find all reachable nodes.

```csharp
var nodeIds = await context.Nodes
    .Traverse(startNodeId: 1, 
              relationshipColumn: "childId",
              maxDepth: 5,
              strategy: GraphTraversalStrategy.Bfs)
    .ToListAsync();

foreach (var nodeId in nodeIds)
{
    Console.WriteLine($"Node {nodeId} is reachable");
}
```

---

### Pattern 2: Filter by Traversal

Get entities linked to traversed nodes.

```csharp
var entities = await context.Orders
    .WhereIn(traversalIds)
    .Where(o => o.IsActive)
    .ToListAsync();
```

---

### Pattern 3: Combined Traversal + Predicate

```csharp
var highValueConnections = await context.Orders
    .TraverseWhere(
        startNodeId: customerId,
        relationshipColumn: "linkedCustomerId",
        maxDepth: 3,
        strategy: GraphTraversalStrategy.Bfs,
        predicate: o => o.Amount > 5000 && o.Status == "Completed")
    .OrderByDescending(o => o.Amount)
    .ToListAsync();
```

---

### Pattern 4: Nested Traversal

Multiple traversals in one query.

```csharp
var relatedProducts = await context.Products
    .Where(p => context.Suppliers
        .Traverse(p.SupplierId, "parentId", 2, GraphTraversalStrategy.Bfs)
        .Contains(context.Suppliers
            .Traverse(targetSupplierId, "parentId", 2, GraphTraversalStrategy.Bfs)
            .FirstOrDefault()))
    .ToListAsync();
```

---

### Pattern 5: Conditional Traversal

```csharp
var result = isPremium
    ? await context.Orders
        .WhereIn(traversalIds)
        .OrderByDescending(o => o.Amount)
        .Take(100)
        .ToListAsync()
    : await context.Orders
        .WhereIn(traversalIds)
        .Take(10)
        .ToListAsync();
```

---

## Performance

### Database-Side Execution

All traversal logic runs in the SharpCoreDB engine:

```
Client (LINQ)
    ↓ (translated)
Database (GRAPH_TRAVERSE SQL function)
    ↓ (executed)
Results (streamed to client)
```

**Benefits:**
- ✅ Zero network overhead for traversal logic
- ✅ No in-memory graph construction
- ✅ Native index utilization
- ✅ Optimal database-side filtering

### Optimization Tips

1. **Use reasonable depth limits**
   ```csharp
   .Traverse(1, "nextId", 5, ...)  // ✅ Good
   .Traverse(1, "nextId", 1000, ...)  // ❌ Bad
   ```

2. **Index ROWREF columns**
   ```sql
   CREATE INDEX idx_next_id ON nodes(nextId);
   ```

3. **Filter before traversal when possible**
   ```csharp
   context.Orders
       .Where(o => o.Status == "Active")      // Filter first
       .WhereIn(traversalIds)                 // Then traverse
       .ToListAsync();
   ```

4. **Use appropriate strategy**
   - BFS for wide, shallow graphs
   - DFS for deep, narrow hierarchies
   - Bidirectional for finding connections between two nodes

5. **Limit results with `.Take()`**
   ```csharp
   .Traverse(1, "next", 10)
   .Distinct()
   .Take(100)
   .ToListAsync();
   ```

---

## Troubleshooting

### Error: "GRAPH_TRAVERSE is not recognized"

**Cause:** Database not using SharpCoreDB provider

**Solution:**
```csharp
var options = new DbContextOptionsBuilder<MyDbContext>()
    .UseSharpCoreDB("database.db")  // ✅ Must use SharpCoreDB
    .Build();
```

---

### Error: "Column does not exist"

**Cause:** Incorrect relationship column name

**Solution:** Verify column exists in database
```csharp
// Check schema
var columns = context.Nodes.AsNoTracking()
    .FromSqlRaw("PRAGMA table_info(nodes)")
    .ToList();

// Use correct name
.Traverse(1, "parentNodeId", 3, GraphTraversalStrategy.Bfs)
```

---

### Error: "Negative max depth"

**Cause:** Negative depth parameter

**Solution:**
```csharp
.Traverse(1, "nextId", Math.Max(0, depth), ...)  // ✅ Correct
```

---

### Slow Queries

**Causes & Solutions:**

| Cause | Solution |
|---|---|
| Large max depth | Reduce depth; use `.Take()` |
| No index on ROWREF column | Create index: `CREATE INDEX idx_rowref ON table(column)` |
| Wrong strategy for graph shape | Use DFS for deep graphs, BFS for wide |
| No WHERE filter | Add `.Where()` to narrow results early |

---

## Advanced Examples

### Example 1: Organizational Hierarchy

Find all direct and indirect subordinates of a manager.

```csharp
var managerId = 10;

var subordinates = await context.Employees
    .Where(e => context.Employees
        .Traverse(
            startNodeId: managerId,
            relationshipColumn: "supervisorId",
            maxDepth: 10,  // Max organizational depth
            strategy: GraphTraversalStrategy.Bfs)
        .Contains(e.Id))
    .OrderBy(e => e.EmployeeNumber)
    .ToListAsync();

Console.WriteLine($"Manager {managerId} has {subordinates.Count} direct and indirect reports");
```

---

### Example 2: Supply Chain Reachability

Find all products obtainable from a supplier.

```csharp
var supplierId = 5;

var availableProducts = await context.Products
    .Where(p => context.SupplierChain
        .Traverse(
            startNodeId: supplierId,
            relationshipColumn: "sourceId",
            maxDepth: 5,
            strategy: GraphTraversalStrategy.Bfs)
        .Contains(p.SourceNodeId))
    .Where(p => p.InStock)
    .Where(p => p.Price < 100)
    .OrderBy(p => p.Price)
    .ToListAsync();
```

---

### Example 3: Social Network Recommendations

Find friends of friends.

```csharp
var userId = 42;

var recommendations = await context.Users
    .Where(u => context.Friendships
        .Traverse(
            startNodeId: userId,
            relationshipColumn: "friendId",
            maxDepth: 2,
            strategy: GraphTraversalStrategy.Bfs)
        .Contains(u.Id))
    .Where(u => !context.BlockedUsers.Any(b => 
        b.UserId == userId && b.BlockedId == u.Id))
    .OrderByDescending(u => u.MutualFriendCount)
    .Take(20)
    .ToListAsync();
```

---

### Example 4: Knowledge Graph Entity Resolution

Find all related concepts.

```csharp
var conceptId = 100;

var relatedConcepts = await context.Concepts
    .Where(c => context.ConceptGraph
        .Traverse(
            startNodeId: conceptId,
            relationshipColumn: "relatedConceptId",
            maxDepth: 3,
            strategy: GraphTraversalStrategy.Dijkstra)  // Weighted!
        .Contains(c.Id))
    .OrderBy(c => c.Distance)
    .ToListAsync();
```

---

### Example 5: Multi-Graph Queries

Query across multiple graph relationships.

```csharp
var result = await context.Orders
    .Where(o => 
        // Via supplier network
        context.Suppliers
            .Traverse(o.SupplierId, "parentSupplierId", 2, GraphTraversalStrategy.Bfs)
            .Contains(targetSupplierId) &&
        // AND via customer hierarchy
        context.Customers
            .Traverse(o.CustomerId, "parentCustomerId", 3, GraphTraversalStrategy.Bfs)
            .Contains(targetCustomerId))
    .ToListAsync();
```

---

## Best Practices

### ✅ DO

- ✅ Use async/await (`.ToListAsync()`, not `.ToList()`)
- ✅ Add WHERE filters early in the query
- ✅ Use appropriate strategies for your graph shape
- ✅ Index ROWREF columns
- ✅ Limit depth to reasonable values
- ✅ Use `.Take()` to limit large result sets
- ✅ Cache frequently traversed results

### ❌ DON'T

- ❌ Use `.ToList().Result` (blocking)
- ❌ Traverse with maxDepth > 100 without investigation
- ❌ Forget to index ROWREF columns
- ❌ Use `.Traverse()` inside `.Select()` (N+1 queries)
- ❌ Traverse graphs with circular references without depth limit
- ❌ Execute same traversal multiple times

---

## Summary

The EF Core GraphRAG integration provides:

| Feature | Benefit |
|---|---|
| **LINQ API** | Type-safe, IntelliSense-enabled graph queries |
| **SQL Translation** | Automatic conversion to GRAPH_TRAVERSE() |
| **Database Execution** | All logic runs server-side, zero overhead |
| **Strategy Support** | BFS, DFS, Bidirectional, Dijkstra |
| **Integration** | Works seamlessly with existing LINQ operators |
| **Performance** | Native index utilization, lazy evaluation |

For more information, see:
- [LINQ_API_GUIDE.md](./LINQ_API_GUIDE.md) - API reference
- [EF_CORE_INTEGRATION_SUMMARY.md](./EF_CORE_INTEGRATION_SUMMARY.md) - Architecture overview
- [GRAPHRAG_IMPLEMENTATION_PLAN.md](./GRAPHRAG_IMPLEMENTATION_PLAN.md) - Implementation details
