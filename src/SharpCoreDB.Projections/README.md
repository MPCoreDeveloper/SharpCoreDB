# SharpCoreDB.Projections

Projection primitives for `SharpCoreDB.EventSourcing`.

**Version:** `v1.7.2`  
**Package:** `SharpCoreDB.Projections`


## Patch updates in v1.7.2

- ✅ Aligned package metadata and version references to the synchronized 1.7.2 release line.
- ✅ Release automation now publishes all packable SharpCoreDB packages in CI/CD.

## Features

- Projection registration and discovery
- Inline and background projection runners
- Durable checkpointing (in-memory and SharpCoreDB-backed stores)
- Hosted background worker support
- OpenTelemetry-ready projection metrics

## Changes in v1.7.2

- Package/docs synchronized to `v1.7.2`
- Durable checkpoint and worker guidance clarified
- Projection metrics guidance aligned with current implementation

## Installation

```bash
dotnet add package SharpCoreDB.Projections --version 1.7.2
```

## Related packages

- `SharpCoreDB.EventSourcing`
- `SharpCoreDB.CQRS`

## Documentation

- `docs/INDEX.md`
- `src/SharpCoreDB.Projections/NuGet.README.md`

