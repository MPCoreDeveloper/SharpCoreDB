## Summary
Add CI smoke tests for core external-tool compatibility scenarios.

## Status
**State:** OPEN

## Why
Compatibility can regress silently without automated checks.

## Scope
- Add automated checks for protocol handshake, simple query, and metadata discovery patterns.
- Cover `psql/libpq` baseline and scripted client interactions.
- Track compatibility regression signals in CI output.

## Implementation Plan
1. Define minimal, deterministic compatibility test suite.
2. Implement harness scripts and CI workflow integration.
3. Add pass/fail gates and artifact publishing.
4. Document how to run locally.

## Acceptance Criteria
- CI runs compatibility smoke checks on target branches.
- Regressions surface with actionable logs.
- Local reproduction workflow documented.

## Dependencies
- Depends on protocol and metadata baseline.
