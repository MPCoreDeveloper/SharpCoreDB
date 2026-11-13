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

- Each **mutant costs a full rebuild**, so budget minutes, not seconds: `concurrency: 2` on this 4-core box
  is about right, and a 33-mutant file takes ~30 s while the same file with `coverage-analysis: off` and
  38 tests took ~2 minutes (more mutants reach the full suite).
- `coverage-analysis: perTest` is the better default here: it marked 20 mutants `NoCoverage` instead of
  running the suite on them, and it was *faster* (34 s vs 115 s) at the same score.
- Keep `break-on-initial-test-failure: true`. A malformed config then fails in seconds with a real message
  instead of stalling.

## BLOCKED: the engine project (`src/SharpCoreDB/SharpCoreDB.csproj`)

`tests/SharpCoreDB.Tests/stryker-config.json` carries the correct settings for the engine
(`project: SharpCoreDB.csproj`, `test-projects: SharpCoreDB.Tests.csproj`, `test-runner: mtp`,
`mutate: ["**/SqlFunctions.cs"]`), but **it does not finish on this box** — measured 2026-11-13, three
attempts:

| Attempt | Settings | Observed |
|---|---|---|
| 1 | `coverage-analysis: perTest`, relative `mutate` pattern that matched nothing | the whole project stayed in scope; instrumentation enumeration at ~6 s per mutant, ids past 32 000 — never reached the tests |
| 2 | `coverage-analysis: off`, `**/SqlFunctions.cs`, `concurrency: 2` | `Number of tests found: 2257`, then `Initial test run started` and no further output for 10+ min |
| 3 | `coverage-analysis: off`, `**/SqlFunctions.cs{176..230}`, `--test-case-filter FullyQualifiedName~SqlFunctionsTests`, `concurrency: 1` | `Number of tests found: 11`, then `Initial test run started` and no further output; the `dotnet` process had burned **500 s of CPU** and **no test host was ever created** in the output tree |

The control rules out test volume and parallelism: the same suite finishes in **119 s / 2265 tests, all
green** when run directly (`SharpCoreDB.Tests.exe`), and 11 filtered tests take under a second. The
functional project completes the identical phases (analysis → build → 70 tests found → initial run →
mutants) in **17 s**.

Scale explains the cost: the engine is **366 files / 110 249 lines / 1.89 MB IL** against the functional
project's 11 files / 1 410 lines / 49 KB — roughly 39×. Stryker pays a whole-assembly weave per run, and on
this hardware that does not converge within any budget worth spending for a baseline.

**Status: BLOCKED** — a tool/hardware cost, not a defect in this repository, so nothing here is "fixed" by
editing code. What unblocks it, in order of cost:

1. Run the same config on a CI/build agent with more cores and no Defender scanning of the working tree —
   nothing needs to change in the repository.
2. `--mutation-level Basic` (fewer mutants, not a smaller weave) — untested here, one long run will tell.
3. Mutate an adapter-sized project that carries engine behaviour through its own tests (the functional and
   linq2DB suites do this today), and keep the engine score for release-time agents.

Until then, **do not quote an engine-project mutation score**: there isn't one, and the `0/0` table a
stalled run prints looks like a clean pass while being exactly the opposite.

## Rules of use

- Treat the mutation score as **informational**. It is not wired into CI, and no workflow was changed.
- Scope `mutate` to the files under work; a whole-project run pays a rebuild per mutant and buys a number
  nobody reads.
- Keep the config next to the test project, and run from that directory.
- When a survivor turns up: reproduce it by hand **before** deciding it is equivalent — the 2026-11-13
  `GetHashCode` survivor and the async `Match` survivor were both real, and one of them was only visible
  with the right `T`.

