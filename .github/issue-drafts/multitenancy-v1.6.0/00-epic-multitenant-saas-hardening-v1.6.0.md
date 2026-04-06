## Summary
Deliver first-class multi-tenant SaaS support in `SharpCoreDB.Server` on top of current `v1.6.0` capabilities.

## Status
**State:** RESOLVED for the current multitenancy draft set in the repository.

Resolved workstreams in repository:
- [x] `09-tenant-encryption-key-management.md`
- [x] `10-per-tenant-quotas-and-limits.md`
- [x] `11-tenant-audit-and-security-events.md`
- [x] `12-saas-sample-docs-and-threat-model.md`
- [x] `13-tenant-ops-backup-restore-migrate.md`

Roadmap note:
- [x] Multitenancy roadmap implementation complete in the current draft set.

Current baseline in codebase:
- Multi-database hosting in one server instance is available (`DatabaseRegistry`, `ServerConfiguration.Databases`).
- System databases (`master`, `model`, `msdb`, `tempdb`) are available.
- AuthN/AuthZ exists (JWT + mTLS + RBAC), but is role-centric and not tenant/database-scoped.
- No first-class runtime tenant provisioning lifecycle API yet.

## Goals
1. Support SaaS pattern: central catalog + one database per tenant.
2. Add runtime tenant provisioning/deprovisioning API (C# and server endpoints).
3. Enforce hard tenant isolation with tenant/database-scoped authorization.
4. Provide operational controls: quotas, encryption keys, auditing, backup/restore.
5. Publish production docs and a reference SaaS sample.

## Non-Goals
- Breaking protocol compatibility for existing clients.
- Forcing shared-database mode as default.

## Workstreams (linked issues)
- Tenant catalog in `master`
- Runtime tenant DB provisioning API
- Runtime attach/detach lifecycle in `DatabaseRegistry`
- Per-database grants model
- Connect/session enforcement of grants
- JWT claim model for tenant/database scope
- Tenant-aware interceptor enforcement (gRPC/REST/binary)
- Optional row-level policy engine for shared DB mode
- Per-tenant encryption key provider + rotation hooks
- Per-tenant quotas and limits
- Tenant security/audit events
- SaaS sample + docs + threat model

## Milestones
- M1: Foundation (catalog + provisioning + runtime registry)
- M2: Security hardening (grants + claims + enforcement)
- M3: Advanced isolation and operations (RLS option + key mgmt + quotas + audit)
- M4: Productization (docs, sample, migration/adoption guide)

## Acceptance Criteria
- All child issues closed.
- Existing `v1.6.0` functionality remains backward compatible.
- Targeted integration tests pass for multi-tenant scenarios.
- Docs published under `docs/server` and linked from package readmes.

## Risks
- Backward-compatibility drift in auth/session flow.
- Ambiguity between server-level role RBAC and tenant-level grants.
- Operational complexity around key rotation and per-tenant quotas.

## Definition of Done
- Feature complete + tests + docs + migration notes.
- Performance and security review completed.
- Release notes prepared for next minor/patch release.
