# Quality: mutation testing with Stryker.NET (state 2026-11-13)

This file records how mutation testing is configured for SharpCoreDB, the exact commands that work on this
box, the calibrated baseline, and the one project that is **not** mutatable here and why. It exists so the
setup is not rediscovered, and so the score is read for what it is: **informational**, not a gate.

Mutation testing answers a different question from coverage. Coverage says a line ran; a mutation says a
test noticed when the line lied. The score is `killed / (killed + survived)` — mutants that fail to compile
and mutants excluded by the `mutate` filter are not part of the denominator, and `NoCoverage` mutants are
reported separately so an untested file cannot look "survived".

## Tooling

- **Stryker.NET 5.1.0**, pinned as a local tool in `.config/dotnet-tools.json`
  (`dotnet tool restore` once per clone). No package version, `global.json`, or CI workflow was changed.
- Runner is **`mtp`** (Microsoft.Testing.Platform), because `dotnet test` does not work in this repository —
  the same reason the test suites are run as executables (see `AGENTS.md`). xunit.v3 4.0.3 ships
  `xunit.v3.mtp-v2`; no downgrade and no `xunit.v3.mtp-off` was needed.
- The tool accepts `--test-runner vstest|mtp`, `--concurrency`, `--test-case-filter`, `--mutation-level`,
  `--mutate`, `--output`, `--since`. `coverage-analysis` and `mutate` are **config-file only**;
  `output` is **CLI only**.

## Where the config lives, and why it is not at the repository root

`stryker-config.json` sits **inside the test-project folder**, not at the root. Two measured reasons:

1. **`test-projects` is forbidden in solution context.** A root config that points at
   `SharpCoreDB.slnx` makes Stryker spin up a test server per test project (28 here) over MTP and it never
   completes the handshake. The stall is in the solution mode, not in the tests.
2. **Config paths resolve relative to the working directory, not to the config file**, and this repository
   has **two root solutions** (`SharpCoreDB.sln`, `SharpCoreDB.slnx`; `SharpCoreDB.CI.slnf` targets the
   `.slnx`). A root-level config cannot unambiguously target one test project.

So: **run `dotnet stryker` from the test-project directory**, and let it pick up the config beside the
`.csproj`. `mutate` patterns are matched against Stryker's own path view, so use path-agnostic globs
(`**/Functional/Option.cs`). A relative pattern such as `../../src/Foo/Bar.cs` silently matches **nothing**,
and every file is then reported `Excluded` with a 0/0 score — a false "nothing to do", not an error.

## Commands

```powershell
# once
dotnet tool restore

# calibrate / re-baseline the Functional project (≈35 s with the committed scope)
Set-Location tests\SharpCoreDB.Functional.Tests
dotnet stryker --output ../../artifacts/stryker-baseline --verbosity info --skip-version-check

# from the repository root, same thing
dotnet stryker --config-file tests\SharpCoreDB.Functional.Tests\stryker-config.json --output artifacts\stryker
```

Reports land in `<output>/reports/mutation-report.{html,json}`; `artifacts/` is gitignored, so nothing large
is ever committed. Exit code is driven by the `break` threshold (0 here), so a run cannot fail a pipeline.


## The calibrated baseline (2026-11-13, functional project)

Scope is deliberately one file, because each mutant pays a rebuild and this is a calibration, not a survey.

- `tests/SharpCoreDB.Functional.Tests/stryker-config.json`
- `project`: `SharpCoreDB.Functional.csproj`, `test-projects`: `SharpCoreDB.Functional.Tests.csproj`
- `test-runner: mtp`, `configuration: Release`, `target-framework: net11.0`, `concurrency: 2`,
  `break-on-initial-test-failure: true`, `coverage-analysis: perTest`
- `mutate: ["**/Functional/Option.cs"]` — 90 lines

| Run | Tests | Mutants tested | Killed | Survived | Score | Wall |
|---|---|---|---|---|---|---|
| baseline before test work | 38 | 33 | 12 | 21 | **36.36 %** | 1 m 54 s¹ |
| after `OptionTests` | 70 | 33 | 33 | 0 | **100.00 %** | 34 s |

