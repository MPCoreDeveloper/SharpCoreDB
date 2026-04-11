# SharpCoreDB.CQRS

CQRS and outbox primitives for `SharpCoreDB`.

**Version:** `v1.7.0`  
**Package:** `SharpCoreDB.CQRS`

## Features

- Command contracts and handler abstractions
- In-memory and DI-backed command dispatchers
- Aggregate root base with pending domain events
- In-memory and persistent SharpCoreDB-backed outbox stores
- Retry/dead-letter-capable outbox dispatch services and hosted workers

## Changes in v1.7.0

- Package/docs synchronized to `v1.7.0`
- Outbox persistence + retry/dead-letter guidance improved
- Integration alignment with EventSourcing/Projections companion packages

## Installation

```bash
dotnet add package SharpCoreDB.CQRS --version 1.7.0
```

## Related packages

- `SharpCoreDB.EventSourcing`
- `SharpCoreDB.Projections`

## Documentation

- `docs/INDEX.md`
- `src/SharpCoreDB.CQRS/NuGet.README.md`
