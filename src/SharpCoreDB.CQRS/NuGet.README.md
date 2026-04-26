# SharpCoreDB.CQRS v1.7.1

CQRS and outbox primitives for `SharpCoreDB`.


## Patch updates in v1.7.1

- ✅ Aligned package metadata and version references to the synchronized 1.7.1 release line.
- ✅ Release automation now publishes all packable SharpCoreDB packages in CI/CD.

## Features

- Command contracts and handler abstractions
- In-memory and DI-backed command dispatching
- Aggregate root base with pending event collection
- In-memory/persistent outbox stores
- Retry/dead-letter-capable outbox dispatch worker support

## Changes in v1.7.1

- Package/docs synchronized to `v1.7.1`
- Outbox reliability guidance expanded (retry + dead-letter + worker flow)

## Installation

```bash
dotnet add package SharpCoreDB.CQRS --version 1.7.1
```

## Documentation

- `docs/INDEX.md`
- `src/SharpCoreDB.CQRS/README.md`

