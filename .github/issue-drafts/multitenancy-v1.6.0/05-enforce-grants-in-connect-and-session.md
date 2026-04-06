## Summary
Enforce database grants during `Connect` and session creation for all protocols.

## Why
Even with grant storage, enforcement must happen at auth/session boundary.

## Scope
- Enforce in gRPC `Connect` and REST session resolver equivalents.
- Deny unauthorized database selection with explicit error codes.
- Add structured logs/metrics for denied attempts.

## Implementation Plan
1. Inject grant-check service in connect/session flows.
2. Add denial path and protocol-consistent errors.
3. Add metrics counters for authorization denials.
4. Add integration tests across gRPC and REST.

## Acceptance Criteria
- Unauthorized database connection attempts are blocked.
- Authorized users can only open sessions for permitted databases.
- Denials are observable through logs/metrics.

## Dependencies
- Depends on per-database grants model.
