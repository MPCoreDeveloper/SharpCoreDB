# Promoting the .NET 11 / C# 15 line to `master`

> **Decision (owner, 2026-10-10):** `master` becomes the **net11.0 / C# 15 preview** line at **2.1.0**, and
> the net10.0 / C# 14 line is **parked on `release/v2.0`** for 2.0.x servicing. This supersedes the
> 2026-09-25 decision recorded in `docs/performance/BEAT_SQLITE_ALL_AXES_PLAN.md` §9 row 9, which kept
> `master` as the net10.0 maintenance line. That row is updated in the same session.
>
> Prepared on `promote/net11-main-20261010` and **merged into `master` on 2026-10-10**: `master` was first
> fast-forwarded to `origin/master` (`0e51fc55`, 12 commits) and then merged with `--no-ff`, so `master`'s
> tree is byte-identical to the validated promote tip. The tag and the push remain **the owner's steps** —
> everything is local and has never been pushed.
>
> One defect was found by the post-flip full check and fixed the same day: see
> [After the flip (2026-10-10)](#after-the-flip-2026-10-10).

## Timing: what "now" costs

- **.NET 11 GA is 2026-11-10** (STS, supported to 2028-11-09); the newest build available on 2026-10-10 is
  `11.0.0-rc.1` (2026-09-08). `global.json` therefore pins `11.0.100-rc.1.26425.128` with
  `allowPrerelease: true`, and **every package built from `master` until GA is RC-built**.
- The repository's own roadmap (`docs/performance/V2_PERFORMANCE_PLAN.md`, WP8) scheduled the migration
  as *"after Nov 2026 GA"*. This decision deliberately front-runs that by one month; the GA checklist
  below is what closes the gap, and it is the only reason the RC pin appears on `master` at all.
- Alternative considered and rejected by this decision: waiting for GA, and multi-targeting
  (`net10.0;net11.0`). Multi-targeting contradicts the design already recorded in `docs/net11/UPGRADE.md`
  ("the v2.1 branch never carries a net10.0 target, so there is no multi-targeting and no per-target
  `#if` drift in the shipped assemblies").

## What was true before the flip (all measured 2026-10-10)

| | `origin/master` (net10 line) | `perf/autonomous-20260921` (net11 line) |
|---|---|---|
| TFM / language | `net10.0` / C# 14 | `net11.0` / C# 15 preview + `runtime-async=on` |
| Version | `2.0.0.3` | `2.1.0-RC.3` |
| `global.json` | **absent** | pins SDK `11.0.100-rc.1.26425.128` |
| CI | `ci.yml` = net10 pipeline (`master`), no coverage job | `ci-net11.yml` = net11 pipeline, coverage + SIMD gate |
| Dockerfile | `ARG VERSION=2.0.0.3`, `sdk:10.0` / `aspnet:10.0` | `ARG VERSION=2.0.0.2` — **master's bump had been lost** |

- Divergence: the net11 line was **309 ahead and 19 behind** (12 dependabot commits + 7 `master` commits),
  with 848 files differing.
- A read-only `git merge-tree` probe predicted, and the real merge confirmed, **65 conflicting paths**: 44
  `.csproj`, 17 `NuGet.README.md`, plus `Directory.Packages.props`, `README.md`, `docs/INDEX.md` and
  `publish-manual.yml`. Both sides had rewritten the *same* version strings, so the conflicts were
  mechanical rather than structural.

## The prepared series (6 commits on `promote/net11-main-20261010`)

| # | Commit | What it does |
|---|---|---|
| 1 | `5461027c` | **Convergence merge** of `origin/master` into the net11 line. Structure, TFM and language version taken from the net11 side; the dependency bumps and install-doc fixes taken from `master` (`AngleSharp` 1.8.1, `BLite` 5.1.1, `Dapper` 2.1.86, `linq2db` 6.5.0, `MailKit`/`MimeKit` 4.18.0, EF Core 10.0.12, `Microsoft.NET.Test.Sdk` 18.10.0, `xunit.v3` 4.0.1, `SharpDispatch` 1.0.2; the package *name* sets were verified identical, 112 = 112, so taking `master`'s file wholesale could not leave a package unreferenced). `SharpCoreDBVersion` stayed at `2.1.0-RC.3` so no intermediate commit on the series is internally inconsistent. It also restores the Dockerfile `ARG VERSION=2.0.0.3` bump the net11 line had lost. **Verified:** build 0 errors (294 warnings), core suite **1995 / 0 failed / 0 errors / 0 skipped in 80,3 s**. |
| 2 | `89cbaf08` | Removes `.github/workflows/ci-net11.yml`; its content is absorbed by commit 3. |
| 3 | `2d7672ab` | **The CI restructure itself.** `ci.yml` (the pipeline `master` triggers) becomes the net11.0 pipeline: `DOTNET_VERSION` pinned to the .NET 11 RC with `include-prerelease`, the four test hosts under `bin/Release/net11.0`, the `coverage` job carried over from `ci-net11.yml`, the core-only dispatch `publish-core` job kept so no release capability is lost, `workflow_dispatch` added for it, and v2.0 tags excluded. `ci-net10.yml` is the old master pipeline retargeted to `release/v2.0` (tags `v2.0*`, pack/publish gated on that branch). `compatibility-smoke.yml` and `dependency-submission.yml` move to the same SDK pin, and the `measure-coverage.ps1` header follows the file. **Preparation note:** this commit's subject reads `docker: …` because a staging race during preparation committed the CI files under the next commit's intended message. The *content* is the CI restructure and is authoritative; the Dockerfile landed in `fd916092`. The owner can squash the series on merge if the label matters — history was deliberately **not** rewritten to fix it. |
| 4 | `fd916092` | The Dockerfile becomes net11-aware (`sdk:11.0` / `aspnet:11.0`) and `docker-publish.yml` drops the `!v2.1*` exclusion that existed only because the image was net10-only — closing the open item recorded in the 2.1.0-RC.3 release notes — while excluding `v2.0.x`, which belongs to the parked line. The container deliberately does **not** copy `global.json`: it uses the image's own SDK, so the repo's RC pin can never fail the image build on a missing SDK. |
| 5 | this commit | Docs: this record, the stale `master`-is-net10 statements (`AGENTS.md`, `docs/net11/UPGRADE.md`, `V2_PERFORMANCE_PLAN.md` WP8, `BEAT_SQLITE_ALL_AXES_PLAN.md` §9 row 9), the `ci-net11.yml` pointers in `SIMD_STANDARDS.md` and `NUGET_PUBLISHING_GUIDE.md`, the `docs/INDEX.md` entries that `master` had and this line lacked, and the `CHANGELOG.md` entry. |
| 6 | release metadata | **Owner-gated and isolated so it can be reviewed or dropped on its own:** version `2.1.0-RC.3` → `2.1.0` in `Directory.Packages.props` (the `SharpCoreDBVersion` property), 46 `.csproj` `<Version>` fields, the Dockerfile `VERSION` arg, the README install commands (20 for 2.1.0, plus the parked line's 21 moved to its latest `2.0.0.3`) and the version lines in `docs/INDEX.md`, `docs/PROJECT_STATUS.md` and `docs/net11/UPGRADE.md`. `global.json` needs no change here: it already exists on the net11 line and rides along with the flip — that is what puts the pin on `master`. **Deliberately not included:** the per-package `<PackageReleaseNotes>` and `NuGet.README.md` prose still names `2.1.0-RC.3`; that text is refreshed when the `2.1.0` release notes are cut (GA checklist item 5), and re-labelling it mechanically would have rewritten the RC.3 validation record. Nothing else in the series depends on this commit. |

## What the owner does at the flip

```bash
# 1. Confirm the net10 line is parked before master moves.
git fetch origin
git rev-parse --verify release/v2.0            # 0e51fc55 - the 2.0.0.3 line

# 2. The merge is already done (2026-10-10). master was fast-forwarded to origin/master and then merged
#    with --no-ff; verify rather than repeat:
git log --oneline --graph -3                   # the flip merge + the post-flip follow-up
git diff --stat promote/net11-main-20261010    # only the follow-up commit's files

# 3. Verify on master itself, then tag and push (never force).
git tag -a v2.1.0 -m "SharpCoreDB 2.1.0 - .NET 11 / C# 15 line"
git push origin master v2.1.0

# 4. That push IS the release, not a rehearsal: `ci.yml`'s `publish` job is gated on
#    `github.ref == 'refs/heads/master'`, so a master push packs and pushes every packable project to
#    NuGet.org. The v2.1.0 tag adds the Docker image run and the gates only (the publish job is skipped
#    for a tag ref). The parked line publishes on a push to its own branch (`ci-net10.yml`,
#    `--skip-duplicate`). Evidence for the 2026-10-10 run: "Outcome" below.
```

### After the flip (2026-10-10)

The post-flip full check (CI-shaped `SharpCoreDB.CI.slnf` build, the four CI test hosts, the deprecated /
vulnerable package gates, the banned-SIMD guard, the metadata sweep and the workflow parse) was run on
`master` and found one **functional** defect plus a set of release-metadata inconsistencies. All of them
are fixed in the follow-up commit on this series:

- **`ci-net10.yml` could never run for the parked line.** GitHub loads a workflow from the ref being
  pushed, so a copy that exists only on `master` produces *no* run for a `release/v2.0` push or a `v2.0*`
  tag — this document claimed the opposite. The parked branch's own `ci.yml` still covers servicing tags
  (`tags: ['v*']`), but branch pushes and PRs to `release/v2.0` had no pipeline at all. The ready fix
  lives on **`fix/net10-parked-ci`** (`c13405df`, one commit ahead of `release/v2.0`, fast-forward only):
  it adds that branch's own `ci-net10.yml`, built from `master`'s copy with the SIMD gate replaced by a
  note. **Why the gate is not in the parked copy:** `tools/check-banned-simd-api.ps1` run against
  `release/v2.0`'s tree exits **1** — 33 banned `Vector<T>` usages, concentrated in
  `src/SharpCoreDB/Query/SimdFilter.cs` (mostly `Vector<double>`) — so the step would fail the build job
  and, because `pack` and `publish` depend on it, block 2.0.x servicing. **Owner decision:** either
  cherry-pick the net11 intrinsics port onto `release/v2.0` (the documented forward-port flow below) and
  add the step back, or ship the parked line without the gate. Merge the prepared fix with
  `git switch release/v2.0 && git merge --ff-only fix/net10-parked-ci`.
- **Release metadata that ships to NuGet.org still described the release candidate**: 22
  `<PackageReleaseNotes>` stamped `v2.1.0-RC.3` (17 linking to the `release/v2.1.0.0-RC.3` branch) and 21
  packed `NuGet.README.md` files, whose `dotnet add package --version 2.1.0-RC.3` lines are what a reader
  copies from the package page. Both now name 2.1.0 and link to `master`.
- **~20 `.csproj` advertised the wrong toolchain** in `<Description>` / `<PackageTags>`
  (`.NET 10` / `C# 14` / `net10` / `csharp14`), and one EF Core description still claimed compatibility
  with SharpCoreDB 1.7.1.
- **A dead generated file and two dead references**: `package_inventory.json` (an old
  `dotnet list package --include-transitive` dump with absolute `D:/source/...` paths, the whole family at
  `net10.0`) referenced by nothing, and two inert `net10.0` / `net8.0` -conditioned `PackageReference`s in
  `SharpCoreDB.Provider.YesSql`.
- **Docs that called the line a release candidate** (`FEATURE_MATRIX.md`, `Vectors/README.md`,
  `graphrag/README.md`, `PROJECT_STATUS.md`) or pointed at the deleted `ci-net11.yml` (`CHANGELOG.md`).

Verified after the fixes on the same tree: CI-shaped build 0 errors, the four CI suites green
(core 1,995 · VectorSearch 248 · EF Core 116 · linq2db 24), the deprecated / vulnerable package gates
clean and the banned-SIMD guard clean.

- Forward-port fixes with `git cherry-pick -x` the way §9 row 9 prescribes, resolving `global.json` /
  `Directory.Build.props` / `Directory.Packages.props` in favour of the net11 build config — take the fix,
  never the config.
- The net11-only API surface stays what `docs/net11/UPGRADE.md` documents: native Zstandard, Runtime Async,
  and the source-gated `SharpCoreDB.Net11` members. Nothing in the shipped storage format changes.

## Outcome (2026-10-10, after the push)

The owner pushed all three refs the same day. What each push triggered, read from the workflow runs
rather than assumed:

| Push | Workflow | Result |
| --- | --- | --- |
| `master` `58f297e9` | `CI/CD Pipeline` (`ci.yml`, run `38066545672`) | **success** — 3-OS build + tests, coverage, `pack`, `publish` |
| tag `v2.1.0` | `CI/CD Pipeline` (`38066540443`) and `Docker` | **success** — the gates and the image; `publish` skipped for a tag ref |
| `release/v2.0` `c13405df` | `CI v2.0 legacy (.NET 10)` (`ci-net10.yml`, run `38066546526`) | **success** — its **first ever run**: 3-OS build + tests, `pack`, `publish` |

The parked line's run is the direct proof that the `ci-net10.yml` fix above works: the workflow now
exists on the ref that is pushed, so a `release/v2.0` push produces a run instead of nothing.

**Publication**, verified against the NuGet API: of the 26 `SharpCoreDB.*` ids in
`Directory.Packages.props`, **25 answer at `2.1.0`** on nuget.org, and `2.1.0` is the newest version of
`sharpcoredb` (after `2.1.0-rc.3`). The 26th, `SharpCoreDB.Server`, has
`<IsPackable>false</IsPackable>` in its `.csproj`, so the pack job's discovery skips it and it has never
been on nuget.org (flat container HTTP 404, search hits 0) — it ships as the server image plus
`SharpCoreDB.Server.Core`. That is pre-existing and deliberate, not a defect this promotion introduced;
the `SharpCoreDB.Server.[0-9]*` pattern in the publish job's layer 3 is simply inert.

A re-push of an existing version is a no-op because the publish step passes `--skip-duplicate`, which
is why the parked line's `2.0.0.3` run published nothing new. Two consequences worth remembering: the
parked line only publishes when `SharpCoreDBVersion` is bumped, and **every later push to `master`
re-runs the publish job** — harmless for an unchanged version, but a `master` push is never a private
operation.

### Local validation note, and one unidentified failure

Re-running the four CI suites on the owner's box to validate this record (a **documentation-only**
change) turned up one failure that could not be identified. A core-suite run reported
`Total: 1995, Errors: 0, Failed: 1`; the four runs after it — and `CI/CD Pipeline` on all three
platforms — reported 1995 / 0 / 0, TRX-verified. That red run was started without `-result-trx`, so the
failing test cannot be named, and no file differs between it and the green runs. The four suites on this
tree are green (core 1,995 · VectorSearch 248 · EF Core 116 · linq2db 24). Local runs now always pass
`-result-trx`, so a recurrence names itself instead of leaving a bare count behind.

### Metadata sweep follow-up (2026-10-10, after the push)

The promotion refreshed the *build* metadata (`Directory.Build.props`, `Directory.Packages.props`, each
package's `.csproj`) and was validated by building, packing and publishing — none of which can see a
claim that lives in prose. A repo-wide grep for the retired line's tokens, run afterwards, found it still
presented as current in **52 files**: the front page (4 places, plus a stale test count, 1,916 to 1,995 on
this tree), `ROADMAP.md`, `CODING_STANDARDS.md`, ~12 shipped package READMEs (`NuGet.README.md` install
snippets still pinning `2.1.0-rc.*`, TFM and language lines, the `Aspire` image tag `2.0.0.3` against
`2.1.0`), ~14 `docs/**` requirement pages — including the load-bearing runtime requirement in
`docs/server/INSTALLATION.md` — the publish-guide template, both `.runsettings` TFMs, `PLATFORM_SUPPORT.md`,
`SharpCoreDB.nuspec` (16 `net10.0` lib paths, tags, the `Microsoft.Data.Sqlite` floor), the `Examples/**`
and `tests/**` guides, and the decorative `// .NET 10` / `// C# 14` source labels. All corrected here.

One user-visible defect fell out of the same pass rather than the grep: the PostgreSQL handshake answered
`server_version` from a `const` literal, `SharpCoreDB 1.7.0 on .NET 10 (PostgreSQL protocol …)`, so every
client of the shipped `2.1.0` server read the pre-promotion product version *and* runtime. It is now derived
from `AssemblyInformationalVersion` and `Environment.Version.Major`, pinned by
`PgCatalogServiceTests.TryHandleCatalogQuery_Version_ReportsCurrentRuntimeAndProductVersion` (it fails
against the old literal, passes after the change — both runs recorded in the session that fixed it).
Integration suite 19 / 0 failed, core suite green under the CI filter.

The label number in that paragraph was the reached-by-hand count, not the total. A follow-up sweep measured the
remainder and swept it: **589 `C# 14` labels across 260 `.cs` files**. The substitution was byte-level on purpose -
those label lines carry check-mark decoration, so an ASCII-slice rewrite corrupts them - and it was verified
line-for-line: in all 260 files every changed line is a `C# 14` -> `C# 15` label and nothing else, and the
non-ASCII byte count of each file is unchanged. The same pass fixed the stale `C# 14` prose claims in `.md`
and corrected the two `master`-pointer sentences the flip had outdated.

One of the 589 labels is not a comment: the BenchmarkDotNet `Description = "Dictionary - Modern C# 14"` in
`Phase2C_InlineArraysCollectionExpressionsBenchmark.cs`. That benchmark's report name now reads `C# 15`, which
is the point of the sweep; nothing in the repository keys on the string, and the three stale
`TestResults/AotNet*/*.xml` artifacts that still contain it are gitignored output of past runs.

Deliberately left alone, so nobody "corrects" it later:

- Historical and dated records: `STABILITY_REPORT.md`, the parked-line comment in `SharpCoreDB.csproj`, the
  changelog's `2.0.0.x` sections, and `ci-net10.yml`, whose *name* is the truthful net10 label.
- `#if NET10_0` blocks in `src/**` and the `net10.0` rows of the multi-targeting matrices: dead on this
  line, but the parked line still compiles them.
- Upstream facts that merely mention a net10-era version (e.g. the `Microsoft.Data.Sqlite` pin OrchardCore
  brings).
- `SharpCoreDB.nuspec` and the root `NuGet.README.md` are on no pack path in this repository, yet they are
  the files a maintainer reads, so they are kept in sync deliberately rather than left to rot.
- The sweep changed claims about *the current line only*; every "on .NET 10" that describes the parked
  `release/v2.0` line is correct as written.
- The `.md` counterpart of the same sweep fixed **26 stale `C# 14` claims in 11 files** - 21 in ten requirement pages and guides, plus the five `// C# 14:` example labels in `.github/CODING_STANDARDS.md` (whose parked-line note, dated v1.0 history entry and an external course title stayed). `master`-pointer sentences that the flip made stale (`docs/Vectors/MUNARIUM_INSPIRATION_PLAN.md`, the toolchain line of `docs/performance/V2_PERFORMANCE_PLAN.md`) were corrected in place.

## The quality gate the promotion tripped, and what it was actually complaining about

SonarCloud's automatic analysis ran on the promotion's merge commit (`58f297e9`, 2026-10-10 18:14) and the
project's quality gate came back **ERROR** on two conditions: `new_reliability_rating` = 3 (C, an A is
required) and `new_security_rating` = 4 (D, an A is required). Maintainability (A), duplication (1.8 % against
a 3 % limit) and hotspots reviewed (100 %) passed, and there is no coverage condition at all: automatic
analysis does not collect coverage, which is also why the gate that failed is *not* the 18 % floor job (that
one is CI-based and lives in `ci.yml`).

Both failing conditions were read from `qualitygates/project_status`, and their causes from
`issues/search?sinceLeakPeriod=true`, which listed **4 MAJOR bugs and 13 vulnerabilities on new code**. The
promotion's own commits had made the whole 2.1 line "new code", so the gate was measuring the release rather
than the docs sweep that followed it. All 17 were addressed in the same session — the per-rule list, with the
mechanism used for each, is in the changelog — and none of them was fixed by weakening what the code
guarantees:

- **Four are deliberate and stay as they are**, annotated `// NOSONAR:<rule>` with the reason: the scalar
  double equality that must agree with the SIMD lanes' per-lane `==`, the medoid's exact equidistance
  tie-break, and the two seeded `Random`s that make the DiskANN graph reproducible. A cryptographic RNG there
  would break index determinism, and an epsilon on either comparison would make the scalar path and the vector
  path disagree about the same input.
- **The rest are real changes**: a field that was tested and then re-read is now pattern-bound; the harness's
  hand-rolled aggregation (carrying an emptiness test the analyzer could prove always true) now goes through
  the existing `MedianArm` helper; the three SQL-text interpolations in the harness are validated and prebuilt
  instead of assembled at the call site; the harness's temp directory is private and owner-only; workflow-level
  `packages: write` is scoped to the publishing job; `codecov/codecov-action` is pinned to a commit SHA; the
  `unsafe fixed` reserved region became an `[InlineArray(22)]` region; and the skill scripts' draws and hashes
  moved to the platform CSPRNG and SHA-256.

`// NOSONAR` is not a hope on this project either — it can be checked against the platform's own data.
`Database.Batch.cs` reports `S3776` on lines 1050, 962, 1359, 1436 and 1749, and *not* on 710 and 1217, which are
exactly its two NOSONAR-annotated lines (the same holds for its `S125` and `S1481` annotations), so the four
annotations added here will clear their findings the same way.

Verification for the batch: the tests project and the benchmark project both build with **0 errors**, the new
`ScdbStructureLayoutTests` is **3/3** (it is what makes the `[InlineArray]` swap safe to believe — the offset,
the 22 bytes and the packed size are asserted, not assumed), and the core suite is **1998 / 0 failed / 0
errors / 0 skipped**, i.e. the 1,995 of the published baseline plus the three new facts. The only remaining step
is a push: automatic analysis re-runs on the default branch and the two ratings should come back A. Nothing
local can pre-verify a SonarCloud re-analysis.

## GA checklist (around 2026-11-10 — the only outstanding work this decision creates)

1. `global.json`: `11.0.100-rc.1.26425.128` → `11.0.100`; drop `allowPrerelease`.
2. Drop `include-prerelease` and move to `11.0.x` in `ci.yml`, `compatibility-smoke.yml`,
   `dependency-submission.yml`, `publish-manual.yml` and `benchmarks.yml` (`ci-net10.yml` is unaffected).
3. Union types move from design to code in a new opt-in `SharpCoreDB.Unions` namespace
   (`docs/net11/UNION_TYPES_DESIGN.md`), still never retrofitted onto existing result types.
4. Re-run the write-path regression gate on the GA runtime and record a fresh baseline; rebuild the Docker
   image once `sdk:11.0` / `aspnet:11.0` resolve to the GA images.
5. The shipped `2.1.0` *build* metadata is refreshed — version fields, `<PackageReleaseNotes>`,
   descriptions and tags (see [After the flip](#after-the-flip-2026-10-10)) — and the prose that still
   claimed the retired line was corrected after the push rather than during it (metadata sweep follow-up
   below); note that the README text embedded in the already-published `2.1.0` packages is immutable and
   only changes with the next version published. What remains is the release *narrative*: the announcement
   text and the `README.md` / `PROJECT_STATUS.md` release text for the tag.

## What must not be done

- No **branch rename** and no default-branch pointer change: a rename stops the CI workflows matching their
  branch filters *and* hands the net11 commits to a net10 pipeline in one move (BEAT §9 row 9).
- Never merge the net11 line into a `net10.0` / C# 14 line — net11-only API and C# 15
  syntax would land in a line that cannot compile it. The flip makes that premise false by replacing
  `master`'s content, which is exactly why the net10 line is parked first.
- No encryption, durability or WAL semantics change rides on this promotion; the storage format is
  untouched and the reopen round-trip matrix stays the gate.


