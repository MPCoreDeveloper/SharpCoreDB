# SharpCoreDB.VectorSearch

SIMD-accelerated vector similarity search for `SharpCoreDB`.

**Version:** `v1.9.5`
**Package:** `SharpCoreDB.VectorSearch`


## Patch updates in v1.9.5

- ✅ **Parameterized query binding fixed** (Issue #336): named-parameter binding is token-aware, so parameter names that are prefixes of others (e.g. `@t` vs `@tid`) no longer corrupt the SQL.
- ✅ **Server parameter pass-through fixed** (Issue #337): `request.Parameters` are forwarded on gRPC, the binary (PostgreSQL) protocol and WebSocket.
- ✅ **ULID encoding is now standards-compliant**: ULIDs follow the official Crockford Base32 spec and are interchangeable with Python/Java/Go implementations.
- ✅ **NuGet dependencies updated** to their latest stable versions.
- ✅ Aligned package metadata and version references to the synchronized 1.9.5 release line.
- ✅ Release automation now publishes all packable SharpCoreDB packages in CI/CD.
- Distance metrics and vector query optimization
- Quantization support (scalar and binary)
- Vector serialization/storage support
- Designed for semantic search and RAG pipelines on .NET 10

## Changes in v1.9.5

- Package/docs aligned to `v1.9.5`
- Documentation refreshed for production vector-search workflows
- Inherits core parser/metadata durability improvements
- No intended breaking changes from v1.5.0

## Installation

```bash
dotnet add package SharpCoreDB.VectorSearch --version 2.0.0.0
```

## Documentation

- `docs/INDEX.md`
- `docs/vectors/README.md`



