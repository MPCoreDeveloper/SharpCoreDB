## Summary
Create a formal compatibility matrix for external tools (DBeaver, Beekeeper Studio, DataGrip, pgAdmin, psql/libpq) with repeatable validation.

## Status
**State:** RESOLVED

Completed in workspace.

## Completed Implementation Notes
- Added `docs/server/TOOL_COMPATIBILITY_MATRIX_v1.7.0.md`.
- Added `docs/server/TOOL_COMPATIBILITY_LIMITATIONS_v1.7.0.md`.
- Added repeatable smoke certification assets under `tests/SharpCoreDB.Server.IntegrationTests/Compatibility/`.
- Linked the new compatibility docs from server documentation entry points.

## Validation
- Smoke procedure and scripts published for repeatable certification runs.
- Workspace build passed.

## Why
Adoption depends on transparent compatibility guarantees.
