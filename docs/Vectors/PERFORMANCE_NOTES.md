# Vector Search — Performance Notes (v2.1 RC)

**Branch:** `release/v2.1.0.0-RC.3` · **Measured:** 2026-09-13, single dev machine, Release build
**Method:** one process, warm-up query excluded, `Stopwatch` around the query loop, recall via
`MeasureRecallAgainstExact` against the exact `FlatIndex` oracle.

## 1. Measured results

### 1.1 Headline operating points

| Corpus | FlatIndex (exact, SoA) | DiskANN (Vamana) | Speedup | Recall@10 | DiskANN build |
|---|---|---|---|---|---|
| n=5,000 · dims=64 · R=32 L_build=64 | 10,353 qps | 7,867 qps | **0.76×** | 99.9% | 3.7 s |
| n=10,000 · dims=256 · R=32 L_build=48 L_search=128 | 1,613 qps | 2,223 qps | **1.38×** | **91.9%** | **6.7 s** |
| n=100,000 · dims=128 · R=32 L_build=64 L_search=128 · 3 passes | 248 qps | 1,746 qps | **7.0×** | 80.6% | 129.3 s |
| n=100,000 · dims=128 · R=32 L_build=64 L_search=512 · 3 passes | 248 qps | 459 qps | **1.85×** | **94.9%** | 129.3 s |

- **FlatIndex allocates ~400 B/query** (the result arrays + the sort only). The scan itself is
  allocation-free over one contiguous buffer.
- **DiskANN allocates 184 B/query** — the returned `VectorSearchResult[]` and nothing else. The
  traversal (`HashSet` + two `PriorityQueue`s + the hit list) now reuses per-thread scratch and is
  allocation-free. It was **2,376 B/query before: a 12.9× reduction**.
- **The graph's advantage is marginal below ~20K vectors** (0.76×–1.4×) and decisive at 100K
  (**7×–23×**). The vectors here are uniform random, which is close to the worst case for *any*
  graph index (no cluster structure to navigate on), so real embeddings should sit above these
  curves.
- These build figures were taken **before** the §5 search-phase work, which made the build ~6–8%
  faster at dims=64 — so the build column is now slightly conservative.

### 1.2 The recall/latency curve at n=100,000 (dims=128, R=32, L_build=64)

Built once, then only the query beam varies — so this isolates the graph from the build. Recall is
measured over 100 vectors against the exact `FlatIndex`, `k=10`.

| L_search | DiskANN qps | Speedup vs exact | Recall@10 · 2 passes | Recall@10 · 3 passes |
|---|---|---|---|---|
| 32 | 5,579 | **22.5×** | 59.3% | **73.0%** |
| 64 | 3,389 | 13.7× | 64.8% | 76.3% |
| 128 | 1,746 | 7.1× | 71.5% | 80.6% |
| 256 | 906 | 3.7× | 80.3% | 87.2% |
| 512 | 459 | 1.9× | 90.1% | **94.9%** |

Two conclusions:

1. **Recall is graph-quality-bound, not beam-bound.** Widening the beam 32 → 512 costs **12× the
   latency** to gain ~17 points. Adding one build pass costs +50% build time and gains **+13.7
   points at the fastest beam** (59.3% → 73.0%) while *keeping* the 22.5× speedup. `BuildPasses` is
   the cheap lever; `L_search` is the per-query knob.
2. **90%+ recall is reachable at 100K** — it just costs the speedup (1.9× at 94.9%), and even that
   point still beats the exact scan.

## 2. What was optimized

