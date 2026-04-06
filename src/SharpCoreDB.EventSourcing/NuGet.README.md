# SharpCoreDB.EventSourcing v1.7.0

Optional event sourcing package for SharpCoreDB.

## What this package is for

Use this package when you want SharpCoreDB to act as an event store for append-only business events, ordered replay, and snapshots.

## What this package does exactly

- Stores events per stream with contiguous sequence numbers
- Exposes a global ordered feed across all streams
- Supports in-memory and persistent SharpCoreDB-backed event stores
- Persists snapshots for faster aggregate reloads
- Supports upcasting during reads for event schema evolution

## What this package does not do

- It is not a CQRS framework
- It does not register or run projections
- It does not provide outbox delivery or command orchestration

## Highlights in v1.7.0

- Synchronized with the SharpCoreDB `1.7.0` package line
- Covers persistent and in-memory event store implementations
- Documents snapshots, snapshot-aware aggregate loading, and ordered event replay

## Included in this scaffold

- `EventStreamId`
- `EventAppendEntry`
- `EventEnvelope`
- `EventSnapshot`
- `EventReadRange`
- `IEventUpcaster`
- `EventUpcasterPipeline`

## Design rules

- Append-only semantics at contract level
- Strong stream identity and per-stream sequence intent
- No mandatory coupling to server runtime
- Separate package boundary to preserve SharpCoreDB core optionality

## Installation

```bash
dotnet add package SharpCoreDB.EventSourcing --version 1.7.0
