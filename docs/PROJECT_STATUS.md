# SharpCoreDB Project Status

**Version:** 1.7.0  
**Status:** ✅ Production Ready (Core .NET packages) / ⚠️ SDK parity in progress  
**Last Updated:** April 6, 2026

## Current Status

SharpCoreDB core .NET packages are release-labeled on `1.7.0` and build successfully, including:

- `SharpCoreDB`
- `SharpCoreDB.Server`
- `SharpCoreDB.Client`
- `SharpCoreDB.Extensions` (including FluentMigrator integration)
- Optional Event Sourcing, Projections, CQRS, Graph, Analytics, and VectorSearch packages

## Implementation Completeness Audit

A repository-wide scan was performed for partial implementation markers (`TODO`, `NotImplemented`, `WIP`, `TBD`).

### Findings

- Core/server .NET runtime paths are implemented and buildable.
- Remaining partial implementations are concentrated in the Python SDK (`pysharpcoredb`) and selected non-production benchmark/test scaffolding.

Detailed findings are documented in:

- `docs/IMPLEMENTATION_AUDIT_v1.7.0.md`

## FluentMigrator Status in v1.7.0

- Embedded mode integration: available
- gRPC migration mode integration: available
- `IMigrationProcessorOptions` obsolete usage was reduced to required FluentMigrator interface bridge points; processor code now uses concrete options internally.

## Documentation Governance

- Canonical docs entry points: `README.md`, `docs/INDEX.md`, `docs/README.md`
- Obsolete/superseded phase-planning artifacts are removed during documentation maintenance.

## Roadmap Issue Closure Tracking

- ✅ `#125` Enforce database grants in Connect and session creation — completed and closed.
- ✅ `#124` Per-database grants model for tenant isolation — completed and closed.
- ✅ `#123` DatabaseRegistry runtime attach/detach APIs — completed and closed.
- ✅ `#122` Runtime tenant database provisioning APIs (gRPC + REST) — completed and closed.
- ✅ `#121` Tenant catalog in master database for SaaS lifecycle metadata — completed and closed.

## Roadmap / TODO (v1.7.2)

- [ ] **Single-file metadata parity:** Make `SingleFileDatabase` explicitly implement `IMetadataProvider` to align metadata discovery behavior with directory-mode `Database`.
  - **Why:** some consumers probe metadata with `db is IMetadataProvider`; explicit implementation improves compatibility and predictability.
  - **Scope:** keep `IDatabase.GetTables()` / `GetColumns()` as canonical path, add explicit `IMetadataProvider` contract on single-file runtime type.
  - **Acceptance:** probing via `IMetadataProvider` works consistently for both directory and single-file databases.
