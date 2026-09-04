# Upgrade & downgrade compatibility

Status: **backward compatible** (a new version opens databases written by older versions).  
Downgrade below this line is **not supported** after the database contains certain on-disk markers.

## Compatibility matrix

| Scenario | Supported? | Notes |
|---|---|---|
| Open a legacy (pre-fixed-width, variable-length) database with the current version | ✅ Yes | Plaintext + encrypted-header legacy files are read natively; a table without `IsFixedWidthRecords` stays variable-length (the persisted flag is authoritative). |
| Open a current fixed-width Columnar database with the current version after reopen cycles | ✅ Yes | Covered by `FormatCompatPolicyTests` / `FixedWidthMigrationTests`; per-table flag persisted. |
| Migrate a legacy table to fixed-width | ✅ Yes (opt-in) | `DatabaseConfig.FixedWidthRecordLayout = true` triggers `MigrateToFixedWidth()` on open (never on read-only opens). |
| Open a database written by the **current** version (contains commit-time tombstone markers) with an **older** version that predates markers | ❌ **Not supported** | Deleted rows are stored as **negative length-prefix markers** (introduced with commit-time tombstones). An older binary that only knows positive length prefixes cannot skip these records and must not be pointed at such a file. |
| Open a fixed-width table with a version that only understands variable-length records | ❌ Not supported | Downgrade requires a migration/export; none is shipped. |

## Downgrade boundary

The on-disk markers (negative length prefixes, written at COMMIT for transactional deletes) make a
database **forward-compatible only**. If you must keep the ability to downgrade, either:

1. Keep a separate pre-upgrade copy of the database, or
2. Do not upgrade software that writes tombstones in place on a database you need to open again
   with the old software, or
3. Export/re-import data (SQL dump) instead of copying `.dat`/metadata files across versions.

Recommended upgrade order:
1. Back up the database directory (or single-file database).
2. Open it with the new version once **read-only** first (this never rewrites data).
3. Open read-write and run the normal DML regression/verification.
4. Only then let the new version write to the file.

## Verification

- In-repo: `FormatCompatPolicyTests`, `FixedWidthMigrationTests`,
  `DefaultEngineSelectionTests`, and the full suite (currently 1768+ tests) cover legacy reads,
  opt-in migration, marker durability across reopen cycles, and the default fast-path engine.
- Planned (CI): a true **cross-version** job that writes a database with a pinned older commit and
  reads it with `master` (requires an old-binary generator; tracked as follow-up).

## Changelog

See `docs/CHANGELOG.md` → `[Unreleased]` → **Hardening** for the marker/downgrade notes that
accompanied the tombstone work (PRs #367/#368) and this policy document.
