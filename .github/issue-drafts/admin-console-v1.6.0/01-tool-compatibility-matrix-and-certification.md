## Summary
Create a formal compatibility matrix for external tools (DBeaver, Beekeeper Studio, DataGrip, pgAdmin, psql/libpq) with repeatable validation.

## Why
Adoption depends on transparent compatibility guarantees.

## Scope
- Define supported tool versions and drivers.
- Record supported workflows: connect, browse DB/tables, run query, introspect metadata.
- Capture known gaps and workarounds.
- Publish matrix in `docs/server`.

## Implementation Plan
1. Define matrix template and pass/fail criteria.
2. Build repeatable smoke scripts for key workflows.
3. Execute validation against candidate tool versions.
4. Publish results and known limitations.

## Acceptance Criteria
- Matrix published and linked from server docs/readme.
- At least 4 mainstream tools validated.
- Known limitations tracked as linked issues.

## Dependencies
- Foundation issue for this epic.
