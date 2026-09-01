# SharpCoreDB.Server

Network database server package for `SharpCoreDB`.

**Version:** `v1.9.5`
**Package:** `SharpCoreDB.Server`


## Patch updates in v1.9.5

- ✅ **Parameterized query binding fixed** (Issue #336): named-parameter binding is token-aware, so parameter names that are prefixes of others (e.g. `@t` vs `@tid`) no longer corrupt the SQL.
- ✅ **Server parameter pass-through fixed** (Issue #337): `request.Parameters` are forwarded on gRPC, the binary (PostgreSQL) protocol and WebSocket.
- ✅ **ULID encoding is now standards-compliant**: ULIDs follow the official Crockford Base32 spec and are interchangeable with Python/Java/Go implementations.
- ✅ **NuGet dependencies updated** to their latest stable versions.
- ✅ Aligned package metadata and version references to the synchronized 1.9.5 release line.
- ✅ Release automation now publishes all packable SharpCoreDB packages in CI/CD.
- HTTPS REST API and WebSocket streaming support
- JWT auth, RBAC, TLS 1.2+, optional mTLS
- Multi-database hosting and production operations support

## Changes in v1.9.5

- Package/docs synchronized to `v1.9.5`
- Documentation aligned to production server capabilities
- Companion client/SDK guidance updated for current release line

## Installation

```bash
dotnet add package SharpCoreDB.Server --version 2.0.0.0
```

## Documentation

- `docs/INDEX.md`
- `docs/server/README.md`
- `src/SharpCoreDB.Server/NuGet.README.md`



