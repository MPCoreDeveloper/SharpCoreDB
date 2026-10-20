# Digest — D3 .NET / runtime accelerators · round 2 · n=1

Sources: `devblogs.microsoft.com/dotnet/performance-improvements-in-net-11/` (Stephen Toub,
2025-… published **2026-09-15**), `learn.microsoft.com/…/dotnet-11/overview` (last updated
**2026-09-09**, "for release candidate 1 (RC 1)")

## Findings

- **Status:** .NET 11 is in RC ("currently in release candidate. General availability is expected in
  **November 2026**"). — MS Learn · 2026-09-09 · accessed 2026-09-24 · **high** · class =
  version/compatibility. *Confirms the brief's "before November 2026" framing.*
- **Runtime Async is now the default shape for `net11.0`**: it "no longer requires
  `<EnablePreviewFeatures>` for projects that target net11.0", and "**the runtime libraries
  themselves are compiled with runtime-async=on**". — MS Learn · 2026-09-09 · accessed 2026-09-24 ·
  **high** · class = runtime feature.
- Measured Runtime Async effect (same process, same runtime, attribute-isolated): at depth 30,
  `Mean` **0.21×** of the classic lowering and `Allocated` **0.07×**; at depth 1, 0.75× / 0.72×; the
  one case where it lost was a **single `Yield`** path (1.30×). — Toub blog · 2026-09-15 · accessed
  2026-09-24 · **high** (vendor engineer, BenchmarkDotNet, reproducible) · class = performance.
  → *Transferable: async wins big on deep chains and small on shallow ones; a single-await path can
  regress. Our `InsertBatchAsync`/`ExecuteBatchSQLAsync` are shallow — do not budget a win there.*
- Runtime Async extra improvements named: "JIT compilation of a dedicated runtime-async version of
  synchronous task-returning methods", "async continuations that **opt out of ExecutionContext
  capture** when no ambient state is in use", "**tail-merged suspension points** that reduce
  generated code size", tiered compilation for runtime async, "task and value-task factory
  intrinsics", "implicit tailcall improvements". — MS Learn · 2026-09-09 · accessed 2026-09-24 ·
  high · class = runtime feature.
- **JIT (net11):** "bounds check elimination, redundant checked context removal, devirtualization,
  switch expression folding, constant-folding SequenceEqual, and redundant branch elimination". —
  MS Learn · 2026-09-09 · accessed 2026-09-24 · **high** · class = runtime feature.
- **Intrinsics (net11):** "new **Arm SVE2** intrinsics, improved hardware-intrinsic cost modeling, a
  faster `Math.BigMul` on x64 that emits a **single MUL** instruction", and "**AVX-VNNI-512**
  hardware intrinsics for vectorized multiply-add workloads". — MS Learn · 2026-09-09 · accessed
  2026-09-24 · **high** · class = runtime feature.
- **NativeAOT:** "faster interface dispatch using a shared dispatch helper, reducing binary size at
  call sites and **improving throughput for interface-heavy workloads**". — MS Learn · 2026-09-09 ·
  accessed 2026-09-24 · **high** · class = runtime feature. → *Our `IStorageEngine`/`IStorage`
  call-per-row dispatch is exactly an interface-heavy workload; an AOT-recommendation in our docs is
  a defensible, measurable claim.*
- **SIMD lane APIs:** "SIMD lane construction and composition APIs (`CreateGeometricSequence`, `Zip`,
  `Unzip`, and the `Concat` family) across `Vector128<T>`, `Vector256<T>`, `Vector512<T>`,
  `Vector64<T>`, and `Vector<T>`". — MS Learn · 2026-09-09 · accessed 2026-09-24 · **high** · class =
  runtime feature. → *These are `System.Runtime.Intrinsics` APIs, so adopting them stays inside the
  repo's `SIMD_STANDARDS.md` rule that forbids `System.Numerics.Vector<T>`.*
- **Libraries:** "Zstandard compression in `System.IO.Compression`", "improved Base64 APIs", new
  numeric APIs including IEEE 754 decimal floating-point. — MS Learn · 2026-09-09 · accessed
  2026-09-24 · high · class = library feature. → *Zstd in-box removes the "add a package" objection
  to a WAL/page compression mode.*
- **C# 15** (default on net11.0): "collection expression arguments, union types, closed hierarchies,
  extension indexers, **labeled break and continue**, memory safety". — MS Learn · 2026-09-09 ·
  accessed 2026-09-24 · high · class = language feature.
- **Deployment caveat, new in 11:** "Updated **minimum hardware requirements** for x86/x64 and Arm64
  architectures, requiring more modern instruction sets". — MS Learn · 2026-09-09 · accessed
  2026-09-24 · **high** · class = constraint. → *A real product decision: a net11 build cannot run on
  as old a CPU as net10.*

## Contradiction / caution worth reporting
The .NET 10 post (round 1) and this one overlap heavily in *category* (bounds checks, devirtualisation,
inlining) but the 11 post supplies the sharper numbers. **Neither post is a storage-engine benchmark**;
every figure is a microbenchmark on a synthetic loop. Transferring them to a row codec is a hypothesis
until measured on our own harness — flagged as an **assumption** in the memlog.

## Looked for, did not find
- Any statement that net11 changes AES-GCM / AES-NI throughput. Not found in either .NET post.
- GC substantive changes: not in the fetched slices of either page.
