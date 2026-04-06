## Summary
Introduce per-database grants to authorize access at database scope, not only role scope.

## Why
Current RBAC is role-based (`admin/writer/reader`) and lacks explicit allowed-database mapping.

## Scope
- Grant model examples:
  - user/service principal -> allowed databases
  - role within database scope
- Persist grants in `master` catalog.
- Admin APIs to grant/revoke/list.

## Implementation Plan
1. Define grant schema and role mapping semantics.
2. Add grant service + storage layer.
3. Add admin endpoints and validation.
4. Add tests for least-privilege access.

## Acceptance Criteria
- User can be authorized for DB A and denied for DB B.
- Grant changes are immediately effective for new sessions.
- Audit events emitted for grant updates.

## Dependencies
- Depends on tenant catalog foundation.
