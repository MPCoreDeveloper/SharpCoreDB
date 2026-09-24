<!-- bmad:context -->
<!-- Verified 2026-09-24 against a1cf15de. Managed by bmad-project-context; edits inside this block are replaced on refresh. Keep anything you want preserved outside the markers. -->

## SharpCoreDB

Embedded + networked database engine for .NET with AES-256-GCM encryption, a gRPC server, and a
package family spanning ADO.NET, EF Core, Dapper, linq2DB, YesSql, Sync, Analytics, VectorSearch,
Graph, EventSourcing, CQRS and Projections. C#/.NET, `LangVersion=preview`. Deep documentation lives
in `docs/` (start at `docs/INDEX.md`); the live performance mission is
`docs/performance/AUTONOMOUS_AGENT_BRIEF.md`. This branch (`perf/*`, v2.1 RC line) is `net11.0` /
C# 15 preview; `master` holds the net10.0 / C# 14 stable packages.

## Policy

- Never push. Commit locally to a feature branch such as `perf/autonomous-YYYYMMDD`; never commit to
  `master` or `release/*` (Brief §9).
- Never force-push, rewrite history, delete the worklog, change `global.json`, modify release/version
  metadata or package versions, or pack/publish.
- Never disable encryption to win, and never trade durability — WAL semantics, crash-consistency and
  the reopen round-trip matrix stay green (Brief §11).
- Never flip the fixed-width layout default: it is a measured −24% regression on the PK-less shape
  (Brief §8 trap 3).
- All documentation in English — docs, READMEs, specs, plans and code comments alike.
- Full SQLite compatibility is required: supported SQLite syntax and features must never be less than
  SQLite's; extra capability is fine.
- Server ships multiple databases plus system databases, and enforces HTTPS/TLS with minimum TLS 1.2 —
  never a plain-HTTP endpoint.
- New features stay optional; Event Sourcing ships as its own NuGet package; issue-driven user
  features outrank server-mode work.
- gRPC is the flagship server protocol; binary/HTTP are secondary.
- The graphical UI lives in the standalone repo `MPCoreDeveloper/SCDMS`, not here.

## Where things are

- Performance mission and its absolute rules: `docs/performance/AUTONOMOUS_AGENT_BRIEF.md`
- Performance plan (single source of truth): `docs/performance/INSERT_UPDATE_PERFORMANCE_PLAN.md`.
  Its only reporting channel is `docs/performance/WORKLOG.md` (append-only) — resume from the last
  `NEXT:` line, never from memory.
- Status, history and feature matrix: `docs/PROJECT_STATUS.md`, `docs/CHANGELOG.md`,
  `docs/FEATURE_MATRIX.md`, `docs/INDEX.md`
- SIMD code must satisfy `.github/SIMD_STANDARDS.md` — `System.Runtime.Intrinsics` only, never
  `System.Numerics.Vector<T>`.
- Regression canaries that must stay green: `ReopenRoundTripMatrixTests`, `FormatCompatPolicyTests`,
  `FixedWidthBulkUpdateTests`, `FixedWidthBulkDeleteTests`, `FixedWidthPatchTests`,
  `WritePathProfilerTests`, `FixedWidthInlineValueTests.Reopen_KeepsInlineAndOverflowValues`,
  `SingleFileDirectoryParityTests`

## Running and verifying

- The .NET 11 RC SDK is pinned in `global.json` (`11.0.100-rc.1.26425.128`, `allowPrerelease`).
  Target `net11.0` / C# 15 preview here; do not change the pin and do not assume a pre-.NET 11 context.
- **`dotnet test` does not work in this repository** — Microsoft.Testing.Platform rejects the VSTest
  target on .NET 10 SDK+. Run the MTP executable directly instead:
  `tests/SharpCoreDB.Tests/bin/Release/net11.0/SharpCoreDB.Tests.exe -filterVSTest 'FullyQualifiedName~MyTestClass'`
  (build it first; the same form writes TRX via `-result-trx <path>`).
- Build the test project directly while iterating:
  `dotnet build tests/SharpCoreDB.Tests/SharpCoreDB.Tests.csproj -c Release -f net11.0` — about 22s.
- CI runs the core suite with `Category!=Debug&Category!=Manual&Category!=Performance`; 30
  `Performance` and 3 `Debug` tests exist and are excluded by default. Tag new slow or benchmark
  tests `[Trait("Category","Performance")]` so they stay out of the default run.
- A clean build still emits ~299 warnings. Do not chase them as part of unrelated work.
- CI restores and builds `SharpCoreDB.CI.slnf` — not the `.sln` — with
  `--configfile NuGet.Config /p:UseLocalProjectReferences=true`.
- Coverage floor is 18% (codecov allows up to a 2% project drop; 50% of new lines in a PR must be
  covered). Verify it before calling work ready for commit.

## Conventions that differ from defaults

- Every NuGet version lives in `Directory.Packages.props` (central package management is on). Never
  add a version to an individual `.csproj`; `Directory.Build.props` additionally pins
  `Microsoft.Data.Sqlite` 10.0.10 and suppresses a large `NoWarn` set.
- Test projects must reference `xunit.v3` 4.0.0 — never `xunit` v2. Keep `xunit.runner.visualstudio` at
  4.0.0+ and `Microsoft.NET.Test.Sdk` at 18.9.0+.
- Prefer Microsoft-backed packages and avoid prerelease ones, the deliberately pinned preview SDK
  being the exception.
- Name tests `MethodName_StateUnderTest_ExpectedBehavior`.
- Prefer targeted test runs over full-suite scans while iterating; query the failing tests directly
  rather than broadening when one hanging test is the bottleneck.

## Known pitfalls

- Quote **ratios, never absolutes**, when comparing against SQLite — SQLite's own reference drifts
  between sessions and even runs. Report encrypted (default) and `NoEncryptMode=true` numbers side by
  side, never one alone.
- Only the `--pk` arm is like-for-like: SQLite's `INTEGER PRIMARY KEY` is rowid with in-place page
  edits, while the default job's SharpCoreDB arm uses a non-key predicate.
- Never infer a verdict from a ratio before reading the code that produces it — this plan has paid
  four times for acting on an inferred reading.
- The write-path gate refuses to record a baseline on a loaded machine. Re-record with
  `--write-baseline` only on a quiet machine, and state the reason in the commit message.
- Document competitor developer experience honestly; if an API is hard to use or its docs disagree
  with the API, that is a real benchmark finding. Ease of integration counts as much as raw numbers.
- Check roadmap status against open issues before calling anything done, and mark the matching issue
  draft document resolved when roadmap work completes.

<!-- /bmad:context -->

