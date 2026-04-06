## Summary
Add diagnostics and lightweight admin actions to `SharpCoreDB.Viewer`.

## Status
**State:** RESOLVED

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

## Resolution
Implemented in v1.7.0. Affected files:
- `tools/SharpCoreDB.Viewer/Models/DiagnosticsSnapshot.cs` — immutable snapshot record
- `tools/SharpCoreDB.Viewer/ViewModels/DiagnosticsViewModel.cs` — PRAGMA-based data collection, admin commands
- `tools/SharpCoreDB.Viewer/Views/DiagnosticsDialog.axaml` — modal diagnostics dialog
- `tools/SharpCoreDB.Viewer/Views/DiagnosticsDialog.axaml.cs` — code-behind; auto-runs on open
- `tools/SharpCoreDB.Viewer/Views/MainWindow.axaml` — Diagnostics menu item (enabled when connected)
- `tools/SharpCoreDB.Viewer/Views/MainWindow.axaml.cs` — `OnDiagnosticsClicked` handler
- `tools/SharpCoreDB.Viewer/Resources/Strings.*.json` — 20 new keys across 6 locales

Features delivered:
- Storage stats: page count, page size, total size, cache size, journal mode
- Health status: PRAGMA integrity_check result with captured-at timestamp
- Table row counts: COUNT(*) per table, sorted alphabetically
- Admin actions: PRAGMA optimize, PRAGMA wal_checkpoint(FULL)
- Export: full snapshot serialized to JSON via file picker
- All operations guarded with IsRunning flag; error/success feedback per action
