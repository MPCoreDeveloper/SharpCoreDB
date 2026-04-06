## Summary
Investigate and deliver an optional lightweight web admin MVP using ASP.NET Core Razor Pages.

## Why
A browser-based console lowers operational friction without committing to a heavy full console rewrite.

## Scope
- Razor Pages MVP for:
  - connection status/health
  - database list and basic browsing
  - ad-hoc query execution (guarded)
  - basic metrics display
- Strict auth and role checks.
- Feature toggle and optional deployment mode.

## Implementation Plan
1. Define web admin boundaries and security model.
2. Build Razor Pages host shell and auth integration.
3. Add core pages for health/metrics/query/browse.
4. Add docs and threat considerations.

## Acceptance Criteria
- MVP pages support core admin workflows.
- Security defaults are strict and documented.
- Feature is optional and does not break server core.

## Dependencies
- Depends on diagnostics and compatibility maturity.
