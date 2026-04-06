# SharpCoreDB.CQRS

Optional CQRS primitives for SharpCoreDB EventSourcing.

## What this package is for

`SharpCoreDB.CQRS` is the package you add when you want a lightweight command-side layer around your domain model without committing to a large CQRS framework.

Use it when your application needs one or more of these capabilities:

- Explicit command contracts such as `PlaceOrderCommand`
- Command handler registration and dispatching
- An aggregate base type that collects pending domain events
- An outbox for reliable delivery of integration messages
- Retry, dead-letter, and hosted worker support for background outbox dispatch
- A bridge from aggregate-raised domain events into outbox messages

## What this package does exactly

This package is responsible for the command and outbox side of a CQRS-style architecture.

It gives you:

- **Command contracts and handlers** through `ICommand` and `ICommandHandler<TCommand>`
- **Command dispatchers** for in-memory and DI-backed execution
- **AggregateRoot** support for collecting pending domain events before persistence or publication
- **Outbox storage abstractions** with in-memory and persistent SharpCoreDB-backed implementations
- **Outbox dispatch orchestration** through `OutboxDispatchService`
- **Retry and dead-letter support** for failed message delivery
- **Hosted background dispatch** for continuous unpublished-message processing
- **Aggregate-to-outbox bridging** so domain events can be published reliably after command handling

## What this package does not do

This package does **not** try to become a complete application framework.

It does not provide:

- MediatR as a required dependency
- HTTP endpoint conventions
- A broker-specific transport implementation
- Event persistence itself
- Projection execution and checkpointing

For event persistence use `SharpCoreDB.EventSourcing`. For read-side processing use `SharpCoreDB.Projections`.

## When to use this package

Choose `SharpCoreDB.CQRS` when you want clear command boundaries and reliable message publication, while still keeping the rest of your architecture under your control.

Typical scenarios:

- Splitting write-side commands from read-side queries
- Collecting aggregate domain events during command handling
- Publishing integration events through an outbox with at-least-once delivery behavior
- Running a lightweight command pipeline without adding an external CQRS framework

## v1.7.0 Highlights

This guide matches the synchronized `1.7.0` package release and documents the current CQRS feature set, including the persistent SharpCoreDB-backed outbox, retry and dead-letter handling, the hosted outbox worker, and the outbox bridge for aggregate domain events.

## Full Documentation

For a complete CQRS guide with end-to-end examples, see:

- `docs/cqrs/README.md`
- `docs/cqrs/QUICKSTART.md`

## Scope

This package provides low-level CQRS building blocks for:

- Command contracts and handlers
- Command dispatcher abstraction
- Aggregate root base type for pending domain events
- Outbox message contracts with in-memory and persistent stores
- Dependency injection extensions for handler registration and command dispatch
- Outbox dispatch service with pluggable publisher contract
- Hosted outbox dispatch background worker

## Non-goals

This package does **not** enforce MediatR usage, HTTP API conventions, or transport-specific messaging integrations.

## Install

```bash
dotnet add package SharpCoreDB.CQRS --version 1.7.0
```

## Quick Start

```csharp
using SharpCoreDB.CQRS;

var dispatcher = new InMemoryCommandDispatcher();
dispatcher.RegisterHandler(new PlaceOrderCommandHandler());

var result = await dispatcher.DispatchAsync(new PlaceOrderCommand("order-1", 199.99m), ct);
Console.WriteLine(result.Success);
```

## Dependency Injection Command Dispatch

```csharp
services.AddSharpCoreDBCqrs();
services.AddCommandHandler<PlaceOrderCommand, PlaceOrderCommandHandler>();

var provider = services.BuildServiceProvider();
var dispatcher = provider.GetRequiredService<ICommandDispatcher>();

var dispatch = await dispatcher.DispatchAsync(new PlaceOrderCommand("order-1", 199.99m), ct);
```

## In-Memory Outbox Dispatch

