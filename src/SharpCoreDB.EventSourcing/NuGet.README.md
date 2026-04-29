# SharpCoreDB.EventSourcing v1.8.0

Event store primitives for `SharpCoreDB`.


## Patch updates in v1.8.0

- ✅ Aligned package metadata and version references to the synchronized 1.8.0 release line.
- ✅ Release automation now publishes all packable SharpCoreDB packages in CI/CD.

## Features

- Append-only per-stream events with ordered sequences
- Global ordered event feed
- In-memory and persistent event store implementations
- Snapshot persistence and snapshot-aware loading
- Optional upcasting pipeline for schema evolution

## Changes in v1.8.0

- Package/docs synchronized to `v1.8.0`
- Production guidance clarified for snapshots and replay workflows

## Installation

```bash
dotnet add package SharpCoreDB.EventSourcing --version 1.8.0
```

## Documentation

- `docs/INDEX.md`
- `src/SharpCoreDB.EventSourcing/README.md`

