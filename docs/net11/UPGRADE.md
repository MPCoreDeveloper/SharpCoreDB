# SharpCoreDB 2.1.0 — .NET 11 / C# 15

> Branch: `master` · Package version: `2.1.0`

## .NET 11 GA (2026-11-10) — what the GA upgrade changed on this line

- `global.json` pins the **GA SDK** `11.0.100` (`rollForward: latestPatch`); `allowPrerelease` is gone,
  so an RC SDK no longer satisfies the pin. `src/SharpCoreDB/global.json` carries the same pin.
- CI installs `11.0.x` and no longer passes `include-prerelease` — `ci.yml`, `compatibility-smoke.yml`,
  `dependency-submission.yml`, `publish-manual.yml` and `benchmarks.yml` (`ci-net10.yml` is unaffected).
- Runtime Async is enabled **once for every net11.0 project** (see §2) instead of on three projects by
  hand, and its two measured limits are documented there.
- `sdk:11.0` / `aspnet:11.0` in `src/SharpCoreDB.Server/Dockerfile` now resolve to the GA images, which
  carry the runtime that Runtime Async compiles against.

## What this is

The 2.1.0 line is **net11.0-only**, built with **C# 15 preview**:

```xml
<TargetFramework>net11.0</TargetFramework>
<LangVersion>preview</LangVersion>
```

- The **net10.0 / C# 14** line is the **v2.0 stable packages**, parked on `release/v2.0` (e.g.
  `SharpCoreDB` 2.0.0.3) since the 2026-10-10 promotion — see `PROMOTION_TO_MAIN.md` in this folder.
- The two lines are maintained on separate branches. The net11 line never carries a net10.0 target, so
  there is no multi-targeting and no per-target `#if` drift in the shipped assemblies.

## What .NET 11 consumers get

### 1. Native Zstandard compression
`BlockCompressionMode.Zstd` is backed by `System.IO.Compression.ZstandardStream` (native zstd):

- `OptionalCompressionLevel` maps to zstd quality: `Fastest → 1`, `Optimal → 5`, `SmallestSize → 19`.
- A frame checksum is appended (`ZstandardCompressionOptions.AppendChecksum = true`), so corruption is
  detected on read.

### 2. Runtime Async (every net11.0 project, since GA)
Every net11.0 project in the repository compiles with `Features=runtime-async=on`, set **once** in the
repository-root `Directory.Build.targets`: async/await suspends through the runtime instead of through a
compiler-generated state machine per method — cleaner stack traces, lower overhead, and no allocation for
the shape consumers actually write. Opt a single project out with `<UseRuntimeAsync>false</UseRuntimeAsync>`.

Measured on the GA SDK (`11.0.100`), 1.000-call loop:

| Shape | Classic codegen | Runtime Async |
|---|---|---|
| `await` a synchronously-completing `async Task<T>` | 71,4 B/call | **0 B/call** |
| `.GetAwaiter().GetResult()` on the same call (not awaited) | 71,4 B/call | 72,0 B/call |
| compiler-generated `IAsyncStateMachine` types in `SharpCoreDB.dll` | 163 | 2 (async iterators only) |

Two limits are stated rather than glossed over: the win applies to the **awaited** shape (a discarded or
blocking call still materialises the `Task`), and `async IAsyncEnumerable<T>` iterators keep a
compiler-generated state machine at GA — runtime async does not cover them yet.

Canary: `tests/SharpCoreDB.Tests/Net11/RuntimeAsyncCodegenTests.cs` (fails on both facts when the flag is
dropped, e.g. `-p:UseRuntimeAsync=false`). The old `DOTNET_RuntimeAsync` / `UNSUPPORTED_RuntimeAsync`
environment variables are gone.

Reference: <https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-11/runtime>

### 3. C# 15 preview API surface (`SharpCoreDB.Net11`, opt-in)
Import `using SharpCoreDB.Net11;` to unlock additive members:

```csharp
using SharpCoreDB.Net11;

TableInfo? docs = db["docs"];                              // extension indexer → TableInfo? (case-insensitive)
List<string> names = Net11Additions.NewList<string>(128);  // collection-expression capacity argument
```

These members are source-gated (`#if NET11_0_OR_GREATER`) and compiled only for net11.0.

### 4. Opt-in union outcomes (`SharpCoreDB.Unions`)
C# 15 union types — GA since 2026-11-10 — exposed as an **additive, opt-in** namespace. Nothing is
retrofitted: `QueryResult`, `ExecuteResult` and every `ExecuteSQL` signature stay byte-identical.

```csharp
using SharpCoreDB.Unions;

QueryOutcome outcome = new QueryFailure("syntax error");

string text = outcome switch
{
    QuerySuccess s => $"rows: {s.Rows.Count}",
    QueryNotFound  => "not found",
    QueryFailure f => $"error: {f.Message}",
};   // exhaustive — dropping an arm is a compile error, which is the whole point
```

`QueryOutcome` (3 cases), `ExecuteOutcome` (rows affected or failure) and `TableLookup` (found or
missing — the shape the C# 15 extension indexer `db["name"]` converts into). Unions are structs and the
case types are `record class`, so equality and deconstruction live on the cases. Values are boxed into
`IUnion.Value`, so a consumer can also inspect one generically. Tests:
`tests/SharpCoreDB.Tests/Net11/UnionOutcomeTests.cs` — the switches there carry no discard arm, so
exhaustiveness is enforced by the compiler rather than asserted at run time.

## Munarium-inspired vector features (also in this RC)

- `DiskAnnIndex` + `DiskAnnConfig` (high-recall ANN), `IVerifiableIndex` with canonical SHA-256
  `ArtifactManifest`, `HybridFusionAlpha`, and the `BuildResult` hierarchy.
- SQL DDL: `CREATE VECTOR INDEX … USING DISKANN`. See `docs/Vectors/DISKANN_SQL_DDL.md` and
  `docs/Vectors/MUNARIUM_INSPIRATION_PLAN.md`.

## What is deliberately NOT in this line yet

| Item | Reason |
|---|---|
| **SIMD Zip / Unzip / Concat / CreateGeometricSequence** | Not applied yet — needs benchmarking against the existing `SimdHelper` AVX-512 → AVX2 → SSE/Neon kernels first. |
| **Broad `[with(capacity: …)]` rewrites** | Would require `#if` gating across shared hot-path code; exposed only through the net11-only helper for now. |

## Consuming the line

```
git fetch origin master
git checkout master
dotnet pack src/SharpCoreDB/SharpCoreDB.csproj -c Release
```

The `.nupkg` carries **only `net11.0` assets** (plus the per-RID assemblies). A project that targets
net10.0 must stay on the `release/v2.0` 2.0 packages.
