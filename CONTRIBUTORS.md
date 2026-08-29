# Contributors

SharpCoreDB is an open-source project — every contribution counts, and we are grateful for the
community members who invest their time and expertise in making it better. 🙏

This file acknowledges community contributors whose work has improved the project beyond the
core maintainers.

## saltus7

- **Issue #341 + PR #342 — Single-file (`.scdb`) encryption bypass**: root-caused why
  `DatabaseOptions.EncryptionKey` was silently ignored by `SingleFileStorageProvider` and provided a
  complete proposed fix — AES-256-GCM at rest on every block I/O path plus a 12-test regression
  suite (plaintext-at-rest, correct-key round-trip, wrong-key rejection, file integrity).
- saltus7 opened [PR #342](https://github.com/MPCoreDeveloper/SharpCoreDB/pull/342) at
  `2026-08-29T05:20Z` — *before* the fix shipped — and their root-cause analysis was accurate:
  the shipped fix in **v1.9.7** (commit `16ed7e22`) follows exactly the integration points
  saltus7 identified.
- We merged our own implementation rather than the PR to keep the release moving, but this
  contributor deserves full credit for the diagnosis and the reference implementation.

---

## YBazanPro

Consistent, high-quality bug reporter — **all 10 issues** filed in this repo have been resolved,
and several drove critical fixes in the 1.9.5 → 1.9.7 release line:

- **#340** — `WHERE IN (...)` regression still present in 1.9.6: the detailed
  `SharpCoreDBConnection` + `.scdb` probe (multi-value lists, SQLite `VALUES`, tuple rows,
  `OR` chains, real `ExecuteNonQuery` counts) that became the permanent `WhereInRegressionTests`
  / `WhereInRegressionEfCoreTests` coverage shipped in **v1.9.7**.
- **#339** — `WHERE IN (...)` regression in 1.9.5 (returned ALL rows): fixed in v1.9.6.
- **#337** — Server dropped `request.Parameters` (gRPC + binary protocol): fixed in v1.9.5.
- **#336** — `INSERT` with 4+ ADO.NET parameters bound to wrong columns: token-aware parameter
  binding fix in v1.9.5.
- **#227** — FluentMigrator processor generated quoted identifiers that broke single-file DDL: fixed.
- **#221** — `SharpCoreDbProcessor` SQL-generation bugs (`UndefinedDefaultValue` sentinel leakage,
  duplicate `PRIMARY KEY` for version tables): fixed.
- **#218** — `SharpCoreDBDataReader` returned aliased column names as values under EF Core: fixed.
- **#216** — missing NuGet packages: resolved.
- **#148** — release-process clarification (FluentMigrator support): addressed.
- **#92** — FluentMigrator-based migration integration: shipped.

---

*Want to see your name here? Open a pull request — every contribution, big or small, is welcome.*
