## Summary
Evolve `tools/SharpCoreDB.Viewer` into a stronger development companion (`Viewer v2` foundation).

## Status
**State:** ✅ RESOLVED — implemented in v1.6.0

## Why
A lightweight first-party desktop utility helps validate features and supports local workflows.

## Scope
- ✅ Improve database/table browser UX — table filter, selected-table state, Refresh button.
- ✅ Add richer query editor and result grid capabilities — multi-statement, F5/Ctrl+Enter shortcuts.
- ✅ Add metadata panes (indexes/constraints/triggers) — columns, indexes, triggers via PRAGMA.
- ✅ Add connection profile management improvements — recent connections saved, Use/Remove actions.

## Implementation Plan
1. ✅ Define MVP capabilities aligned to current server features.
2. ✅ Refactor view-model/service layers for extensibility.
3. ✅ Implement query/result and metadata exploration upgrades.
4. ✅ Add user docs and basic smoke tests.

## Acceptance Criteria
- ✅ Viewer supports practical daily development tasks.
- ✅ Metadata and query UX clearly improved over baseline.
- ✅ Documentation includes usage and limitations — see `docs/viewer/viewer-v2-usage.md`.

## Dependencies
- Benefits from metadata/protocol improvements.

## Resolved in
- `tools/SharpCoreDB.Viewer/ViewModels/MainWindowViewModel.cs` — table filter, metadata panes, preview, keyboard shortcuts
- `tools/SharpCoreDB.Viewer/ViewModels/ConnectionDialogViewModel.cs` — recent connection profiles
- `tools/SharpCoreDB.Viewer/Models/AppSettings.cs` — `ConnectionProfile` model
- `tools/SharpCoreDB.Viewer/Services/SettingsService.cs` — profile persistence helpers
- `tools/SharpCoreDB.Viewer/Views/MainWindow.axaml` — explorer UX with filter + metadata panes
- `tools/SharpCoreDB.Viewer/Views/ConnectionDialog.axaml` — recent connections UI
- `tools/SharpCoreDB.Viewer/Views/MainWindow.axaml.cs` — F5/Ctrl+Enter keyboard handler
- `tools/SharpCoreDB.Viewer/Resources/Strings.*.json` — all locales updated
- `docs/viewer/viewer-v2-usage.md` — user documentation
