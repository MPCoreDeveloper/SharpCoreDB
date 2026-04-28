# GraphRAG — Advanced Graph Analytics for SharpCoreDB

**Status:** ✅ Production Ready (`v1.7.2`)  
**Primary package:** `SharpCoreDB.Graph.Advanced`  
**Companion packages:** `SharpCoreDB.Graph`, `SharpCoreDB.VectorSearch`

---

## Overview

GraphRAG in SharpCoreDB combines semantic vector retrieval with graph analytics for context-aware ranking and discovery.

The current `v1.7.2` line is centered on these maintained building blocks:

- `SharpCoreDB.Graph.Advanced` for graph-aware ranking, community detection, centrality metrics, subgraph analysis, SQL integration, caching, and profiling support.
- `SharpCoreDB.Graph` for traversal, A* pathfinding, and graph query execution primitives.
- `SharpCoreDB.VectorSearch` for embeddings storage, similarity search, and semantic retrieval workflows.

For package-level mapping, see `../FEATURE_MATRIX_v1.7.2.md`.

---

## Current capabilities

### Graph analytics

- Community detection: Louvain, Label Propagation, Connected Components
- Centrality metrics: Degree, Betweenness, Closeness, Eigenvector, Clustering
- Subgraph analysis: K-core, clique, and triangle detection
- Graph-aware ranking and result profiling helpers

### GraphRAG execution paths

- `GRAPH_RAG` SQL support for single-statement semantic + graph retrieval
- Service registration helpers for DI-based provider wiring
- Cache-aware community computation and result reuse
- Metrics and observability hooks for profiling query behavior

### Integration guidance

- SQL-first workflow: `GRAPH_RAG_SINGLE_SQL.md`
- API-first workflow: `LINQ_API_GUIDE.md`
- EF Core workflow: `EF_CORE_COMPLETE_GUIDE.md`
- Telemetry and operations: `METRICS_AND_OBSERVABILITY_GUIDE.md`
- Query planning details: `QUERY_PLAN_CACHING.md`

---

## Quick start

```bash
dotnet add package SharpCoreDB.Graph.Advanced --version 1.7.2
dotnet add package SharpCoreDB.Graph --version 1.7.2
dotnet add package SharpCoreDB.VectorSearch --version 1.7.2
```

```csharp
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.Graph.Advanced.SqlIntegration;

var services = new ServiceCollection();
services.AddSharpCoreDB();
services.AddSharpCoreDBGraphRagSql(options =>
{
    options.GraphTableName = "graph_edges";
    options.EmbeddingTableName = "document_embeddings";
    options.EmbeddingDimensions = 16;
});
```

> Register a concrete `IDatabase`/`Database` instance before adding GraphRAG SQL integration so the provider can resolve the active database.

---

## Observability and performance

- Result caching is built into the GraphRAG engine to avoid repeated community recomputation.
- Metrics guidance is documented in `METRICS_AND_OBSERVABILITY_GUIDE.md`.
- Query-planning guidance is documented in `QUERY_PLAN_CACHING.md`.
- Use the maintained docs in `00_START_HERE.md` for current workflows; phase-design files are archival background only.

---

## Validation coverage

GraphRAG behavior is covered by targeted tests in the SharpCoreDB test suite, including:

- SQL parser and execution coverage
- DI registration coverage
- Provider request validation coverage
- Traversal, ranking, caching, and observability coverage

---

## Related docs

- `00_START_HERE.md`
- `GRAPH_RAG_SINGLE_SQL.md`
- `METRICS_AND_OBSERVABILITY_GUIDE.md`
- `../FEATURE_MATRIX_v1.7.2.md`
- `../../src/SharpCoreDB.Graph.Advanced/README.md`

