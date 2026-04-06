## Summary
Implement tenant-aware audit trail and security event logging.

## Status
Completed in workspace.

## Completed Implementation Notes
- Added tenant security audit event model, in-memory store, sink abstraction, and emission service.
- Emitted security events from login, connect, grant change, provisioning, and denied-access paths.
- Added secured diagnostics endpoint for tenant security audit inspection.
- Added operational documentation for retention and export guidance.

## Validation
- Targeted integration tests passed: `TenantSecurityAuditTests` and related auth/authorization audit tests.
- Workspace build passed.

## Why
Production SaaS requires traceability for auth, authorization, and data-access events.

## Scope
- Audit events for: login, connect, grant changes, provisioning, denied access.
- Include tenant id, database name, principal, protocol, timestamp, decision reason.
- Export/retention guidance in docs.

## Implementation Plan
1. Define audit event schema.
2. Emit events from auth/provisioning/grant enforcement paths.
3. Add sink integration points and structured logging.
4. Add tests validating critical audit event emission.

## Acceptance Criteria
- Security-relevant actions generate tenant-aware audit records.
- Denied operations are fully traceable.
- Documentation includes operational usage guidance.

## Dependencies
- Cross-cutting dependency on auth and provisioning issues.
