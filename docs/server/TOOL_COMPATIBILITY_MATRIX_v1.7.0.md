# SharpCoreDB Server Tool Compatibility Matrix v1.6.0

## Purpose
This document defines the external database tool compatibility matrix for SharpCoreDB Server and records the repeatable certification criteria used to evaluate common workflows.

## Certification Scope
The following workflows are considered part of baseline compatibility:
- connect using PostgreSQL-compatible drivers over TLS
- browse server and database metadata
- list tables and columns
- execute ad-hoc SQL queries
- inspect basic result sets
- note metadata/introspection gaps and required workarounds

## Pass/Fail Criteria
A tool version is marked with one of the following states:
- `Certified` - baseline workflows succeeded using the documented smoke procedure
- `Partial` - connection and query execution worked, but one or more metadata or UX workflows had known gaps
- `Planned` - tooling is in scope but not yet certified with the repeatable procedure
- `Not Recommended` - a blocking protocol or metadata gap makes the tool unsuitable at this stage

A certification run must capture:
1. tool name and tested version
2. driver/protocol used
3. TLS/authentication assumptions
4. workflow outcomes
5. known limitations and workarounds

## Compatibility Matrix
| Tool | Tested Version | Driver / Protocol | Status | Connect | Browse Metadata | Run Query | Notes |
|---|---:|---|---|---|---|---|---|
| `psql` / `libpq` | `16.x` | PostgreSQL wire | Certified | Yes | Partial | Yes | Best baseline for protocol verification; metadata browsing is limited to CLI workflows |
| `DBeaver Community` | `24.x` | PostgreSQL JDBC | Partial | Yes | Partial | Yes | Basic browsing/query execution expected; advanced PostgreSQL metadata depends on `pg_catalog` completeness |
| `Beekeeper Studio` | `4.x` | PostgreSQL | Partial | Yes | Partial | Yes | Good ad-hoc SQL UX; some advanced schema panels depend on PostgreSQL-specific catalog metadata |
| `JetBrains DataGrip` | `2024.x` | PostgreSQL JDBC | Partial | Yes | Partial | Yes | Introspection works for common objects; advanced DDL and metadata inspection need further compatibility work |
| `pgAdmin 4` | `8.x` | PostgreSQL/libpq | Partial | Yes | Partial | Yes | Useful for compatibility checks; object tree richness depends on catalog expansion roadmap |

## Known Gaps
- PostgreSQL metadata richness is not yet complete for all GUI introspection panels.
- Some tools expect broader `pg_catalog` and `information_schema` parity than current server builds expose.
- GUI-specific UX features such as dependency graphs and DDL reconstruction may show reduced fidelity until follow-up roadmap phases land.

## Workarounds
- Prefer query execution and basic schema browsing as the validated baseline.
- Use `psql` smoke validation as the primary protocol acceptance check.
- When GUI metadata panels look incomplete, verify objects through SQL queries instead of assuming object absence.

## Repeatable Certification Assets
Use the assets in `tests/SharpCoreDB.Server.IntegrationTests/Compatibility/`:
- `ToolCompatibilitySmoke.README.md`
- `psql-smoke.sql`
- `run-tool-compatibility-smoke.ps1`

## Versioning Note
This matrix is versioned to `v1.6.0` and should be updated whenever protocol or metadata compatibility changes materially.
