# SharpCoreDB v2.1 Preview — .NET 11 / C# 15

> Branch: `release/v2.1.0.0-preview.1` · Package version: `2.1.0-preview.1`

## What this is

The v2.1 preview is built from current `master` and multi-targets:

```xml
<TargetFrameworks>net10.0;net11.0</TargetFrameworks>
```

- **net10.0** consumers get exactly the same API and behavior as v2.0 (C# 14, byte-identical, 1679 tests green).
- **net11.0** consumers get the net11-native features below. C# 15 preview language is enabled **only** on this target
  (net10.0 stays on C# 14), so nothing about the existing API surface changes.

## What .NET 11 consumers get automatically

### 1. Native Zstandard compression
`BlockCompressionMode.Zstd` is now backed by `System.IO.Compression.ZstandardStream` (native zstd):

- `OptionalCompressionLevel` maps to zstd quality: `Fastest → 1`, `Optimal → 5`, `SmallestSize → 19`.
- A frame checksum is appended (`ZstandardCompressionOptions.AppendChecksum = true`), so corruption is
  detected on read.
- On **net10.0**, `Zstd` throws `PlatformNotSupportedException` (documented in `BlockCompressionMode`).

### 2. Runtime Async
The net11.0 target compiles with `Features=runtime-async=on`: async/await emits **runtime-native state machines**
instead of per-method generated ones — fewer heap allocations and cleaner stack traces. No API or behavior change.
The net10.0 target is untouched.

### 3. C# 15 preview API surface (`SharpCoreDB.Net11`, opt-in)
Import `using SharpCoreDB.Net11;` to unlock additive members:

```csharp
using SharpCoreDB.Net11;

TableInfo? docs = db["docs"];                 // extension indexer → TableInfo? (case-insensitive)
List<string> names = Net11Additions.NewList<string>(128); // collection-expression capacity argument
```

- The extension indexer cannot conflict with existing members: `Database` has no indexer of its own.
- These members are source-gated (`#if NET11_0_OR_GREATER`) and compiled only for net11.0.

## What is deliberately NOT in this preview

| Item | Reason |
|---|---|
| **Union types** | Available in the C# 15 preview compiler, but the syntax is still stabilizing; will be designed as a new opt-in namespace (never retrofitted onto existing result types). |
| **SIMD Zip / Unzip / Concat / CreateGeometricSequence** | Not applied yet — needs benchmarking against the existing `SimdHelper` AVX-512 → AVX2 → SSE/Neon kernels first. |
| **Broad `[with(capacity: …)]` rewrites** | Would require `#if` gating across shared hot-path code; only exposed via the net11-only helper for now. |

None of the above touches the net10.0 target.

## Backward compatibility guarantees

- No existing member changed; only additive members were introduced.
- net10.0: 1679 tests green (unchanged).
- net11.0: 1688 tests green, including Zstd round-trip, storage compression/reopen, and the new net11 tests.

## Consuming the preview

```
git fetch origin release/v2.1.0.0-preview.1
git checkout release/v2.1.0.0-preview.1
dotnet pack src/SharpCoreDB/SharpCoreDB.csproj -c Release
```

The `.nupkg` carries both `net10.0` and `net11.0` assets; NuGet picks the appropriate one per consuming project.
