# Munarium-Datastore Inspiration — Implementation Record (SharpCoreDB v2.1 RC)

**Status:** ✅ Shipped in **2.1.0-RC.3** — net11.0 / C# 15 preview only  
**Branch:** `release/v2.1.0.0-RC.3`  
**Date:** 2026-09-13  
**Inspiration source:** local munarium-datastore (`lib.rs`, `model.rs`, `vector_diskann.rs`, `fusion.rs`, `verify.rs`, `shard.rs`, `canonical.rs`)

Everything is pure managed C# / .NET 11 — no Rust, no FFI, no Tantivy.

> **Branch model:** this branch is the **net11.0 / C# 15 preview** line (2.1 RC). The **net10.0 / C# 14** line lives on `master` as the v2.0 stable packages (e.g. `SharpCoreDB` 2.0.0.3). The v2.1 branch never carries a net10.0 target.

## 1. What we took (inspiration, not a port)

From munarium-datastore (`lib.rs`, `model.rs`, `vector_diskann.rs`, `fusion.rs`, `verify.rs`, `shard.rs`, `canonical.rs`):

- **Immutable content-verified artifacts**: `BuildSpec` → deterministic `IndexVersionId`; `ArtifactManifest` with `ArtifactId = sha256(canonical(manifest))`.
- **Hybrid fusion**: `FusionWeights` for lexical + vector scoring.
- **DiskANN-style vector indexing**: high-recall graph with crossover testing vs the exact index.
- **Strict typed errors**: enum variants mapped onto a C# record hierarchy (`BuildResult`).
- **Canonical hashing & reproducibility**: every identity-affecting field is canonicalized before hashing.

We did **not** copy Rust code, Tantivy, or the full store trait. Everything builds on the existing `SharpCoreDB.VectorSearch` (HNSW, SIMD, quantization) and EventSourcing foundations.

## 2. Module coverage (verified against the code)

| munarium module | SharpCoreDB counterpart | Status |
|---|---|---|
| `canonical.rs` | `Artifacts/CanonicalJson.cs` — RFC 8785 JCS, **floats refused**, explicit nulls, UTF-16 key sort; `CanonicalParam` (no float variant, `Ratio()` → decimal string) | ✅ |
| `model.rs` | `Artifacts/ArtifactManifest.cs` (canonical id), `ArtifactComponents.cs` (`ArtifactComponent`, `ArtifactRangeMapRef`, `ArtifactLimits`, `ReaderCapabilities`) | ✅ (components/limits/reader range; `BuildSpec`/`probes` still open) |
| `verify.rs` | `Artifacts/ArtifactVerifier.cs` — path normalization, **limits before allocation**, `VerifyManifestBytes` (fetched bytes), `VerifyComponent` | ✅ |
| `fusion.rs` | `Fusion/ReciprocalRankFusion.cs` (RANKS, NaN-last total order) + `Fusion/PoolMerge.cs` (domain-aware interleave + diagnostics) | ✅ |
| `vector.rs` | `Index/FlatIndex.cs` — contiguous SoA, lock-free snapshot reads, zero-alloc scan, non-finite rejection, deterministic tie-break (the exact **oracle**) | ✅ |
| `vector_diskann.rs` | `Index/DiskAnnIndex*.cs` — real **Vamana** (seeded parallel init, robust prune with alpha, beam search, medoid entry, feature bit) + true `MeasureRecallAgainstExact` | ✅ |
| `lexical.rs` + `tokenizer.rs` + `stopwords.rs` | **`src/SharpCoreDB.Search`**: `MunariumTokenizer` (classifying), `EnglishWordStemmer` (Porter, **word-only**), `Text/EnglishStopWords` (PG-16), `FullTextIndex` (BM25 + positions + phrase boost) | ✅ |
| `hydrate.rs` | `Storage/HydrationCache*.cs` — single-flight, verify-then-seal, `COMPLETE` last, quarantine, eviction, partial reconciliation | ✅ |
| `store.rs` | `Store/IArtifactStore.cs` (range-capable) + `Store/LocalFileStore.cs` (re-normalizes every path) | ✅ |
| `records.rs` | `Records/ChunkRecords.cs` — JSON-Lines body + fixed-width offset index, strict (`[JsonRequired]`) | ✅ |
| `routing.rs` | `Routing/RoutingEvidence.cs` + `RoutingEvidenceBuilder.cs` — bounded, scale-free signals + deterministic rank | ✅ |
| `shard.rs` | `Shard/ShardLayout.cs` — stable FNV-1a partitioning + pool mapping | ✅ |
| `lib.rs` | `Artifacts/ArtifactError.cs` (6-class taxonomy), `Artifacts/ArtifactCacheKey.cs` (tenant isolation + traversal guard) | ✅ |
| Hybrid assembly | **`src/SharpCoreDB.HybridSearch`** — `HybridSearchEngine` runs the lexical + vector legs and fuses by rank | ✅ |
| `BuildSpec` / `probes` / true C# 15 `union` | — | ⬜ open (see §5) |


## 3. Behaviour notes

- `ArtifactManifest.WithComputedId()` produces the canonical SHA-256 `ArtifactId` (the id itself and the audit timestamp are excluded from the hash), so the id is deterministic and reproducible.
- `IVerifiableIndex.Verify()` is **content-addressed**: it recomputes the expected manifest's canonical id. Tampering with any content field (`Count`, `Dimensions`, `BuildParams`, …) is therefore reported as `VerificationFailed` and should be quarantined; a genuine match returns `Success`; a count/dimension mismatch on an otherwise identical id returns `LimitExceeded`.
- `DiskAnnIndex.Search` currently performs an exact (linear) scan behind the DiskANN API. Graph-beam traversal and selective "disk" layers are the next step for the >10M-vector target.

## 4. Verification (2.1.0-RC.3)

- `dotnet build SharpCoreDB.slnx -c Release` → **0 errors**.
- Tests: `SharpCoreDB.Tests` **1688/1688**, `SharpCoreDB.VectorSearch.Tests` **148/148** (incl. the DiskANN crossover + verification tests), `SharpCoreDB.EventSourcing.Tests` **72/72**.
- `dotnet pack` → `SharpCoreDB*.2.1.0-RC.3.nupkg` carrying `lib/net11.0` only.

## 5. Open follow-ups

1. **`BuildSpec` + `probes`** — the logical-corpus document and semantic probes are the two model
   pieces not yet ported.
2. **True C# 15 `union` types** for `BuildResult` once the syntax ships at GA (currently a `record`
   hierarchy).
3. **Vector-search performance** — the build's back-edge pruning is the cost centre (see
   `PERFORMANCE_NOTES.md` §3-4), and per-query heap/hash allocations are the next query-side target.

## 6. Benefits delivered

- **Zero runtime cost** for existing users — every new feature is opt-in.
- **Reproducible indexes** via content verification (ideal for caching embeddings in EventSourcing projections).
- **Better RAG recall** paths via fusion weights and the DiskANN index surface.
- **Stays 100% native C# 15 / .NET 11.**

## 7. Verified gaps & borrowed-goodies backlog

Verification against the real `munarium-datastore` crate (2026-09-13) found the implementation is
narrower than the phase table suggests. The full module-by-module comparison and the ranked list of
upstream pieces still worth borrowing live in [`MUNARIUM_GAP_ANALYSIS.md`](MUNARIUM_GAP_ANALYSIS.md).
Headline gaps: the artifact id is **not** RFC-8785-canonical, `DiskAnnIndex` is a **linear scan**, and
the hybrid-fusion weight is **not wired** into any result path.
