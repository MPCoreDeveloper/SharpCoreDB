# SharpCoreDB.Graph.Advanced v1.9.5

Advanced graph analytics and GraphRAG package for `SharpCoreDB`.


## Patch updates in v1.9.5

- ✅ **Parameterized query binding fixed** (Issue #336): named-parameter binding is token-aware, so parameter names that are prefixes of others (e.g. `@t` vs `@tid`) no longer corrupt the SQL.
- ✅ **Server parameter pass-through fixed** (Issue #337): `request.Parameters` are forwarded on gRPC, the binary (PostgreSQL) protocol and WebSocket.
- ✅ **ULID encoding is now standards-compliant**: ULIDs follow the official Crockford Base32 spec and are interchangeable with Python/Java/Go implementations.
- ✅ **NuGet dependencies updated** to their latest stable versions.
- ✅ Aligned package metadata and version references to the synchronized 1.9.5 release line.
- ✅ Release automation now publishes all packable SharpCoreDB packages in CI/CD.

## Features


- Community detection (Louvain, Label Propagation, Connected Components)
- Centrality metrics (degree, betweenness, closeness, eigenvector, clustering)
- Subgraph analysis (K-core, clique, triangle)
- Graph-aware semantic ranking and profiling helpers
- SQL integration for graph analytics workflows

## Changes in v1.9.5

- Advanced package delivered as part of the synchronized `v1.9.5` release
- Documentation aligned for GraphRAG + analytics usage patterns

## Installation

```bash
dotnet add package SharpCoreDB.Graph.Advanced --version 2.0.0.0
```

## Documentation

- `docs/INDEX.md`
- `docs/graphrag/00_START_HERE.md`



