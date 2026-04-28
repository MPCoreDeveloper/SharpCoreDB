# SharpCoreDB.CQRS Guide (v1.7.2)

This guide documents the optional `SharpCoreDB.CQRS` package and how to use it with SharpCoreDB Event Sourcing.

## What this package is for

`SharpCoreDB.CQRS` adds a lightweight command-side layer on top of SharpCoreDB so applications can model explicit commands, dispatch them through handlers, collect domain events on aggregates, and publish integration messages through an outbox.

## What this package does exactly

At a package level, `SharpCoreDB.CQRS` is responsible for:

- command contracts and handlers
- command dispatching
- aggregate-side pending domain event collection
- outbox storage and dispatch orchestration
- retry, dead-letter, and hosted worker support
- bridging aggregate events into reliable outbox messages

## What this package does not do

`SharpCoreDB.CQRS` does not persist event streams and it does not execute projections. Those responsibilities stay in `SharpCoreDB.EventSourcing` and `SharpCoreDB.Projections`.

## v1.7.2 Highlights

The `1.7.2` synchronized release keeps the CQRS docs aligned with the shipped package surface: persistent outbox storage, retry and dead-letter metadata, the hosted outbox worker, and the aggregate-to-outbox bridge are all included in the documented baseline.

## Quickstart

For a fast setup, see:

- `docs/cqrs/QUICKSTART.md`

## 1. Package Scope

`SharpCoreDB.CQRS` provides:

- Command contracts and handlers
- In-memory and DI-based command dispatchers
- `AggregateRoot` base class for pending domain events
- Outbox model and dispatch service
- In-memory outbox store
- Persistent SharpCoreDB-backed outbox store
- Retry-aware outbox failure recording (`attempt_count`, `last_error`, `next_attempt_utc`)
- Hosted outbox worker (`PeriodicTimer` polling)

Non-goals:

- No required dependency on MediatR
- No required transport (Kafka/Rabbit/etc. are integrated via `IOutboxPublisher`)

---

## 2. Installation

```bash
dotnet add package SharpCoreDB.CQRS --version 1.7.2
```

---

## 3. Commands and Handlers

### 3.1 Command contract

```csharp
using SharpCoreDB.CQRS;

public readonly record struct PlaceOrderCommand(string OrderId, decimal Amount) : ICommand;
```

### 3.2 Handler

```csharp
using SharpCoreDB.CQRS;

public sealed class PlaceOrderCommandHandler : ICommandHandler<PlaceOrderCommand>
{
    public Task<CommandDispatchResult> HandleAsync(PlaceOrderCommand command, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(command.OrderId))
        {
            return Task.FromResult(CommandDispatchResult.Failure("OrderId is required."));
        }

        if (command.Amount <= 0)
        {
            return Task.FromResult(CommandDispatchResult.Failure("Amount must be > 0."));
        }

        return Task.FromResult(CommandDispatchResult.Success());
    }
}
```

---

## 4. Dispatching Commands

### 4.1 In-memory dispatcher

```csharp
var dispatcher = new InMemoryCommandDispatcher();
dispatcher.RegisterHandler(new PlaceOrderCommandHandler());

var result = await dispatcher.DispatchAsync(new PlaceOrderCommand("order-1", 199.99m), ct);
Console.WriteLine(result.Success);
```

### 4.2 DI-backed dispatcher

```csharp
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.CQRS;

var services = new ServiceCollection();
services.AddSharpCoreDBCqrs();
services.AddCommandHandler<PlaceOrderCommand, PlaceOrderCommandHandler>();

var provider = services.BuildServiceProvider();
var dispatcher = provider.GetRequiredService<ICommandDispatcher>();

var result = await dispatcher.DispatchAsync(new PlaceOrderCommand("order-2", 249.50m), ct);
```

---

## 5. Aggregate Root and Pending Events

Use `AggregateRoot` to capture domain events and dequeue them for persistence/outbox publication.

```csharp
using SharpCoreDB.CQRS;

public sealed class OrderAggregate : AggregateRoot
{
    public void Place(string orderId, decimal amount)
    {
        RaiseEvent(new OrderPlaced(orderId, amount));
    }
}

public readonly record struct OrderPlaced(string OrderId, decimal Amount);

var aggregate = new OrderAggregate();
aggregate.Place("order-3", 50m);

var pendingEvents = aggregate.DequeuePendingEvents();
```

---

## 6. Outbox Basics

### 6.1 Core abstractions

- `OutboxMessage`: payload + routing metadata
- `IOutboxStore`: add/read/publish/failure-record lifecycle
- `IOutboxPublisher`: pluggable external publisher contract
- `OutboxDispatchService`: orchestrates read -> publish -> publish/failure outcome

### 6.2 Publisher implementation

```csharp
using SharpCoreDB.CQRS;

public sealed class ConsoleOutboxPublisher : IOutboxPublisher
{
    public Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Console.WriteLine($"Published {message.MessageId} ({message.MessageType})");
        return Task.CompletedTask;
    }
}
```

---

## 7. In-memory Outbox Example

