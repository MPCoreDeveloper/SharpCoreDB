# SharpCoreDB Server Observability Setup v1.7.0

## Purpose
This guide provides a baseline observability setup for SharpCoreDB Server diagnostics workflows using built-in health and metrics APIs.

## Endpoint Baseline
- `GET /api/v1/health` (open): quick health + triage checks
- `GET /api/v1/health/detailed` (open): enriched health diagnostics and protocol counters
- `GET /api/v1/metrics` (JWT): actionable request, latency, error, and protocol telemetry
- `GET /health` (open): ASP.NET Core health probe

## KPI Set (Phase 05)
Use these KPIs as your baseline dashboard and alert dimensions:

1. Request volume
   - `totalRequests`
   - `queryRequests`
   - `nonQueryRequests`
2. Reliability
   - `failedRequests`
   - `errorRatePercent`
   - `lastFailureCode`
3. Performance
   - `averageLatencyMs`
   - `totalRowsReturned`
4. Capacity
   - `activeConnections`
   - `activeSessions`
   - `memoryUsageMb`
5. Protocol behavior
   - `protocolMetrics.grpc.*`
   - `protocolMetrics.rest.*`
   - `protocolMetrics.binary.*`

## Prometheus Scraping Baseline
SharpCoreDB currently exposes observability as JSON payloads. For Prometheus, use a JSON exporter (or equivalent bridge) and map fields from `/api/v1/metrics`.

### Example `prometheus.yml`
```yaml
global:
  scrape_interval: 15s

scrape_configs:
  - job_name: sharpcoredb-json
    metrics_path: /probe
    static_configs:
      - targets:
          - https://localhost:8443/api/v1/metrics
    params:
      module: [sharpcoredb_metrics]
    relabel_configs:
      - source_labels: [__address__]
        target_label: __param_target
      - target_label: __address__
        replacement: json-exporter:7979
```

### Example JSON exporter module
```yaml
modules:
  sharpcoredb_metrics:
    metrics:
      - name: sharpcoredb_total_requests
        path: "{.totalRequests}"
      - name: sharpcoredb_failed_requests
        path: "{.failedRequests}"
      - name: sharpcoredb_error_rate_percent
        path: "{.errorRatePercent}"
      - name: sharpcoredb_avg_latency_ms
        path: "{.averageLatencyMs}"
      - name: sharpcoredb_active_connections
        path: "{.activeConnections}"
      - name: sharpcoredb_active_sessions
        path: "{.activeSessions}"
      - name: sharpcoredb_total_rows_returned
        path: "{.totalRowsReturned}"
```

## Dashboard Baseline Panels
Use these initial Grafana panels for admin and operations triage:

1. **Request Throughput**
   - Query: `rate(sharpcoredb_total_requests[1m])`
2. **Error Rate (%)**
   - Query: `avg_over_time(sharpcoredb_error_rate_percent[5m])`
3. **Latency (ms)**
   - Query: `avg_over_time(sharpcoredb_avg_latency_ms[5m])`
4. **Active Sessions / Connections**
   - Query: `sharpcoredb_active_sessions`, `sharpcoredb_active_connections`
5. **Rows Returned Trend**
   - Query: `rate(sharpcoredb_total_rows_returned[1m])`

## Alerting Baseline
Suggested starting alert thresholds:

- `errorRatePercent >= 10` for 5 minutes
- `averageLatencyMs >= 250` for 5 minutes
- `activeSessions` above expected steady-state capacity
- `checks.request_errors == degraded` from `/api/v1/health`
- `checks.databases == degraded` from `/api/v1/health`

## Operational Notes
- Keep API authentication enabled for `/api/v1/metrics` in production.
- Restrict health endpoint exposure using network policy or gateway rules.
- Correlate spikes in `binary.totalMessages` with protocol client behavior during GUI certification runs.
- Track `lastFailureCode` changes to detect new regression classes quickly.