¹ The first line was measured with `coverage-analysis: off` (115 s). The same state under the committed
`perTest` setting scored identically (36.36 %, 13 mutants tested after 20 were marked `NoCoverage`) in 34 s,
which is why `perTest` is the committed value.

`472 mutants created` for the whole assembly; the `mutate` filter excludes 387, 49 are compile errors inside
the generated-files/other-file scope, 3 are removed by the "block already covered" filter, 33 are tested.
Compile-error and ignored mutants are outside the score by definition — the 36.36 % → 100 % movement is
entirely `Option<T>` behaviour being pinned by new assertions.

## How the numbers were verified (not trusted)

A score is worthless until someone has checked that its verdicts match reality. Four checks were run:

1. **A `Killed` mutant was reproduced by hand.** Mutant 397 (`_isSome` → `true` in the synchronous
   `Match`) was applied to the source; two existing tests failed
   (`NullSafetyComparisonTests.RealWorkload_BatchLookupWithMissingReferences`,
   `…ExceptionDrivenMissingData_IsSlowerThanOptionFlow`). Stryker's `Killed` was truthful.
2. **A `Survived` mutant was reproduced by hand.** The same mutation in the **async** `Match` overload
   (line 65) left the whole suite green — a genuine test gap, not a tool artefact. The async overload had no
   test at all.
3. **The added tests were proven red before green.** Each new assertion was checked against the mutation it
   is meant to catch: the `IfNone(Func<T>)` mutant fails
   `IfNoneFactory_WhenNone_ReturnsFactoryValue`; the "hash the value even when empty" mutant fails
   `GetHashCode_WhenNone_IsZeroEvenWhenDefaultHashesNonZero`. Both pass again with the source restored, and
   `git diff -- src/` was empty after every cycle.
4. **Every survivor was read, not assumed.** One `GetHashCode` survivor looked like an equivalent mutant
   (`EqualityComparer<T>.Default.GetHashCode(null)` returns 0, so `true ? hash(_value) : 0` is invisible for
   reference types). It is *not* equivalent: with a value type whose default hashes non-zero it changes the
   result. That case is now in `OptionTests` as `NonZeroHashValue`.

Two test-suite defects were found this way and fixed in the same session — the async `Match` overload and
the `IfNone(Func<T>)` None branch were untested, and the `None` hash sentinel was unpinned. **No product
source was changed**; the source was mutated, measured, and restored (`git diff -- src/` empty).

## Seeds noticed while calibrating (documented, not re-litigated)

- `coverage-analysis: perTest` is the better default here: it marks mutants `NoCoverage` instead of running
  the suite on them, it supports mutant *mixing* (one instrumented build, one activation per mutant), and it
  was faster at the same score (34 s vs 115 s with `off`). Under `perTest` the per-mutant cost is small —
  the 2026-11-13 engine calibration tested 49 mutants in **2 s**. Under `coverage-analysis: off` each mutant
  pays a full rebuild, and that is the setting that made the earlier engine attempts look hopeless.
- Keep `break-on-initial-test-failure: true`. A malformed config then fails in seconds with a real message
  instead of stalling.

## The engine project is runnable — corrected 2026-11-13

An earlier revision of this file marked `src/SharpCoreDB` **BLOCKED** ("initial run never returns"). That was
wrong, and the correction matters because it changes the whole budget model.

What actually happens: Stryker spends a long time in its own analysis phase before the first mutant is tested,
and it does that **inside its own process** — no test host is created and the CPU sits at ~1.2 cores. The
phase completes; it just takes longer than the 10-minute timeout the earlier attempts used.

Measured with a **clean minimal host** (`ProjectReference` to the engine only, no coverlet, no MTP coverage
extension, no runsettings, no serial xunit config, 11 tests) to rule the test host out as the cause:

| Phase | Observed |
|---|---|
| analysis + build + `Number of tests found: 11` | 14 s |
| mutation generation (52 192 mutants for the 110 249-line assembly) | **9 m 44 s** |
| `coverage-analysis: off` — 119 mutants for `SqlFunctions.cs` | killed at 10 min (rebuild per mutant) |
| `coverage-analysis: perTest` — 49 mutants for `SqlFunctions.cs` | **2 s** of testing, 10 m 41 s total |

