# SharpCoreDB.Projections OpenTelemetry Metrics (v1.7.0 / V 1.70)

This document describes the projection metrics emitted by `OpenTelemetryProjectionMetrics`.

## Meter

- Name: `SharpCoreDB.Projections`
- Version: `1.7.0`

## Instruments

- `sharpcoredb.projections.runs.total` (`Counter<long>`)
- `sharpcoredb.projections.events_processed.total` (`Counter<long>`)
- `sharpcoredb.projections.runs.failed` (`Counter<long>`)
- `sharpcoredb.projections.run.duration_ms` (`Histogram<double>`)
- `sharpcoredb.projections.lag.events` (`Histogram<long>`)
- `sharpcoredb.projections.checkpoint.age_ms` (`Histogram<double>`)

## Tags

All instruments emit these tags:

- `projection`
- `database`
- `tenant`

## Server export guidance

When integrating with `SharpCoreDB.Server`, include meter `SharpCoreDB.Projections` in your OpenTelemetry meter provider, then export to Prometheus/OpenTelemetry collector.
