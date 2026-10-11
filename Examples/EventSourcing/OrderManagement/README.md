# Order Management System - Event Sourcing Demo

This demo showcases **SharpCoreDB.EventSourcing** capabilities using a realistic Order Management System. It demonstrates key event sourcing patterns including event appends, state reconstruction, event replay, and global event feeds.

> Note: This sample uses `InMemoryEventStore` for simplicity. For persistent storage with SharpCoreDB, see `examples/EventSourcing/OrderManagement.PersistentDemo`.

---

## 🎯 What This Demo Shows

1. **Event Sourcing Basics**
   - Append-only event streams
   - Immutable events
   - State derived from events

2. **Order Lifecycle**
   - Create order with items
   - Add/remove items
   - Confirm order
   - Process payment
   - Ship order
   - Deliver order
   - Cancel order

3. **Advanced Features**
   - Event replay and state reconstruction
   - Snapshot policy (auto snapshot every N events)
   - Snapshot-first aggregate loading
   - Point-in-time queries (temporal queries)
   - Global event feed (for projections)
   - Per-stream sequence tracking
   - Event versioning

---

## 📁 Project Structure

```
OrderManagement/
├── Program.cs           # Main demo with 5 scenarios
├── OrderAggregate.cs    # Domain aggregate with commands
├── OrderEvents.cs       # Event definitions
├── OrderManagement.csproj
└── README.md           # This file
```

---

## 🚀 Running the Demo

### Prerequisites
- .NET 11 SDK
- SharpCoreDB.EventSourcing package

### Build and Run
```bash
cd examples/EventSourcing/OrderManagement
dotnet build
dotnet run
```

### Expected Output
The demo runs 5 scenarios demonstrating different event sourcing patterns:

1. **Create and Evolve Order** - Shows order lifecycle from creation to delivery with optional auto-snapshots
2. **Rebuild State from Snapshot + Events** - Demonstrates snapshot-first loading and replay
3. **Multiple Orders & Global Feed** - Shows global ordering across streams
4. **Point-in-Time Query** - Rebuilds state at specific sequence
5. **Stream Statistics** - Shows stream metadata

---

## 💡 Key Concepts

### Event Sourcing Pattern

Instead of storing current state:
```csharp
// Traditional CRUD (stores current state)
var order = new Order { 
    Status = OrderStatus.Delivered,
    Items = [...],
    Total = 1109.97
};
db.Update(order); // Overwrites previous state
```

Event sourcing stores all changes:
```csharp
// Event Sourcing (stores all events)
await eventStore.AppendEventAsync(streamId, new EventAppendEntry(
    EventType: "OrderCreated",
    Payload: SerializeEvent(new OrderCreatedEvent { ... }),
    ...
));
await eventStore.AppendEventAsync(streamId, new EventAppendEntry(
    EventType: "ItemAdded",
    Payload: SerializeEvent(new ItemAddedEvent { ... }),
    ...
));
// State = replay all events
```

**Benefits:**
- Complete audit trail
- Time travel (point-in-time queries)
- Event replay for debugging
- Build multiple read models from same events

---

### Commands vs Events

**Commands** (imperative, can fail):
```csharp
order.ConfirmOrder();      // Command: "Do this"
order.MarkAsPaid(...);     // Can throw exception if invalid
```

**Events** (past tense, already happened):
```csharp
new OrderConfirmedEvent { ... }  // Event: "This happened"
new OrderPaidEvent { ... }       // Immutable fact
```

---

### State Reconstruction

The `OrderAggregate` rebuilds its state by replaying events:

```csharp
public static OrderAggregate FromEventStream(IReadOnlyList<EventEnvelope> events)
{
    var aggregate = new OrderAggregate();
    
    foreach (var envelope in events)
    {
        var orderEvent = DeserializeEvent(envelope);
        aggregate.Apply(orderEvent); // Updates internal state
        aggregate.Version = envelope.Sequence;
    }
    
    return aggregate;
}
```

The `Apply` method is the heart of event sourcing - it contains the logic to update state based on events.

---

## 📊 Demo Scenarios Explained

### Demo 1: Create and Evolve Order

Creates an order and walks through the complete lifecycle:

```
ORDER-001
├─ OrderCreated     (items: 2, total: $1059.97)
├─ ItemAdded        (keyboard added, new total: $1139.96)
├─ OrderConfirmed   (status → Confirmed)
├─ OrderPaid        (status → Paid)
├─ OrderShipped     (status → Shipped, tracking: TRACK-123)
└─ OrderDelivered   (status → Delivered)
```

Each step appends an event to the stream. State is always reconstructable from events.

### Demo 2: Rebuild State from Events

Rebuilds `ORDER-001` using the snapshot-aware load API:

```csharp
var loadResult = await eventStore.LoadWithSnapshotAsync(
    streamId,
    fromEvents: static events => OrderAggregate.FromEventStream(events),
    fromSnapshot: static snapshotData => OrderAggregate.FromSnapshot(snapshotData),
    replayFromSnapshot: static (aggregate, events) => aggregate.Replay(events));
```

This prints:
- Whether a snapshot was used
- Snapshot version (if present)
- Number of events replayed after snapshot

### Demo 3: Multiple Orders & Global Feed

Creates multiple orders and shows the global event feed:

```
Global Sequence | Stream      | Event Type
----------------|-------------|------------------
1               | ORDER-001   | OrderCreated
2               | ORDER-001   | ItemAdded
3               | ORDER-001   | OrderConfirmed
4               | ORDER-001   | OrderPaid
5               | ORDER-001   | OrderShipped
6               | ORDER-001   | OrderDelivered
7               | ORDER-002   | OrderCreated
8               | ORDER-002   | OrderConfirmed
9               | ORDER-002   | OrderPaid
10              | ORDER-003   | OrderCreated
11              | ORDER-003   | OrderCancelled
```

