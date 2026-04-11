# SharpCoreDB.EventSourcing

Event store primitives for `SharpCoreDB`.

**Version:** `v1.7.0`  
**Package:** `SharpCoreDB.EventSourcing`

## Features

- Append-only event streams with per-stream ordering
- Global ordered event feed for replay/catch-up
- In-memory and persistent (`SharpCoreDbEventStore`) implementations
- Snapshot persistence and snapshot-aware aggregate loading
- Optional upcasting pipeline support

## Changes in v1.7.0

- Package/docs synchronized to `v1.7.0`
- Snapshot and replay guidance clarified for production workflows
- Persistent and in-memory parity documented as first-class support

## Installation

```bash
dotnet add package SharpCoreDB.EventSourcing --version 1.7.0
```

## Related packages

- `SharpCoreDB.Projections`
- `SharpCoreDB.CQRS`

## Documentation

- `docs/INDEX.md`
- `src/SharpCoreDB.EventSourcing/NuGet.README.md`
