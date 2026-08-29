# 9. Server Mode

> The `SharpCoreDB.Server` family turns the embedded engine into a production network database.
> Deep dives: [`docs/server/README.md`](../server/README.md) ·
> [`docs/server/QUICKSTART.md`](../server/QUICKSTART.md) ·
> [`docs/server/REST_API.md`](../server/REST_API.md) ·
> [`docs/server/SECURITY.md`](../server/SECURITY.md)

---

## 9.1 Protocols

| Protocol | Transport | Status |
|----------|-----------|--------|
| **gRPC** | HTTP/2 + HTTP/3, TLS 1.2+ | Primary |
| REST | HTTPS | Supported |
| Binary TCP | custom framed protocol (`docs/server/BINARY_PROTOCOL_SPEC.md`) | Supported |
| WebSocket | streaming | Supported |

## 9.2 Hosting

- Docker container, Windows Service, Linux systemd, macOS launchd
- Multi-database hosting with system databases: `master`, `msdb`, `tempdb`
- Connection pooling (1,000+ concurrent connections), graceful shutdown

See [`docs/server/INSTALLATION.md`](../server/INSTALLATION.md).

## 9.3 Security

- TLS 1.2+ enforced — no plain-HTTP endpoints
- JWT authentication, optional mTLS
- RBAC roles: Admin / Writer / Reader
- **Row-Level Security** — `RowLevelPolicyEngine`, `Enforced`/`Audit` modes, per-tenant
  discriminator-column filtering
- Rate limiting (fixed-window, per-IP, configurable)
- System-database security: [`docs/server/SYSTEM_DATABASES_SECURITY.md`](../server/SYSTEM_DATABASES_SECURITY.md)

## 9.4 Multitenancy

- Per-tenant databases and shared-schema tenancy with RLS
- Tenant-scoped backup/restore and operations runbook
- Reference + threat model:
  [`docs/server/MULTITENANT_SAAS_REFERENCE_v1.7.0.md`](../server/MULTITENANT_SAAS_REFERENCE_v1.7.0.md) ·
  [`docs/server/MULTITENANT_THREAT_MODEL_v1.7.0.md`](../server/MULTITENANT_THREAT_MODEL_v1.7.0.md)

## 9.5 Observability

- Prometheus-compatible metrics endpoint, health checks
- OpenTelemetry-ready projection metrics
- Structured logs; setup guide:
  [`docs/server/OBSERVABILITY_SETUP_v1.7.0.md`](../server/OBSERVABILITY_SETUP_v1.7.0.md)

## 9.6 Client access

- `.NET` gRPC clients (`docs/server/CLIENT_GUIDE.md`)
- ODBC/JDBC strategy for BI tools:
  [`docs/server/ODBC_JDBC_STRATEGY.md`](../server/ODBC_JDBC_STRATEGY.md)
- Admin tooling: [`docs/server/ADMIN_TOOLING_GUIDE.md`](../server/ADMIN_TOOLING_GUIDE.md)
