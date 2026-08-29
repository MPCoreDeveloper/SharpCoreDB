<div align="center">
  <img src="https://raw.githubusercontent.com/MPCoreDeveloper/SharpCoreDB/master/SharpCoreDB.jpg" alt="SharpCoreDB Logo" width="200"/>
  
  # SharpCoreDB
  
  High-performance encrypted embedded + network-capable database engine for .NET 10.

  **Version:** `v1.9.5`
  **Package:** `SharpCoreDB`

---


## Patch updates in v1.9.5

- ✅ **Parameterized query binding fixed** (Issue #336): named-parameter binding is token-aware, so parameter names that are prefixes of others (e.g. `@t` vs `@tid`) no longer corrupt the SQL.
- ✅ **Server parameter pass-through fixed** (Issue #337): `request.Parameters` are forwarded on gRPC, the binary (PostgreSQL) protocol and WebSocket.
- ✅ **ULID encoding is now standards-compliant**: ULIDs follow the official Crockford Base32 spec and are interchangeable with Python/Java/Go implementations.
- ✅ **NuGet dependencies updated** to their latest stable versions.
- ✅ Aligned package metadata and version references to the synchronized 1.9.5 release line.
- ✅ Release automation now publishes all packable SharpCoreDB packages in CI/CD.
- SQL engine with ACID transactions, WAL, B-tree/hash indexing, and FTS
- SIMD-optimized execution paths (including `Vector256.LoadUnsafe` hot paths)
- Query plan caching, prepared statements, and compiled-query support
- Works as the core engine behind server, analytics, vector, graph, and distributed packages

---

## Changes in v1.9.5

- Synchronized ecosystem release to `v1.9.5`
- SQL lexer/parser fixes for parameterized compiled-query execution
- Metadata durability improvements (flush/reopen reliability)
- Backward-compatible Brotli metadata support
- Validated with 1,490+ tests and no intended breaking changes from v1.5.0

---

## Installation

```bash
dotnet add package SharpCoreDB --version 2.0.0
```

---

## Related packages

- `SharpCoreDB.Server` / `SharpCoreDB.Client`
- `SharpCoreDB.Analytics`
- `SharpCoreDB.VectorSearch`
- `SharpCoreDB.Graph` / `SharpCoreDB.Graph.Advanced`
- `SharpCoreDB.EventSourcing` / `SharpCoreDB.Projections` / `SharpCoreDB.CQRS`

---

## Documentation

- `docs/INDEX.md`
- `docs/README.md`

---

## License

MIT License - Free for commercial and personal use. See [LICENSE](../../LICENSE)

---

**Last Updated:** July 28, 2026 | Version: 1.9.5

*Made with ❤️ by the SharpCoreDB team*




