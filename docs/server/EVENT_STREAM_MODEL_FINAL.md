# Event Stream Model and Sequence Semantics
## Final Specification (Week 2 Lockdown)

**Status:** Final Specification (Ready for Implementation)  
**Locked Date:** 2026-03-03  
**Applies to:** `SharpCoreDB.EventSourcing` v1.7.0+

---

## 1. Per-Stream Sequence Model

### 1.1 Definition

Each stream has an **independent, monotonically increasing sequence counter**.

```
StreamId: "user-123"
  Event 1: sequence=1, global_sequence=100
  Event 2: sequence=2, global_sequence=105
  Event 3: sequence=3, global_sequence=108

StreamId: "order-456"
  Event 1: sequence=1, global_sequence=101
  Event 2: sequence=2, global_sequence=106
  Event 3: sequence=3, global_sequence=109
```

### 1.2 Invariants (LOCKED)

1. **No gaps:** Sequences are contiguous integers starting at 1.
   - Valid: 1, 2, 3, 4, 5
   - Invalid: 1, 2, 4, 5 (gap at 3)

2. **No duplicates:** No two events in a stream share the same sequence.

3. **Monotonic growth:** Sequence only increases (Sequence(n+1) > Sequence(n)).

4. **Atomicity per append:** Single or batch append is atomic.
   - Either all events are assigned and persisted together, or none.
   - Partial batch failures are not allowed (all-or-nothing).

### 1.3 Guarantees at Append Time

When `AppendEventAsync()` or `AppendEventsAsync()` completes successfully:

1. ✅ Events are persisted (durable).
2. ✅ Sequences are assigned and will never change.
3. ✅ No event with the same stream/sequence will ever exist again.
4. ✅ Calling `AppendEventAsync()` again will receive sequence+1.

---

## 2. Global Sequence Model

### 2.1 Definition

A **single, monotonically increasing counter** across all streams and events.

```
Global Event Order (across all streams):
  Event 1: StreamId="user-123", sequence=1, global_sequence=1
  Event 2: StreamId="order-456", sequence=1, global_sequence=2
  Event 3: StreamId="user-123", sequence=2, global_sequence=3
  Event 4: StreamId="order-456", sequence=2, global_sequence=4
```

### 2.2 Invariants (LOCKED)

1. **No gaps:** Global sequences are contiguous integers starting at 1 (if at least 1 event exists).

2. **Uniqueness:** Each event has exactly one global_sequence value.

3. **Monotonic growth:** Events appended later have higher global_sequence.

4. **Ordering stability:** The order of events by global_sequence is immutable.
   - If Event A has global_sequence=10 and Event B has global_sequence=11, Event A always appears before Event B in global order.

### 2.3 Projection-Friendly Guarantee

Applications can use global_sequence to safely resume projections:

```csharp
// Projection checkpoint
var lastProcessedGlobal = 1000;

// Resume reading from checkpoint
var result = await store.ReadAllAsync(fromGlobalSequence: lastProcessedGlobal + 1, limit: 1000);
foreach (var envelope in result.Events)
{
    ApplyToProjection(envelope);
    lastProcessedGlobal = envelope.GlobalSequence;
}

// Save checkpoint for next run
SaveCheckpoint(lastProcessedGlobal);
```

**Guarantee:** This pattern is safe; no events are skipped or processed twice.

---

## 3. Append Semantics

### 3.1 Single Event Append

```csharp
var entry = new EventAppendEntry(
    EventType: "UserCreated",
    Payload: """{ "userId": "123", "email": "user@example.com" }""".ToBytes(),
    Metadata: """{ "correlationId": "abc-def" }""".ToBytes(),
    TimestampUtc: DateTimeOffset.UtcNow);

var result = await store.AppendEventAsync(
    new EventStreamId("user-123"),
    entry);

// Result contains:
// - StreamId: "user-123"
// - AppendedSequence: 1 (or next sequence if stream exists)
// - GlobalSequence: (monotonic across all appends)
// - Success: true (or false if store error)
```

**Semantics:**
- Event is persisted atomically.
- Sequence is assigned exactly once.
- If append fails (Success=false), caller should retry; event was not persisted.

### 3.2 Batch Append

