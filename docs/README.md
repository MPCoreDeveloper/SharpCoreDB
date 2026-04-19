# SharpCoreDB Documentation

This folder contains the maintained documentation set for SharpCoreDB (`v1.7.0`).

## Start Here

- `INDEX.md` - Canonical documentation index.
- `FEATURE_MATRIX_v1.7.0.md` - Consolidated feature coverage by package.
- `PROJECT_STATUS.md` - Current project status.
- `IMPLEMENTATION_AUDIT_v1.7.0.md` - Repository-wide implementation audit.
- `../README.md` - Product overview and quick start.

## Core Areas

- `server/` - Server operation, security, APIs, and protocols.
- `sql/` - SQL dialect capabilities, extensions, and compatibility guidance.
- `scdb/` - Storage engine and SCDB guidance.
- `serialization/` - Binary format and serialization internals.
- `analytics/` - Analytics capabilities and usage.
- `Vectors/` - Vector search and semantic retrieval.
- `graphrag/` - GraphRAG and advanced graph workflows.
- `distributed/` - Distributed architecture components.
- `sync/` - Dotmim.Sync provider usage.
- `migration/` - Migration and interoperability guides.

## SQL extension docs (v1.7.0)

- `sql/SQL_DIALECT_EXTENSIONS_v1.7.0.md` - SharpCoreDB-specific SQL extensions including `GRAPH_RAG`, `OPTIONALLY`, and `IS SOME`/`IS NONE`.
- `graphrag/GRAPH_RAG_SINGLE_SQL.md` - Single-statement GraphRAG SQL syntax and DI integration.
- `functional/OPTIONALLY_SQL_OPTION_SUPPORT_v1.7.0.md` - Option<T> mapping semantics and usage patterns.

## Package Documentation

Per-package docs are maintained in `src/*/README.md` and `src/*/NuGet.README.md`, aligned to `v1.7.0`.

## Cleanup Policy

Obsolete phase-status, kickoff, and superseded planning docs are removed during maintenance. Historical snapshots are not canonical product docs.
