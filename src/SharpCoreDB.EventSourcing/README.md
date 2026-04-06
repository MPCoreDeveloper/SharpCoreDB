# SharpCoreDB.EventSourcing

Optional event sourcing primitives for SharpCoreDB.

## What this package is for

`SharpCoreDB.EventSourcing` is the package you add when you want to store domain changes as an append-only event stream instead of only storing the latest row state.

Use it when your application needs one or more of these capabilities:

- A durable history of business events such as `OrderCreated`, `OrderPaid`, or `InvoiceSent`
- Replay of past events to rebuild aggregate state
- A global ordered event feed for projection catch-up and integration processing
- Snapshots to speed up aggregate rehydration
- The same programming model for both in-memory tests and persistent SharpCoreDB-backed storage

## What this package does exactly

This package is responsible for the event store layer itself.

It gives you:

- **Append-only event writes** per stream with ordered sequence numbers
- **Per-stream reads** for rebuilding one aggregate or business entity
- **Global ordered reads** for projection catch-up and integration processing
- **Snapshot persistence** so large streams can be reloaded without replaying every event
- **Snapshot-aware aggregate loading** via `LoadWithSnapshotAsync`
- **Optional upcasting** so older persisted event payloads can be transformed into newer shapes during reads
- **In-memory and persistent implementations** with the same core abstractions

## What this package does not do

This package does **not** implement the higher orchestration layers around event sourcing.

It does not provide:

- A full CQRS framework
- Command dispatching
- Projection registration and background projection hosting
- Transport-specific messaging or broker integrations
- An opinionated aggregate base class

Those concerns are intentionally kept in separate optional packages such as `SharpCoreDB.Projections` and `SharpCoreDB.CQRS`.

## When to use this package

Choose `SharpCoreDB.EventSourcing` when you need event persistence and replay, but you still want to decide yourself how commands, projections, or messaging are composed.

Typical scenarios:

- Domain-driven systems with aggregate rehydration from events
- Audit-heavy applications where full business history matters
- Systems that build read models from a global event feed
- Test suites that want in-memory event storage first and durable storage later

## v1.7.0 Highlights

This guide matches the synchronized `1.7.0` package release and covers the current event store feature set: in-memory and persistent stores, ordered global feeds, snapshots, snapshot-aware aggregate loading, and optional upcasting for schema evolution.

## Scope

This package provides low-level contracts for:
- Append-only event entries
- Ordered stream reads
- Global ordered feed records
- Lightweight snapshots
- Per-stream sequence tracking
- Thread-safe in-memory implementation
- Persistent SharpCoreDB-backed implementation

## Non-goals

This package does **not** implement a full CQRS framework, aggregate orchestration, or opinionated projection pipelines.

## Install

```bash
dotnet add package SharpCoreDB.EventSourcing --version 1.7.0
```

## Optionality

`SharpCoreDB.EventSourcing` is intentionally separate from `SharpCoreDB` so users can keep the core dependency graph minimal when event sourcing is not needed.

---

## Quick Start

### 1. Create an Event Store

```csharp
using SharpCoreDB.EventSourcing;

// In-memory store for testing/lightweight scenarios
var inMemoryStore = new InMemoryEventStore();

// Persistent store backed by SharpCoreDB database
var persistentStore = new SharpCoreDbEventStore(database);
```

### 2. Append Events to a Stream

```csharp
// Define a stream
var streamId = new EventStreamId("order-12345");

// Create an event
var orderCreated = new EventAppendEntry(
    EventType: "OrderCreated",
    Payload: """{"orderId": "12345", "total": 99.99}"""u8.ToArray(),
    Metadata: """{"userId": "user-42"}"""u8.ToArray(),
    TimestampUtc: DateTimeOffset.UtcNow
);

// Append to stream
var result = await eventStore.AppendEventAsync(streamId, orderCreated);
Console.WriteLine($"Event appended at sequence {result.AppendedSequence}, global {result.GlobalSequence}");
```

### 3. Append Multiple Events (Batch)

