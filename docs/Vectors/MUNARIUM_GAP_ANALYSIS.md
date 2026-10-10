# Munarium-Datastore — Gap Analysis (what SharpCoreDB actually borrowed vs what is there)

**Date:** 2026-09-13 · **Branch:** `release/v2.1.0.0-RC.3`
**Reference source (local):** `D:\repos\MPCoreDeveloper\munarium\server\src\munarium-datastore\src`
**Upstream:** <https://github.com/iokaio/munarium/tree/main/server/src/munarium-datastore/src>

> **Why this document exists.** The earlier `MUNARIUM_INSPIRATION_PLAN.md` claimed the
> integration was complete. Verification against the real crate showed it is a **thin slice**
> (~7,000 lines of Rust upstream; SharpCoreDB implemented a handful of types). This file records
> what exists, what does not, and which pieces are worth borrowing next.

## 0. Headline

The v2.1 RC "munarium-inspired" work is real but **narrow**: it reproduced the *names and the
idea* of three concepts (content-verified artifacts, DiskANN index type, hybrid-fusion weight),
not their mechanics. In particular:

- the canonical hash is **not** the spec the upstream uses, and it would be **rejected** by it;
- the "DiskANN" index is a **linear scan** behind a DiskANN-shaped API;
- "hybrid fusion" is a single scalar weight and is **not wired into any result path**.

## 1. Module-by-module status

| Upstream module | What it actually does | SharpCoreDB status |
|---|---|---|
| `canonical.rs` (171) | RFC 8785 (JCS) canonical JSON; **floats refused**; UTF-16 code-unit key sort; minimal escaping | ❌ `ArtifactManifest.WithComputedId()` uses `System.Text.Json` with camelCase + `WhenWritingNull` (drops nulls) and hashes a **`float HybridFusionAlpha`** — the upstream canonicalizer would throw on that float |
| `model.rs` (375) | `BuildSpec` + sealed `ArtifactManifest` (`build_spec_sha256`, `artifact_plan_sha256`, `engines[]`, `components[]`, `range_map`, `counts`, `reader` range, `probes[]`); "**no `skip_serializing_if` anywhere**" | ⚠️ Only a flat `ArtifactManifest` record: no `BuildSpec`, no components, no reader range, no probes |
| `verify.rs` (334) | `Limits` enforced **before allocation**; component-path normalization (rejects `..`, absolute, drive, UNC, backslash, control chars); `verify_manifest_bytes` hashes the **fetched** bytes; per-component length+sha256; `ReaderCapabilities` | ❌ Only an id comparison; no limits, no path safety, no component verification, no reader caps |
| `fusion.rs` (696) | Rank-based **Reciprocal Rank Fusion** (never raw scores), domain-aware pool merge with `mixed_domain` diagnostics, versioned policy, NaN-safe total ordering | ❌ `HybridFusionAlpha` is a scalar weight; not wired into GraphRAG (Phase 3) |
| `vector.rs` (283) | Exact cosine **oracle** (`FlatVectorIndex`) every approximate engine is measured against, `VectorIndex` trait, `Candidate` | ⚠️ `FlatIndex` exists (comparable intent) |
| `vector_diskann.rs` (981) | Real ANN via Microsoft's first-party `diskann` crate: `ENGINE_ID`, `ENGINE_REVISION=0.56.0`, `FEATURE_BIT="vector.diskann.v1"`, `GraphParams`, `ProviderError` | ❌ `DiskAnnIndex` is an exact linear scan (`Search` iterates all vectors; source comment: *"TODO: full greedy search"*) |
| `lexical.rs` (782) + `tokenizer.rs` (455) + `stopwords.rs` (145) | Tantivy BM25 adapter + a classifying tokenizer (URLs/emails/hosts/paths) with a PostgreSQL parity fixture | ❌ Not implemented at all |
| `records.rs` (149) | Chunk/record component engine | ❌ Not implemented |
| `hydrate.rs` (1058) | L1 hydration/eviction: single-flight, residency, leases, quarantine, `.partial` → atomic sealed leaf, `COMPLETE` marker last | ❌ Not implemented |
| `store.rs` (144) | `ArtifactStore` trait (range-capable from day one) + `LocalFileStore` | ❌ Not implemented |
| `routing.rs` (389) | Query routing | ❌ Not implemented |
| `shard.rs` (658) | Sharding | ❌ Not implemented |
| `lib.rs` (218) | `Error` with 6 operator-actionable classes (`Integrity`/`Unsupported`/`Limit`/`Path`/`Invalid`/`Canonical`); `ArtifactCacheKey` with tenant isolation + path-traversal guards | ❌ 3-variant `BuildResult` only; no cache key / tenant isolation |
| `tests/` (1,355) | `contract_vectors`, `diskann_contract`, `lexical_parity`, `round_trip`, `vector_crossover` | ⚠️ Only `DiskAnnIndexTests` |