```csharp
var entries = new[]
{
    new EventAppendEntry("UserCreated", payload1, metadata1, now),
    new EventAppendEntry("ProfileUpdated", payload2, metadata2, now),
    new EventAppendEntry("PasswordChanged", payload3, metadata3, now),
};

var results = await store.AppendEventsAsync(
    new EventStreamId("user-123"),
    entries);

// results is a list of 3 AppendResult objects:
// [0]: AppendedSequence=1, GlobalSequence=100
// [1]: AppendedSequence=2, GlobalSequence=101
// [2]: AppendedSequence=3, GlobalSequence=102
```

**Semantics:**
- All events are appended atomically (all-or-nothing).
- Sequences are contiguous (no gaps within the batch).
- Global sequences are consecutive (no gaps across appends).
- If batch fails, no event is persisted; caller retries entire batch.

### 3.3 Append Failure Handling

```csharp
var result = await store.AppendEventAsync(streamId, entry);

if (!result.Success)
{
    // Event was NOT persisted
    // Reasons: disk full, corruption, transaction conflict, etc.
    // Caller should:
    // 1. Log the error
    // 2. Retry (with exponential backoff)
    // 3. Or fail-fast if retries exhausted
    
    Logger.Error("Failed to append event", new { StreamId = streamId });
    // Retry or handle gracefully
}
else
{
    // Event is durable and won't change
    Console.WriteLine($"Appended at sequence {result.AppendedSequence}");
}
```

---

## 4. Read Semantics

### 4.1 Stream Read (Sequence Range)

```csharp
// Read events 5 through 15 from stream "user-123"
var range = new EventReadRange(FromSequence: 5, ToSequence: 15);
var result = await store.ReadStreamAsync(
    new EventStreamId("user-123"),
    range);

// result.Events contains events with sequences [5, 6, 7, ..., 15] from "user-123"
// result.TotalCount = count of all events matching range (even if limit was applied)
```

**Semantics:**
- Range is **inclusive** on both ends: [FromSequence, ToSequence].
- Events are returned in **ascending sequence order**.
- If range is empty (no events in that sequence window), returns empty list.
- If stream doesn't exist, returns empty list (not an error).

### 4.2 Global Ordered Read

```csharp
// Read next 1000 events in global order, starting from global sequence 5001
var result = await store.ReadAllAsync(
    fromGlobalSequence: 5001,
    limit: 1000);

// result.Events contains 1000 events (or fewer if < 1000 remain) ordered by global_sequence
// result.TotalCount = total events available from fromGlobalSequence onward
```

**Semantics:**
- Events are returned in **global_sequence order** (ascending).
- Mix of streams is preserved (events appear in the order they were globally appended, regardless of which stream they belong to).
- Pagination-friendly: use `lastEvent.GlobalSequence + 1` for next call.

### 4.3 Stream Length Query

```csharp
var length = await store.GetStreamLengthAsync(new EventStreamId("user-123"));

// length = highest sequence ever assigned to this stream
// If stream doesn't exist or is empty, returns 0
```

