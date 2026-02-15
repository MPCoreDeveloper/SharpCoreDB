# SharpCoreDB.Graph

**Status:** Early scaffolding (GraphRAG Phase 1)  
**Target Framework:** .NET 10 / C# 14  
**Package:** `SharpCoreDB.Graph`

---

## Overview

`SharpCoreDB.Graph` provides lightweight graph traversal capabilities for SharpCoreDB based on `ROWREF` adjacency. It is designed to be optional and **zero-impact** for existing SharpCoreDB users.

This package is the foundation for GraphRAG support (vector + graph hybrid queries).

---

## Key Goals

- **Index-free adjacency:** `ROWREF` columns store direct row pointers.
- **Traversal primitives:** BFS / DFS, shortest path, reachability.
- **SQL integration:** `GRAPH_TRAVERSE()` and related functions.
- **Zero dependencies:** Pure managed C# 14, NativeAOT compatible.

---

## Usage (Planned)

```csharp
services.AddSharpCoreDB();
services.AddGraphSupport();
```

---

## Status

This project currently provides **scaffolding only**. Functional traversal logic will be added in subsequent phases.
