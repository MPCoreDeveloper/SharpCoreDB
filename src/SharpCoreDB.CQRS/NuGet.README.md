# SharpCoreDB.CQRS v1.7.0

Optional CQRS package for SharpCoreDB.

## What this package is for

Use this package when you want command dispatching, aggregate-side pending events, and reliable outbox-based publication without adopting a full CQRS framework.

## What this package does exactly

- Defines command contracts and handlers
- Dispatches commands in-memory or through dependency injection
- Collects pending domain events on aggregates
- Stores unpublished outbox messages in memory or in SharpCoreDB
- Retries failed publication and supports dead-letter handling
- Runs background outbox dispatch with a hosted worker

## What this package does not do

- It does not persist event streams
- It does not execute projections
- It does not require MediatR or any transport-specific broker integration

## Highlights in v1.7.0

- Synchronized with the SharpCoreDB `1.7.0` package line
- Documents persistent outbox storage, retry policy, dead-letter handling, and hosted worker support
- Keeps CQRS guidance aligned with the current Event Sourcing and outbox integration story

## Included in this scaffold

- Command contracts and handlers
- In-memory command dispatcher
- Service-provider command dispatcher
- CQRS dependency injection extensions
- Aggregate root base type
- Outbox message model and in-memory outbox store
- **Persistent SharpCoreDB-backed outbox store**
- Outbox dispatch service and publisher contract
- **Configurable retry policy and dead-letter handling**
- **Hosted outbox dispatch background worker**
- `AddPersistentOutbox()`, `AddOutboxRetryPolicy()`, and `AddOutboxWorker()` DI helpers

## Installation

```bash
dotnet add package SharpCoreDB.CQRS --version 1.7.0
