# SharpCoreDB Documentation

This folder contains the maintained documentation set for SharpCoreDB (`v1.7.0`).

## Start Here

- `INDEX.md` - Canonical documentation index.
- `FEATURE_MATRIX_v1.7.0.md` - Consolidated feature coverage by package.
- `PROJECT_STATUS.md` - Current project status.
- `IMPLEMENTATION_AUDIT_v1.7.0.md` - Repository-wide implementation audit.
- `../README.md` - Product overview and quick start.

## Maintained documentation areas

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

## Current package entry points

- Core: `../src/SharpCoreDB/README.md`, `../src/SharpCoreDB/NuGet.README.md`
- Data access: `../src/SharpCoreDB.Data.Provider/README.md`, `../src/SharpCoreDB.EntityFrameworkCore/README.md`, `../src/SharpCoreDB.EntityFrameworkCore/USAGE.md`, `../src/SharpCoreDB.Extensions/README.md`
- Analytics and search: `../src/SharpCoreDB.Analytics/README.md`, `../src/SharpCoreDB.VectorSearch/README.md`, `../src/SharpCoreDB.Graph/README.md`, `../src/SharpCoreDB.Graph.Advanced/README.md`
- Server and clients: `../src/SharpCoreDB.Server/README.md`, `../src/SharpCoreDB.Client/README.md`
- Optional architecture modules: `../src/SharpCoreDB.EventSourcing/README.md`, `../src/SharpCoreDB.Projections/README.md`, `../src/SharpCoreDB.CQRS/README.md`
- Optional functional adapters: `../src/SharpCoreDB.Functional/README.md`, `../src/SharpCoreDB.Functional.Dapper/README.md`, `../src/SharpCoreDB.Functional.EntityFrameworkCore/README.md`
- Additional integrations: `../src/SharpCoreDB.Identity/README.md`, `../src/SharpCoreDB.Serilog.Sinks/README.md`, `../src/SharpCoreDB.Provider.Sync/README.md`

## SQL extension docs (v1.7.0)

- `sql/SQL_DIALECT_EXTENSIONS_v1.7.0.md` - SharpCoreDB-specific SQL extensions including `GRAPH_RAG`, `OPTIONALLY`, and `IS SOME`/`IS NONE`.
- `graphrag/GRAPH_RAG_SINGLE_SQL.md` - Single-statement GraphRAG SQL syntax and DI integration.
- `functional/OPTIONALLY_SQL_OPTION_SUPPORT_v1.7.0.md` - Option<T> mapping semantics and usage patterns.

## Cleanup policy

Obsolete phase-status, kickoff, duplicate, and superseded planning docs are removed during maintenance. Historical snapshots are not canonical product docs unless they are explicitly linked from `INDEX.md`.
