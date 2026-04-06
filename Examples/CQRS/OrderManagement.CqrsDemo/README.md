# Order Management - Explicit CQRS Demo (v1.7.0)

This demo shows an explicit **CQRS-only** flow with `SharpCoreDB.CQRS`.
It is intentionally different from the Event Sourcing sample in `Examples/EventSourcing/OrderManagement`.

## Goal

Show CQRS as:

- a dedicated command side (write model + command handlers)
- a dedicated query side (read model + query service)
- projector-style updates from command-side notifications to read-side state

Without Event Sourcing in this sample:

- no event stream persistence
- no replay-based reconstruction
- no point-in-time queries

## Project Structure

```text
OrderManagement.CqrsDemo/
├── Program.cs                 # Demo runner and scenario output
├── OrderCqrsDemo.cs           # Commands, handlers, write/read model, projector
├── OrderManagement.CqrsDemo.csproj
└── README.md
```

## CQRS Flow in This Demo

1. `PlaceOrderCommand` is dispatched to `PlaceOrderCommandHandler`.
2. Handler writes the current order state to a write repository.
3. Handler emits a projection notification (`OrderPlacedNotification`).
4. `OrderReadProjector` updates a separate read store.
5. Queries read from `OrderQueryService` only (read model only).

The same pattern is repeated for:

- `AddOrderLineCommand`
- `ConfirmOrderCommand`
- `MarkOrderPaidCommand`

## Run

```bash
cd Examples/CQRS/OrderManagement.CqrsDemo
dotnet build
dotnet run
```

## Expected Output Highlights

- command execution results (`OK` / `FAILED`)
- read-model summary (query side)
- write-model state (command side)
- explicit comparison block: **CQRS vs Event Sourcing**

## CQRS vs Event Sourcing in This Repository

| Topic | CQRS Demo (`Examples/CQRS/OrderManagement.CqrsDemo`) | Event Sourcing Demo (`Examples/EventSourcing/OrderManagement`) |
|---|---|---|
| Primary split | Commands vs queries | Events as source of truth |
| Stored data | Current write-side state + read-side projection state | Immutable event stream (optionally snapshots) |
| Rebuild strategy | Not replay-driven in this sample | Replay events (or snapshot + replay) |
| Time travel | Not included | Included (point-in-time queries) |
| Global feed | Not included | Included |

## Why this demo exists

The Event Sourcing demos already explain event-stream-first design.
This project exists to make the **pure CQRS separation** explicit and easy to compare side-by-side.
