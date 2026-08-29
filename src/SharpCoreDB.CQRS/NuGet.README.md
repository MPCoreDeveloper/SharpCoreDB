# SharpCoreDB.CQRS v1.9.5

CQRS and outbox primitives for `SharpCoreDB`.


## Patch updates in v1.9.5

- ✅ **Parameterized query binding fixed** (Issue #336): named-parameter binding is token-aware, so parameter names that are prefixes of others (e.g. `@t` vs `@tid`) no longer corrupt the SQL.
- ✅ **Server parameter pass-through fixed** (Issue #337): `request.Parameters` are forwarded on gRPC, the binary (PostgreSQL) protocol and WebSocket.
- ✅ **ULID encoding is now standards-compliant**: ULIDs follow the official Crockford Base32 spec and are interchangeable with Python/Java/Go implementations.
- ✅ **NuGet dependencies updated** to their latest stable versions.
- ✅ Aligned package metadata and version references to the synchronized 1.9.5 release line.
- ✅ Release automation now publishes all packable SharpCoreDB packages in CI/CD.

## Features


- Command contracts and handler abstractions
- In-memory and DI-backed command dispatching
- Aggregate root base with pending event collection
- In-memory/persistent outbox stores
- Retry/dead-letter-capable outbox dispatch worker support

## Changes in v1.9.5

- Package/docs synchronized to `v1.9.5`
- Outbox reliability guidance expanded (retry + dead-letter + worker flow)

## Installation

```bash
dotnet add package SharpCoreDB.CQRS --version 2.0.0
```

## Documentation

- `docs/INDEX.md`
- `src/SharpCoreDB.CQRS/README.md`



