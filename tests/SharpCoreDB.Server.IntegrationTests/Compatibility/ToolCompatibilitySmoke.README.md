# Tool Compatibility Smoke Procedure v1.8.0

This folder contains repeatable assets for validating external tool compatibility against SharpCoreDB Server.

## Baseline Assumptions
- SharpCoreDB Server is running with TLS enabled.
- A PostgreSQL-compatible connection endpoint is reachable.
- A test database and test credentials exist.

## Smoke Workflows
1. establish a TLS connection
2. list schemas/tables
3. run a simple `SELECT 1`
4. run metadata inspection queries
5. record observed gaps or tool-specific workarounds

## Assets
- `psql-smoke.sql` - SQL commands for protocol, query, and metadata checks
- `run-tool-compatibility-smoke.ps1` - helper wrapper for `psql`

## Recording Results
For each tool/version, record:
- connection success
- metadata browsing outcome
- query execution outcome
- limitations and workarounds

Update `docs/server/TOOL_COMPATIBILITY_MATRIX_v1.8.0.md` after each certification run.
