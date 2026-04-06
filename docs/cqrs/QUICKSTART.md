# SharpCoreDB.CQRS Quickstart (v1.7.0)

This quickstart shows the fastest way to run CQRS + outbox dispatch with `SharpCoreDB.CQRS`.

## 1) Install

```bash
dotnet add package SharpCoreDB.CQRS --version 1.7.0
```

## 2) Define a command and handler

```csharp
using SharpCoreDB.CQRS;

public readonly record struct PlaceOrderCommand(string OrderId, decimal Amount) : ICommand;

public sealed class PlaceOrderCommandHandler : ICommandHandler<PlaceOrderCommand>
{
    public Task<CommandDispatchResult> HandleAsync(PlaceOrderCommand command, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(command.OrderId) || command.Amount <= 0)
        {
            return Task.FromResult(CommandDispatchResult.Failure("Invalid order command."));
        }

        return Task.FromResult(CommandDispatchResult.Success());
    }
}
```

## 3) Configure services

```csharp
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB;
using SharpCoreDB.CQRS;

var services = new ServiceCollection();
services.AddSharpCoreDB();
services.AddSharpCoreDBCqrs();

services.AddOutboxRetryPolicy(options =>
{
    options.BaseDelay = TimeSpan.FromSeconds(1);
    options.MaxDelay = TimeSpan.FromSeconds(30);
    options.MaxAttempts = 3;
    options.DeadLetterTableName = "scdb_outbox_deadletter";
});

services.AddCommandHandler<PlaceOrderCommand, PlaceOrderCommandHandler>();
services.AddPersistentOutbox();
services.AddSingleton<IOutboxPublisher, ConsoleOutboxPublisher>();
services.AddOutboxWorker(options =>
{
    options.BatchSize = 50;
    options.PollInterval = TimeSpan.FromSeconds(5);
});

var provider = services.BuildServiceProvider();
```

## 4) Dispatch a command

```csharp
var dispatcher = provider.GetRequiredService<ICommandDispatcher>();
var result = await dispatcher.DispatchAsync(new PlaceOrderCommand("order-1", 199.99m), ct);

Console.WriteLine($"Dispatch Success: {result.Success}");
```

## 5) Write and dispatch outbox messages

```csharp
var store = provider.GetRequiredService<IOutboxStore>();
await store.AddAsync(new OutboxMessage(
    MessageId: Guid.NewGuid().ToString("N"),
    AggregateId: "order-1",
    MessageType: "OrderPlaced",
    Payload: "{}"u8.ToArray(),
    CreatedAtUtc: DateTimeOffset.UtcNow,
    IsPublished: false), ct);

var dispatch = provider.GetRequiredService<OutboxDispatchService>();
var published = await dispatch.DispatchUnpublishedAsync(100, ct);

Console.WriteLine($"Published: {published}");
```

## 6) Inspect and requeue dead-letter messages

```csharp
var outboxStore = provider.GetRequiredService<IOutboxStore>();
var deadLetters = await outboxStore.GetDeadLettersAsync(100, ct);

Console.WriteLine($"DeadLetters: {deadLetters.Count}");

// After fixing the root cause, requeue a specific message for retry
if (deadLetters.Count > 0)
{
    await outboxStore.RequeueDeadLetterAsync(deadLetters[0].MessageId, ct);
    Console.WriteLine($"Requeued: {deadLetters[0].MessageId}");
}
```

## 7) Minimal publisher example

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

## 8) ES-Outbox bridge (auto-publish domain events)

Bridge events raised inside an `AggregateRoot` directly into the outbox:

```csharp
using SharpCoreDB.CQRS;
using SharpCoreDB.EventSourcing;

// Register the bridge alongside existing CQRS services
services.AddSharpCoreDBCqrs();
services.AddOutboxEventPublisher();
services.AddSingleton<IOutboxPublisher, ConsoleOutboxPublisher>();
services.AddOutboxWorker(opts => opts.PollInterval = TimeSpan.FromSeconds(5));

var provider = services.BuildServiceProvider();
var publisher = provider.GetRequiredService<IOutboxEventPublisher>();

// Raise events and publish them to the outbox in one step
var order = new OrderAggregate();
order.PlaceOrder("order-42", 99.99m);

var published = await order.PublishPendingEventsToOutboxAsync("order-42", publisher, ct);
Console.WriteLine($"Published {published} event(s) to outbox");
```

Duplicate message IDs are automatically rejected by the outbox store, ensuring idempotent delivery.

## Next

- Full guide: `docs/cqrs/README.md`
- Package overview: `src/SharpCoreDB.CQRS/README.md`