**Use case:** Build read models or projections by consuming the global feed sequentially.

### Demo 4: Point-in-Time Query

Rebuilds `ORDER-001` at sequence 3 (before payment):

```csharp
var partialEvents = await eventStore.ReadStreamAsync(
    new EventStreamId("ORDER-001"),
    new EventReadRange(1, 3)  // Only first 3 events
);

var orderAtPoint = OrderAggregate.FromEventStream(partialEvents.Events);
// Status: Confirmed (not yet Paid)
```

**Use case:** Debugging ("What was the state when the bug occurred?"), auditing, compliance.

### Demo 5: Stream Statistics

Shows metadata for each stream:

```
ORDER-001: 6 events
ORDER-002: 3 events
ORDER-003: 2 events
```

**Use case:** Monitoring, stream health checks, identifying hot streams.

---

## 🔧 How It Works

### 1. Define Events

Events are immutable records representing facts:

```csharp
public record OrderCreatedEvent : OrderEvent
{
    public required string CustomerId { get; init; }
    public required List<OrderItem> Items { get; init; }
    public required decimal TotalAmount { get; init; }
}
```

### 2. Create Aggregate

Aggregate handles commands and applies events:

```csharp
public class OrderAggregate
{
    public OrderStatus Status { get; private set; }
    
    public void ConfirmOrder()
    {
        if (Status != OrderStatus.Draft)
            throw new InvalidOperationException("Cannot confirm");
        
        var orderConfirmed = new OrderConfirmedEvent { ... };
        Apply(orderConfirmed);
        PendingEvents.Add(orderConfirmed);
    }
    
    private void Apply(OrderConfirmedEvent evt)
    {
        Status = OrderStatus.Confirmed;
    }
}
```

### 3. Persist Events

Save pending events to the event store:

```csharp
foreach (var orderEvent in order.PendingEvents)
{
    var entry = new EventAppendEntry(
        EventType: orderEvent.GetType().Name,
        Payload: orderEvent.Serialize(),
        Metadata: Array.Empty<byte>(),
        TimestampUtc: orderEvent.Timestamp
    );
    
    await eventStore.AppendEventAsync(streamId, entry);
}
```

### 4. Reconstruct State

Load aggregate from events:

```csharp
var events = await eventStore.ReadStreamAsync(streamId, new EventReadRange(1, long.MaxValue));
var order = OrderAggregate.FromEventStream(events.Events);
```

---

## 🎓 Best Practices Demonstrated

### ✅ Aggregate Design
- Commands validate business rules
- Events are facts (past tense)
- `Apply` methods update state
- Aggregate is self-contained

### ✅ Event Naming
- Past tense: `OrderCreated`, not `CreateOrder`
- Specific: `ItemAdded`, not `OrderUpdated`
- Domain language: Use business terms

### ✅ Event Payload
- Serialize as JSON for readability
- Include all data needed to apply event
- Keep events small and focused

### ✅ Stream Naming
- Use domain identifiers: `ORDER-001`
- Consistent format across streams
- Consider partitioning strategy

### ✅ Error Handling
- Commands throw exceptions for invalid operations
- Events never fail (they're facts)
- Validate in commands, not in `Apply`

---

## 🏗️ Extending This Demo

### Add Snapshots
For long event streams, use policy-based snapshot persistence:

```csharp
var policy = new SnapshotPolicy(EveryNEvents: 100);

await eventStore.AppendEventsWithSnapshotPolicyAsync(
    streamId,
    entries,
    policy,
    snapshotFactory: version => new EventSnapshot(
        streamId,
        Version: version,
        SnapshotData: order.ToSnapshotData(version),
        CreatedAtUtc: DateTimeOffset.UtcNow));
```

### Add Projections
Build read models from the global feed:

```csharp
var feed = await eventStore.ReadAllAsync(lastProcessedSequence + 1);
foreach (var evt in feed.Events)
{
    // Update read model based on event
    if (evt.EventType == "OrderCreated")
    {
        // Add to "All Orders" view
    }
}
```

### Add Event Upcasting
Handle schema changes:

```csharp
if (envelope.EventType == "OrderCreatedV1")
{
    var v1 = Deserialize<OrderCreatedEventV1>(envelope.Payload);
    var v2 = new OrderCreatedEventV2 { ... }; // Convert to V2
    return v2;
}
```

---

## 📚 Related Documentation

- [Event Sourcing RFC](../../../docs/server/EVENT_SOURCING_RFC.md)
- [Event Stream Model](../../../docs/server/EVENT_STREAM_MODEL_FINAL.md)
- [SharpCoreDB.EventSourcing README](../../../src/SharpCoreDB.EventSourcing/README.md)
- [Issue #55 Acceptance Criteria](../../../docs/server/ISSUE_55_ACCEPTANCE_CRITERIA.md)

---

## 🎯 Key Takeaways

1. **Event sourcing stores events, not state**
   - State is derived by replaying events
   - Complete audit trail included

2. **Aggregates enforce business rules**
   - Commands validate and create events
   - Events update internal state

3. **Event streams are append-only**
   - Events are immutable facts
   - No updates or deletes

4. **Per-stream and global ordering**
   - Each stream has independent sequence
   - Global sequence enables projections

5. **SharpCoreDB.EventSourcing is lightweight**
   - No framework overhead
   - You control the orchestration
   - Optional - doesn't impact core SharpCoreDB

---

## 🔗 Next Steps

- Try modifying the order lifecycle
- Add new event types (e.g., `OrderRefunded`)
- Build a read model from the global feed
- Implement snapshots for performance
- Create unit tests for aggregate logic

---

**Happy Event Sourcing!** 🎉
