# Digest — D3 .NET / runtime accelerators · round 1 · n=1

Sources: `learn.microsoft.com/…/dotnet-10/overview` (last updated 2025-11-07),
`devblogs.microsoft.com/dotnet/performance-improvements-in-net-10/` (Stephen Toub, 2025-09-10)

## Findings

- .NET 10 runtime: "improvements in **JIT inlining**, **method devirtualization**, and **stack
  allocations**", plus **AVX10.2** support, NativeAOT enhancements, improved codegen for **struct
  arguments**, and **enhanced loop inversion**. — MS Learn · 2025-11-07 · accessed 2026-09-24 ·
  **high** · class = runtime feature.
- Concrete measured JIT win, with disassembly: **assertions from switch targets** (`dotnet/runtime#113998`)
  let the JIT prove span indices bounded inside a `switch (span.Length)`; the bounds-check helper
  `CORINFO_HELP_RNGCHKFAIL` and **six jumps to it evaporate**, code size **103 → 70 bytes**. — Toub
  blog · 2025-09-10 · accessed 2026-09-24 · **high** · class = performance (measured, primary vendor
  engineer, reproducible BenchmarkDotNet + DisassemblyDiagnoser snippet).
- The blog's subject is explicitly .NET 9 → .NET 10 (i.e. our previous → current baseline), not
  .NET 11. **Freshness flag: this is one release behind our `net11.0` target**; a .NET 11
  performance post must be chased in round 2 before any "net11 gives us X" claim. — methodological,
  logged as an **assumption**.
- Pattern transfer for a storage engine: the win is *conditional* on the code shape (switch on
  length, then index). Our row codec / fixed-width patch code that switches on a column count and
  then indexes a span is exactly this shape — **candidate, not a claim** until measured.
- Struct-argument codegen + stack allocation + devirtualization matter most for our
  `IStorageEngine`/`IStorage` interface dispatch and for per-row `struct` codecs. — MS Learn ·
  2025-11-07 · accessed 2026-09-24 · medium (feature list is high; "matters to us" is derived) ·
  class = runtime feature + inference.

## Leads worth chasing (round 2)
- "Performance Improvements in .NET 11" (Toub) — does it exist yet, and what does it add over 10?
- `DOTNET_`/runtime-config knobs that a library can *recommend* but not force: TieredPGO, QuickJIT
  for loops, `TieredCompilation` defaults — measured, not asserted.
- `System.Runtime.Intrinsics` per-ISA guidance for memory-bandwidth-bound loops (our SIMD scans).

## Looked for, did not find
- In the fetched slice: nothing about GC allocation-rate thresholds or `ArrayPool` sizing — the
  blocked middle of the 380 KB blog page. Round 2 target if the GC column comes back load-bearing.
