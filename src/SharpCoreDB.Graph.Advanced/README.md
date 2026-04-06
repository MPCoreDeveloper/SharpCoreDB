# SharpCoreDB.Graph.Advanced

**Version:** 1.7.0  
**Status:** ✅ Production Ready

**Target Framework:** .NET 10 / C# 14  
**Package:** `SharpCoreDB.Graph.Advanced` v1.7.0

---

## Overview

`SharpCoreDB.Graph.Advanced` is the analytics layer on top of `SharpCoreDB.Graph`.

### Difference Between `SharpCoreDB.Graph` and `SharpCoreDB.Graph.Advanced`

- `SharpCoreDB.Graph` = core graph execution package
  - traversal and pathfinding
  - BFS, DFS, bidirectional traversal, A*
  - graph query execution and traversal optimization
  - use this when you need to move through a graph or find routes/paths

- `SharpCoreDB.Graph.Advanced` = higher-level analytics and GraphRAG package
  - community detection: Louvain, Label Propagation, Connected Components
  - centrality metrics: Degree, Betweenness, Closeness, Eigenvector, Clustering Coefficient
  - subgraph analysis: K-core, Clique detection, Triangle detection
  - SQL integration layer for graph analytics workflows
  - GraphRAG ranking with vector-search integration
  - result caching and profiling helpers
  - use this when you need graph analysis, ranking, graph intelligence, or semantic graph retrieval

In short:
- choose `SharpCoreDB.Graph` for graph traversal and pathfinding
- choose `SharpCoreDB.Graph.Advanced` for graph analytics and GraphRAG
- use both together when you need execution plus analytics

---

## Installation

```bash
dotnet add package SharpCoreDB.Graph.Advanced --version 1.7.0
```

### Dependencies

`SharpCoreDB.Graph.Advanced` builds on:

- `SharpCoreDB`
- `SharpCoreDB.Graph`
- `SharpCoreDB.VectorSearch`

---

## Typical Use Cases

- Knowledge graph retrieval with semantic ranking
- Social network community analysis
- Influence and centrality scoring
- Dense subgraph detection
- Graph-aware recommendation pipelines

---

## Documentation

- Release notes: `docs/release/PHASE12_RELEASE_NOTES.md`
- API reference: `docs/api/SharpCoreDB.Graph.Advanced.API.md`
- Repository index: `docs/INDEX.md`

---

**Version:** 1.7.0 | **Package:** `SharpCoreDB.Graph.Advanced` | **Phase:** 12 Complete
