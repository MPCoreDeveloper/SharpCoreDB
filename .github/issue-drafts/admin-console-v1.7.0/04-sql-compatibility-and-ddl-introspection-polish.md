## Summary
Improve SQL compatibility and DDL/introspection behavior required by admin tools.

## Status
**State:** RESOLVED ✅
**Completed:** 2026-04-06

### Delivered
- Added explicit result-shape schemas for common empty `pg_catalog` introspection views:
  - `pg_indexes`, `pg_index`, `pg_constraint`, `pg_proc`, `pg_trigger`, `pg_description`, `pg_stat_user_tables`, `pg_sequence`, `pg_attrdef`, `pg_depend`
- Fixed catalog source detection ambiguity (longest-match selection) so `pg_indexes` no longer resolves to `pg_index`
- Normalized binary protocol command tags for common statement classes:
  - row-returning: `SELECT n`
  - DML: `INSERT 0 n`, `UPDATE n`, `DELETE n`
  - DDL/transaction/session commands: `CREATE TABLE`, `CREATE INDEX`, `DROP TABLE`, `DROP INDEX`, `ALTER TABLE`, `BEGIN`, `COMMIT`, `ROLLBACK`, `SET`
- Added regression coverage:
  - `PgCatalogServiceTests` for `pg_indexes`, `pg_constraint`, and `pg_proc` shape validation
  - `BinaryProtocolHandshakeTests` for DDL and `SELECT` command-tag validation

## Why
Many tools execute introspection SQL and DDL probes beyond regular CRUD workloads.

## Scope
- Identify unsupported or partially supported SQL patterns from tool traces.
- Improve compatibility paths for common introspection statements.
- Normalize command tags and result metadata where needed.

## Implementation Plan
1. Capture failing SQL traces from target tools.
2. Group failures by parser, execution, metadata, or result-shape category.
3. Implement incremental fixes with regression tests.
4. Re-run tool compatibility scripts.

## Acceptance Criteria
- Reduced introspection/DDL failures in target tools.
- Regression test set for known query patterns.
- Release notes document improved compatibility.

## Dependencies
- Depends on matrix data and protocol hardening.
