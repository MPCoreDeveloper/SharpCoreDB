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

Consistent, high-quality bug reporter — 10 issues filed (all resolved), several of which drove
critical fixes in the 1.9.5 → 1.9.7 release line:

- **Issue #340 — `WHERE IN (...)` regression still present in 1.9.6**: the detailed
  `SharpCoreDBConnection` + `.scdb` verification probe (multi-value lists, SQLite `VALUES`,
  tuple rows, `OR` chains, real `ExecuteNonQuery` counts) that became the permanent
  `WhereInRegressionTests` / `WhereInRegressionEfCoreTests` regression coverage shipped in
  **v1.9.7**.
- **Issue #339 — `WHERE IN (...)` regression in 1.9.5** (returned ALL rows): fixed in v1.9.6.
- **Issue #337 — Server dropped `request.Parameters`** (gRPC + binary protocol): parameter
  forwarding fix in v1.9.5.
- **Issue #336 — `INSERT` with 4+ ADO.NET parameters bound to wrong columns**: token-aware
  parameter binding fix in v1.9.5.
- **Earlier findings**: FluentMigrator processor SQL-generation bugs (#221), quoted-identifier
  handling in single-file DDL (#227), `SharpCoreDBDataReader` aliased-column behavior with EF
  Core (#218), missing NuGet packages (#216), and migration-process feedback (#92, #148).

---

*Want to see your name here? Open a pull request — every contribution, big or small, is welcome.*
