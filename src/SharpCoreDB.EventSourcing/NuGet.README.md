# SharpCoreDB.EventSourcing v1.7.1

Event store primitives for `SharpCoreDB`.


## Patch updates in v1.7.1

- ✅ Aligned package metadata and version references to the synchronized 1.7.1 release line.
- ✅ Release automation now publishes all packable SharpCoreDB packages in CI/CD.

## Features

- Append-only per-stream events with ordered sequences
- Global ordered event feed
- In-memory and persistent event store implementations
- Snapshot persistence and snapshot-aware loading
- Optional upcasting pipeline for schema evolution

## Changes in v1.7.1

- Package/docs synchronized to `v1.7.1`
- Production guidance clarified for snapshots and replay workflows

## Installation

```bash
dotnet add package SharpCoreDB.EventSourcing --version 1.7.1
```

## Documentation

- `docs/INDEX.md`
- `src/SharpCoreDB.EventSourcing/README.md`

