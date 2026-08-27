# SharpCoreDB.EventSourcing v1.9.5

Event store primitives for `SharpCoreDB`.


## Patch updates in v1.9.5

- ✅ **Parameterized query binding fixed** (Issue #336): named-parameter binding is token-aware, so parameter names that are prefixes of others (e.g. `@t` vs `@tid`) no longer corrupt the SQL.
- ✅ **Server parameter pass-through fixed** (Issue #337): `request.Parameters` are forwarded on gRPC, the binary (PostgreSQL) protocol and WebSocket.
- ✅ **ULID encoding is now standards-compliant**: ULIDs follow the official Crockford Base32 spec and are interchangeable with Python/Java/Go implementations.
- ✅ **NuGet dependencies updated** to their latest stable versions.
- ✅ Aligned package metadata and version references to the synchronized 1.9.5 release line.
- ✅ Release automation now publishes all packable SharpCoreDB packages in CI/CD.

## Features


- Append-only per-stream events with ordered sequences
- Global ordered event feed
- In-memory and persistent event store implementations
- Snapshot persistence and snapshot-aware loading
- Optional upcasting pipeline for schema evolution

## Changes in v1.9.5

- Package/docs synchronized to `v1.9.5`
- Production guidance clarified for snapshots and replay workflows

## Installation

```bash
dotnet add package SharpCoreDB.EventSourcing --version 1.9.5
```

## Documentation

- `docs/INDEX.md`
- `src/SharpCoreDB.EventSourcing/README.md`



