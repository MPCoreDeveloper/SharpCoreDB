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
  lives on **`fix/net10-parked-ci`** (branched from `release/v2.0`), which adds that branch's own
  `ci-net10.yml` — with the `Fail on banned portable SIMD API` step omitted, because the parked line
  still carries one `System.Numerics.Vector<T>` usage and the checker that enforces the standard was
  added on the net11 line. Merge it with `git switch release/v2.0 && git merge --ff-only fix/net10-parked-ci`.
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

## GA checklist (around 2026-11-10 — the only outstanding work this decision creates)

1. `global.json`: `11.0.100-rc.1.26425.128` → `11.0.100`; drop `allowPrerelease`.
2. Drop `include-prerelease` and move to `11.0.x` in `ci.yml`, `compatibility-smoke.yml`,
   `dependency-submission.yml`, `publish-manual.yml` and `benchmarks.yml` (`ci-net10.yml` is unaffected).
3. Union types move from design to code in a new opt-in `SharpCoreDB.Unions` namespace
   (`docs/net11/UNION_TYPES_DESIGN.md`), still never retrofitted onto existing result types.
4. Re-run the write-path regression gate on the GA runtime and record a fresh baseline; rebuild the Docker
   image once `sdk:11.0` / `aspnet:11.0` resolve to the GA images.
5. The shipped `2.1.0` metadata is already refreshed — version fields, `<PackageReleaseNotes>`,
   `NuGet.README.md`, descriptions and tags (see [After the flip](#after-the-flip-2026-10-10)); what
   remains is the release *narrative*: the announcement text and the `README.md` / `PROJECT_STATUS.md`
   release text for the tag.

## What must not be done

- No **branch rename** and no default-branch pointer change: a rename stops the CI workflows matching their
  branch filters *and* hands the net11 commits to a net10 pipeline in one move (BEAT §9 row 9).
- Never merge the net11 line into a `master` that still builds `net10.0` / C# 14 — net11-only API and C# 15
  syntax would land in a line that cannot compile it. The flip makes that premise false by replacing
  `master`'s content, which is exactly why the net10 line is parked first.
- No encryption, durability or WAL semantics change rides on this promotion; the storage format is
  untouched and the reopen round-trip matrix stays the gate.


