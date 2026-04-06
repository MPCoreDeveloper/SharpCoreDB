## Summary
Upgrade diagnostics surfaces for admin and monitoring workflows.

## Status
**State:** RESOLVED ✅
**Completed:** 2026-04-06

### Delivered
- Expanded server metrics collector to include actionable in-memory telemetry snapshots:
  - request volume (`totalRequests`, query/non-query split)
  - reliability (`failedRequests`, `errorRatePercent`, `lastFailureCode`)
  - performance (`averageLatencyMs`, row and network byte totals)
  - protocol counters (`grpc`, `rest`, `binary` connection/request/message/error stats)
- Added protocol-level instrumentation:
  - binary protocol connection/message/error tracking in `BinaryProtocolHandler`
  - gRPC `Connect`, `Disconnect`, and `ExecuteNonQuery` request/latency/error metrics
- Upgraded health payloads for faster triage:
  - `HealthCheckService` now emits check statuses, error rate summary, recent failure signal, and per-protocol diagnostics
  - `/api/v1/health` now includes triage check map and reliability fields
- Upgraded `/api/v1/metrics` payload with actionable telemetry fields and protocol metrics map
- Added observability contract tests:
  - `ObservabilityContractTests.GetSnapshot_AfterTraffic_ShouldExposeActionableCounters`
  - `ObservabilityContractTests.GetDetailedHealth_WhenErrorsPresent_ShouldSurfaceTriageSignals`
- Added operator documentation:
  - `docs/server/OBSERVABILITY_SETUP_v1.7.0.md` with Prometheus JSON-scrape baseline and dashboard panel queries
  - Updated `docs/server/REST_API.md` and `docs/server/QUICKSTART.md` examples for upgraded diagnostics contracts

## Why
Developers and operators need reliable health and metrics insight without deep internals access.

## Scope
- Expand metrics coverage (connections, queries, latency, errors, protocol-level counters).
- Improve health endpoints with richer status payloads.
- Document Prometheus scraping and dashboards baseline.

## Implementation Plan
1. Define diagnostics KPI set for server/admin use.
2. Extend metrics collector and endpoint payloads.
3. Add tests for metrics/health contracts.
4. Publish observability setup guide.

## Acceptance Criteria
- Metrics endpoints provide actionable server telemetry.
- Health endpoint supports quick triage.
- Docs include sample dashboard queries/panels.

## Dependencies
- Independent but supports all UI/tooling tracks.
