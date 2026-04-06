## Summary
Create a tenant catalog in `master` to manage tenant metadata and database mappings.

## Status
**State:** RESOLVED

## Why
Current server supports multiple databases but lacks first-class tenant metadata/lifecycle state.

## Scope
- Add catalog schema design for:
  - `tenants`
  - `tenant_databases`
  - `tenant_lifecycle_events`
- Include fields: tenant id/key, display name, status, created/updated timestamps, plan/tier, primary db name/path.
- Add startup bootstrap/migrations for catalog tables.

## Implementation Plan
1. Define SQL schema and indexes in server docs/spec.
2. Implement catalog bootstrap in server startup path.
3. Add repository/service abstraction for catalog CRUD.
4. Add tests for create/read/update/disable tenant entries.

## Acceptance Criteria
- `master` contains catalog tables with indexes.
- Catalog can register and resolve tenants deterministically.
- Integration tests prove persistence and reload behavior.

## Dependencies
- Blocks runtime provisioning API issue.