| Change | Effect |
|---|---|
| Contiguous struct-of-arrays vector storage (was `ConcurrentDictionary<long, float[]>` + `query.ToArray()` per query) | Cache-friendly, allocation-free scan; deterministic (distance, id) order; NaN/Inf rejected at `Add` |
| Vamana graph replaces the previous "DiskANN" (a linear scan behind a DiskANN-shaped API) | Real ANN: recall is now measurable and tunable |
| Adjacency stores **slots, not ids** | Removed the id→slot dictionary lookup from the traversal inner loop: **1.12× → 1.42×** at the balanced configuration (+25% query throughput, recall unchanged) |
| **Batched back-edges, one prune per target** (see §3) | **Build 13.8 s → 6.7 s (2.06×)** and recall 90.7% → 91.9% |
| Parallel, per-node-seeded random initialization | Deterministic and parallel-safe |
| **Per-thread scratch traversal state** (see §1.1) | Query allocation **2,376 B → 184 B (12.9×)**, and no GC pressure in the query loop |
| **Per-query search-list override**: `Search(query, k, searchListSize)` | The beam becomes a runtime knob instead of a build-time constant, so recall/latency trades can be measured and tuned per query without rebuilding the graph |
| **`BuildPasses` config** (was hardcoded to 2) | The build-time/recall lever: +1 pass bought **+13.7 points of recall at the fastest beam** (see §1.2) |
| **Candidate dedup in graph construction** (see §4) | Prune runtime **−14.4%**, whole build **−2.7%**, recall unchanged |
| **Stamped `int[]` visited marking** (see §5) | Beam-search visited set: build **−6 to −8%** at n=10K/40K, recall unchanged |
| **Hoisted query norm for cosine** (see §5) | **−17%** per cosine call in isolation and bit-identical results, but neutral end-to-end: the search phase is memory-bound |
| **Phase instrumentation** (see §4) | Located the build's bottleneck after the restructuring: the sequential greedy search at 61–65%, not the prune |

## 3. The build's cost centre — found, restructured, and resolved

The per-node Vamana searches were first parallelized over a frozen per-pass snapshot (the standard
parallel-graph-construction structure). Measured, that was a dead end:

| Build variant | Build time | Recall@10 |
|---|---|---|
| Sequential, evolving graph, prune-after-every-insert | 13.8 s | 90.7% |
| Parallel searches over a frozen graph | 12.8 s | 86.4% |
| Parallel searches, frozen graph, 3 passes | 17.6 s | 88.9% |

**~8% build-time gain for ~4 points of recall loss.** The searches were never the bottleneck: the
build was dominated by the **back-edge pruning**. `AddEdgeWithPrune` ran a full `RobustPrune` after
*every* overflowing insertion, which makes one target's list cost **O(R³)** in distance
computations across a pass.

**The fix (implemented):** batch the back-edges per target and prune **once** per target, then apply
the batches in parallel — each target's list is touched by exactly one task, and the source order was
fixed by the sequential search phase, so the build stays deterministic:

| Build variant | Build time | Recall@10 |
|---|---|---|
| Prune-after-every-insert (before) | 13.8 s | 90.7% |
| **Batched back-edges, one prune per target (now)** | **6.7 s** | **91.9%** |

**2.06× faster build AND +1.2 points of recall** — the recall improves because batching lets more
back-edges survive, which is exactly the connectivity the earlier fix showed matters. The search
phase deliberately stays sequential over the evolving graph.

## 4. Pairwise-distance caching inside a prune: measured, then rejected

The open item was "cache pairwise distances inside a prune, because the O(R²) dominance loop
recomputes candidate-to-candidate distances". The premise turned out to be wrong in the interesting
way, so this was measured before being built:

| Experiment | Result |
|---|---|
| A memo scoped to **one prune** (`HashSet`/dictionary per call) | **9.9%–11.4%** of the distance calls are repeat *requests* — but see below |
| A memo scoped to the **whole build** | **83.8% reuse** (n=2,000) … and **54× SLOWER**: 44.8 s vs 0.825 s, plus 195 MB of memo at n=2,000 (GBs at n=100k) |

**Why the global cache loses so badly:** the reuse is real but has no *temporal* locality — each pair
is touched twice, far apart in the build — so the memo has to grow to nearly every pair the build
ever computes. And the thing it replaces is cheap: a 64-dim SIMD distance takes ~15–30 ns, while a
concurrent-dictionary lookup+insert takes ~30–80 ns. **Caching is slower than recomputing.** At
n=100k the memo would also need hundreds of millions of entries.

**Why there are intra-prune hits at all:** the candidate list is the greedy search's hits *plus* the
node's current edges, and those two sets overlap. Re-adding an edge the search already returned puts
a **duplicate slot** into `ordered` (measured: ~13 duplicate entries per prune, ~13% of the working
set), and the dominance loop then requests the same pair twice.

**The fix that the data did support** — skip candidates the search already returned, using the hit
set the search now publishes (O(1) membership):

| n=10,000 · dims=64 · R=32 · L_build=64 · 2 passes | prune | build | recall@10 |
|---|---|---|---|
| before | 0.865 s | 2,975 ms | 98.8% |
| **after** | **0.740 s** (−14.4%) | **2,894 ms** (−2.7%) | **98.8%** |

(median of repeated runs; the build's first run in a process is ~18% slower from cold-start JIT and
was excluded.)

**The build's bottleneck has moved.** Phase instrumentation after the §3 restructuring:

