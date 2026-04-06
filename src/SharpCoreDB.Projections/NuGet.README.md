# SharpCoreDB.Projections v1.7.0

Optional projection package for SharpCoreDB Event Sourcing.

## What this package is for

Use this package when you want to consume stored events and turn them into read models, dashboards, denormalized views, or other derived state.

## What this package does exactly

- Registers and executes projections
- Persists projection checkpoints so catch-up can resume safely
- Supports in-memory and SharpCoreDB-backed checkpoint stores
- Runs projections inline or in a hosted background worker
- Exposes projection metrics for diagnostics and OpenTelemetry export

## What this package does not do

- It does not store events
- It does not dispatch commands
- It does not impose a full CQRS framework

## Highlights in v1.7.0

- Synchronized with the SharpCoreDB `1.7.0` package line
- Documents durable SharpCoreDB-backed checkpoints and hosted worker support
- Includes OpenTelemetry-ready projection metrics in the baseline package guidance

## Included in this scaffold

- Projection registration with `ProjectionBuilder`
- Projection execution context contracts
- Projection checkpoint model and store contract
- In-memory checkpoint store
- SharpCoreDB-backed checkpoint store
- Inline projection runner
- PeriodicTimer-based background projection worker
- `IServiceCollection` registration extensions
- Hosted worker hook for server-ready background projection execution
- Lightweight metrics contracts (`IProjectionMetrics`) and in-memory metrics collector
- OpenTelemetry metrics adapter (`OpenTelemetryProjectionMetrics`) for Prometheus/OTel pipelines
- Projection execution mode and runner options

## Installation

```bash
dotnet add package SharpCoreDB.Projections --version 1.7.0
