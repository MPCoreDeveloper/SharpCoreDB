## Summary
Investigate and deliver an optional lightweight web admin MVP using ASP.NET Core Razor Pages.

## Status
**State:** RESOLVED

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

## Implementation Notes
- Feature-gated via `ServerConfiguration.EnableWebAdmin` (default: `false`).
- Cookie-based authentication scheme `"WebAdmin"` — separate from the JWT Bearer scheme used by API/gRPC.
- Admin role required (`DatabaseRole.Admin`); sign-in validates existing `UserConfiguration` password hash (SHA-256).
- Razor Pages root: `src/SharpCoreDB.Server/WebAdmin/Pages/` (configured via `WithRazorPagesRoot`).
- Pages delivered:
  - `GET /admin` — Dashboard with health stats, uptime, and per-check status.
  - `GET /admin/databases` — Configured databases with runtime online/offline status, encryption, storage mode.
  - `GET|POST /admin/query` — Guarded ad-hoc SQL with 500-row cap and full query logging.
  - `GET /admin/metrics` — MetricsCollector snapshot (active connections, latency, error rate, protocol breakdown).
  - `GET /admin/login` — Sign-in form (anonymous, admin role enforced after authentication).
  - `POST /admin/logout` — Cookie sign-out.
- Security defaults:
  - `CookieSecurePolicy.Always` (HTTPS only).
  - `SameSiteMode.Strict`, `HttpOnly = true`.
  - Anti-forgery tokens on all POST handlers.
  - All query executions logged at Warning level via `ILogger`.
  - Route authorization configured declaratively via `AuthorizeFolder("/Admin")` + `AllowAnonymousToPage("/Admin/Login")`.
- Does not affect existing JWT/gRPC/REST endpoints.

## Acceptance Criteria
- MVP pages support core admin workflows. ✅
- Security defaults are strict and documented. ✅
- Feature is optional and does not break server core. ✅

## Dependencies
- Depends on diagnostics and compatibility maturity.