| Phase | n=2,000 | n=10,000 |
|---|---|---|
| Greedy search (sequential) | **61%** | **65%** |
| Robust prune (phase A) | 33% | 29% |
| Back-edge apply (parallel) | 2% | 3% |
| Carrying current edges into the candidate set | 1% | ≤1% |

So the prune is no longer where the build's time goes — the *searches* are, and they must stay
sequential to see an evolving graph (§3). That reorders the remaining work:

## 5. The search phase: two changes, and what they revealed

The search phase (61–65% of build time, §4) does ~2,000–3,000 distance computations per node per
pass. Two changes were made and measured at n=10,000 / n=40,000, dims=64, R=32, L_build=64, 2 passes:

**1. Visited marking: `HashSet<int>` → stamped `int[]`.** A beam search touches its visited set once
per neighbour examined, and an array read is several times cheaper than hashing plus a bucket probe.
The stamp is what makes a reused array safe: a slot counts as visited only if it carries *this*
search's id, so stale marks can never match. Semantics are identical by construction, and recall
confirms it.

| | n=10,000 | n=40,000 | recall@10 |
|---|---|---|---|
| before | 2,976 ms | 17,727 ms | 98.8% / 94.5% |
| **after** | **2,720–2,751 ms** | **16,412–17,035 ms** | **98.8% / 94.5%** |

**~6–8% off the build, zero recall cost.** (Spread is ±5–8% run to run on this machine, so these are
ranges over repeated probe runs, cold first run excluded.)

**2. Cosine: hoist the query's squared norm out of the traversal.** Cosine computes `dot`, `‖a‖²`
and `‖b‖²`, but a search evaluates thousands of candidates against ONE query, so `‖a‖²` is constant
across the loop — a third of the reduction work, recomputed per candidate. Its own microbenchmark:

| dims | `CosineDistance` (before) | with hoisted query norm | dot only (floor) |
|---|---|---|---|
| 64 | 20.4 ns | **17.0 ns** | 11.6 ns |
| 128 | 22.1 ns | 17.4 ns | 15.0 ns |
| 256 | 37.6 ns | 30.7 ns | 30.3 ns |

`DistanceMetrics.CosineDistanceWithQuerySquaredNorm` + `SquaredNorm` implement this. The accumulation
order is unchanged, so the distance is **bit-identical** — guarded by
`tests/SharpCoreDB.VectorSearch.Tests/CosineNormHoistingTests.cs` across every SIMD tier and the
scalar tail (21 cases).

**In the build it is neutral (within noise), and that is the finding:** at n=40,000 the vector buffer
is ~10 MB and every distance call streams from outside L2, so a 17% arithmetic saving buys nothing.
**The search phase is memory-bound, not compute-bound.**

### What that leaves

1. **`L_build`** — the distance count is linear in it, so this is the direct dial. It is a recall
   trade, already quantified in §1.2.
2. **Block-parallel search phase** — the only lever that scales with cores for memory-bound work.
   The searches must see an evolving graph for recall (§3 measured −4 points for a fully frozen
   pass), but a *block-sequential, in-block-parallel* phase keeps the graph within a block and
   updates it between blocks, so the recall cost should be a fraction of that. It needs a
   deterministic read snapshot per block (double-buffered adjacency) and a recall measurement — a
   deliberate trade, not a free speedup.
3. **Push to 1M vectors** and find where the graph's memory (~`R` ints/vector) and the 184 B/query
   result array stop being comfortable.
4. Only then consider blocked/on-disk adjacency (the munarium datastore's "selective disk access"
   positioning) for corpora that do not fit in memory.

**Done:** per-query traversal allocations (§1.1), the 100K re-measurement (§1.2), the build
restructuring (§3), candidate dedup (§4) and the two search-phase changes above.

## 6. Reproducing

The numbers came from a temporary probe test that built a `FlatIndex` and a `DiskAnnIndex` over the
same vectors, then timed the query loops with `Stopwatch` and read
`GC.GetAllocatedBytesForCurrentThread()` around the loop for the allocation figures. The probe has
been removed; recreate it the same way if you need to re-measure. Recall may be taken either from
`DiskAnnIndex.MeasureRecallAgainstExact(flat, k, queries)` or, when varying the beam, by comparing
`Search(query, k, searchListSize)` against `FlatIndex.Search` directly — that is what
`tests/SharpCoreDB.VectorSearch.Tests/DiskAnnTuningTests.cs` does for the regression guards.
