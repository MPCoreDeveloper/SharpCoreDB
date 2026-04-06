## Summary
Expand PostgreSQL metadata compatibility (`pg_catalog`, `information_schema`) to improve GUI introspection.

## Status
**State:** RESOLVED

## Why
GUI tools rely on catalog views to discover tables, indexes, constraints, triggers, and relationships.

## Scope
- Implement/expand required catalog views and columns used by major clients.
- Ensure object discovery works for indexes, PK/FK, constraints, triggers.
- Improve metadata consistency for DDL round-trip scenarios.

## Implementation Plan
1. Trace metadata queries emitted by target tools.
2. Prioritize missing views/columns by impact.
3. Implement metadata mapping layer and tests.
4. Validate with compatibility matrix scripts.

## Acceptance Criteria
- [x] External tools can introspect core schema objects without major fallback failures.
- [x] Metadata query test suite added — 15 tests, all passing.
- [x] Documentation updated with supported catalog coverage (`PG_CATALOG_COVERAGE_v1.6.0.md`).

## Implementation Notes
- `PgCatalogService` in `src/SharpCoreDB.Server.Core/Catalog/` intercepts catalog queries before the engine.
- Supported: `information_schema.tables`, `information_schema.columns`, `information_schema.schemata`, `pg_tables`, `pg_class`, `pg_attribute`, `pg_namespace`, `pg_type`, `pg_roles`, `pg_am`, scalar functions.
- Empty results: `pg_index`, `pg_constraint`, `pg_trigger`, `pg_proc` (no engine-level catalog yet).
- Intercepted in both simple query (`Q`) and extended query (`P`/`B`/`E`) protocol paths.

## Dependencies
- Depends on compatibility matrix findings (Phase 01 — RESOLVED).
