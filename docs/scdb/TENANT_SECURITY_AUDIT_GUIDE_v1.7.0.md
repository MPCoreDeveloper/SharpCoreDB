# Tenant Security Audit Guide (v1.7.0)

## Overview
SharpCoreDB Server emits tenant-aware security audit events for login, connect, grant changes, provisioning, and denied access decisions.

## Event Fields
Each event includes:
- `timestamp_utc`
- `event_type`
- `tenant_id`
- `database_name`
- `principal`
- `protocol`
- `is_allowed`
- `decision_code`
- `reason`

## Runtime Access
Use the secured endpoint:
- `GET /api/v1/tenant-security/audit?maxCount=100`
- Requires `admin` role.

## Retention
- In-memory retention is bounded by `TenantSecurityAuditStore` capacity.
- For long-term retention, attach one or more `ITenantSecurityAuditSink` implementations.

## Export Guidance
Recommended production export targets:
- SIEM (Microsoft Sentinel, Splunk, Elastic)
- Audit data lake with immutable retention policy
- Regulated archive storage for compliance windows

## Operational Notes
- Denied operations are tracked with explicit `decision_code` values.
- Provisioning and grant changes emit auditable control-plane events.
- Keep clock synchronization enabled (NTP) for cross-system trace correlation.