```csharp
var events = new[]
{
    new EventAppendEntry("OrderCreated", payload1, metadata1, DateTimeOffset.UtcNow),
    new EventAppendEntry("OrderPaid", payload2, metadata2, DateTimeOffset.UtcNow),
    new EventAppendEntry("OrderShipped", payload3, metadata3, DateTimeOffset.UtcNow)
};

var results = await eventStore.AppendEventsAsync(streamId, events);
// All events assigned contiguous sequences atomically
```

### 4. Read Stream Events

```csharp
// Read events from sequence 1 to 100
var readResult = await eventStore.ReadStreamAsync(
    streamId,
    new EventReadRange(fromSequence: 1, toSequence: 100)
);

foreach (var envelope in readResult.Events)
{
    Console.WriteLine($"[{envelope.Sequence}] {envelope.EventType} @ {envelope.TimestampUtc}");
    // Process envelope.Payload, envelope.Metadata
}
```

### 5. Read All Events (Global Feed)

```csharp
// Read all events across all streams in global order
var allEvents = await eventStore.ReadAllAsync(
    fromGlobalSequence: 1,
    limit: 1000
);

Console.WriteLine($"Total events available: {allEvents.TotalCount}");
foreach (var evt in allEvents.Events)
{
    Console.WriteLine($"[Global {evt.GlobalSequence}] {evt.StreamId.Value} - {evt.EventType}");
}
```

### 6. Check Stream Length

```csharp
var length = await eventStore.GetStreamLengthAsync(streamId);
Console.WriteLine($"Stream has {length} events");
```

### 7. Save and Load Snapshots

```csharp
var snapshot = new EventSnapshot(
    StreamId: streamId,
    Version: currentVersion,
    SnapshotData: SerializeAggregate(state),
    CreatedAtUtc: DateTimeOffset.UtcNow);

await eventStore.SaveSnapshotAsync(snapshot);

var latest = await eventStore.LoadSnapshotAsync(streamId);
if (latest is { } loaded)
{
    Console.WriteLine($"Loaded snapshot at version {loaded.Version}");
}
```

### 8. Load Aggregate with Snapshot + Replay

```csharp
var loadResult = await eventStore.LoadWithSnapshotAsync(
    streamId,
    fromEvents: static events => OrderAggregate.FromEventStream(events),
    fromSnapshot: static snapshotData => OrderAggregate.FromSnapshot(snapshotData),
    replayFromSnapshot: static (aggregate, events) => aggregate.Replay(events));

Console.WriteLine($"Loaded version {loadResult.Version}");
Console.WriteLine($"Snapshot used: {loadResult.Snapshot is not null}");
Console.WriteLine($"Replayed events: {loadResult.ReplayedEvents}");
```

### 9. Auto Snapshot Every N Events (Optional)

```csharp
var policy = new SnapshotPolicy(EveryNEvents: 100);

var append = await eventStore.AppendEventsWithSnapshotPolicyAsync(
    streamId,
    entries,
    policy,
    snapshotFactory: version => new EventSnapshot(
        streamId,
        Version: version,
        SnapshotData: SerializeAggregate(order),
        CreatedAtUtc: DateTimeOffset.UtcNow));

Console.WriteLine($"Snapshot created: {append.SnapshotCreated}");
```

### 10. Event Versioning with Upcasting (Optional)

```csharp
var upcasters = new IEventUpcaster[]
{
    new OrderCreatedV1ToV2Upcaster(),
    new OrderCreatedV2ToV3Upcaster(),
};

var upcasted = await eventStore.ReadStreamUpcastedAsync(
    streamId,
    new EventReadRange(1, long.MaxValue),
    upcasters,
    ct);
```

Use `EventUpcasterPipeline` when you want explicit ordered schema evolution over raw persisted events.

---

## Core Concepts

### EventStreamId

Strongly-typed stream identifier. Each stream has independent sequence numbering.

```csharp
var streamId = new EventStreamId("aggregate-123");
```

### EventAppendEntry

Represents an event to be appended. Contains:
- `EventType`: string identifier (e.g., "OrderCreated")
- `Payload`: byte[] with serialized event data
- `Metadata`: byte[] for correlation/causation IDs
- `TimestampUtc`: when the event was created

### EventEnvelope

Returned when reading events. Adds:
- `Sequence`: per-stream sequence number (1-based, contiguous)
- `GlobalSequence`: global ordering across all streams

### Per-Stream Sequence

