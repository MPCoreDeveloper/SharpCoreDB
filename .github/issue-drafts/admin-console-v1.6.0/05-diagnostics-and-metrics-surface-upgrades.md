## Summary
Upgrade diagnostics surfaces for admin and monitoring workflows.

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
