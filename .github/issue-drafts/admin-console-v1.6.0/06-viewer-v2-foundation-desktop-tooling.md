## Summary
Evolve `tools/SharpCoreDB.Viewer` into a stronger development companion (`Viewer v2` foundation).

## Status
**State:** OPEN

## Why
A lightweight first-party desktop utility helps validate features and supports local workflows.

## Scope
- Improve database/table browser UX.
- Add richer query editor and result grid capabilities.
- Add metadata panes (indexes/constraints/triggers where available).
- Add connection profile management improvements.

## Implementation Plan
1. Define MVP capabilities aligned to current server features.
2. Refactor view-model/service layers for extensibility.
3. Implement query/result and metadata exploration upgrades.
4. Add user docs and basic smoke tests.

## Acceptance Criteria
- Viewer supports practical daily development tasks.
- Metadata and query UX clearly improved over baseline.
- Documentation includes usage and limitations.

## Dependencies
- Benefits from metadata/protocol improvements.
