# GraphRAG Implementation Startpoint

**Date:** 2026-02-15  
**Status:** Draft — Engineering Startpoint (Git backup)  
**Scope:** Architecture decision record + initial implementation direction  
**Audience:** Core engine engineers, extension developers

**Status Update (2025-02-15):** ROWREF data type + serialization are implemented. `GraphTraversalEngine` provides BFS/DFS traversal (including edge-table traversal). `GRAPH_TRAVERSE()` SQL evaluation and EF Core LINQ translation are implemented. Adjacency caching, path finding, and traversal optimization remain planned.

---

## 1. Decision Summary

GraphRAG is implemented as a **two-layer design** to avoid breaking existing users and to keep the base engine lean:

1. **Core Engine (`SharpCoreDB`) — minimal plumbing**
   - Adds `DataType.RowRef` for index-free adjacency.
   - Adds type parsing + serialization for `ROWREF`.
   - Adds extension points for graph traversal.

2. **Graph Extension (`SharpCoreDB.Graph`) — graph engine**
   - Contains traversal algorithms (BFS/DFS).
   - Registers SQL functions such as `GRAPH_TRAVERSE()` via `ICustomFunctionProvider`.
   - Registers traversal services via `IGraphTraversalProvider`.

This matches the existing extension pattern used by `SharpCoreDB.VectorSearch` and guarantees **zero impact** for users who do not opt in.

---

## 2. Why This Does Not Break Existing Users

- **RowRef is additive:** a new enum value in `DataType`. Existing schemas never emit it.
- **Optional features:** graph logic lives in a separate NuGet package.
- **DI-registered:** graph support activates only when `AddGraphSupport()` is called.
- **SQL parser:** `ROWREF` keyword is new but does not affect existing SQL grammar.
- **No behavior changes:** no existing API is modified; only new types and extension points are added.

---

## 3. Core Engine Changes (Minimal)

| Area | File | Change |
|---|---|---|
| Data type | `src/SharpCoreDB/DataTypes.cs` | Add `DataType.RowRef` enum value |
| Serialization | `src/SharpCoreDB/DataStructures/Table.Serialization.cs` | Serialize/deserialize `RowRef` as 8-byte `long` |
| DDL parser | `src/SharpCoreDB/Services/EnhancedSqlParser.DDL.cs` | Recognize `ROWREF` keyword |
| Extension point | `src/SharpCoreDB/Interfaces/IGraphTraversalProvider.cs` | New optional interface |

**Expected net change:** ~200 LOC, no behavioral impact when unused.

---

## 4. Graph Extension Package (`SharpCoreDB.Graph`)

### Project Goals

- Provide traversal algorithms with zero external dependencies.
- Use C# 14 and zero-allocation patterns (ArrayPool, Span, Lock).
- Integrate with SQL through `ICustomFunctionProvider`.

### Current Class Layout

| File | Role | Status |
|---|---|---|
| `GraphSearchExtensions.cs` | `AddGraphSupport()` DI registration | Implemented |
| `GraphSearchOptions.cs` | Configuration: default max depth, cache options | Implemented |
| `GraphFunctionProvider.cs` | Exposes `GRAPH_TRAVERSE()` | Implemented |
| `GraphTraversalEngine.cs` | BFS/DFS traversal implementation | Implemented |
| `AdjacencyListIndex.cs` | Optional in-memory adjacency cache | Planned |
| `PathFinder.cs` | Shortest path and reachability | Planned |
| `GraphTraversalOptimizer.cs` | Cost estimation for traversal predicates | Planned |

---

## 5. Integration Pattern (Matches VectorSearch)

```csharp
services.AddSharpCoreDB();
services.AddGraphSupport(); // Registers graph components via DI
```

- Core engine queries `ICustomFunctionProvider` and `IGraphTraversalProvider` only when registered.
- If not registered, graph-related SQL functions are treated as unsupported.

---

## 6. Target SQL Surface (Phase 2+)

```sql
GRAPH_TRAVERSE(table, start_node, relationship_column, max_depth [, strategy])
SHORTEST_PATH(table, start_node, end_node, relationship_column [, max_depth])
IS_REACHABLE(table, start_node, end_node, relationship_column [, max_depth])
```

---

## 7. Initial Work Items (Start Here)

1. Add `DataType.RowRef` to `DataTypes.cs`.
2. Add serialization for `RowRef` in `Table.Serialization.cs`.
3. Add DDL parsing for `ROWREF` in `EnhancedSqlParser.DDL.cs`.
4. Add `IGraphTraversalProvider` interface.
5. Create `SharpCoreDB.Graph` project with minimal DI registration and placeholders.

---

## 8. Current Progress Snapshot

- ✅ `DataType.RowRef` and serialization support added in core.
- ✅ ROWREF parsing and validation hooks added in DDL parsing.
- ✅ `IGraphTraversalProvider` interface added for graph extension points.
- ✅ `SharpCoreDB.Graph` project scaffolding created.
- ✅ `GraphTraversalEngine` scaffolded with BFS/DFS validation and traversal logic.
- ✅ **Phase 1 Integration:** SQL AST extended with `GraphTraverseNode`.
- ✅ **Phase 1 Integration:** GRAPH_TRAVERSE parsing in `EnhancedSqlParser.Expressions.cs`.
- ✅ **Phase 1 Integration:** Visitor pattern support for `GraphTraverseNode` in query execution.
- ✅ **Phase 2 Integration:** Edge-table traversal support (`TraverseUsingEdgeTable`).
- ✅ **Phase 3 Integration:** Hybrid graph-vector optimizer with execution order hints.

---

## 9. Implementation Status by Phase

### Phase 1: Core ROWREF Graph (Complete ✅)
- ROWREF column type with FK validation
- BFS/DFS graph traversal engine
- `GRAPH_TRAVERSE()` function registration
- SQL AST and parser support

### Phase 2: Edge Table Support (Complete ✅)
- External edge table traversal (source → target)
- BFS/DFS with edge tables
- Fallback for non-ROWREF scenarios

### Phase 3: Hybrid Optimization (In Progress)
- Graph + vector query detection
- Execution order hints
- Planned: query plan reordering

### Phase 4: EF Core Translation (Planned)
- `EF.Functions.GraphTraverse()` support
- LINQ-to-SQL translation
- Provider-specific optimizations

---

## 10. Git Backup Context

This document is the **engineering startpoint** and should be committed before implementation begins. It provides architectural intent and preserves the initial plan for reference during reviews.
