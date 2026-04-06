## Summary
Implement tenant-aware authorization checks in gRPC interceptor and REST authorization path.

## Why
Protocol-level interception is required to avoid bypass and guarantee uniform enforcement.

## Scope
- gRPC interceptor checks tenant/database claims + grants.
- REST middleware/controller policy checks equivalent constraints.
- Binary protocol parity check point.

## Implementation Plan
1. Add tenant-aware policy service abstraction.
2. Integrate service into gRPC and REST request path.
3. Add binary protocol hook for database-scope authorization.
4. Add end-to-end tests for positive/negative cases.

## Acceptance Criteria
- Same user receives same allow/deny decision across protocols.
- Authorization decisions are deterministic and auditable.

## Dependencies
- Depends on claim model and connect/session enforcement.
