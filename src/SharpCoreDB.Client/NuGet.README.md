# SharpCoreDB.Client v1.9.5

.NET client library for `SharpCoreDB.Server`.


## Patch updates in v1.9.5

- ✅ **Parameterized query binding fixed** (Issue #336): named-parameter binding is token-aware, so parameter names that are prefixes of others (e.g. `@t` vs `@tid`) no longer corrupt the SQL.
- ✅ **Server parameter pass-through fixed** (Issue #337): `request.Parameters` are forwarded on gRPC, the binary (PostgreSQL) protocol and WebSocket.
- ✅ **ULID encoding is now standards-compliant**: ULIDs follow the official Crockford Base32 spec and are interchangeable with Python/Java/Go implementations.
- ✅ **NuGet dependencies updated** to their latest stable versions.
- ✅ Aligned package metadata and version references to the synchronized 1.9.5 release line.
- ✅ Release automation now publishes all packable SharpCoreDB packages in CI/CD.

## Features


- ADO.NET-style connection/command/data-reader API
- Async query and command execution
- Parameterized command support
- gRPC-first connectivity model for server deployments

## Changes in v1.9.5

- Package/docs aligned to the synchronized `v1.9.5` release line
- Client guidance updated for current server protocol/security defaults

## Installation

```bash
dotnet add package SharpCoreDB.Client --version 2.0.0
```

## Documentation

- `docs/INDEX.md`
- `docs/server/CLIENT_GUIDE.md`