Each stream maintains its own sequence counter starting at 1. Concurrent appends to the same stream are serialized to ensure no gaps.

### Global Sequence

All events receive a global sequence number for ordered reading across streams. Useful for building projections and read models.

---

## Concurrency Guarantees

- **Thread-safe**: `InMemoryEventStore` uses lock-based synchronization
- **Atomic batch append**: All events in `AppendEventsAsync` succeed or none do
- **No gaps**: Stream sequences are always contiguous (1, 2, 3, ...)
- **Lock-free reads**: Read operations don't block writes

---

## Example: Complete Event Sourcing Flow

```csharp
using SharpCoreDB.EventSourcing;

// 1. Create store
var store = new InMemoryEventStore();
var orderId = new EventStreamId("order-999");

// 2. Append lifecycle events
await store.AppendEventAsync(orderId, 
    new EventAppendEntry("OrderCreated", GetPayload("created"), Array.Empty<byte>(), DateTimeOffset.UtcNow));
    
await store.AppendEventAsync(orderId,
    new EventAppendEntry("OrderPaid", GetPayload("paid"), Array.Empty<byte>(), DateTimeOffset.UtcNow));
    
await store.AppendEventAsync(orderId,
    new EventAppendEntry("OrderShipped", GetPayload("shipped"), Array.Empty<byte>(), DateTimeOffset.UtcNow));

// 3. Rebuild state from events
var events = await store.ReadStreamAsync(orderId, new EventReadRange(1, long.MaxValue));
var currentState = ReplayEvents(events.Events);
Console.WriteLine($"Order status: {currentState}");

// 4. Read global feed (all orders)
var globalFeed = await store.ReadAllAsync(1, 100);
Console.WriteLine($"{globalFeed.TotalCount} total events across all streams");

static byte[] GetPayload(string state) => System.Text.Encoding.UTF8.GetBytes($"{{\"state\":\"{state}\"}}");

static string ReplayEvents(IReadOnlyList<EventEnvelope> events)
{
    var state = "Unknown";
    foreach (var evt in events)
    {
        state = evt.EventType switch
        {
            "OrderCreated" => "Created",
            "OrderPaid" => "Paid",
            "OrderShipped" => "Shipped",
            _ => state
        };
    }
    return state;
}
```

---

## Performance Characteristics (InMemoryEventStore)

| Operation | Latency | Notes |
|-----------|---------|-------|
| Single append | < 1ms | Lock contention on same stream |
| Batch append (100) | < 10ms | Atomic transaction |
| Read stream (100) | < 1ms | No locks, direct list access |
| Read all (100) | < 5ms | Merges all streams |

---

## Persistence Notes

`InMemoryEventStore` is for **testing and lightweight scenarios only**. Data is lost when the process ends.

For persistent storage with SharpCoreDB, use `SharpCoreDbEventStore`.

Both stores support snapshot persistence and snapshot-aware load APIs:
- `SaveSnapshotAsync(EventSnapshot)`
- `LoadSnapshotAsync(EventStreamId, maxVersion)`
- `LoadWithSnapshotAsync(...)`
- `AppendEventsWithSnapshotPolicyAsync(...)`

Event schema evolution support:
- `IEventUpcaster`
- `EventUpcasterPipeline`
- `ReadStreamUpcastedAsync(...)`
- `ReadAllUpcastedAsync(...)`

For other production backends, implement `IEventStore` with:
- Event Store DB
- PostgreSQL (jsonb columns)
- Azure Cosmos DB (change feed)

---

## Testing

This package includes 25+ unit tests covering:
- Append operations (single and batch)
- Concurrent appends (thread safety)
- Read operations (stream and global)
- Edge cases (empty streams, large batches, inverted ranges)
- Data integrity (sequence contiguity, payload preservation)

Run tests:
```bash
dotnet test tests/SharpCoreDB.EventSourcing.Tests
```

---

## References

- [Event Sourcing RFC](../../docs/server/EVENT_SOURCING_RFC.md)
- [Event Stream Model Spec](../../docs/server/EVENT_STREAM_MODEL_FINAL.md)
- [Issue #55 Acceptance Criteria](../../docs/server/ISSUE_55_ACCEPTANCE_CRITERIA.md)

---

## License

MIT License. See LICENSE file in repository root.
