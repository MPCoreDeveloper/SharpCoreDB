# SharpCoreDB Web Viewer (Razor Pages) Implementation Plan

**Version:** v1.7.0  
**Status:** Draft roadmap  
**Scope:** Local-first, cross-platform web UI for SharpCoreDB  
**Target stack:** .NET 10 + ASP.NET Core Razor Pages

---

## 1. Goals

- Deliver a first-party web viewer that runs locally on Windows, Linux, and macOS.
- Prioritize Razor Pages for maintainability and fast delivery in the current repository context.
- Support both directory-based and single-file (`.scdb`) databases.
- Keep the tool local-first and secure-by-default.
- Reuse existing SharpCoreDB APIs and desktop viewer learnings where practical.

### Non-goals (MVP)

- No cloud-hosted multi-tenant control plane.
- No heavy SPA framework requirement.
- No advanced visual query designer in initial release.

---

## 2. Product Scope (MVP)

### Core user flows

1. Start the app locally and open browser on `https://localhost:<port>`.
2. Connect to a database (directory or `.scdb`) with optional password.
3. Browse tables and inspect table metadata.
4. Execute SQL (single and multi-statement) and inspect result sets.
5. Run basic data operations from SQL editor (read/write DDL/DML).

### MVP features

- Connection form with mode selection (directory/single-file).
- Recent connections list (without storing passwords).
- Table explorer with live filtering.
- Metadata panes (columns, indexes, triggers).
- SQL editor + execution panel.
- Result grid with paging/limits.
- Basic diagnostics panel for errors and timings.

---

## 3. Architecture (Razor Pages-first)

### Proposed project layout

- `tools/SharpCoreDB.WebViewer/`
  - `Pages/` (Razor Pages UI)
  - `Services/` (connection/session/query services)
  - `Models/` (request/response DTOs + view models)
  - `wwwroot/` (minimal JS/CSS assets)

### Core components

- `IViewerConnectionService`
  - Opens/closes database sessions.
  - Validates path/mode/password inputs.
- `IViewerQueryService`
  - Executes SQL with cancellation and timeout support.
  - Returns typed grid model for Razor rendering.
- `IMetadataService`
  - Retrieves schema, columns, indexes, and triggers.
- `IRecentConnectionsStore`
  - Persists non-sensitive profile data only.

### UI model

- Server-rendered Razor Pages for primary workflows.
- Small progressive enhancement with lightweight JavaScript for table filtering and query UX.
- No Blazor dependency in MVP.

---

## 4. Security and Local Runtime Rules

- Default bind: `localhost` only.
- HTTPS enabled by default for local endpoints (dev certificate support).
- No plaintext password persistence.
- Optional session timeout and lock after inactivity.
- Input validation and SQL execution safeguards (statement size/timeout limits).

---

## 5. Cross-platform Delivery

- Validate runtime on Windows, Linux, macOS with .NET 10.
- Support framework-dependent run (`dotnet run`) and publish profiles.
- Provide startup scripts/examples for each OS.
- Keep filesystem path handling platform-neutral.

---

## 6. Phased Roadmap

## Phase 0 — Foundation

- Create web viewer project skeleton.
- Wire dependency injection and base services.
- Add configuration options (port, TLS, limits).

## Phase 1 — Connection + Explorer

- Implement connection workflow and recent profiles.
- Implement table list + metadata retrieval.
- Add basic validation and user-facing errors.

## Phase 2 — SQL Execution

- Add SQL editor and execute endpoint.
- Support multi-statement execution with final result rendering.
- Add cancellation token and timeout controls.

## Phase 3 — UX Hardening

- Improve result grid readability and paging.
- Add diagnostics traces and user-friendly failure messages.
- Add localization baseline (English-first, extensible).

## Phase 4 — Release Readiness

- End-to-end smoke tests on all supported OS targets.
- Packaging and docs (install/run/troubleshooting).
- Compatibility verification against SharpCoreDB features used by viewer.

---

## 7. Acceptance Criteria

- App runs locally via browser on all three desktop OS families.
- User can connect, browse schema, and execute SQL without desktop UI dependency.
- No passwords are stored in recent connection profiles.
- Common query/metadata errors are surfaced with actionable messages.
- Documentation exists for local startup, configuration, and limitations.

---

## 8. Risks and Mitigations

- **Risk:** Query execution can block UI experience for long statements.  
  **Mitigation:** enforce cancellation + timeout and async execution paths.

- **Risk:** Cross-platform path/permissions issues.  
  **Mitigation:** centralize path validation and add OS-specific smoke tests.

- **Risk:** Feature parity pressure with desktop viewer.  
  **Mitigation:** define MVP boundaries and track parity backlog explicitly.

---

## 9. Initial Backlog (Implementation-ready)

1. Create `tools/SharpCoreDB.WebViewer` Razor Pages project targeting .NET 10.
2. Add connection settings model and secure profile storage abstraction.
3. Implement open/close connection service over SharpCoreDB provider APIs.
4. Build explorer page with table list and metadata sections.
5. Build query page with SQL input, execute action, and result grid rendering.
6. Add cancellation token wiring and configurable query timeout.
7. Add integration tests for connection and query workflows.
8. Publish usage documentation in `docs/viewer/`.

---

## 10. Future Enhancements (Post-MVP)

- Saved query library and query history.
- Export result sets (CSV/JSON/Parquet).
- Optional advanced interactive components where justified.
- Optional remote mode aligned with SharpCoreDB.Server TLS and auth model.
