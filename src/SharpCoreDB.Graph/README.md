# SharpCoreDB.Graph

Graph traversal and pathfinding extension for `SharpCoreDB`.

**Version:** `v1.9.5`
**Package:** `SharpCoreDB.Graph`


## Patch updates in v1.9.5

- ✅ **Parameterized query binding fixed** (Issue #336): named-parameter binding is token-aware, so parameter names that are prefixes of others (e.g. `@t` vs `@tid`) no longer corrupt the SQL.
- ✅ **Server parameter pass-through fixed** (Issue #337): `request.Parameters` are forwarded on gRPC, the binary (PostgreSQL) protocol and WebSocket.
- ✅ **ULID encoding is now standards-compliant**: ULIDs follow the official Crockford Base32 spec and are interchangeable with Python/Java/Go implementations.
- ✅ **NuGet dependencies updated** to their latest stable versions.
- ✅ Aligned package metadata and version references to the synchronized 1.9.5 release line.
- ✅ Release automation now publishes all packable SharpCoreDB packages in CI/CD.
- A* pathfinding for shortest-path workloads
- SQL integration via graph traversal helpers
- Traversal strategy optimization and metrics components
- Hybrid graph/vector optimization integration points

## Changes in v1.9.5

- Package release aligned to `v1.9.5`
- Documentation updated to current graph package scope
- Inherits v1.9.5 core parser/durability improvements
- Companion package `SharpCoreDB.Graph.Advanced` added for analytics + GraphRAG workflows

## Installation

```bash
dotnet add package SharpCoreDB.Graph --version 2.0.0
```

## Documentation

- `docs/INDEX.md`
- `src/SharpCoreDB.Graph.Advanced/README.md`



