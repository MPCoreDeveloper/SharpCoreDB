# SharpCoreDB.Graph.Advanced

Advanced graph analytics and GraphRAG package for `SharpCoreDB`.

**Version:** `v1.8.0`  
**Package:** `SharpCoreDB.Graph.Advanced`


## Patch updates in v1.8.0

- ✅ Aligned package metadata and version references to the synchronized 1.8.0 release line.
- ✅ Release automation now publishes all packable SharpCoreDB packages in CI/CD.

## Features

- Community detection: Louvain, Label Propagation, Connected Components
- Centrality metrics: Degree, Betweenness, Closeness, Eigenvector, Clustering
- Subgraph analysis: K-core, clique, and triangle detection
- Graph-aware ranking for GraphRAG workflows
- SQL integration helpers, result caching, and profiling support

## What's new in v1.8.0

- Advanced graph analytics package aligned with the `v1.8.0` ecosystem release line
- Maintained GraphRAG SQL registration guidance for DI-based applications
- Documentation consolidated around current graph, vector, and observability workflows

## Installation

```bash
dotnet add package SharpCoreDB.Graph.Advanced --version 1.8.0
```

## Quick start

```csharp
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.Graph.Advanced.SqlIntegration;
using SharpCoreDB.Interfaces;

var services = new ServiceCollection();
services.AddSharpCoreDB();
services.AddSingleton<IDatabase>(sp =>
    sp.GetRequiredService<DatabaseFactory>().Create("./graph.scdb", "StrongPassword!"));

services.AddSharpCoreDBGraphRagSql(options =>
{
    options.GraphTableName = "graph_edges";
    options.EmbeddingTableName = "document_embeddings";
    options.EmbeddingDimensions = 16;
});
```

## Maintained docs

- `../../docs/graphrag/00_START_HERE.md`
- `../../docs/graphrag/README.md`
- `../../docs/graphrag/GRAPH_RAG_SINGLE_SQL.md`
- `../../docs/graphrag/METRICS_AND_OBSERVABILITY_GUIDE.md`
- `NuGet.README.md`

## Notes

- Register a concrete `Database`/`IDatabase` before calling `AddSharpCoreDBGraphRagSql(...)`.
- Keep GraphRAG guidance in the maintained docs above instead of adding phase-specific duplicate package notes.

