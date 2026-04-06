## Summary
Add optional row-level policy engine for shared-database multi-tenancy mode.

## Status
**State:** RESOLVED

## Why
Database-per-tenant is preferred; some users still require shared DB with policy isolation.

## Scope
- Optional policy framework (disabled by default).
- Tenant discriminator enforcement for read/write paths.
- Policy bypass protection and test hardening.

## Implementation Plan
1. Define policy abstraction and configuration schema.
2. Implement query rewrite/filter injection or execution-time enforcement.
3. Add write-path tenant consistency checks.
4. Add tests for leakage prevention and bypass attempts.

## Acceptance Criteria
- Shared DB mode can enforce tenant row isolation when enabled.
- No cross-tenant read/write leakage in test suite.
- Feature remains optional and non-breaking.

## Dependencies
- Independent optional track after foundation security.
