## Summary
Add per-tenant operational tooling for backup, restore, and migration workflows.

## Status
**State:** RESOLVED

Completed in workspace. Pending git commit/push for the latest phase-13 changes in the current working tree.

## Completed Implementation Notes
- Added tenant-scoped backup and restore operation contracts.
- Implemented `TenantBackupRestoreService` with validation and rollback handling.
- Added `TenantMigrationPlanningService` with migration steps and execution hooks.
- Exposed tenant backup, restore, and migration-plan REST endpoints.
- Published operational guidance in `docs/server/MULTITENANT_BACKUP_RESTORE_MIGRATION_v1.6.0.md`.

## Validation
- Integration tests cover restore correctness and rollback behavior.
- Workspace build passed.

## Why
SaaS operations need tenant-scoped disaster recovery and move/maintenance workflows.

## Scope
- Tenant-scoped backup export.
- Tenant-scoped restore with validation checks.
- Tenant migration between server instances (plan + execution hooks).

## Implementation Plan
1. Define operation contracts and safety prechecks.
2. Implement backup/restore services and endpoint/API wrappers.
3. Add migration plan and transfer orchestration hooks.
4. Add integration tests for restore correctness and rollback behavior.

## Acceptance Criteria
- Operators can backup/restore a single tenant without broad outage.
- Restore verifies integrity before activation.
- Migration guidance and procedures are documented.

## Dependencies
- Depends on tenant catalog and runtime provisioning tracks.
