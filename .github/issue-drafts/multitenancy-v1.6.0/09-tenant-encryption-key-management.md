## Summary
Introduce per-tenant encryption key integration and rotation hooks.

## Status
Completed in workspace.

## Completed Implementation Notes
- Added `ITenantEncryptionKeyProvider` and `ConfigurationTenantEncryptionKeyProvider`.
- Added tenant encryption config contracts in `ServerConfiguration.Security`.
- Persisted encryption metadata in tenant database mappings.
- Integrated key resolution into tenant provisioning and runtime registration.
- Added `TenantEncryptionKeyRotationService` with rollback-aware rotation flow.
- Exposed encryption key reference and rotation hooks in tenant API surfaces.

## Validation
- Targeted integration tests passed: `TenantEncryptionKeyManagementTests`.
- Workspace build passed.

## Why
Tenant-level security/compliance often requires key separation and rotation support.

## Scope
- Key provider abstraction for tenant DB encryption keys.
- Rotation workflow and rekey support.
- KMS integration extension points.

## Implementation Plan
1. Define key provider interface and config contract.
2. Map tenant->key reference in catalog.
3. Add rotation orchestration and status tracking.
4. Add tests for key retrieval failures and rotation safety.

## Acceptance Criteria
- Tenant DB can be provisioned with dedicated key references.
- Rotation flow is auditable and recoverable.
- Existing non-tenant key path remains supported.

## Dependencies
- Depends on tenant catalog and provisioning flow.