```csharp
services.AddSharpCoreDBCqrs();
services.AddSingleton<IOutboxPublisher, KafkaOutboxPublisher>();

var provider = services.BuildServiceProvider();
var outboxDispatch = provider.GetRequiredService<OutboxDispatchService>();

var published = await outboxDispatch.DispatchUnpublishedAsync(100, ct);
```

## Persistent Outbox (SharpCoreDB-backed)

Replace the default in-memory store with a durable store backed by a SharpCoreDB table:

```csharp
services.AddSharpCoreDB();
services.AddSharpCoreDBCqrs();
services.AddPersistentOutbox(); // default table: "scdb_outbox"
// or with a custom table name:
services.AddPersistentOutbox("my_outbox_table");
```

The outbox table is created automatically on first use.

## Persistent Outbox Retry Policy and Dead-Letter

Configure retry policy and dead-letter behavior:

```csharp
services.AddSharpCoreDBCqrs();
services.AddOutboxRetryPolicy(options =>
{
    options.BaseDelay = TimeSpan.FromSeconds(1);
    options.MaxDelay = TimeSpan.FromMinutes(2);
    options.MaxAttempts = 5;
    options.DeadLetterTableName = "scdb_outbox_deadletter";
});
```

Read dead-lettered messages:

```csharp
var outboxStore = provider.GetRequiredService<IOutboxStore>();
var deadLetters = await outboxStore.GetDeadLettersAsync(100, ct);
```

Requeue a dead-lettered message after fixing the root cause:

```csharp
// Move back to the outbox with attempt_count reset to 0
await outboxStore.RequeueDeadLetterAsync("message-id-to-requeue", ct);
```

## Hosted Outbox Dispatch Worker

Register a background service that automatically polls and dispatches unpublished messages:

```csharp
services.AddSharpCoreDBCqrs();
services.AddSingleton<IOutboxPublisher, KafkaOutboxPublisher>();
services.AddOutboxWorker(opts =>
{
    opts.BatchSize = 50;
    opts.PollInterval = TimeSpan.FromSeconds(5);
});
```

Combine with persistent outbox for fully durable at-least-once delivery:

```csharp
services.AddSharpCoreDB();
services.AddSharpCoreDBCqrs();
services.AddPersistentOutbox();
services.AddSingleton<IOutboxPublisher, KafkaOutboxPublisher>();
services.AddOutboxWorker(opts => opts.PollInterval = TimeSpan.FromSeconds(10));
```

## Aggregate Root

```csharp
public class OrderAggregate : AggregateRoot
{
    public void PlaceOrder(string orderId, decimal amount)
    {
        RaiseEvent(new OrderPlaced(orderId, amount));
    }
}

var order = new OrderAggregate();
order.PlaceOrder("order-1", 199.99m);

// Collect and clear pending events
var events = order.DequeuePendingEvents();
```

## ES-Outbox Bridge

Bridge domain events from `AggregateRoot` into the outbox for reliable delivery. Events raised via `RaiseEvent` are converted to `OutboxMessage` entries with deterministic idempotent message IDs.

### Register the bridge

```csharp
services.AddSharpCoreDBCqrs();
services.AddOutboxEventPublisher();
services.AddSingleton<IOutboxPublisher, KafkaOutboxPublisher>();
```

### Publish pending events to the outbox

```csharp
var publisher = provider.GetRequiredService<IOutboxEventPublisher>();

var order = new OrderAggregate();
order.PlaceOrder("order-1", 199.99m);

// Publish all pending events to the outbox and clear the list
var published = await order.PublishPendingEventsToOutboxAsync("order-1", publisher, ct);
Console.WriteLine($"Published {published} event(s) to outbox");
```

The outbox dispatch worker then picks up these messages for downstream delivery, providing at-least-once guarantees.

### Idempotency

Both `InMemoryOutboxStore` and `SharpCoreDbOutboxStore` reject duplicate message IDs automatically. `AddAsync` returns `false` when a duplicate is detected, preventing double-publish even under retries.
