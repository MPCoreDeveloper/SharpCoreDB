# SharpCoreDB.Distributed

Distributed capabilities extension for `SharpCoreDB`.

**Version:** `v1.9.5`
**Package:** `SharpCoreDB.Distributed`


## Patch updates in v1.9.5

- ✅ **Parameterized query binding fixed** (Issue #336): named-parameter binding is token-aware, so parameter names that are prefixes of others (e.g. `@t` vs `@tid`) no longer corrupt the SQL.
- ✅ **Server parameter pass-through fixed** (Issue #337): `request.Parameters` are forwarded on gRPC, the binary (PostgreSQL) protocol and WebSocket.
- ✅ **ULID encoding is now standards-compliant**: ULIDs follow the official Crockford Base32 spec and are interchangeable with Python/Java/Go implementations.
- ✅ **NuGet dependencies updated** to their latest stable versions.
- ✅ Aligned package metadata and version references to the synchronized 1.9.5 release line.
- ✅ Release automation now publishes all packable SharpCoreDB packages in CI/CD.
- Distributed transaction components (2PC-oriented)
- Sharding and shard-routing abstractions
- Replication streaming coordination and monitoring primitives

## Changes in v1.9.5

- Package/docs synchronized to `v1.9.5`
- Distributed feature set aligned with current replication/transaction modules
- Documentation updated for enterprise distributed scenarios

## Installation

```bash
dotnet add package SharpCoreDB.Distributed --version 2.0.0
```

## Documentation

- `docs/INDEX.md`
- `src/SharpCoreDB.Distributed/NuGet.README.md`



