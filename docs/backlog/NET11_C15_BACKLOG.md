# V2 Backlog — .NET 11 / C# 15-gebonden werk

**Status:** Gevuld · 2026-08-31 · v2.1-lijn (`release/v2.1.0.0`)
**Doel:** Items hieronder kunnen pas worden uitgevoerd zodra de bijbehorende runtime-/compiler-
functies beschikbaar zijn (target: .NET 11 GA, november 2026). Alles wat **nu al kan** en de
performance verhoogt om het SQLite-gat te dichten, wordt buiten deze backlog uitgevoerd — zie
[`docs/performance/V2_PERFORMANCE_PLAN.md`](../performance/V2_PERFORMANCE_PLAN.md).

## Waarom deze items geblokkeerd zijn

Zie `V2_PERFORMANCE_PLAN.md` §4.0 (preview-7-metingen, 2026-08-30):

| Functie | In preview 7? | Blokkade |
|---|---|---|
| Numeriek `LangVersion 15.0` | ❌ | Preview-compiler geeft `CS1617`; `LangVersion latest` is de tijdelijke workaround |
| Runtime Async | ✅ (net11) | automatic; geen code-wijziging tot GA-baseline |
| AVX-VNNI-512 / Arm SVE2 | ⚠️ | SVE2 is `SYSLIB5003` evaluation-only; SVE2 uitstellen tot GA |
| SIMD lane APIs | ✅ (preview 7) | vereist een columnar-layout refactor, geen point-edit |
| Zstandard (`ZstdCompressor`) | ❌ | niet in preview 7; uitgesteld tot later preview/GA |
| IEEE 754 `Decimal32/64/128` | ❌ | niet in preview 7; uitgesteld tot GA |
| C# 15 union types / closed hierarchies | ⚠️ | nog niet gestabiliseerd; valideren vóór AST-refactor |

## Backlog

| # | Item | Afhankelijkheid | Aanraakgebied (indicatie) |
|---|------|-----------------|---------------------------|
| B1 | `LangVersion latest` → `15.0` | .NET 11 GA | `Directory.Build.props`, `global.json` |
| B2 | Runtime-native async in async hot paths | Runtime Async (net11 GA) | `Execute*Async`, `InsertBatchAsync`, `ExecuteBatchSQLAsync`, server-paden |
| B3 | AVX-VNNI-512 (x64) + Arm SVE2 intrinsics achter `SIMD_ENABLED`-guards | AVX-VNNI-512 net11; SVE2 eval-only tot GA | `DistanceMetrics`, `SimdHelper`, vector search (HNSW) |
| B4 | SIMD lane APIs (`Zip`/`Unzip`/`CreateGeometricSequence`/`Concat`) in columnar codecs | APIs ✓ in preview 7, maar vereist columnar-layout refactor | Delta, Gorilla, XorFloat, RLE, bit-packing, SIMD row scanning |
| B5 | Zstandard WAL/page-compressie (opt-in, default uit) | `ZstdCompressor` in `System.IO.Compression` | WAL + page-compressie (net als bestaande Brotli/GZip block-compressie) |
| B6 | IEEE 754 `Decimal32/64/128` + `INumberBase<TSelf>.TryParsePartial` | runtime (niet in preview 7) | decimal-column parsing/serialisatie |
| B7 | C# 15 union types / closed hierarchies voor de SQL-AST | compiler-stabilisatie (Phase 4) | `SqlParser`, planner, AST/SQL-node design |
| B8 | Automatic JIT / NativeAOT-dispatch wins meten | net11 GA (geen code-wijziging) | re-benchmark + `V2_PERFORMANCE_PLAN.md` §3.2 bijwerken |

## Niet performance-gerelateerd (apart bijhouden, niet deze backlog)

- Native AOT-waarschuwings-cleanup: B-tree factory ipv. reflectie, `ParseVectorValue`/`.scdb` JSON
  naar source-generated context.
- `SingleFileDatabase` → `IMetadataProvider` pariteit (metadata-detectie via `db is IMetadataProvider`).
