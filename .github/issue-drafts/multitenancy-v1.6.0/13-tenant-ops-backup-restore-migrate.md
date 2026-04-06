## Summary
Add per-tenant operational tooling for backup, restore, and migration workflows.

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
