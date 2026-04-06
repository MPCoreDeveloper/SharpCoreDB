## Summary
Extend JWT claims model to include tenant/database scope and enforce claim integrity.

## Status
**State:** RESOLVED

## Why
Current token model carries identity/roles; SaaS needs explicit tenant scope semantics.

## Scope
- Define claims: `tenant_id`, `allowed_databases` (or references), `scope_version`.
- Token generation and validation updates.
- Backward-compatible handling for legacy tokens.

## Implementation Plan
1. Define claim contract and versioning strategy.
2. Update token generation in auth service.
3. Update validation + principal mapping.
4. Add tests for claim parsing and backward compatibility.

## Acceptance Criteria
- New tokens carry tenant-aware scope claims.
- Legacy tokens are handled predictably (configurable policy).
- Claims are consumed by enforcement components.

## Dependencies
- Depends on grants model decisions.
