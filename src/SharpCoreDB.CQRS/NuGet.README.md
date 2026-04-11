# SharpCoreDB.CQRS v1.7.0

CQRS and outbox primitives for `SharpCoreDB`.

## Features

- Command contracts and handler abstractions
- In-memory and DI-backed command dispatching
- Aggregate root base with pending event collection
- In-memory/persistent outbox stores
- Retry/dead-letter-capable outbox dispatch worker support

## Changes in v1.7.0

- Package/docs synchronized to `v1.7.0`
- Outbox reliability guidance expanded (retry + dead-letter + worker flow)

## Installation

```bash
dotnet add package SharpCoreDB.CQRS --version 1.7.0
```

## Documentation

- `docs/INDEX.md`
- `src/SharpCoreDB.CQRS/README.md`
