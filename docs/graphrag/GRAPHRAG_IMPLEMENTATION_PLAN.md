# GraphRAG Comprehensive Implementation Plan

**Version:** 1.0  
**Date:** 2026-02-15  
**Authors:** SharpCoreDB Engineering  
**Status:** Draft — Ready for Technical Review  
**Prerequisites:** SharpCoreDB v1.3.0 (current)

---

## Table of Contents

1. [Goals and Non-Goals](#1-goals-and-non-goals)
2. [Architecture Overview](#2-architecture-overview)
3. [Phase 1 — ROWREF Column Type and Index-Free Adjacency](#3-phase-1--rowref-column-type-and-index-free-adjacency)
4. [Phase 2 — Graph Traversal Engine](#4-phase-2--graph-traversal-engine)
5. [Phase 3 — SQL Integration (GRAPH_TRAVERSE)](#5-phase-3--sql-integration-graph_traverse)
6. [Phase 4 — Hybrid Vector + Graph Queries](#6-phase-4--hybrid-vector--graph-queries)
7. [Phase 5 — EF Core and API Surface](#7-phase-5--ef-core-and-api-surface)
8. [Testing Strategy](#8-testing-strategy)
9. [Performance Targets and Benchmarks](#9-performance-targets-and-benchmarks)
10. [Migration and Backwards Compatibility](#10-migration-and-backwards-compatibility)
11. [File Inventory](#11-file-inventory)
12. [Risk Register](#12-risk-register)
13. [Milestones and Schedule](#13-milestones-and-schedule)

---

## 1. Goals and Non-Goals

### Goals

| # | Goal | Success Criteria |
|---|------|-----------------|
| G1 | O(1) index-free adjacency via direct row pointers | `ROWREF` column resolves in <1 μs vs current FK O(log n) |
| G2 | BFS/DFS graph traversal engine | 1M nodes traversed in <100 ms |
| G3 | SQL-accessible graph functions | `GRAPH_TRAVERSE()` integrates with existing query optimizer |
| G4 | Hybrid Vector + Graph queries | Single query combining HNSW similarity + N-hop structural filter |
| G5 | Zero new external dependencies | All features ship in existing SharpCoreDB + VectorSearch DLLs |
| G6 | Full backward compatibility | Existing schemas, APIs, and serialization formats unchanged |

### Non-Goals

- Full Cypher/GQL query language implementation.
- Property graph model with typed edge labels (future v2.x consideration).
- Distributed graph partitioning or sharding.
- Real-time graph streaming (CDC).

---

## 2. Architecture Overview

### Layered Design

```
┌─────────────────────────────────────────────────────┐
│                   API / SQL Layer                    │
│  GRAPH_TRAVERSE() · SHORTEST_PATH() · HybridQuery   │
├─────────────────────────────────────────────────────┤
│               Graph Traversal Layer                  │
│  GraphTraversalEngine · GraphTraversalOptimizer      │
│  AdjacencyListIndex · CycleDetector · PathFinder     │
├─────────────────────────────────────────────────────┤
│               Query Optimizer Integration            │
│  QueryOptimizer (extend) · HybridGraphVectorOpt      │
├─────────────────────────────────────────────────────┤
│                 Storage Layer                         │
│  DataType.RowRef · Table.Serialization (extend)      │
│  BTreeIndexManager (extend) · IStorageEngine (reuse) │
├─────────────────────────────────────────────────────┤
│               Existing Infrastructure                │
│  IStorageEngine · HNSW Index · ForeignKeyConstraint  │
│  SerializationService · EnhancedSqlParser            │
└─────────────────────────────────────────────────────┘
```

### Key Design Decisions

| Decision | Rationale |
|----------|-----------|
| `ROWREF` stores raw `long` (8 bytes), not a composite pointer | Aligns with `IStorageEngine.Read(tableName, long storageReference)` — O(1) resolution |
| BFS uses `ArrayPool<long>` for the frontier queue | Zero-allocation hot path per project coding standards |
| Graph traversal returns `HashSet<long>` (row IDs) | Composable with existing WHERE/IN clause execution |
| No separate edge table required (SurrealDB model) | Edges are ROWREF columns on existing tables — simpler, faster |
| `Lock` class (not `object`) for concurrency | C# 14 requirement per coding standards |

---

## 3. Phase 1 — ROWREF Column Type and Index-Free Adjacency

**Target:** v1.4.0 (Q3 2026)  
**Effort:** ~1.5 weeks, ~600 LOC  
**Risk:** 🟢 Low

### 3.1 Add `DataType.RowRef` Enum Value

**File:** `src/SharpCoreDB/DataTypes.cs`

```csharp
/// <summary>
/// Direct row reference type. Stores the physical row ID (long) of
/// a record in a target table, enabling O(1) index-free adjacency.
/// Used for graph traversal and lightweight relationship modeling.
/// </summary>
RowRef,
```

**Validation rules:**
- Value must be a valid `long` (≥ 0, or -1 / `long.MinValue` for null).
- When the column has a `ForeignKeyConstraint`, the referenced row must exist in the target table (enforced at insert/update time).
- Cascade delete/update follows existing `FkAction` behavior.

### 3.2 Serialization Support

**File:** `src/SharpCoreDB/DataStructures/Table.Serialization.cs`

Add `DataType.RowRef` case to the serialize/deserialize switch. Storage format: `BitConverter.GetBytes((long)value)` — 8 bytes, identical to `DataType.Long` on the wire but semantically distinct.

```
Wire format:
┌────────────────────────┐
│  8 bytes (little-endian long)  │
│  = direct storage reference    │
└────────────────────────┘
```

### 3.3 Column Definition and Schema Validation

**File:** `src/SharpCoreDB/DataStructures/Table.cs`

When a column of type `RowRef` is defined:
1. Require a companion `ForeignKeyConstraint` pointing to the target table + column.
2. At insert/update time, validate the referenced row exists (unless deferred validation is enabled for batch loads).
3. Support `NULL` ROWREF (no relationship).

**SQL syntax (extends parser):**

```sql
CREATE TABLE employees (
    id INTEGER PRIMARY KEY,
    name TEXT,
    manager ROWREF REFERENCES employees(id) ON DELETE SET NULL
);

-- Insert with direct row reference
INSERT INTO employees VALUES (1, 'Alice', NULL);
INSERT INTO employees VALUES (2, 'Bob', 1);     -- Bob's manager = row 1 (Alice)
INSERT INTO employees VALUES (3, 'Carol', 2);   -- Carol's manager = row 2 (Bob)
```

### 3.4 Parser Extension

**Files:**
- `src/SharpCoreDB/Services/EnhancedSqlParser.DDL.cs` — recognize `ROWREF` as a column type token
- `src/SharpCoreDB/Services/SqlParser.DDL.cs` — same for legacy parser path

Map `ROWREF` → `DataType.RowRef` in the type resolution logic. The `REFERENCES` clause already parses; link it to `ForeignKeyConstraint` as today.

### 3.5 EF Core Provider Mapping

**File:** `src/SharpCoreDB.EntityFrameworkCore/` (type mapping)

Map `DataType.RowRef` ↔ `long` in the EF Core type mapper. Fluent API:

```csharp
entity.Property(e => e.ManagerId)
      .HasColumnType("ROWREF")
      .HasForeignKey("employees", "id");
```

### 3.6 Deliverables Checklist

```
[ ] DataType.RowRef enum value
[ ] Serialization (Table.Serialization.cs) — serialize/deserialize 8-byte long
[ ] Schema validation — ROWREF columns require FK constraint
[ ] NULL handling — long.MinValue or -1 sentinel for null ROWREF
[ ] Parser DDL — ROWREF keyword in CREATE TABLE
[ ] Parser DML — INSERT/UPDATE validate ROWREF targets
[ ] EF Core type mapping
[ ] Unit tests: 30+ covering CRUD, NULL, cascade, invalid ref
[ ] Benchmark: ROWREF resolve vs FK index lookup
```

---

## 4. Phase 2 — Graph Traversal Engine

**Target:** v1.4.0 (Q3 2026)  
**Effort:** ~3 weeks, ~2,000 LOC  
**Risk:** 🟡 Medium

### 4.1 `GraphTraversalEngine`

**New file:** `src/SharpCoreDB/Graph/GraphTraversalEngine.cs`

Core class providing BFS and DFS traversal over ROWREF columns.

```csharp
/// <summary>
/// Executes breadth-first and depth-first graph traversals over ROWREF columns.
/// Uses ArrayPool-backed frontier for zero-allocation hot path.
/// Thread-safe for concurrent reads via Lock class.
/// </summary>
public sealed class GraphTraversalEngine(Database database)
{
    /// <summary>
    /// Traverses the graph starting from <paramref name="startNodeId"/> following
    /// the ROWREF column <paramref name="relationshipColumn"/> up to
    /// <paramref name="maxDepth"/> hops. Returns all reachable row IDs.
    /// </summary>
    public HashSet<long> TraverseBfsAsync(
        string tableName,
        long startNodeId,
        string relationshipColumn,
        int maxDepth,
        CancellationToken ct = default);

    /// <summary>
    /// Depth-first traversal variant. Preferred for "is X reachable from Y?"
    /// queries with early termination.
    /// </summary>
    public List<long> TraverseDfsAsync(
        string tableName,
        long startNodeId,
        string relationshipColumn,
        int maxDepth,
        CancellationToken ct = default);
}
```

**Algorithm (BFS):**

```
BFS(startNode, relationshipColumn, maxDepth):
    visited = HashSet<long>()
    frontier = Queue<(long nodeId, int depth)>()   // ArrayPool-backed
    frontier.Enqueue((startNode, 0))
    visited.Add(startNode)

    while frontier is not empty:
        ct.ThrowIfCancellationRequested()
        (current, depth) = frontier.Dequeue()

        if depth >= maxDepth:
            continue

        // Read the row via IStorageEngine.Read() — O(1) with ROWREF
        row = ReadRow(tableName, current)
        neighbors = GetRowRefValues(row, relationshipColumn)

        for each neighbor in neighbors:
            if neighbor not in visited:
                visited.Add(neighbor)
                frontier.Enqueue((neighbor, depth + 1))

    return visited
```

**Performance characteristics:**
- Time: O(V + E) where V = visited nodes, E = edges traversed
- Space: O(V) for visited set
- Allocations: Frontier queue from `ArrayPool<long>`, returned to pool on completion

### 4.2 Multi-ROWREF (Adjacency Lists)

For tables where a node has **multiple** outgoing edges (e.g., a document cites many documents), support two patterns:

**Pattern A — Multiple ROWREF columns:**
```sql
CREATE TABLE code_blocks (
    id INTEGER PRIMARY KEY,
    calls ROWREF REFERENCES code_blocks(id),
    implements ROWREF REFERENCES interfaces(id),
    extends ROWREF REFERENCES code_blocks(id)
);
```

**Pattern B — Edge table with ROWREF pairs:**
```sql
CREATE TABLE edges (
    id INTEGER PRIMARY KEY,
    source ROWREF REFERENCES nodes(id),
    target ROWREF REFERENCES nodes(id),
    edge_type TEXT,
    weight REAL
);
```

The traversal engine supports both patterns. Pattern B is more flexible for multi-edge graphs.

### 4.3 `AdjacencyListIndex`

**New file:** `src/SharpCoreDB/Graph/AdjacencyListIndex.cs`

Optional in-memory adjacency cache for hot graphs. Built on `ConcurrentDictionary<long, long[]>`.

```csharp
/// <summary>
/// In-memory adjacency list index for frequently traversed ROWREF columns.
/// Loads lazily from storage; invalidated on writes via Table change tracking.
/// </summary>
public sealed class AdjacencyListIndex(string tableName, string columnName)
{
    private readonly ConcurrentDictionary<long, long[]> _adjacency = new();
    private readonly Lock _buildLock = new();

    /// <summary>
    /// Returns neighbor row IDs for the given node. Loads from storage on cache miss.
    /// </summary>
    public ReadOnlySpan<long> GetNeighbors(long nodeId);

    /// <summary>
    /// Invalidates the cache entry for a specific node (called on INSERT/UPDATE/DELETE).
    /// </summary>
    public void Invalidate(long nodeId);

    /// <summary>
    /// Preloads the entire adjacency list from storage. Use for batch traversal scenarios.
    /// </summary>
    public async Task PreloadAsync(CancellationToken ct = default);
}
```

### 4.4 Cycle Detection and Depth Limiting

Built into the traversal engine via the `visited` set (BFS) / stack (DFS). No separate component needed.

**Safety guarantees:**
- Maximum depth hard limit: configurable, default 100.
- Visited set prevents infinite loops on cyclic graphs.
- `CancellationToken` checked every iteration for cooperative cancellation.

### 4.5 Path Finding

**New file:** `src/SharpCoreDB/Graph/PathFinder.cs`

```csharp
/// <summary>
/// Finds shortest paths between nodes using bidirectional BFS.
/// Falls back to A* when edge weights are available (edge table pattern).
/// </summary>
public sealed class PathFinder(GraphTraversalEngine engine)
{
    /// <summary>
    /// Finds the shortest path (by hop count) between two nodes.
    /// Uses bidirectional BFS — meets in the middle for O(b^(d/2)) vs O(b^d).
    /// Returns null if no path exists within maxDepth.
    /// </summary>
    public List<long>? FindShortestPathAsync(
        string tableName,
        long startNodeId,
        long targetNodeId,
        string relationshipColumn,
        int maxDepth = 100,
        CancellationToken ct = default);

    /// <summary>
    /// Returns all paths (up to a limit) between two nodes.
    /// Useful for "show me how X and Y are connected."
    /// </summary>
    public List<List<long>> FindAllPathsAsync(
        string tableName,
        long startNodeId,
        long targetNodeId,
        string relationshipColumn,
        int maxDepth = 10,
        int maxPaths = 100,
        CancellationToken ct = default);
}
```

### 4.6 Deliverables Checklist

```
[ ] GraphTraversalEngine (BFS + DFS)
[ ] ArrayPool-backed frontier queue
[ ] Multi-ROWREF column traversal
[ ] Edge table pattern traversal
[ ] AdjacencyListIndex (optional cache)
[ ] PathFinder (bidirectional BFS)
[ ] Cycle detection via visited set
[ ] Configurable depth limits
[ ] CancellationToken end-to-end
[ ] Unit tests: 100+ covering graphs of various topologies
[ ] Benchmark: BFS on 10K / 100K / 1M node synthetic graphs
```

---

## 5. Phase 3 — SQL Integration (GRAPH_TRAVERSE)

**Target:** v1.5.0 (Q4 2026)  
**Effort:** ~2 weeks, ~1,000 LOC  
**Risk:** 🟡 Medium

### 5.1 SQL Function Registration

Register `GRAPH_TRAVERSE` as a built-in SQL function in the parser.

**Syntax:**

```sql
GRAPH_TRAVERSE(table, start_node, relationship_column, max_depth [, strategy])
```

- `table` — table name (string literal or subquery)
- `start_node` — starting row ID (integer literal or subquery)
- `relationship_column` — ROWREF column name (string literal)
- `max_depth` — maximum hop count (integer literal)
- `strategy` — optional: `'BFS'` (default) or `'DFS'`

**Returns:** Set of `long` row IDs (usable in `IN` clause).

### 5.2 Parser Changes

**Files:**
- `src/SharpCoreDB/Services/EnhancedSqlParser.Expressions.cs` — parse `GRAPH_TRAVERSE(...)` as a function call expression
- `src/SharpCoreDB/Services/EnhancedSqlParser.Select.cs` — handle in WHERE/IN context

**AST representation:**

```csharp
/// <summary>
/// Represents a GRAPH_TRAVERSE() function call in the AST.
/// </summary>
public sealed record GraphTraverseExpression(
    string TableName,
    Expression StartNode,
    string RelationshipColumn,
    int MaxDepth,
    TraversalStrategy Strategy = TraversalStrategy.Bfs);
```

### 5.3 Query Optimizer Integration

**File:** `src/SharpCoreDB/Planning/QueryOptimizer.cs` or `src/SharpCoreDB/Optimization/QueryOptimizer.cs`

Extend the cost model to estimate graph traversal cost:

```
Cost(GRAPH_TRAVERSE) = EstimatedNodes × CostPerHop
    where CostPerHop ≈ 0.001ms (ROWREF O(1) read)
    and EstimatedNodes ≈ avg_degree ^ maxDepth (capped by table size)
```

**Optimization rules:**
1. If `GRAPH_TRAVERSE` result set is estimated < 1,000 rows, apply graph filter **first**, then scan/index.
2. If result set is large, apply index filters first, then graph filter.
3. Cache traversal results within the same query execution context.

### 5.4 Additional SQL Functions (v1.5.0+)

```sql
-- Shortest path between two nodes
SHORTEST_PATH(table, start_node, end_node, relationship_column [, max_depth])
-- Returns: ordered list of row IDs forming the path

-- Node degree (number of outgoing edges)
NODE_DEGREE(table, node_id, relationship_column)
-- Returns: integer count

-- Check reachability
IS_REACHABLE(table, start_node, end_node, relationship_column [, max_depth])
-- Returns: boolean
```

### 5.5 Example Queries

```sql
-- Find all employees reporting to Alice (row 1) within 3 levels
SELECT * FROM employees
WHERE id IN (GRAPH_TRAVERSE('employees', 1, 'manager', 3));

-- Code analysis: find all methods called by Controller.Index()
SELECT method_name, file_path FROM methods
WHERE method_id IN (
    GRAPH_TRAVERSE('methods',
        (SELECT id FROM methods WHERE name = 'Index' AND class = 'Controller'),
        'calls',
        5,
        'DFS')
);

-- Knowledge base: find articles citing article 42 within 2 hops
SELECT title, author FROM articles
WHERE article_id IN (GRAPH_TRAVERSE('citations', 42, 'cites', 2))
ORDER BY publish_date DESC;
```

### 5.6 Deliverables Checklist

```
[ ] GRAPH_TRAVERSE() parser support
[ ] GraphTraverseExpression AST node
[ ] Execution plan integration
[ ] Cost estimation for graph traversal
[ ] SHORTEST_PATH() function
[ ] NODE_DEGREE() function
[ ] IS_REACHABLE() function
[ ] Unit tests: 50+ covering SQL syntax, edge cases, error messages
[ ] Integration tests: end-to-end query → traversal → result
```

---

## 6. Phase 4 — Hybrid Vector + Graph Queries

**Target:** v1.6.0 (Q1 2027)  
**Effort:** ~3 weeks, ~1,500 LOC  
**Risk:** 🟠 Medium-High

### 6.1 The GraphRAG Query Pattern

The defining capability: combine semantic similarity with structural constraints.

```sql
-- "Find code chunks semantically similar to my query,
--  but only if they belong to the call graph of ClassX"
SELECT chunk_id, content, vector_distance(embedding, @query_embedding) AS score
FROM code_chunks
WHERE
    vector_distance(embedding, @query_embedding) < 0.3
    AND chunk_id IN (
        GRAPH_TRAVERSE('code_chunks',
            (SELECT id FROM classes WHERE name = 'DataRepository'),
            'belongs_to',
            3)
    )
ORDER BY score ASC
LIMIT 10;
```

### 6.2 `HybridGraphVectorOptimizer`

**New file:** `src/SharpCoreDB.VectorSearch/Optimization/HybridGraphVectorOptimizer.cs`

```csharp
/// <summary>
/// Optimizes queries combining vector similarity and graph traversal predicates.
/// Selects optimal predicate ordering based on estimated selectivity.
/// </summary>
public sealed class HybridGraphVectorOptimizer
{
    /// <summary>
    /// Determines whether to execute graph filter or vector filter first.
    /// Heuristic: apply the more selective filter first.
    /// </summary>
    public ExecutionOrder DeterminePredicateOrder(
        GraphTraverseExpression graphPredicate,
        VectorDistancePredicate vectorPredicate,
        TableStatistics tableStats);
}
```

**Decision matrix:**

| Graph Result Estimate | Vector Result Estimate | Execute First |
|---|---|---|
| < 1,000 rows | Any | Graph → then vector on filtered set |
| Any | < 1,000 rows | Vector → then graph on filtered set |
| Both large | Both large | Graph (cheaper per row) → then vector |

### 6.3 C# API for AI Agents

**New file:** `src/SharpCoreDB.VectorSearch/GraphRagQuery.cs`

Fluent API for programmatic GraphRAG queries (primary use case for AI agents):

```csharp
/// <summary>
/// Fluent builder for hybrid Vector + Graph queries.
/// Designed for AI agent memory retrieval patterns.
/// </summary>
public sealed class GraphRagQueryBuilder(Database db, IVectorIndex vectorIndex)
{
    /// <summary>
    /// Sets the vector similarity constraint.
    /// </summary>
    public GraphRagQueryBuilder WithSimilarity(
        float[] queryEmbedding,
        float maxDistance = 0.3f,
        int topK = 10);

    /// <summary>
    /// Adds a graph traversal constraint.
    /// </summary>
    public GraphRagQueryBuilder WithGraphConstraint(
        string tableName,
        long startNodeId,
        string relationshipColumn,
        int maxDepth);

    /// <summary>
    /// Executes the hybrid query and returns matching row IDs with scores.
    /// </summary>
    public async Task<List<GraphRagResult>> ExecuteAsync(
        CancellationToken ct = default);
}

/// <summary>
/// Result of a GraphRAG query combining vector similarity score and graph distance.
/// </summary>
public sealed record GraphRagResult(
    long RowId,
    float SimilarityScore,
    int GraphDistance);
```

### 6.4 Deliverables Checklist

```
[ ] HybridGraphVectorOptimizer
[ ] Predicate ordering heuristic
[ ] GraphRagQueryBuilder fluent API
[ ] GraphRagResult record
[ ] Integration with existing VectorQueryOptimizer
[ ] SQL: combined WHERE vector_distance + GRAPH_TRAVERSE
[ ] Unit tests: 40+ covering hybrid query patterns
[ ] Integration tests: end-to-end with HNSW + graph
[ ] Benchmark: hybrid query on 100K nodes with 768-dim vectors
```

---

## 7. Phase 5 — EF Core and API Surface

**Target:** v1.6.0–v2.0.0  
**Effort:** ~2 weeks, ~800 LOC  
**Risk:** 🟡 Medium

### 7.1 EF Core LINQ Support

```csharp
// Query via EF Core
var results = await context.Employees
    .Where(e => EF.Functions.GraphTraverse(
        e.Id, "manager", maxDepth: 3)
        .Contains(targetId))
    .ToListAsync();

// Hybrid: vector + graph via EF Core
var chunks = await context.CodeChunks
    .Where(c => EF.Functions.VectorDistance(c.Embedding, queryEmbedding) < 0.3)
    .Where(c => EF.Functions.GraphTraverse(
        startNodeId, "belongs_to", maxDepth: 3)
        .Contains(c.Id))
    .OrderBy(c => EF.Functions.VectorDistance(c.Embedding, queryEmbedding))
    .Take(10)
    .ToListAsync();
```

### 7.2 Model Configuration

```csharp
modelBuilder.Entity<Employee>(entity =>
{
    entity.Property(e => e.ManagerId)
          .HasColumnType("ROWREF");

    entity.HasGraphRelationship(e => e.ManagerId)
          .ReferencesTable("employees")
          .ReferencesColumn("id")
          .OnDeleteSetNull();
});
```

### 7.3 Deliverables Checklist

```
[ ] EF.Functions.GraphTraverse() translation
[ ] EF.Functions.ShortestPath() translation
[ ] HasColumnType("ROWREF") support
[ ] HasGraphRelationship() fluent config
[ ] Unit tests: 20+ covering LINQ → SQL translation
```

---

## 8. Testing Strategy

### Test Categories

| Category | Location | Count (estimated) | Framework |
|---|---|---|---|
| ROWREF unit tests | `tests/SharpCoreDB.Tests/Graph/RowRefTests.cs` | 30+ | xUnit |
| Traversal engine tests | `tests/SharpCoreDB.Tests/Graph/GraphTraversalEngineTests.cs` | 60+ | xUnit |
| Path finder tests | `tests/SharpCoreDB.Tests/Graph/PathFinderTests.cs` | 30+ | xUnit |
| SQL function tests | `tests/SharpCoreDB.Tests/Graph/GraphSqlFunctionTests.cs` | 50+ | xUnit |
| Hybrid query tests | `tests/SharpCoreDB.VectorSearch.Tests/GraphRagQueryTests.cs` | 40+ | xUnit |
| EF Core provider tests | `tests/SharpCoreDB.Tests/Graph/EfCoreGraphTests.cs` | 20+ | xUnit |
| Benchmark tests | `tests/SharpCoreDB.Benchmarks/GraphBenchmarks.cs` | 10+ | BenchmarkDotNet |

### Test Graph Topologies

```
1. Linear chain:     A → B → C → D → E
2. Tree:             A → {B, C}, B → {D, E}, C → {F, G}
3. DAG:              A → {B, C}, B → D, C → D (shared node)
4. Cyclic:           A → B → C → A
5. Star:             Center → {N1, N2, ..., N1000}
6. Dense mesh:       Every node connects to 10 random others
7. Disconnected:     Two separate components, verify isolation
8. Self-loop:        A → A (must not infinite-loop)
9. Large scale:      1M nodes, avg degree 5 (benchmark only)
```

### Key Test Scenarios

```
[ ] ROWREF insert with valid target → succeeds
[ ] ROWREF insert with invalid target → throws
[ ] ROWREF insert with NULL → stores sentinel
[ ] ROWREF cascade delete → removes dependents
[ ] BFS depth=0 → returns only start node
[ ] BFS depth=1 on tree → returns direct children
[ ] BFS on cyclic graph → terminates, no duplicates
[ ] DFS on deep chain (depth 1000) → no stack overflow (iterative)
[ ] PathFinder on disconnected graph → returns null
[ ] PathFinder on direct neighbor → returns [start, target]
[ ] GRAPH_TRAVERSE in SQL → correct row IDs returned
[ ] GRAPH_TRAVERSE with subquery start_node → works
[ ] Hybrid: vector + graph → filters correctly
[ ] Concurrent BFS reads → thread-safe
[ ] Cancellation mid-traversal → throws OperationCanceledException
```

---

## 9. Performance Targets and Benchmarks

### Baseline Targets

| Scenario | Target | Measurement Method |
|---|---|---|
| ROWREF single resolve | < 1 μs | BenchmarkDotNet micro-benchmark |
| ROWREF vs FK index lookup | ≥ 10x faster | Side-by-side comparison |
| BFS 10K nodes, depth 3 | < 5 ms | BenchmarkDotNet |
| BFS 100K nodes, depth 3 | < 20 ms | BenchmarkDotNet |
| BFS 1M nodes, depth 3 | < 100 ms | BenchmarkDotNet |
| DFS path finding (1M nodes) | < 50 ms | BenchmarkDotNet |
| Hybrid query (100K nodes, 768-dim) | < 200 ms | Integration benchmark |
| Memory: BFS 1M nodes | < 50 MB overhead | Memory profiler |

### Benchmark Infrastructure

**New file:** `tests/SharpCoreDB.Benchmarks/GraphBenchmarks.cs`

```csharp
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net10)]
public class GraphBenchmarks
{
    [Params(10_000, 100_000, 1_000_000)]
    public int NodeCount { get; set; }

    [Params(1, 3, 5)]
    public int MaxDepth { get; set; }

    [Benchmark(Baseline = true)]
    public HashSet<long> BfsTraversal() { ... }

    [Benchmark]
    public List<long> DfsTraversal() { ... }

    [Benchmark]
    public List<long>? ShortestPath() { ... }

    [Benchmark]
    public HashSet<long> ForeignKeyJoinEquivalent() { ... }
}
```

---

## 10. Migration and Backwards Compatibility

### Schema Compatibility

- `ROWREF` is a **new** column type. Existing schemas are unaffected.
- Databases created before v1.4.0 open without changes.
- `ROWREF` columns serialize as 8-byte long — same wire format as `DataType.Long`.
- Schema version bump in database header to indicate ROWREF support.

### API Compatibility

- All new APIs are **additive**. No existing method signatures change.
- `GraphTraversalEngine` is a new class — no impact on existing code.
- `GRAPH_TRAVERSE()` is a new SQL function — existing queries are unaffected.

### Upgrade Path

```
v1.3.0 → v1.4.0:
  - Drop-in DLL replacement
  - No schema migration needed
  - ROWREF columns can be added to existing tables via ALTER TABLE
  - Existing FK columns can be *converted* to ROWREF via:
      ALTER TABLE t ALTER COLUMN c SET DATA TYPE ROWREF;
    (preserves long values, changes type metadata only)
```

---

## 11. File Inventory

### New Files

| File | Phase | Purpose |
|---|---|---|
| `src/SharpCoreDB/Graph/GraphTraversalEngine.cs` | 2 | BFS/DFS traversal engine |
| `src/SharpCoreDB/Graph/AdjacencyListIndex.cs` | 2 | Optional in-memory adjacency cache |
| `src/SharpCoreDB/Graph/PathFinder.cs` | 2 | Shortest path / all paths algorithms |
| `src/SharpCoreDB/Graph/TraversalStrategy.cs` | 2 | Enum: BFS, DFS |
| `src/SharpCoreDB/Graph/GraphTraverseExpression.cs` | 3 | AST node for GRAPH_TRAVERSE() |
| `src/SharpCoreDB/Graph/GraphTraversalOptimizer.cs` | 3 | Cost estimation for graph traversal |
| `src/SharpCoreDB.VectorSearch/Optimization/HybridGraphVectorOptimizer.cs` | 4 | Vector + graph predicate ordering |
| `src/SharpCoreDB.VectorSearch/GraphRagQuery.cs` | 4 | Fluent API for hybrid queries |
| `tests/SharpCoreDB.Tests/Graph/RowRefTests.cs` | 1 | ROWREF column type tests |
| `tests/SharpCoreDB.Tests/Graph/GraphTraversalEngineTests.cs` | 2 | Traversal engine tests |
| `tests/SharpCoreDB.Tests/Graph/PathFinderTests.cs` | 2 | Path finding tests |
| `tests/SharpCoreDB.Tests/Graph/GraphSqlFunctionTests.cs` | 3 | SQL function tests |
| `tests/SharpCoreDB.VectorSearch.Tests/GraphRagQueryTests.cs` | 4 | Hybrid query tests |
| `tests/SharpCoreDB.Benchmarks/GraphBenchmarks.cs` | 2 | Performance benchmarks |

### Modified Files

| File | Phase | Change |
|---|---|---|
| `src/SharpCoreDB/DataTypes.cs` | 1 | Add `RowRef` enum value |
| `src/SharpCoreDB/DataStructures/Table.Serialization.cs` | 1 | Serialize/deserialize ROWREF |
| `src/SharpCoreDB/DataStructures/Table.cs` | 1 | ROWREF column validation |
| `src/SharpCoreDB/Services/EnhancedSqlParser.DDL.cs` | 1 | Parse ROWREF keyword |
| `src/SharpCoreDB/Services/EnhancedSqlParser.Expressions.cs` | 3 | Parse GRAPH_TRAVERSE() |
| `src/SharpCoreDB/Services/EnhancedSqlParser.Select.cs` | 3 | Handle in WHERE/IN |
| `src/SharpCoreDB/Planning/QueryOptimizer.cs` or `src/SharpCoreDB/Optimization/QueryOptimizer.cs` | 3 | Graph traversal cost model |
| `src/SharpCoreDB.EntityFrameworkCore/` (type mapping) | 1, 5 | ROWREF ↔ long mapping |
| `src/SharpCoreDB.VectorSearch/VectorQueryOptimizer.cs` | 4 | Hybrid predicate support |

---

## 12. Risk Register

| # | Risk | Probability | Impact | Mitigation | Owner |
|---|------|---|---|---|---|
| R1 | ROWREF dangling reference after row delete | Low | High | FK constraints + CASCADE already handle this | Phase 1 |
| R2 | Stack overflow on deep DFS | Low | Medium | Use iterative DFS with explicit stack (not recursion) | Phase 2 |
| R3 | Infinite loop on cyclic graph | Low | High | `HashSet<long>` visited set + hard depth limit | Phase 2 |
| R4 | Query plan explosion with graph predicates | Medium | High | Aggressive pruning + cardinality estimation caps | Phase 3 |
| R5 | Performance regression on non-graph queries | Low | High | Graph optimizer only activates when GRAPH_TRAVERSE present | Phase 3 |
| R6 | Memory pressure on 1M+ node graphs | Medium | Medium | ArrayPool + streaming traversal (yield results) | Phase 2 |
| R7 | EF Core LINQ translation complexity | Medium | Low | Defer full LINQ support to Phase 5; SQL API available earlier | Phase 5 |
| R8 | Serialization format ambiguity (ROWREF vs Long) | Low | Medium | Store type metadata in column schema; wire format identical | Phase 1 |

---

## 13. Milestones and Schedule

```
Phase 1: ROWREF Column Type               ━━━━━━━ (1.5 weeks)
  ├─ M1.1: DataType.RowRef + serialization     Day 1-3
  ├─ M1.2: Parser DDL support                  Day 3-5
  ├─ M1.3: Schema validation + FK integration  Day 5-7
  └─ M1.4: Unit tests + benchmark              Day 7-10

Phase 2: Graph Traversal Engine            ━━━━━━━━━━━━━ (3 weeks)
  ├─ M2.1: GraphTraversalEngine (BFS)          Day 1-5
  ├─ M2.2: GraphTraversalEngine (DFS)          Day 5-8
  ├─ M2.3: AdjacencyListIndex                  Day 8-12
  ├─ M2.4: PathFinder                          Day 12-16
  └─ M2.5: Tests + benchmarks                  Day 16-21

Phase 3: SQL Integration                   ━━━━━━━━━━ (2 weeks)
  ├─ M3.1: GRAPH_TRAVERSE() parser             Day 1-5
  ├─ M3.2: Execution plan integration          Day 5-8
  ├─ M3.3: Query optimizer extension           Day 8-11
  └─ M3.4: Additional functions + tests        Day 11-14

Phase 4: Hybrid Vector + Graph             ━━━━━━━━━━━━━ (3 weeks)
  ├─ M4.1: HybridGraphVectorOptimizer          Day 1-7
  ├─ M4.2: GraphRagQueryBuilder API             Day 7-14
  └─ M4.3: Integration tests + benchmarks      Day 14-21

Phase 5: EF Core + API Polish             ━━━━━━━━━━ (2 weeks)
  ├─ M5.1: EF.Functions.GraphTraverse()        Day 1-7
  ├─ M5.2: HasGraphRelationship() fluent API   Day 7-10
  └─ M5.3: Documentation + samples             Day 10-14

Total estimated: ~12 weeks engineering time
```

### Release Mapping

| Phase | Release | Quarter |
|---|---|---|
| Phase 1 + Phase 2 | v1.4.0 | Q3 2026 |
| Phase 3 | v1.5.0 | Q4 2026 |
| Phase 4 + Phase 5 | v1.6.0 | Q1 2027 |
| Production hardening | v2.0.0 | Q2 2027 |

---

## Appendix A: Competitor Reference

| Feature | SharpCoreDB (planned) | KùzuDB | SurrealDB | Neo4j |
|---|---|---|---|---|
| Embedded .NET | ✅ | ❌ (C++) | ❌ (Go) | ❌ (JVM) |
| Zero dependencies | ✅ | ❌ | ❌ | ❌ |
| Vector search | ✅ (HNSW) | ❌ | ❌ | ✅ (plugin) |
| Graph traversal | ✅ (ROWREF) | ✅ (native) | ✅ (Record Links) | ✅ (Cypher) |
| Hybrid vector+graph | ✅ | ❌ | ❌ | ⚠️ (complex) |
| Index-free adjacency | ✅ (ROWREF) | ✅ | ✅ | ✅ |
| Single DLL deployment | ✅ | ❌ | ❌ | ❌ |

## Appendix B: Glossary

| Term | Definition |
|---|---|
| **ROWREF** | Column type storing a direct row ID (long) pointing to a record in a target table. O(1) resolution. |
| **Index-free adjacency** | Graph traversal pattern where each node stores direct pointers to neighbors, avoiding index lookups. |
| **GraphRAG** | Retrieval-Augmented Generation combining vector similarity search with graph structural constraints. |
| **BFS** | Breadth-first search — explores all neighbors at current depth before moving deeper. |
| **DFS** | Depth-first search — explores as deep as possible before backtracking. |
| **HNSW** | Hierarchical Navigable Small World — approximate nearest neighbor index used for vector search. |
| **Adjacency list** | Data structure mapping each node to its list of neighbor node IDs. |
