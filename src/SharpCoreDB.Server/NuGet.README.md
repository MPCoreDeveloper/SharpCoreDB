# SharpCoreDB.Server v1.9.5

Network database server package for `SharpCoreDB`.


## Patch updates in v1.9.5

- ✅ **Parameterized query binding fixed** (Issue #336): named-parameter binding is token-aware, so parameter names that are prefixes of others (e.g. `@t` vs `@tid`) no longer corrupt the SQL.
- ✅ **Server parameter pass-through fixed** (Issue #337): `request.Parameters` are forwarded on gRPC, the binary (PostgreSQL) protocol and WebSocket.
- ✅ **ULID encoding is now standards-compliant**: ULIDs follow the official Crockford Base32 spec and are interchangeable with Python/Java/Go implementations.
- ✅ **NuGet dependencies updated** to their latest stable versions.
- ✅ Aligned package metadata and version references to the synchronized 1.9.5 release line.
- ✅ Release automation now publishes all packable SharpCoreDB packages in CI/CD.

## Features


- gRPC-first protocol stack (HTTP/2 + HTTP/3)
- HTTPS REST API and WebSocket streaming support
- JWT, RBAC, TLS 1.2+, and optional mTLS
- Multi-database hosting and production operations hooks
- Health checks, metrics, and deployment options (Docker/services)

## Changes in v1.9.5

- Package/docs synchronized to `v1.9.5`
- Server documentation updated to current production feature set
- Client/SDK references aligned with current ecosystem packages

## Installation

```bash
dotnet add package SharpCoreDB.Server --version 1.9.5
```

## Documentation

- `docs/INDEX.md`
- `docs/server/README.md`
- `docs/server/QUICKSTART.md`



