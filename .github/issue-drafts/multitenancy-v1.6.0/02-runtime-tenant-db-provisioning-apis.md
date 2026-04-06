## Summary
Add runtime tenant provisioning/deprovisioning APIs (gRPC + REST) to create and manage tenant databases.

## Why
Tenant DBs are currently startup-config oriented; SaaS needs runtime onboarding from C#.

## Scope
- New endpoints:
  - `CreateTenantDatabase`
  - `DeleteTenantDatabase`
  - `GetTenantProvisioningStatus`
- Idempotency key support.
- Validation and conflict handling.

## Implementation Plan
1. Define protocol contracts for gRPC and REST.
2. Implement orchestration service for provisioning workflow.
3. Wire API surface to orchestration service.
4. Add retries + safe rollback for partial failures.
5. Add integration tests for success/conflict/idempotency.

## Acceptance Criteria
- Tenant DB can be created at runtime via API.
- Repeated create with same idempotency key is safe.
- Errors are actionable and auditable.

## Dependencies
- Depends on tenant catalog and runtime registry APIs.
