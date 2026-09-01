# SharpCoreDB.EventSourcing

Event store primitives for `SharpCoreDB`.

**Version:** `v1.9.5`
**Package:** `SharpCoreDB.EventSourcing`


## Patch updates in v1.9.5

- ✅ **Parameterized query binding fixed** (Issue #336): named-parameter binding is token-aware, so parameter names that are prefixes of others (e.g. `@t` vs `@tid`) no longer corrupt the SQL.
- ✅ **Server parameter pass-through fixed** (Issue #337): `request.Parameters` are forwarded on gRPC, the binary (PostgreSQL) protocol and WebSocket.
- ✅ **ULID encoding is now standards-compliant**: ULIDs follow the official Crockford Base32 spec and are interchangeable with Python/Java/Go implementations.
- ✅ **NuGet dependencies updated** to their latest stable versions.
- ✅ Aligned package metadata and version references to the synchronized 1.9.5 release line.
- ✅ Release automation now publishes all packable SharpCoreDB packages in CI/CD.

## Features

- Append-only event streams with per-stream ordering
- Global ordered event feed for replay/catch-up
- In-memory and persistent (`SharpCoreDbEventStore`) implementations
- Snapshot persistence and snapshot-aware aggregate loading
- Optional upcasting pipeline support

## Changes in v1.9.5

- Package/docs synchronized to `v1.9.5`
- Snapshot and replay guidance clarified for production workflows
- Persistent and in-memory parity documented as first-class support

## Installation

```bash
dotnet add package SharpCoreDB.EventSourcing --version 2.0.0.0
```

## Related packages

- `SharpCoreDB.Projections`
- `SharpCoreDB.CQRS`

## Documentation

- `docs/INDEX.md`
- `src/SharpCoreDB.EventSourcing/NuGet.README.md`


