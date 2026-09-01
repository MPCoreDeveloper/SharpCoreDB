# SharpCoreDB.Projections v1.9.5

Projection primitives for `SharpCoreDB.EventSourcing`.


## Patch updates in v1.9.5

- ✅ **Parameterized query binding fixed** (Issue #336): named-parameter binding is token-aware, so parameter names that are prefixes of others (e.g. `@t` vs `@tid`) no longer corrupt the SQL.
- ✅ **Server parameter pass-through fixed** (Issue #337): `request.Parameters` are forwarded on gRPC, the binary (PostgreSQL) protocol and WebSocket.
- ✅ **ULID encoding is now standards-compliant**: ULIDs follow the official Crockford Base32 spec and are interchangeable with Python/Java/Go implementations.
- ✅ **NuGet dependencies updated** to their latest stable versions.
- ✅ Aligned package metadata and version references to the synchronized 1.9.5 release line.
- ✅ Release automation now publishes all packable SharpCoreDB packages in CI/CD.

## Features


- Projection registration and execution scaffolding
- Inline and background projection runners
- In-memory and persistent checkpoint stores
- Hosted worker orchestration
- OpenTelemetry-ready projection metrics

## Changes in v1.9.5

- Package/docs synchronized to `v1.9.5`
- Durable checkpointing and worker guidance clarified

## Installation

```bash
dotnet add package SharpCoreDB.Projections --version 2.0.0.0
```

## Documentation

- `docs/INDEX.md`
- `src/SharpCoreDB.Projections/README.md`