## 2. The "hidden goodies" worth borrowing (ranked)

1. **Real canonical hashing (RFC 8785 JCS, no floats, explicit nulls).** Without it the artifact id
   is not reproducible across languages and the "content-verified" claim is hollow. Fix: a JCS writer
   (UTF-16 key sort, integer-only numbers, explicit nulls) and drop the float from the manifest
   (carry ratios as decimal strings at a declared scale, as upstream does).
2. **Rank-based RRF fusion** (`fuse` / `fuse_pools`) instead of a scalar alpha. Two legs with
   incomparable score scales (BM25 vs cosine) cannot be weighted by one number without silently
   reordering results.
3. **An actual incremental graph + beam search for `DiskAnnIndex`** (or wire the Microsoft `diskann`
   crate). A linear scan behind a DiskANN API gives no recall/latency profile to measure.
4. **Verification hardening** — `Limits` before allocation, component-path normalization,
   verify-the-fetched-bytes (not a re-serialization), per-component sha256.
5. **Probes** — a probe turns "the hashes matched" into "the index answers"; checksums alone pass a
   correctly transferred but wrongly built index.
6. **L1 hydration cache** with single-flight + quarantine + atomic sealed leaf (safe concurrent
   residency).
7. **`ArtifactStore` trait** (range-capable) so a later remote-range reader is not a breaking change.
8. **Operator-actionable error taxonomy** (Integrity vs Unsupported vs Limit vs Path vs Invalid).
9. **Lexical/Tantivy stack** (classifying tokenizer + stopwords + BM25 parity fixtures) — a whole
   capability SharpCoreDB does not have.
10. **Tenant-isolated cache keys** — identical content in two tenants must not share residency.

## 3. Correlation with the Phase table in `MUNARIUM_INSPIRATION_PLAN.md`

| Phase | Status | Gap after verification |
|---|---|---|
| 1 — options/index type/metadata | ✅ Done | surface only |
| 2 — manifest + canonical id + `IVerifiableIndex` | ✅ Done | **id is not spec-canonical** (see §1) |
| 3 — hybrid-fusion scorer | ⚠️ Partial | scalar weight, not wired |
| 4 — DiskANN index + crossover test | ✅ Done | **linear scan**, not ANN |
| 5 — SQL DDL + tests + docs | ⚠️ Partial | no recall@K benchmark |
| 6 — true C# 15 unions | ❌ Not done | record hierarchy |

## 4. Licensing note

Upstream `munarium-datastore` is **Apache-2.0**; SharpCoreDB is **MIT**. Concept-level inspiration is
fine, but any **code port** must keep the Apache-2.0 attribution/NOTICE. So far nothing is a literal
port, so this is a note for the follow-ups above — not a current problem.

## 5. Suggested next step

Treat §2 items 1 and 2 as correctness bugs (the artifact id and the fusion claim are both
overstated today), and items 3, 4 and 5 as the "real DiskANN + real verification" milestone.
