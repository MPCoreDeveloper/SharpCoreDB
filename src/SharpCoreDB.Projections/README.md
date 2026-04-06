# SharpCoreDB.Projections

Optional projection primitives for SharpCoreDB Event Sourcing.

## What this package is for

`SharpCoreDB.Projections` is the package you add when stored events must be turned into queryable read models, dashboards, materialized views, denormalized tables, or integration-facing state.

Use it when your application needs one or more of these capabilities:

- Catch up from the global event feed and build read models
- Track how far a projection has processed with durable checkpoints
- Run projections inline or in a background worker
- Expose projection health and throughput through in-memory or OpenTelemetry metrics
- Rebuild derived state after schema or projection logic changes

## What this package does exactly

This package is responsible for the projection execution layer.

It gives you:

- **Projection registration and discovery** through `ProjectionBuilder` and DI extensions
- **Projection runners** that process events and invoke projection handlers
- **Checkpoint storage** so projections can resume from the last processed global sequence
- **In-memory and SharpCoreDB-backed checkpoint stores**
- **Background worker orchestration** for continuous catch-up processing
- **Execution mode options** for inline and hosted scenarios
- **Projection metrics** for local diagnostics or OpenTelemetry export

## What this package does not do

This package does **not** replace the event store or the command layer.

It does not provide:

- Event persistence itself
- Command dispatching
- Aggregate orchestration
- A required messaging transport
- An opinionated CQRS application framework

For event storage use `SharpCoreDB.EventSourcing`. For command-side and outbox primitives use `SharpCoreDB.CQRS`.

## When to use this package

Choose `SharpCoreDB.Projections` when you already have events and now need derived read-side state.

Typical scenarios:

- Building order summaries, search models, or reporting tables from domain events
- Maintaining tenant-specific read models in the background
- Replaying all events into a new read model after a deployment
- Measuring projection lag, throughput, and failure behavior

## v1.7.0 Highlights

This guide matches the synchronized `1.7.0` package release and documents the current projection feature set: durable SharpCoreDB-backed checkpoints, background worker orchestration, execution mode options, and OpenTelemetry-ready metrics.

## Scope

This package provides low-level projection contracts for:

- Projection registration and discovery
- Inline/background projection execution options
- Checkpoint persistence abstractions
- In-memory checkpoint storage for local processing
- SharpCoreDB-backed checkpoint persistence for durable catch-up
- Periodic background worker orchestration
- Lightweight projection metrics contracts and in-memory metrics collection
- OpenTelemetry metrics adapter for Prometheus/OTel export pipelines

## Non-goals

This package does **not** ship opinionated CQRS handlers, MediatR integrations, or transport-specific subscription pipelines.

## Install

```bash
dotnet add package SharpCoreDB.Projections --version 1.7.0
```

## Quick Start

```csharp
using SharpCoreDB.EventSourcing;
using SharpCoreDB.Projections;

var projectionBuilder = new ProjectionBuilder()
    .AddProjection<OrderReadModelProjection>();

var checkpointStore = new InMemoryProjectionCheckpointStore();
var runner = new InlineProjectionRunner(checkpointStore);

var worker = new BackgroundProjectionWorker(
    runner,
    new ProjectionEngineOptions
    {
        ExecutionMode = ProjectionExecutionMode.Background,
        BatchSize = 1000,
        PollInterval = TimeSpan.FromMilliseconds(250),
        RunOnStart = true,
    });

var processed = await worker.RunAsync(
    eventStore,
    [new OrderReadModelProjection()],
    databaseId: "main",
    tenantId: "tenant-a",
    cancellationToken: ct);
```

## Dependency Injection and Hosted Worker

```csharp
services.AddSingleton<IEventStore>(eventStore);

services.AddSharpCoreDBProjections(options =>
{
    options.BatchSize = 1000;
    options.PollInterval = TimeSpan.FromMilliseconds(250);
    options.RunOnStart = true;
    options.MaxIterations = null;
});

services.AddProjection<OrderReadModelProjection>();
services.UseSharpCoreDBProjectionCheckpoints(); // Requires IDatabase registration
services.AddSharpCoreDBProjectionHostedWorker(options =>
{
    options.DatabaseId = "main";
    options.TenantId = "tenant-a";
    options.FromGlobalSequence = 1;
});

services.UseOpenTelemetryProjectionMetrics(); // Emits Meter: SharpCoreDB.Projections

var metrics = services.BuildServiceProvider().GetRequiredService<IProjectionMetrics>();
```

## Persistent Checkpoint Store

```csharp
using SharpCoreDB.Projections;

var checkpointStore = new SharpCoreDbProjectionCheckpointStore(database);
await checkpointStore.SaveCheckpointAsync(
    new ProjectionCheckpoint("OrdersProjection", "main", "tenant-a", 1200, DateTimeOffset.UtcNow),
    ct);
```

## Metrics Snapshot Access (In-Memory)

```csharp
var metrics = new InMemoryProjectionMetrics();
var runner = new InlineProjectionRunner(checkpointStore, metrics);

var snapshot = metrics.GetSnapshot("OrderReadModelProjection", "main", "tenant-a");
```

## Notes

`SharpCoreDB.Projections` is optional and remains separate from `SharpCoreDB` core to preserve minimal dependencies.

## OpenTelemetry / Prometheus Export

`OpenTelemetryProjectionMetrics` emits projection metrics on meter `SharpCoreDB.Projections`.

For instrument names and tags, see `OPEN_TELEMETRY_METRICS.md`.

## SharpCoreDB.Server Integration (Optional)

```json
{
  "Server": {
    "Projections": {
      "Enabled": true,
      "EnableHostedWorker": true,
      "UsePersistentCheckpoints": true,
      "UseOpenTelemetryMetrics": true,
      "DatabaseName": "app",
      "CheckpointTableName": "scdb_projection_checkpoints",
      "RuntimeDatabaseId": "main",
      "RuntimeTenantId": "default",
      "FromGlobalSequence": 1,
      "BatchSize": 1000,
      "PollIntervalMilliseconds": 250,
      "RunOnStart": true
    }
  }
}