```csharp
var store = new InMemoryOutboxStore();
var publisher = new ConsoleOutboxPublisher();
var dispatch = new OutboxDispatchService(store, publisher);

await store.AddAsync(new OutboxMessage(
    MessageId: "msg-1",
    AggregateId: "order-1",
    MessageType: "OrderPlaced",
    Payload: "{}"u8.ToArray(),
    CreatedAtUtc: DateTimeOffset.UtcNow,
    IsPublished: false),
    ct);

var published = await dispatch.DispatchUnpublishedAsync(100, ct);
```

---

## 8. Persistent Outbox Example (SharpCoreDB-backed)

### 8.1 Registration

```csharp
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB;
using SharpCoreDB.CQRS;

var services = new ServiceCollection();
services.AddSharpCoreDB();
services.AddSharpCoreDBCqrs();
services.AddPersistentOutbox(); // default table: scdb_outbox
services.AddSingleton<IOutboxPublisher, ConsoleOutboxPublisher>();

var provider = services.BuildServiceProvider();
```

### 8.2 Dispatch usage

```csharp
var dispatch = provider.GetRequiredService<OutboxDispatchService>();
var published = await dispatch.DispatchUnpublishedAsync(50, ct);
```

### 8.3 Table behavior

For modern schema tables, the store keeps retry metadata columns:

- `attempt_count` (`INTEGER`)
- `last_error` (`TEXT`)
- `next_attempt_utc` (`TEXT`, round-trip timestamp)

For legacy tables without retry metadata, store operations remain compatible.

---

## 9. Failure Tracking, Retry Policy, and Dead-Letter Handling

When a publisher throws for one message:

1. Dispatch does **not** abort the entire batch.
2. It calls `IOutboxStore.RecordFailureAsync(messageId, error, ct)`.
3. Store increments attempt count and schedules the next attempt using configured backoff.
4. When attempts reach `MaxAttempts`, the message is moved to dead-letter storage.

### 9.1 Configure retry policy

```csharp
services.AddSharpCoreDBCqrs();
services.AddOutboxRetryPolicy(options =>
{
    options.BaseDelay = TimeSpan.FromSeconds(2);
    options.MaxDelay = TimeSpan.FromMinutes(2);
    options.MaxAttempts = 5;
    options.DeadLetterTableName = "scdb_outbox_deadletter";
});
```

### 9.2 Read dead-letter messages

```csharp
var outboxStore = provider.GetRequiredService<IOutboxStore>();
var deadLetters = await outboxStore.GetDeadLettersAsync(100, ct);
```

### 9.3 Requeue a dead-lettered message

When a dead-lettered message is ready to retry (after a root cause fix, for example), move it back to the outbox with a reset attempt counter:

```csharp
var outboxStore = provider.GetRequiredService<IOutboxStore>();

// Inspect dead letters and decide which to requeue
var deadLetters = await outboxStore.GetDeadLettersAsync(100, ct);
foreach (var message in deadLetters)
{
    Console.WriteLine($"Dead letter: {message.MessageId} ({message.MessageType})");
}

// Move a specific message back to the live outbox
await outboxStore.RequeueDeadLetterAsync("message-id-to-requeue", ct);
```

The requeue operation:

- Removes the message from the dead-letter table
- Inserts it back into the outbox with `attempt_count = 0` and `next_attempt_utc` set to now
- Makes it immediately eligible for the next dispatch cycle

If the message ID does not exist in the dead-letter table, the call is a no-op.

Effect: at-least-once delivery with bounded retries and deterministic dead-lettering.

---

## 10. Hosted Worker (Background Dispatch)

Run dispatch continuously with `AddOutboxWorker`.

```csharp
services.AddOutboxWorker(options =>
{
    options.BatchSize = 100;
    options.PollInterval = TimeSpan.FromSeconds(5);
    // options.MaxIterations = 10; // Optional for bounded runs/tests
});
```

Typical production setup:

```csharp
services.AddSharpCoreDB();
services.AddSharpCoreDBCqrs();
services.AddPersistentOutbox();
services.AddSingleton<IOutboxPublisher, ConsoleOutboxPublisher>();
services.AddOutboxWorker(opts =>
{
    opts.BatchSize = 50;
    opts.PollInterval = TimeSpan.FromSeconds(10);
});
```

---

## 11. Recommended Usage Pattern

1. Handle command.
2. Persist aggregate/events.
3. Write integration event to outbox.
4. Let dispatcher/worker publish externally.
5. On success: mark published.
6. On failure: record retry metadata and reschedule.

This keeps command-side transactions isolated from external transport failures.

---

## 12. Testing Guidance

Recommended tests for CQRS integrations:

- Handler behavior tests (`Success` / validation failures)
- Dispatcher tests (registered/unregistered handlers)
- Outbox tests:
  - add/read lifecycle
  - mark published behavior
  - failure recording updates retry metadata
  - scheduled messages excluded until due
  - legacy schema compatibility
- Background worker tests:
  - periodic dispatch
  - cancellation propagation
  - exception logging/continuation

The repository contains xUnit v3 coverage for these paths in `tests/SharpCoreDB.CQRS.Tests`.

