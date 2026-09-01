# SharpCoreDB.Projections

Projection primitives for `SharpCoreDB.EventSourcing`.

**Version:** `v1.9.5`
**Package:** `SharpCoreDB.Projections`


## Patch updates in v1.9.5

- ✅ **Parameterized query binding fixed** (Issue #336): named-parameter binding is token-aware, so parameter names that are prefixes of others (e.g. `@t` vs `@tid`) no longer corrupt the SQL.
- ✅ **Server parameter pass-through fixed** (Issue #337): `request.Parameters` are forwarded on gRPC, the binary (PostgreSQL) protocol and WebSocket.
- ✅ **ULID encoding is now standards-compliant**: ULIDs follow the official Crockford Base32 spec and are interchangeable with Python/Java/Go implementations.
- ✅ **NuGet dependencies updated** to their latest stable versions.
- ✅ Aligned package metadata and version references to the synchronized 1.9.5 release line.
- ✅ Release automation now publishes all packable SharpCoreDB packages in CI/CD.
- Inline and background projection runners
- Durable checkpointing (in-memory and SharpCoreDB-backed stores)
- Hosted background worker support
- OpenTelemetry-ready projection metrics

## Changes in v1.9.5

- Package/docs synchronized to `v1.9.5`
- Durable checkpoint and worker guidance clarified
- Projection metrics guidance aligned with current implementation

## Installation

```bash
dotnet add package SharpCoreDB.Projections --version 2.0.0.0
```

## Related packages

- `SharpCoreDB.EventSourcing`
- `SharpCoreDB.CQRS`

## Documentation

- `docs/INDEX.md`
- `src/SharpCoreDB.Projections/NuGet.README.md`



