# SharpCoreDB v2.1 RC — .NET 11 / C# 15

> Branch: `release/v2.1.0.0-RC.3` · Package version: `2.1.0-RC.3`

## What this is

The v2.1 RC line is **net11.0-only**, built with **C# 15 preview**:

```xml
<TargetFramework>net11.0</TargetFramework>
<LangVersion>preview</LangVersion>
```

- The **net10.0 / C# 14** line is the **v2.0 stable packages on `master`** (e.g. `SharpCoreDB` 2.0.0.3).
- The two lines are maintained on separate branches. The v2.1 branch never carries a net10.0 target, so
  there is no multi-targeting and no per-target `#if` drift in the shipped assemblies.

## What .NET 11 consumers get

### 1. Native Zstandard compression
`BlockCompressionMode.Zstd` is backed by `System.IO.Compression.ZstandardStream` (native zstd):

- `OptionalCompressionLevel` maps to zstd quality: `Fastest → 1`, `Optimal → 5`, `SmallestSize → 19`.
- A frame checksum is appended (`ZstandardCompressionOptions.AppendChecksum = true`), so corruption is
  detected on read.

### 2. Runtime Async
The project compiles with `Features=runtime-async=on`: async/await emits **runtime-native state machines**
instead of per-method generated ones — cleaner stack traces, better debuggability, lower overhead. No API
or behavior change. Opt a single project out with `<UseRuntimeAsync>false</UseRuntimeAsync>`
(the old `DOTNET_RuntimeAsync` / `UNSUPPORTED_RuntimeAsync` environment variables are gone).

Reference: <https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-11/runtime>

### 3. C# 15 preview API surface (`SharpCoreDB.Net11`, opt-in)
Import `using SharpCoreDB.Net11;` to unlock additive members:

```csharp
using SharpCoreDB.Net11;

TableInfo? docs = db["docs"];                              // extension indexer → TableInfo? (case-insensitive)
List<string> names = Net11Additions.NewList<string>(128);  // collection-expression capacity argument
```

These members are source-gated (`#if NET11_0_OR_GREATER`) and compiled only for net11.0.

## Munarium-inspired vector features (also in this RC)

- `DiskAnnIndex` + `DiskAnnConfig` (high-recall ANN), `IVerifiableIndex` with canonical SHA-256
  `ArtifactManifest`, `HybridFusionAlpha`, and the `BuildResult` hierarchy.
- SQL DDL: `CREATE VECTOR INDEX … USING DISKANN`. See `docs/Vectors/DISKANN_SQL_DDL.md` and
  `docs/Vectors/MUNARIUM_INSPIRATION_PLAN.md`.

## What is deliberately NOT in this RC

| Item | Reason |
|---|---|
| **Union types** | Verified working in RC1 (`union` keyword + `[Union]`/`IUnion`); blueprint in `docs/net11/UNION_TYPES_DESIGN.md`. Held back until the preview syntax stabilizes at GA; will live in a new opt-in `SharpCoreDB.Unions` namespace (never retrofitted onto existing result types). |
| **SIMD Zip / Unzip / Concat / CreateGeometricSequence** | Not applied yet — needs benchmarking against the existing `SimdHelper` AVX-512 → AVX2 → SSE/Neon kernels first. |
| **Broad `[with(capacity: …)]` rewrites** | Would require `#if` gating across shared hot-path code; exposed only through the net11-only helper for now. |

## Consuming the RC

```
git fetch origin release/v2.1.0.0-RC.3
git checkout release/v2.1.0.0-RC.3
dotnet pack src/SharpCoreDB/SharpCoreDB.csproj -c Release
```

The `.nupkg` carries **only `net11.0` assets** (plus the per-RID assemblies). A project that targets
net10.0 must stay on the `master` 2.0 packages.
