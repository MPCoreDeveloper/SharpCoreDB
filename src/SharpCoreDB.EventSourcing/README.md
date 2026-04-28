# SharpCoreDB.EventSourcing

Event store primitives for `SharpCoreDB`.

**Version:** `v1.7.2`  
**Package:** `SharpCoreDB.EventSourcing`


## Patch updates in v1.7.2

- ✅ Aligned package metadata and version references to the synchronized 1.7.2 release line.
- ✅ Release automation now publishes all packable SharpCoreDB packages in CI/CD.

## Features

- Append-only event streams with per-stream ordering
- Global ordered event feed for replay/catch-up
- In-memory and persistent (`SharpCoreDbEventStore`) implementations
- Snapshot persistence and snapshot-aware aggregate loading
- Optional upcasting pipeline support

## Changes in v1.7.2

- Package/docs synchronized to `v1.7.2`
- Snapshot and replay guidance clarified for production workflows
- Persistent and in-memory parity documented as first-class support

## Installation

```bash
dotnet add package SharpCoreDB.EventSourcing --version 1.7.2
```

## Related packages

- `SharpCoreDB.Projections`
- `SharpCoreDB.CQRS`

## Documentation

- `docs/INDEX.md`
- `src/SharpCoreDB.EventSourcing/NuGet.README.md`

