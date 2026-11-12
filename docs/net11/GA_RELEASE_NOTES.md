# .NET 11 GA release — notes and checklist

> Status: **prepared, not released.** Everything on the repository side is done and verified; the steps in
> §4 are owner actions by policy (version metadata, tag, merge/publish). Prepared 2026-11-12.

## 1. What this release is

The `master` line moves from the .NET 11 **RC** to the .NET 11 **GA** SDK (`11.0.100`) and turns the
Runtime Async switch on for **every** project instead of three hand-copied ones. It also adds the opt-in
`SharpCoreDB.Unions` surface and a capability-based regression gate. **No storage-format, encryption,
durability or WAL change** — the reopen round-trip matrix and the format-compat policies are green.

## 2. Measured effects (GA runtime `11.0.0`, this box, same binary per A/B)

| | before | after |
|---|---|---|
| awaited, synchronously-completing `async Task<T>` | 71,4 B/call | **0 B/call** |
| compiler `IAsyncStateMachine` types in `SharpCoreDB.dll` | 163 | **2** (the async iterators) |
| INSERT with `DOTNET_gcServer=1` (host lever, documented) | ~200.000 ops/s | **~359.000 ops/s** (`≈1,75×`) |
| batch INSERT allocation (`object[]` overload) | 919 B/row | **894 B/row** (−2,7 %, canary-pinned) |
| gate against the previous release's baseline | — | **PASSED**, worst cell **1,08×**, UPDATE **faster** on both arms (0,85×/0,94×) |

Gate note: the UPDATE cell's median is disturbance-dominated on this box (one rep can carry a ~50 ms GC
pause), so the gate now records and compares the **best-of-reps** capability when the baseline carries it.
That is why the release can state the UPDATE numbers without them flipping run to run.

## 3. Upgrade notes for consumers

- **A GA .NET 11 SDK is required.** `global.json` pins `11.0.100` with `rollForward: latestPatch` and no
  `allowPrerelease`, so an RC SDK no longer satisfies the pin.
- Still **`net11.0` only** (C# 15). The `net10.0` / C# 14 packages remain the v2.0 line on `release/v2.0`.
- **Runtime Async is on for every project** and needs no opt-in. A project can opt out with
  `<UseRuntimeAsync>false</UseRuntimeAsync>`. Two limits are documented rather than glossed: the zero-allocation
  result applies to the **awaited** shape (a blocking `.GetAwaiter().GetResult()` still materialises the task),
  and `async IAsyncEnumerable<T>` iterators keep a compiler state machine.
- **`SharpCoreDB.Unions` is opt-in and additive**: `QueryOutcome`, `ExecuteOutcome`, `TableLookup`. Nothing is
  retrofitted — `QueryResult`, `ExecuteResult` and every `ExecuteSQL` signature are unchanged, and `BuildResult`
  stays a record hierarchy.
- **Batch INSERT allocates 2,7 % less per row** with identical behaviour (absent column → its default,
  explicit `NULL` → `NULL`).
- On many-core hosts, `DOTNET_gcServer=1` is the measured write-path lever (`docs/manual/performance.md` §7.7);
  `SharpCoreDB.Server` already sets it.

## 4. Checklist

Done and verified in the repository (evidence in `artifacts/`):

- [x] `global.json` (both copies) pins the GA SDK; `allowPrerelease` removed.
- [x] CI pins `11.0.x` without `include-prerelease` (`ci.yml`, `compatibility-smoke.yml`,
      `dependency-submission.yml`, `publish-manual.yml`, `benchmarks.yml`); `ci-net10.yml` untouched.
- [x] Runtime Async defined once in `Directory.Build.targets` (per-project opt-out), canary
      `RuntimeAsyncCodegenTests`.
- [x] `SharpCoreDB.CI.slnf` builds with the GA SDK — **Build succeeded, 0 errors**.
- [x] Core suite **2124 / 0 failed / 0 errors / 0 skipped**; the eight standing canaries **57 / 0 / 0 / 0**.
- [x] Write-path gate **PASSED** on the GA runtime; capability baseline recorded (`rawBest`/`defaultBest`).
- [x] Documentation-consistency tests **9/9**; the GA checklist in `docs/net11/PROMOTION_TO_MAIN.md` is closed.
- [x] Server image rebuilt on the GA base images: `sharpcoredb-server:net11-ga` (604 MB), reports runtime
      `11.0.0`.

Owner actions (policy: version metadata, packing and publishing are the owner's):

- [ ] **Version bump** in the `src/**/*.csproj` files (`Version`, `AssemblyVersion`, `FileVersion`) plus
      `<PackageReleaseNotes>` / description / tags — the rail keeps the agent out of release metadata.
- [ ] **Tag** the release (`v2.1.x`) after the merge.
- [ ] **Merge to `master`** — that push runs `pack` and *Publish to NuGet.org*
      (`.github/workflows/ci.yml`), or use **Manual NuGet Publish**
      (`.github/workflows/publish-manual.yml`) with an explicit `packVersion`.
- [ ] **GitHub Release** with the notes from §1–§3.
- [ ] **Push the server image**, if this release publishes one:
      `docker tag sharpcoredb-server:net11-ga ghcr.io/mpcoredeveloper/sharpcoredb-server:<tag>`.
- [ ] Optionally re-record the baseline once more from an **elevated** shell with `WSearch` stopped
      (`scripts/quiet-machine.ps1 -Apply -StopServices` until it says `QUIET`), so the reference is taken on a
      fully quiet box — it is not required for this release, because the capability statistic is already
      reproducible to ~1 % on the UPDATE cells.

## 5. What was deliberately not done

Version fields, `<PackageReleaseNotes>`, packing, publishing, the tag and the Docker push — all owner steps
under the standing rail. Nothing in this release changes the store format, so an existing database file remains
readable in both directions.