**Semantics:**
- Returns the **highest sequence number** assigned.
- Useful for checking stream existence (0 = doesn't exist).
- Useful for knowing where next append will start.

---

## 5. Snapshot Model

### 5.1 Definition

A snapshot is a **mutable checkpoint** at a known stream version:

```csharp
public readonly record struct EventSnapshot(
    EventStreamId StreamId,
    long Version,              // Stream sequence at which snapshot was taken
    ReadOnlyMemory<byte> SnapshotData,
    DateTimeOffset CreatedAtUtc);
```

### 5.2 Snapshot Semantics (LOCKED)

1. **Optionality:** Snapshots are optional. Apps may never use them.

2. **Mutability:** Snapshots can be updated/replaced. Unlike events, snapshots are **not immutable**.
   - Old snapshots can be deleted.
   - New snapshots can overwrite old ones at the same or different version.

3. **Version semantics:** Version corresponds to the stream sequence at which the snapshot was taken.
   - Example: A snapshot at version=100 represents the aggregate state after events 1-100 have been applied.

4. **Non-enforcement:** The engine does not enforce snapshotting policies.
   - Apps decide: snapshot every N events, after each hour, on-demand, etc.
   - Engine provides storage, not policy.

### 5.3 Typical Usage

```csharp
// Read all events and apply to aggregate
var all = await store.ReadStreamAsync(streamId, new EventReadRange(1, long.MaxValue));
var aggregate = new MyAggregate();
foreach (var envelope in all.Events)
{
    aggregate.Apply(envelope);
}

// Now save a snapshot at current version
var snapshot = new EventSnapshot(
    StreamId: streamId,
    Version: aggregate.Version,  // e.g., 100
    SnapshotData: SerializeAggregate(aggregate),
    CreatedAtUtc: DateTimeOffset.UtcNow);

await store.SaveSnapshotAsync(snapshot);  // (Future API, Week 5)

// Next time, load snapshot and only replay new events
var snapshot = await store.LoadSnapshotAsync(streamId);  // (Future API)
var aggregate = DeserializeAggregate(snapshot.SnapshotData);

// Replay events after snapshot version
var newEvents = await store.ReadStreamAsync(
    streamId,
    new EventReadRange(snapshot.Version + 1, long.MaxValue));
foreach (var envelope in newEvents.Events)
{
    aggregate.Apply(envelope);
}
```

---

## 6. Concurrency and Thread Safety

### 6.1 Append Concurrency

**Guarantee:** Appending to the same stream from multiple threads is **safe**.

- Appends are serialized per stream (via lock or transaction).
- No two appends to the same stream can interleave.
- Sequences will be assigned in order, without gaps.

```csharp
// Safe: Two threads appending to the same stream
var task1 = store.AppendEventAsync(streamId, event1);
var task2 = store.AppendEventAsync(streamId, event2);
await Task.WhenAll(task1, task2);

// result1.AppendedSequence = 1, result1.GlobalSequence = 100
// result2.AppendedSequence = 2, result2.GlobalSequence = 101
// (or vice versa, but always in order)
```

### 6.2 Read Concurrency

**Guarantee:** Reading is lock-free and concurrent.

- Multiple threads can read from the same stream simultaneously.
- Reads do not block appends.
- Reads return a consistent snapshot at a point in time (isolation).

### 6.3 Mixed Append/Read Concurrency

**Guarantee:** Appends and reads are properly isolated.

- A read will not see partially-applied batches.
- Events are either fully visible or not visible; no torn reads.

---

## 7. Durability and Failure Recovery

### 7.1 Durability Guarantee

When `AppendResult.Success == true`:
- **Event is durable.** Even if process crashes immediately after, the event is persisted.
- **Sequence is permanent.** The assigned sequence will never change.

### 7.2 Failure Scenarios

| Scenario | Outcome |
|----------|---------|
| Append returns Success=true, then crash | ✅ Event is persisted; sequence is intact |
| Append returns Success=false | ❌ Event was NOT persisted; retry safe |
| Crash during append before response | ⚠️ Indeterminate; must check if event exists by trying to read |
| Disk full during append | ❌ Append fails; error returned |
| Corruption detected | ❌ Engine throws exception; app must handle |

---

## 8. Migration from Non-Event-Sourcing Tables

### 8.1 One-Time Import

If you have an existing log table, import to event store:

```csharp
// Old table with (id, stream_id, event_type, payload, timestamp)
var oldTable = db.GetTable("event_log");

foreach (var row in oldTable.Where(x => x.deleted == false))
{
    var entry = new EventAppendEntry(
        EventType: row.event_type,
        Payload: row.payload,
        Metadata: row.metadata ?? [],
        TimestampUtc: row.timestamp);
    
    await store.AppendEventAsync(
        new EventStreamId(row.stream_id),
        entry);
}
```

**Important:** Sequences will be reassigned during import. Use the new sequence values, not the old ones.

---

## 9. Performance Expectations

| Operation | Expected Latency | Notes |
|-----------|------------------|-------|
| Append single | < 5ms (DB) | Per-stream lock contention determines ceiling |
| Append batch (100) | < 50ms (DB) | Amortized < 0.5ms per event |
| Read stream (100 events) | < 1ms (cache) | Indexed reads |
| Read all (100 events) | < 5ms (cache) | Global sequence index |
| Snapshot save | < 5ms (DB) | Mutable, no append ordering constraint |

---

## 10. Acceptance Criteria (M1 Lockdown)

✅ **All items locked:**

1. [x] Per-stream sequence model with invariants documented
2. [x] Global sequence model with invariants documented
3. [x] Append-only guarantees formalized
4. [x] Atomicity (all-or-nothing) per append specified
5. [x] Read semantics (stream range, global order) defined
6. [x] Snapshot model (optional, mutable) locked
7. [x] Concurrency model (append serialization, read lock-free) specified
8. [x] Durability guarantees formalized
9. [x] Failure handling documented
10. [x] Performance targets set

---

**Status:** ✅ **LOCKED FOR IMPLEMENTATION**

No changes to this spec without explicit approval. Week 3 implementation proceeds on these guarantees.
