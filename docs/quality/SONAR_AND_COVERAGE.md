# Quality: SonarCloud gate and coverage (state 2026-11-13)

This file records how the SonarCloud **quality gate** and **coverage** for
`MPCoreDeveloper_SharpCoreDB` are configured, what each red condition meant when it was fixed, and what is still an
owner action. It exists so the same investigation is not repeated and so the two suppressions below are not
"re-litigated" without the reasoning.

## The gate

Gate definitions live in SonarCloud, not in this repository. Current status on `master`: **OK**.

| Condition | Threshold | Value | Note |
|---|---|---|---|
| New reliability rating | A | A | 1.0 |
| New security rating | A | A | 1.0 |
| New maintainability rating | A | A | 1.0 |
| New duplicated lines density | ≤ 3% | 1.2% | |
| New security hotspots reviewed | 100% | 100% | |
| New coverage | ≥ 80% | *empty* | See "Coverage" — **this condition has no data, and will start being enforced the moment coverage is imported** |

### How the gate was turned green (2026-11-13)

The gate was red on one condition, `new_security_rating` = E, and every one of the 16 vulnerabilities Sonar
reported sat inside the new-code period. All are now cleared:

- `githubactions:S7637` — `uses: NuGet/login@v1` in `.github/workflows/publish.yml` was pinned to the commit the
  `v1` tag pointed at (`8d196754b4036150537f80ac539e15c2f1028841`). A moving tag can be repointed at unreviewed
  code, and that step handles the publishing credentials; `ci.yml` already pins `codecov-action` for the same
  rule. **Rule for new workflows: pin every action to a SHA.**
- `shell:S8541` / `githubactions:S8541` — Python package installs in `pysharpcoredb/publish.sh` and its nested
  workflow now use `--only-binary=:all:`, so no `setup.py` from the index runs at install time. The same flag is
  on the two `pip install requests` calls (`compatibility-smoke.yml`, `tests/CompatibilitySmoke/run-smoke.ps1`).
- `githubactions:S8544` — those installs are version-pinned (`build==1.6.1`, `twine==7.0.0`, `requests==2.34.2`).
- `githubactions:S7679` — `publish.yml`'s `nuspec_field` / `already_on_feed` bind their positional parameters to
  named locals.

## The one deliberate exclusion: `S2083` in `SingleFileStorageProvider.cs`

`roslyn.sonaranalyzer.security.cs:S2083` ("I/O function should not be called with paths from untrusted data")
reported 12 BLOCKER findings in `src/SharpCoreDB/Storage/SingleFileStorageProvider.cs`. They are **false
positives by design**: that class opens, migrates, backs up and deletes the database file at the path the *host*
supplied — the model SQLite has. The path comes from `DatabaseOptions` / `DatabaseFactory`, never from SQL text or
from network input, so the trust boundary is the application that calls the library.

What was actually done, in this order (worth knowing, because two of the three do not work):

1. `sonar.issue.ignore.multicriteria` committed in `sonar-project.properties` — **inert under automatic
   analysis**, which does not apply the parameter. Kept as the record of intent and as the mechanism that works
   once the project moves to a CI-based scan.
2. `[SuppressMessage]` on the class — **ignored by SonarCloud's server-side security analyzer**. Removed again;
   the reasoning lives in a comment on the class instead.
3. Project setting `sonar.issue.ignore.multicriteria` (e1 → that rule, that file) set through
   `api/settings/set`, and the 12 existing issues transitioned to *false positive* with the justification
   attached (`api/issues/bulk_change`, `do_transition=falsepositive`).

Scope check: the exclusion names one rule and one file. A path injection anywhere else in the codebase is still
reported.

## Coverage

Two different systems report coverage today, and neither is Sonar:

- **CI, in-job.** `.github/workflows/ci.yml`, job `coverage`: `tools/measure-coverage.ps1` wraps the MTP test
  hosts in `dotnet-coverage` (the MTP executable has no coverage switch of its own), merges the reports, and
  enforces `codecov.yml`'s **18% floor** by failing the job. Instrumentation is why the allocation canaries are
  excluded with `Category!=Allocation`.
- **codecov.** `artifacts/coverage/*.cobertura.xml` is uploaded unpinned to codecov, which reports **42.51% of
  59,211 lines** on `master` (2026-11-13) and enforces the 50%-of-new-lines patch status.

**Sonar has no coverage measures at all**, because the project is analysed by SonarCloud *automatic analysis*,
which cannot import a coverage report. That is also why the gate's `new_coverage` condition shows an empty value:
it is inert until coverage arrives — and, per its 80% threshold, it will start failing on new code the moment it
does.

### Turning coverage on inside Sonar (owner action, two steps)

1. SonarCloud → project → *Administration → Analysis Method* → **turn automatic analysis off** (it cannot be
   enabled together with a CI-based scan).
2. Add the `SONAR_TOKEN` repository secret (GitHub → *Settings → Secrets and variables → Actions*). A
   SonarCloud user token already exists in the owner's environment; it is not committed.

Then the scan can run in CI, which needs three additions: a `SonarSource/sonarqube-scan-action` step (pinned to a
SHA, per the S7637 rule above), `sonar.cs.opencover.reportsPaths` / `sonar.cs.cobertura.reportsPaths` pointing at
the coverage job's output, and a `needs: coverage` ordering so the report exists before the scan. Expect the
new-code coverage condition (80%) to become live at that point.
