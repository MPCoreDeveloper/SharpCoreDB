# SQLite Compatibility Matrix (SharpCoreDB)

**Status:** See `docs/compatibility/SQLITE_POSTGRESQL_AGGREGATE_SYNTAX_v1.7.0.md` for the current, complete compatibility matrix.

This file is superseded by that document.

## Requirement
SharpCoreDB must be **100% compatible with SQLite syntax and behavior** for all operations users could perform in SQLite. We may extend beyond SQLite, but **must never support less than SQLite**. This requirement applies to the sync provider, schema provisioning, and all generated SQL.

## Gaps to close (see priority list in main matrix)

See **Section 11** of the main compatibility matrix for the ordered list of SQLite parity gaps, ranked P0–P3 by user impact.
