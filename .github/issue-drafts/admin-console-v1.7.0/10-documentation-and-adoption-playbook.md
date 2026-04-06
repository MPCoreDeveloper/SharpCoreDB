## Summary
Publish a practical adoption playbook for admin and tooling workflows.

## Status
**State:** RESOLVED

## Implementation Notes

Completed for v1.7.0. Deliverable: `docs/server/ADMIN_TOOLING_GUIDE.md`.

**Delivered content:**
- Three-endpoint overview (binary protocol / gRPC / HTTPS REST) with default ports.
- Tool compatibility matrix: 8 GUI tools, 5 CLI tools, 6 BI/analytics tools, 10 programmatic drivers — with per-tool status (connect, browse, query, export, import) and notes.
- Six setup flows: DBeaver, psql CLI, Npgsql (.NET), Python (psycopg2), Power BI Desktop (ODBC), Web Admin UI.
- Known limitations table with 8 entries and mitigation steps.
- Diagnostics & monitoring quickstart: health check endpoint, metrics endpoint, Prometheus integration example, SharpCoreDB Viewer diagnostics walkthrough.
- Migration guidance for: embedded → server mode, custom HTTP/gRPC → standard API, file database → server hosted.
- Connection string reference for all supported drivers/protocols.
- Version support matrix (v1.4–v1.6).
- `docs/INDEX.md` updated to link both new docs.

## Why
Users need clear guidance on what works now, what is in progress, and recommended paths.

## Scope
- Update docs with:
  - tool compatibility matrix
  - recommended setup flows
  - known limitations and mitigations
  - diagnostics/monitoring quickstart
- Include migration guidance from custom tooling to external clients.

## Implementation Plan
1. Define documentation information architecture.
2. Draft setup guides and troubleshooting sections.
3. Add workflow examples for common personas.
4. Link from root and package readmes.

## Acceptance Criteria
- Docs enable fast, low-friction onboarding. ✅
- Limitations and support levels are transparent. ✅
- Docs remain version-aligned to `v1.7.0` policy. ✅

## Dependencies
- Finalization workstream tied to all roadmap outputs.
