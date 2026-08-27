# SharpCoreDB NuGet Package

This package is part of SharpCoreDB, a high-performance embedded database for .NET 10.


## Patch updates in v1.9.5

- ✅ **Parameterized query binding fixed** (Issue #336): named-parameter binding is token-aware, so parameter names that are prefixes of others (e.g. `@t` vs `@tid`) no longer corrupt the SQL.
- ✅ **Server parameter pass-through fixed** (Issue #337): `request.Parameters` are forwarded on gRPC, the binary (PostgreSQL) protocol and WebSocket.
- ✅ **ULID encoding is now standards-compliant**: ULIDs follow the official Crockford Base32 spec and are interchangeable with Python/Java/Go implementations.
- ✅ **NuGet dependencies updated** to their latest stable versions.

For full documentation, see: https://github.com/MPCoreDeveloper/SharpCoreDB/blob/master/docs/INDEX.md

## Quick Start

See the main repository for usage examples.

# SharpCoreDB.Graph v1.9.5

**Lightweight Graph Traversal Engine**

A* pathfinding and graph algorithms **30-50% faster than alternatives** with pure managed C# 14 code.

## ✨ What's New in v1.9.5

- ✅ Inherits metadata improvements from SharpCoreDB v1.9.5
- ✅ Phase 6 complete: A* pathfinding with 30-50% improvement
- ✅ Lightweight graph traversal
- ✅ NativeAOT compatible
- ✅ Zero breaking changes

## 🚀 Key Features

- **A* Pathfinding**: Efficient shortest-path algorithms
- **Graph Traversal**: BFS, DFS, and custom traversal patterns
- **ROWREF Adjacency**: Natural foreign key relationships as graphs
- **Pure C# 14**: No external dependencies, NativeAOT ready
- **Performance**: 30-50% improvement over alternatives

## 🎯 Use Cases

- **Route Planning**: Find optimal paths through networks
- **Network Analysis**: Graph connectivity and patterns
- **Hierarchies**: Navigate tree-like data structures
- **Social Graphs**: Friend networks and connections
- **Supply Chains**: Trace product flows

## 📚 Documentation

- [Graph Overview](https://github.com/MPCoreDeveloper/SharpCoreDB/blob/master/docs/graph/README.md)
- [Full Documentation](https://github.com/MPCoreDeveloper/SharpCoreDB/blob/master/docs/INDEX.md)

## 📦 Installation

```bash
dotnet add package SharpCoreDB.Graph --version 1.9.5
```

**Requires:** SharpCoreDB v1.9.5+

---

**Version:** 1.9.5 | **Status:** ✅ Production Ready | **Phase:** 6 Complete




