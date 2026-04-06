## Summary
Expose supported runtime attach/detach operations in `DatabaseRegistry` for tenant DB lifecycle.

## Why
`DatabaseRegistry` initializes from configuration; runtime provisioning needs controlled dynamic registration.

## Scope
- Public async methods:
  - `RegisterDatabaseAsync(...)`
  - `UnregisterDatabaseAsync(...)`
- Concurrency-safe attach/detach semantics.
- Graceful connection draining on detach.

## Implementation Plan
1. Refactor registry internals for dynamic operations.
2. Add locks/guards for duplicate registration and in-flight detach.
3. Implement safe shutdown path per database instance.
4. Add unit + integration tests for race scenarios.

## Acceptance Criteria
- Databases can be attached/detached without server restart.
- No cross-database impact during detach.
- Deterministic error behavior for duplicates/missing entries.

## Dependencies
- Used by provisioning APIs and operations tooling.
