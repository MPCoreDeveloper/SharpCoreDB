# SharpCoreDB.Provider.Sync

Dotmim.Sync provider package for `SharpCoreDB`.

**Version:** `v1.9.5`
**Package:** `SharpCoreDB.Provider.Sync`


## Patch updates in v1.9.5

- ✅ **Parameterized query binding fixed** (Issue #336): named-parameter binding is token-aware, so parameter names that are prefixes of others (e.g. `@t` vs `@tid`) no longer corrupt the SQL.
- ✅ **Server parameter pass-through fixed** (Issue #337): `request.Parameters` are forwarded on gRPC, the binary (PostgreSQL) protocol and WebSocket.
- ✅ **ULID encoding is now standards-compliant**: ULIDs follow the official Crockford Base32 spec and are interchangeable with Python/Java/Go implementations.
- ✅ **NuGet dependencies updated** to their latest stable versions.
- ✅ Aligned package metadata and version references to the synchronized 1.9.5 release line.
- ✅ Release automation now publishes all packable SharpCoreDB packages in CI/CD.
- Change tracking and tombstone management components
- Sync metadata/schema support for SharpCoreDB
- Builder/adaptor abstractions for sync pipelines

## Changes in v1.9.5

- Package/docs synchronized to `v1.9.5`
- Documentation aligned with current provider components
- Guidance updated for modern sync and local-first scenarios

## Installation

```bash
dotnet add package SharpCoreDB.Provider.Sync --version 2.0.0
```

## Documentation

- `docs/INDEX.md`
- `src/SharpCoreDB.Provider.Sync/NuGet.README.md`



