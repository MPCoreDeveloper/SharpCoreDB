## Summary
Add diagnostics and lightweight admin actions to `SharpCoreDB.Viewer`.

## Why
Users requested direct access to runtime metrics and common operational actions.

## Scope
- Diagnostics panel (health, core metrics, active sessions summary).
- Safe admin actions: refresh schema, session cleanup trigger, export diagnostics snapshot.
- Audit-conscious UX for sensitive operations.

## Implementation Plan
1. Define diagnostics view contracts and data polling strategy.
2. Implement non-destructive admin action commands.
3. Add localization resources and error handling.
4. Add tests/smoke verification for command flows.

## Acceptance Criteria
- Viewer exposes actionable diagnostics data.
- Admin actions are guarded and logged.
- UX remains responsive under load.

## Dependencies
- Depends on diagnostics endpoint upgrades.
