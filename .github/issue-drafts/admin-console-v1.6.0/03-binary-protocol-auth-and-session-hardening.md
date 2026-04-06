## Summary
Harden binary protocol authentication/session behavior for external GUI and driver expectations.

## Status
**State:** OPEN

## Why
Current binary protocol startup/auth flow is functional but needs stronger parity and production behavior.

## Scope
- Align auth flow semantics with expected PostgreSQL client patterns where feasible.
- Improve session context handling and version/status reporting.
- Add robust error codes/messages for client troubleshooting.

## Implementation Plan
1. Audit startup/auth/session paths against protocol expectations.
2. Add missing status parameters and negotiation behavior.
3. Improve auth failure handling and observability.
4. Add protocol integration tests for client libraries.

## Acceptance Criteria
- Stable connect/auth behavior for tested tools.
- Improved deterministic error handling and logging.
- Protocol tests cover key handshake scenarios.

## Dependencies
- Parallel to metadata expansion; feeds matrix certification.