So the engine is mutatable on this box. The 10-minute analysis is a **fixed cost per run**, and under
`perTest` the testing itself is nearly free (mutation switching: one instrumented build, one activation per
mutant). Two consequences drive the matrix design:

1. **Amortise the fixed cost** — slice per folder, not per file. A per-file slice pays 10 minutes for a
   handful of mutants.
2. **Never use `coverage-analysis: off` on the engine** — that is what turns 119 mutants into an hour of
   rebuilds and made the first attempts look like a hang.

### First real engine numbers (`SqlFunctions.cs`, 11 tests, `perTest`)

| Mutants | Killed | Survived | NoCoverage | CompileError | Score |
|---|---|---|---|---|---|
| 119 in scope | **33** | **16** | **70** | 41 (assembly-wide) | **27.73 %** |

`SqlFunctions` is a good example of what mutation testing is for: a file with passing tests, 70 mutants no
test reaches at all, and 16 wrong behaviours that a green suite accepts. That is the work queue.

## The slice matrix

Config lives in one place: `scripts/mutation-matrix.json`. A slice is *(project under test, test host, mutate
scope)*; `scripts/run-mutation-matrix.ps1` writes a temporary Stryker config per slice, runs it, and
aggregates every JSON report into `artifacts/mutation/summary.{json,md}`.

```powershell
pwsh scripts/run-mutation-matrix.ps1 -List -Tier all      # what exists
pwsh scripts/run-mutation-matrix.ps1 -Tier pr             # fast modules (~15 slices)
pwsh scripts/run-mutation-matrix.ps1 -Tier nightly        # engine slices (8)
pwsh scripts/run-mutation-matrix.ps1 -Slice functional -BreakAt 90
pwsh scripts/run-mutation-matrix.ps1 -AggregateOnly       # re-render the summary
```

- `pr` tier: one slice per adapter/module project, each with its own test host.
- `nightly` tier: the engine in eight folder slices (`engine-services`, `engine-storage`,
  `engine-datastructures`, `engine-database`, `engine-core`, `engine-optimizations`,
  `engine-timeseries-query`, `engine-misc`), hosted by `SharpCoreDB.Tests` with `perTest`.
- The script exits non-zero only when a slice *could not run* (error, timeout, missing report). A low score
  is reported, not enforced, until `-BreakAt` is passed.
- `.github/workflows/mutation.yml` runs the `pr` tier on a PR labelled `mutation`, the `nightly` tier on a
  schedule, or any tier manually. It uploads per-slice reports and publishes the summary to the job summary.

### Score reporting rules

- Score = `(killed + timeout + runtimeError) / (killed + survived + timeout + runtimeError + noCoverage)`.
  `CompileError` and `Ignored` are outside the score, and `NoCoverage` counts as **not detected**: a line no
  test reaches must not raise the score.
- Quote the slice and the mutant count with the score. "41.53 %" alone is not a fact about a module; "147
  killed / 87 survived / 120 no-coverage over 354 mutants in `SharpCoreDB.Functional`" is.

### First matrix datapoint (2026-11-13)

| Slice | Scope | Killed | Survived | NoCoverage | Score | Wall |
|---|---|---|---|---|---|---|
| `functional` | whole project, 11 files | 147 | 87 | 120 | **41.53 %** | 188 s |

`Option.cs` alone scores 100 % after the tests added in the first session; the module score is 41.53 %
because the other ten files still carry 87 surviving mutants and 120 uncovered ones.

## Rules of use

- Treat the mutation score as **informational** first. The workflow never blocks `ci.yml`, and `--break-at`
  stays 0 until a slice's scope is stable enough that a threshold means something.
- Slice by folder/file under work and keep the scope small enough to read; the report's value is the
  survivor list, not the percentage.
- Keep the per-test-project config (e.g. `tests/SharpCoreDB.Functional.Tests/stryker-config.json`) for
  interactive runs, and the manifest for anything automated. Both use path-agnostic `mutate` globs.
- When a survivor turns up: reproduce it by hand **before** deciding it is equivalent — the 2026-11-13
  `GetHashCode` survivor and the async `Match` survivor were both real, and one of them was only visible
  with the right `T`.

