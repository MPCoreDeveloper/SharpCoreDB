## Summary
Add CI smoke tests for core external-tool compatibility scenarios.

## Status
**State:** RESOLVED

## Implementation Notes

Completed for v1.6.0. Deliverables: `tests/CompatibilitySmoke/` and `.github/workflows/compatibility-smoke.yml`.

**Delivered:**

`tests/CompatibilitySmoke/` directory:
- `appsettings.smoke.json` — minimal server config for CI: temp DB paths, no encryption, test credentials, dev cert reference, all three protocol endpoints enabled.
- `smoke_tests.py` — Python smoke test runner with 7 tests across two layers:
  - **HTTP REST**: health check, JWT authentication, simple query (SELECT 1), metadata discovery (information_schema).
  - **Binary protocol**: TCP connect, SSLRequest negotiation (SSLRequest → 'S'), PostgreSQL startup message handshake.
  - Outputs coloured terminal report and structured `smoke-results.json`.
  - Exit code 0 = all passed; 1 = failures; 2 = server not ready.
- `run-smoke.ps1` — PowerShell automation script: builds server, generates dev cert via `dotnet dev-certs`, patches config with absolute paths, starts server in background, polls health, runs Python tests, tears down, cleans up.
- `README.md` — local reproduction documentation: prerequisites, two run options, expected output, config table, troubleshooting section.

`.github/workflows/compatibility-smoke.yml`:
- Triggers on push/PR to master/develop and `workflow_dispatch`.
- Ubuntu job: setup .NET + Python, install requests, build server, generate dev cert, patch config via Python script, start server in background, poll health endpoint, run smoke tests, print results summary, stop server, upload artifacts (14-day retention).

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
- CI runs compatibility smoke checks on target branches. ✅
- Regressions surface with actionable logs. ✅
- Local reproduction workflow documented. ✅

## Dependencies
- Depends on protocol and metadata baseline.
