## Summary
Deliver an administration and observability roadmap for `SharpCoreDB` that prioritizes compatibility with existing database tools and optionally adds a lightweight native admin experience.

## Status
**State:** RESOLVED ✅

Resolved workstreams in repository:
- [x] `01-tool-compatibility-matrix-and-certification.md` — **RESOLVED**
- [x] `02-pg-catalog-and-information-schema-expansion.md` — **RESOLVED**
- [x] `03-binary-protocol-auth-and-session-hardening.md` — **RESOLVED**
- [x] `04-sql-compatibility-and-ddl-introspection-polish.md` — **RESOLVED**
- [x] `05-diagnostics-and-metrics-surface-upgrades.md` — **RESOLVED**
- [x] `06-viewer-v2-foundation-desktop-tooling.md` — **RESOLVED**
- [x] `07-viewer-diagnostics-and-admin-actions.md` — **RESOLVED**
- [x] `08-optional-web-admin-razor-pages-mvp.md` — **RESOLVED**
- [x] `09-odbc-jdbc-driver-strategy-feasibility.md` — **RESOLVED**
- [x] `10-documentation-and-adoption-playbook.md` — **RESOLVED**
- [x] `11-tool-compatibility-ci-smoke-tests.md` — **RESOLVED**

All workstreams complete. Epic 100% delivered for v1.7.0.

This roadmap is based on current `v1.7.0` behavior:
- `tools/SharpCoreDB.Viewer` exists but remains minimal.
- Server binary protocol is PostgreSQL-wire-compatible in foundation form.
- Existing docs/spec mention compatibility direction, but broad GUI tool certification and metadata completeness are not finalized.

## Problem Statement
Users need practical development and operations tooling now:
- Browse databases/data reliably.
- Inspect indexes, constraints, relationships, triggers, and metadata.
- Run ad-hoc SQL with clear result UX.
- Access diagnostics and server metrics.

Building a full admin console from scratch is expensive; compatibility with mature tools (DBeaver, Beekeeper, DataGrip, pgAdmin) is likely the fastest value path.

## Goals
1. Make external tool compatibility first-class and measurable.
2. Close metadata introspection gaps (`pg_catalog`/`information_schema` equivalents).
3. Improve query UX and diagnostics endpoints.
4. Evolve `SharpCoreDB.Viewer` into a stronger developer utility.
5. Optionally deliver a lightweight web admin for core operations.

## Non-Goals
- Full SSMS/TDS emulation.
- Replacing all external database clients with a bespoke UI in one release.

## Workstreams
- External tool compatibility matrix and CI smoke validation.
- Metadata compatibility expansion.
- Protocol/auth hardening for GUI ecosystems.
- SQL compatibility and error/reporting polish.
- Diagnostics and monitoring enhancements.
- Viewer modernization.
- Optional lightweight web admin track.
- ODBC/JDBC strategy and feasibility pack.

## Milestones
- M1: Compatibility Baseline and Matrix
- M2: Metadata + Protocol Maturity
- M3: Diagnostics + Viewer UX
- M4: Optional Web Admin and Driver Strategy

## Acceptance Criteria
- Documented support matrix with tested versions and known limitations.
- Significant metadata introspection improvements in common PostgreSQL clients.
- Stable diagnostics endpoints and actionable telemetry.
- Updated docs and onboarding guidance.

## Definition of Done
- Epic checklist completed.
- All child issues closed with tests/docs.
- Release notes include compatibility status and migration guidance.
- Version labels/docs aligned to `v1.7.0` policy.
