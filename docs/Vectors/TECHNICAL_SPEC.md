# Technical Specification: Vector Search & Storage

> **Document Type:** Architecture Decision Record + Technical Specification  
> **Author:** SharpCoreDB Architecture Team  
> **Status:** Approved for Implementation  
> **Date:** 2026-02  
> **Applies to:** SharpCoreDB 1.2.0+

---

## Table of Contents

1. [Executive Summary](#1-executive-summary)
2. [Lessons Learned from Existing Solutions](#2-lessons-learned-from-existing-solutions)
3. [Architecture Options Analysis](#3-architecture-options-analysis)
4. [Chosen Architecture: Hybrid Native + DI Extension](#4-chosen-architecture-hybrid-native--di-extension)
5. [SIMD Strategy](#5-simd-strategy)
6. [Memory Management & Scaling](#6-memory-management--scaling)
7. [Storage Format](#7-storage-format)
8. [Index Architecture (HNSW)](#8-index-architecture-hnsw)
9. [SQL Integration Points](#9-sql-integration-points)
10. [Thread Safety & Concurrency](#10-thread-safety--concurrency)
11. [NativeAOT & Trimming Compatibility](#11-nativeaot--trimming-compatibility)
12. [Error Handling Strategy](#12-error-handling-strategy)

---

## 1. Executive Summary

SharpCoreDB Vector Search adds optional vector embedding storage and similarity search to the existing database engine. The design goal is **zero impact** on existing users while providing a path from embedded (10K vectors on mobile) to enterprise scale (10M vectors on server).

**Key design constraint:** SharpCoreDB wants to be both a **file-based embedded database** AND scale to a **full-featured RDBMS**. This means:
- Memory usage MUST be configurable and bounded
- Indexing MUST be optional (exact search for small datasets, HNSW for large)
- All features MUST work with both `StorageMode.Directory` and `StorageMode.SingleFile`
- Vector module MUST NOT introduce mandatory dependencies

---

## 2. Lessons Learned from Existing Solutions

### 2.1 sqlite-vec (C, SQLite Extension)

**What they did right:**
- Virtual table approach keeps core SQLite untouched
- Function-based API (`vec_distance_cosine()`) is SQL-standard friendly
- Binary quantization support from day one
- Builds on SQLite's existing BLOB storage

**Mistakes we learn from:**
- ❌ **MATCH operator syntax** is non-standard and confusing (`WHERE embedding MATCH ?`)  
  → **We use:** Standard `ORDER BY vec_distance_cosine(col, @q) LIMIT k` 
- ❌ **Virtual table overhead** — every row access goes through vtab interface, even for non-vector columns  
  → **We use:** Native column type with BLOB storage, zero overhead for non-vector columns
- ❌ **No concurrent writes** — SQLite's write lock means HNSW build blocks everything  
  → **We use:** Read-write lock on HNSW (concurrent reads, exclusive write)
- ❌ **C-only implementation** means platform-specific builds for each target  
  → **We use:** Pure managed C# — single binary for all platforms

### 2.2 pgvector (C, PostgreSQL Extension)

**What they did right:**
- `vector(N)` column type is clean and intuitive
- Supports both IVF and HNSW index types
- Integrated with PostgreSQL's query planner

**Mistakes we learn from:**
- ❌ **Memory explosion with HNSW** — no configurable memory limits, graph lives entirely in shared_buffers  
  → **We add:** `MaxMemoryMB` hard limit, lazy loading, eviction under memory pressure
- ❌ **Custom operators** (`<=>`, `<->`, `<#>`) are PostgreSQL-specific and not portable  
  → **We use:** Standard function syntax `vec_distance_cosine(a, b)` — works in any SQL tool
- ❌ **No quantization in early versions** — had to retrofit scalar/binary quantization  
  → **We design:** Quantization support in the storage format from day one (reserved header bits)
- ❌ **Index build blocks writes** — HNSW index creation on 1M rows blocks all inserts  
  → **We plan:** Background index building with WAL integration (Phase 4)

### 2.3 Chroma (Python)

**What they did right:**
- Simple API for non-SQL users
- Multiple distance metrics from day one
- Metadata filtering alongside vector search

**Mistakes we learn from:**
- ❌ **Started HNSW-only** — had to add flat search later when users complained about small-dataset overhead  
  → **We start:** With flat (exact) search as default, HNSW as optional upgrade
- ❌ **No persistence story initially** — in-memory only, lost data on restart  
  → **We have:** Full persistence from day one (both Directory and SingleFile storage modes)
- ❌ **Python GIL kills concurrent performance**  
  → **We use:** C# with `Lock` class, no GIL, true parallel reads

### 2.4 Qdrant (Rust)

**What they did right:**
- Excellent memory management with memory-mapped segments
- Configurable quantization (scalar, product, binary)
- Pre-filtering + post-filtering hybrid search

**Mistakes we learn from:**
- ❌ **Server-only architecture** — can't be embedded  
  → **We are:** Embedded-first, server-optional
- ❌ **Complex configuration** — 50+ config parameters overwhelm users  
  → **We provide:** 3 presets (Embedded, Standard, Enterprise) + fine-grained override

### 2.5 EF Core 10 + SQL Server 2025 Vector Support

**What they did right (and we follow):**
- `SqlVector<float>` type with dimension metadata
- `VectorDistance()` function — clean, SQL-standard
- Integration with existing query planner

**Key insight:** Microsoft chose **function-based syntax** over custom operators, validating our approach.

### 2.6 Cosmos DB Vector Search

**What they did right:**
- `VectorDistance()` in standard SQL queries
- Support for IVF, HNSW, and DiskANN in one product
- `ORDER BY VectorDistance(...) LIMIT k` pattern

**Key insight:** The `ORDER BY function() LIMIT k` pattern is becoming the industry standard for vector search in SQL databases.

---

## 3. Architecture Options Analysis

### Option 1: Direct Core Integration

Add `DataType.Vector` to the enum, modify parser, serializer, and function evaluator.

| Aspect | Assessment |
|--------|-----------|
| Backwards compat | ✅ Enum is additive |
| Optionality | ⚠️ Code always present, never reached without VECTOR column |
| Performance | ✅ Zero overhead (branch prediction on DataType switch) |
| Maintenance | ❌ Changes spread across 10+ files, hard to ship as separate NuGet |
| AOT/Trimming | ✅ No reflection |
| Scalability | ⚠️ All memory management in core |

**Verdict:** Good performance but poor modularity. Cannot be a separate NuGet package.

### Option 2: DI-based Extension (ICustomFunctionProvider + ICustomTypeProvider)

Register vector handling via dependency injection. Core has interfaces, vector module implements them.

| Aspect | Assessment |
|--------|-----------|
| Backwards compat | ✅ Nothing registered = exact same behavior |
| Optionality | ✅ Separate NuGet, opt-in via `AddVectorSupport()` |
| Performance | ⚠️ Interface dispatch per function call (~2ns overhead) |
| Maintenance | ✅ Vector code fully isolated, core stays clean |
| AOT/Trimming | ✅ DI is AOT-safe, no reflection needed |
| Scalability | ✅ Module manages its own memory |

**Verdict:** Best modularity but slight function call overhead.

### Option 3: AssemblyLoadContext Plugin

Load vector module dynamically at runtime.

| Aspect | Assessment |
|--------|-----------|
| Backwards compat | ✅ DLL not present = no behavior change |
| Optionality | ✅ Physical file presence controls feature |
| Performance | ❌ Reflection-based dispatch, no inlining |
| Maintenance | ❌ Cross-assembly debugging, version compat issues |
| AOT/Trimming | ❌ **BLOCKER** — `AssemblyLoadContext` not available in NativeAOT |
| Scalability | N/A — eliminated |

**Verdict: ELIMINATED** — Incompatible with `PublishTieredAot=true` in SharpCoreDB.csproj.

### Option 4: Hybrid Wrapper (Separate Vector Table)

`VectorTable` wraps a regular `Table` with sidecar HNSW index.

| Aspect | Assessment |
|--------|-----------|
| Backwards compat | ✅ Separate table type |
| Optionality | ✅ Feature flag |
| Performance | ⚠️ Double indirection for row access |
| Maintenance | ⚠️ Hooks in Table for index updates, tight coupling |
| AOT/Trimming | ✅ No reflection |
| Scalability | ⚠️ Two objects to synchronize |

**Verdict:** Over-engineered for the benefit. Hooks in Table create coupling.

### Option 5: External Libraries

| Sub-option | Assessment |
|------------|-----------|
| 5A: FAISS via P/Invoke | ❌ No ARM64 builds, cross-platform nightmare, NativeAOT P/Invoke complexity |
| 5B: ML.NET | ❌ ~50MB dependency, massive overkill |
| 5C: Pure managed `System.Numerics.Vector<float>` | ✅ Zero dependencies, auto-SIMD, cross-platform |

**Verdict:** 5A and 5B eliminated. 5C is the SIMD strategy for any architecture choice.

---

## 4. Chosen Architecture: Hybrid Native + DI Extension

### Decision

**Combine Option 2 (DI extension) + DataType.Vector in core (minimal) + Option 5C (pure SIMD)**

### Rationale

The key insight is that we need a **two-layer architecture**:

1. **Core layer (SharpCoreDB):** Minimal extension points — `DataType.Vector` enum value, `ICustomFunctionProvider` interface, and fallback in `ParseDataType()` / `EvaluateFunction()`. This is ~50 LOC of changes.

2. **Extension layer (SharpCoreDB.VectorSearch):** All vector logic — serialization, distance metrics, HNSW index, memory management, SQL function implementations. This is a separate NuGet package.

### Why `DataType.Vector` in Core?

Adding `Vector` to the `DataType` enum (one line) gives us:
- Proper type metadata in table schema
- Correct serialization routing without interface dispatch on hot path
- Schema validation (reject non-vector data in VECTOR columns)
- Compatibility with existing tooling (Dapper, EF Core mappers)

The alternative (storing as BLOB with metadata) means every BLOB access must check "is this secretly a vector?" — adding overhead to ALL BLOB queries.

### Why DI Extension for Functions?

Vector functions (`vec_distance_cosine`, etc.) are called in query evaluation — a moderately hot path but not the innermost loop. Interface dispatch (~2ns) is negligible compared to the actual distance computation (~50μs for 1536-dim cosine).

### Architecture Diagram

```
┌─────────────────────────────────────────────────────────────┐
│                    User Application                          │
├──────────────┬──────────────────────────────────────────────┤
│   SQL API    │  services.AddSharpCoreDB()                    │
│              │          .AddVectorSupport()     ← OPTIONAL   │
├──────────────┴──────────────────────────────────────────────┤
│                                                              │
│  ┌─────────────────────────────────┐                         │
│  │     SharpCoreDB (Core)          │                         │
│  │                                 │                         │
│  │  DataType.Vector (enum)         │ ← 1 line change        │
│  │  ICustomFunctionProvider        │ ← new interface         │
│  │  ICustomTypeProvider            │ ← new interface         │
│  │  SqlFunctions fallback          │ ← ~10 LOC change       │
│  │  ParseDataType fallback         │ ← ~5 LOC change        │
│  │  ColumnDefinition.Dimensions    │ ← 1 property           │
│  │                                 │                         │
│  │  Everything else: UNCHANGED     │                         │
│  └─────────────┬───────────────────┘                         │
│                │ DI registration                              │
│  ┌─────────────▼───────────────────┐                         │
│  │  SharpCoreDB.VectorSearch       │ ← SEPARATE NuGet       │
│  │  (optional, zero-dependency)    │                         │
│  │                                 │                         │
│  │  VectorFunctionProvider         │ cosine, L2, dot         │
│  │  VectorTypeProvider             │ serialize/deserialize   │
│  │  DistanceMetrics (SIMD)         │ System.Numerics.Vector  │
│  │  HnswIndex                      │ pure managed            │
│  │  VectorSearchOptions            │ memory config           │
│  │  VectorSerializer               │ float[] ↔ byte[]       │
│  └─────────────────────────────────┘                         │
│                                                              │
├──────────────────────────────────────────────────────────────┤
│  Storage Layer (unchanged)                                    │
│  ┌──────────┐ ┌──────────────┐ ┌──────────────┐             │
│  │Directory │ │  SingleFile  │ │  Columnar    │             │
│  │ Storage  │ │   (.scdb)    │ │  Storage     │             │
│  └──────────┘ └──────────────┘ └──────────────┘             │
│  Vectors stored as typed BLOBs — no storage layer changes    │
└──────────────────────────────────────────────────────────────┘
```

### Core Changes Summary

| File | Change | LOC |
|------|--------|-----|
| `DataTypes.cs` | Add `Vector = 10` to enum | 3 |
| `Interfaces/ICustomFunctionProvider.cs` | New interface | 25 |
| `Interfaces/ICustomTypeProvider.cs` | New interface | 30 |
| `Services/SqlFunctions.cs` | Add provider fallback in `EvaluateFunction()` | 15 |
| `DataStructures/Table.cs` | Add `Vector` case to `ParseDataType()` | 3 |
| `Services/SqlParser.Helpers.cs` | Add `Vector` case to `ParseValue()` | 5 |
| `Services/SqlAst.DML.cs` | Add `Dimensions` property to `ColumnDefinition` | 5 |
| `DatabaseExtensions.cs` | Accept providers in `AddSharpCoreDB()` | 10 |
| **Total core changes** | | **~96 LOC** |

---

## 5. SIMD Strategy

### Hardware Detection (already present in `SimdHelper.Core.cs`)

SharpCoreDB already detects SIMD capabilities:
- `Avx512F.IsSupported` → 512-bit (16 floats/cycle)
- `Avx2.IsSupported` → 256-bit (8 floats/cycle)
- `Sse2.IsSupported` → 128-bit (4 floats/cycle)
- `AdvSimd.IsSupported` → ARM NEON 128-bit (4 floats/cycle)

### Implementation Approach

```
Distance computation hot path:

1. Vector<float> loop  ← PRIMARY (cross-platform, auto-width)
   Uses System.Numerics.Vector<float> which auto-selects
   AVX-512/AVX2/SSE2/NEON based on hardware.
   Processes Vector<float>.Count floats per iteration.

2. Scalar tail loop    ← FALLBACK (handles remaining elements)
   Standard float arithmetic for elements that don't fill
   a complete SIMD register.

No platform-specific intrinsics needed!
System.Numerics.Vector<T> handles everything.
```

### Why `System.Numerics.Vector<T>` instead of `Vector128/256/512`?

1. **Auto-width:** `Vector<float>.Count` is 4 (SSE2), 8 (AVX2), or 16 (AVX-512) at runtime
2. **Cross-platform:** Same code path for x64 and ARM64
3. **No branching:** No need for `if (Avx2.IsSupported)` chains
4. **JIT-friendly:** RyuJIT aggressively optimizes these operations
5. **Matches existing pattern:** SharpCoreDB already uses `SimdHelper` with similar approach

### Performance Expectations

For 1536-dimension cosine distance (OpenAI embedding size):

| Platform | SIMD Width | Floats/Cycle | Time per Distance | Throughput |
|----------|-----------|-------------|-------------------|------------|
| x64 AVX-512 | 512-bit | 16 | ~0.8 μs | 1.2M/s |
| x64 AVX2 | 256-bit | 8 | ~1.5 μs | 650K/s |
| x64 SSE2 | 128-bit | 4 | ~3 μs | 330K/s |
| ARM64 NEON | 128-bit | 4 | ~3.5 μs | 285K/s |
| Scalar | 32-bit | 1 | ~12 μs | 83K/s |

---

## 6. Memory Management & Scaling

### Design Principle: Configurable at Every Level

SharpCoreDB serves both embedded (Raspberry Pi, mobile) and server (64GB RAM) deployments. Vector search memory must be **bounded and configurable**.

### Memory Tiers

```csharp
public sealed class VectorSearchOptions
{
    // === Memory Limits ===
    
    /// <summary>
    /// Hard memory limit for all vector indexes combined (MB).
    /// 0 = unlimited (server mode). Default: 256 MB.
    /// </summary>
    public int MaxMemoryMB { get; set; } = 256;

    /// <summary>
    /// Load HNSW index only on first vector query (saves startup memory).
    /// Default: true for embedded, false for server.
    /// </summary>
    public bool LazyIndexLoading { get; set; } = true;

    /// <summary>
    /// Release index memory when GC detects memory pressure.
    /// Index will be rebuilt on next query (lazy loading must be enabled).
    /// Default: true for embedded/mobile.
    /// </summary>
    public bool EvictIndexOnMemoryPressure { get; set; } = true;

    // === Presets ===
    
    public static VectorSearchOptions Embedded => new()
    {
        MaxMemoryMB = 50,
        LazyIndexLoading = true,
        EvictIndexOnMemoryPressure = true,
        DefaultIndexType = VectorIndexType.Flat, // Exact search, low memory
        MaxDimensions = 1536,
    };

    public static VectorSearchOptions Standard => new()
    {
        MaxMemoryMB = 512,
        LazyIndexLoading = true,
        EvictIndexOnMemoryPressure = false,
        DefaultIndexType = VectorIndexType.Hnsw,
        MaxDimensions = 4096,
    };

    public static VectorSearchOptions Enterprise => new()
    {
        MaxMemoryMB = 0, // Unlimited
        LazyIndexLoading = false,
        EvictIndexOnMemoryPressure = false,
        DefaultIndexType = VectorIndexType.Hnsw,
        DefaultQuantization = QuantizationType.Scalar8,
        MaxDimensions = 4096,
    };
}
```

### Memory Budget Calculation

```
Per vector (1536-dim, float32):
  Raw data:     1536 × 4 bytes = 6,144 bytes = 6 KB
  HNSW node:    ~256 bytes (M=16, avg 2 layers)
  Total:        ~6.4 KB per vector

For 100K vectors:
  Raw:          600 MB
  HNSW graph:   25 MB
  Total:        ~625 MB

With scalar quantization (int8):
  Raw:          150 MB (4x reduction)
  HNSW graph:   25 MB (unchanged)
  Total:        ~175 MB

With binary quantization (1-bit):
  Raw:          ~19 MB (32x reduction)
  HNSW graph:   25 MB (unchanged)
  Total:        ~44 MB
```

### Scaling Strategy

```
Dataset Size    Recommended Approach              Memory
─────────────  ────────────────────────────────  ──────────
< 1K           Flat (exact), no index             < 10 MB
1K - 10K       Flat (exact), optional HNSW        10 - 100 MB
10K - 100K     HNSW (M=16, ef=200)               100 MB - 1 GB
100K - 1M      HNSW + scalar quantization         250 MB - 2.5 GB
1M - 10M       HNSW + binary quantization          200 MB - 2 GB
> 10M          DiskANN (future Phase 5)            Disk-based
```

---

## 7. Storage Format

### Vector Column Storage

Vectors are stored as typed BLOBs in the existing storage layer. The `DataType.Vector` enum value tells the serializer to use the optimized vector binary format.

```
Vector Binary Format:
┌──────────────┬───────────┬──────────────────────────────┐
│ Header (8B)  │ Dims (4B) │ Float32 Data (dims × 4B)     │
├──────────────┼───────────┼──────────────────────────────┤
│ Magic: 0xVEC │ 1536      │ [f32][f32][f32]...[f32]      │
│ Version: 1   │           │                              │
│ Flags: 0x00  │           │ Total: 1536 × 4 = 6144 bytes│
│ Reserved     │           │                              │
└──────────────┴───────────┴──────────────────────────────┘

Header flags (future-proof):
  Bit 0: Quantization type (00=none, 01=scalar8, 10=binary, 11=reserved)
  Bit 2: Normalized flag (1 = pre-normalized, skip norm in cosine)
  Bit 3-7: Reserved for future use
```

### Why not just raw float[]?

1. **Version header** allows future format changes without migration
2. **Dimension count** enables runtime validation (detect column dimension mismatch)
3. **Flags** support quantization without changing the container format
4. **Magic bytes** prevent accidental interpretation of regular BLOBs as vectors
5. **Overhead:** 12 bytes per vector (< 0.2% for 1536-dim) — negligible

### HNSW Index Persistence

```
HNSW Index Storage Format (sidecar to table data):
┌────────────────────────────────────────────────────────┐
│ Header                                                  │
│  Magic: "HNSW"                                          │
│  Version: 1                                             │
│  Dimensions: uint32                                     │
│  M: uint16, efConstruction: uint16                      │
│  MaxLevel: uint8                                        │
│  EntryPointId: int64                                    │
│  NodeCount: uint32                                      │
├────────────────────────────────────────────────────────┤
│ Node Table (sorted by ID for binary search)             │
│  [NodeId: int64][Level: uint8][NeighborCounts: uint8[]] │
│  [L0_Neighbors: int64[]][L1_Neighbors: int64[]]...      │
├────────────────────────────────────────────────────────┤
│ Checksum: CRC32                                         │
└────────────────────────────────────────────────────────┘

Storage location:
  Directory mode:  {dbPath}/{tableName}_{columnName}.hnsw
  SingleFile mode: Stored as a metadata block in .scdb file
```

---

## 8. Index Architecture (HNSW)

### Algorithm Overview

HNSW (Hierarchical Navigable Small World) builds a multi-layer proximity graph:

```
Level 3:  [A] ─────────────────── [D]
Level 2:  [A] ──── [B] ───────── [D]
Level 1:  [A] ── [B] ── [C] ── [D] ── [E]
Level 0:  [A]-[B]-[C]-[D]-[E]-[F]-[G]-[H]-[I]-[J]
```

- **Insert:** Navigate from top layer to target layer, connect to M nearest neighbors at each layer
- **Search:** Navigate from top to bottom, expand candidates at level 0 with `ef_search`
- **Complexity:** O(log N) for search, O(N log N) for build

### Key Parameters

| Parameter | Default | Range | Effect |
|-----------|---------|-------|--------|
| `M` | 16 | 4-64 | Connections per node. Higher = better recall, more memory |
| `efConstruction` | 200 | 50-2000 | Build-time search width. Higher = better index quality, slower build |
| `efSearch` | 50 | 10-500 | Query-time search width. Higher = better recall, slower query |
| `maxLevel` | auto | - | `ln(N) / ln(M)` — calculated automatically |

### Thread Safety Model

```
HNSW Operations:
  Search (read):   Concurrent ← Lock-free, ConcurrentDictionary for nodes
  Insert (write):  Exclusive  ← Lock class for graph modifications
  Delete (write):  Exclusive  ← Tombstone + lazy cleanup

Pattern: ReadWriteLock equivalent via Lock + concurrent data structures
```

### Memory Layout

```csharp
// Each HNSW node:
internal sealed class HnswNode
{
    internal long Id;                    //  8 bytes
    internal float[] Vector;             //  dims × 4 bytes (reference)
    internal int Level;                  //  4 bytes
    internal long[][] Neighbors;         //  ~M × levels × 8 bytes

    // For M=16, avg 2 levels, 1536-dim:
    // 8 + 4 + (16 × 2 × 8) = 268 bytes per node (excluding vector)
}
```

---

## 9. SQL Integration Points

### Parser Changes

```
Affected files in SharpCoreDB core:

SqlParser.DDL.cs:
  ExecuteCreateTable() — parse VECTOR(N) type → DataType.Vector + dimensions metadata
  NEW: ExecuteCreateVectorIndex() — CREATE VECTOR INDEX ... USING HNSW(...)
  NEW: ExecuteDropVectorIndex() — DROP VECTOR INDEX ...

SqlParser.DML.cs:
  ExecuteInternal() — add case ("CREATE", "VECTOR") for vector index DDL
  ExecuteInsert() — no change needed (vectors pass through as BLOB)
  ExecuteSelect() — no change needed (ORDER BY function() already works)

SqlFunctions.cs:
  EvaluateFunction() — add provider fallback for vec_* functions

Table.cs:
  ParseDataType() — add "VECTOR" case → DataType.Vector
```

### Query Execution Flow for Vector Search

```
User SQL:
  SELECT id, title, vec_distance_cosine(embedding, @query) AS distance
  FROM documents
  ORDER BY distance
  LIMIT 10

Execution flow:
  1. SqlParser.ExecuteSelect() → parse columns, FROM, ORDER BY, LIMIT
  2. For each column expression:
     - "id", "title" → direct column access (unchanged)
     - "vec_distance_cosine(embedding, @query)" → FunctionCallNode
  3. FunctionCallNode → SqlFunctions.EvaluateFunction("VEC_DISTANCE_COSINE", args)
  4. EvaluateFunction → core doesn't know this function → fallback to ICustomFunctionProvider
  5. VectorFunctionProvider.Evaluate() → DistanceMetrics.CosineDistance(a, b)
  6. Results sorted by distance, LIMIT 10 applied
  7. Return top-10 results

Future optimization (Phase 4):
  - Query planner detects "ORDER BY vec_distance + LIMIT" pattern
  - Routes to HNSW index instead of full table scan
  - Index returns pre-sorted candidates, skipping sort step
```

---

## 10. Thread Safety & Concurrency

### Existing SharpCoreDB Patterns

The codebase uses:
- `Lock` class (C# 14) for mutual exclusion
- `ConcurrentDictionary` for concurrent access
- `Channel<T>` for producer-consumer patterns
- Read-only snapshots for consistent reads

### Vector Module Thread Safety

```csharp
// VectorFunctionProvider: Stateless → thread-safe by design
// VectorSerializer: Static methods, no state → thread-safe
// DistanceMetrics: Pure functions → thread-safe

// HnswIndex: Custom synchronization
public sealed class HnswIndex
{
    private readonly Lock _buildLock = new();           // For insert/delete
    private readonly ConcurrentDictionary<long, HnswNode> _nodes = new();  // For reads

    // Search: Lock-free (reads ConcurrentDictionary + immutable neighbor arrays)
    // Insert: Acquires _buildLock (exclusive write)
    // Delete: Acquires _buildLock (marks tombstone)
}
```

---

## 11. NativeAOT & Trimming Compatibility

### Constraints from SharpCoreDB.csproj

```xml
<TieredPGO>true</TieredPGO>
<TieredPGOOptimize>true</TieredPGOOptimize>
<PublishTieredAot>true</PublishTieredAot>
```

### Compliance Checklist

| Requirement | Vector Module Approach |
|-------------|----------------------|
| No `Assembly.Load()` | ✅ DI registration at compile time |
| No `Type.GetType()` by name | ✅ Generic DI only |
| No `Activator.CreateInstance()` | ✅ `new` constructor calls only |
| `System.Text.Json` source-gen | ✅ `JsonSerializer.Deserialize<float[]>()` is AOT-safe |
| No `Expression.Compile()` | ✅ No expression trees |
| `System.Numerics.Vector<T>` AOT | ✅ Fully supported in NativeAOT |
| `MemoryMarshal` AOT | ✅ Intrinsic, fully supported |

### Trimming

```csharp
// If SharpCoreDB.VectorSearch is not referenced:
//   - ICustomFunctionProvider/ICustomTypeProvider remain in core (tiny)
//   - All vector code is trimmed away (not referenced)
//   - Zero bytes of vector code in final binary
```

---

## 12. Error Handling Strategy

### Validation Errors (fail fast)

```csharp
// Dimension mismatch
ArgumentException: "Vector has 768 dimensions, column 'embedding' requires 1536"

// Invalid vector data
ArgumentException: "Cannot parse vector: expected JSON float array, got 'hello world'"

// Dimension too large
ArgumentException: "Vector dimensions 8192 exceeds maximum 4096"

// Function argument error
ArgumentException: "vec_distance_cosine requires 2 vector arguments, got 1"
```

### Runtime Errors (recoverable)

```csharp
// Memory limit exceeded
InvalidOperationException: "HNSW index for 'documents.embedding' would exceed MaxMemoryMB (256 MB). 
                            Consider scalar quantization or increasing the limit."

// Index not found
InvalidOperationException: "No vector index on 'documents.embedding'. Using exact search (slow for large tables)."
// ↑ This is a WARNING, not an error. Falls back to exact search automatically.
```

### Design Principle: Degrade Gracefully

```
Scenario                          Behavior
──────────────────────────────── ──────────────────────────────────────
Vector module not installed       vec_* functions → NotSupportedException
HNSW index not created            Falls back to exact (flat) search + warning
Memory limit exceeded             Reject new HNSW index, existing exact search works
Dimension mismatch on INSERT      Reject the INSERT with clear error message
NaN/Inf in vector                 Reject with validation error
Vector module installed, no use   Zero overhead (providers registered but never called)
```

---

## Appendix A: Rejected Alternatives

### Custom SQL Operators (`<=>`, `<->`)

**Rejected because:**
1. Requires parser changes that affect all query parsing (performance risk)
2. Not standard SQL — users can't use the same queries with other tools
3. pgvector's biggest complaint is operator confusion

### Virtual Tables (sqlite-vec style)

**Rejected because:**
1. SharpCoreDB has no virtual table infrastructure
2. Would require building an entire vtab layer just for vectors
3. Adds overhead to all row accesses through vtab indirection

### Separate Vector Database File

**Rejected because:**
1. Breaks single-file deployment model (.scdb)
2. Transaction coordination between two files is complex
3. Users expect one database = one connection

---

## Appendix B: Future Considerations

### Distributed Vector Search (Phase 5+)

When SharpCoreDB scales to RDBMS mode:
- HNSW graph sharding across nodes
- Distributed approximate search with result merging
- Replication-aware index invalidation

### Integration Points

- `Microsoft.Extensions.AI.IEmbeddingGenerator<string, Embedding<float>>` compatibility
- `SharpCoreDB.EntityFrameworkCore` — `HasVectorProperty()` + `VectorDistance()` LINQ support
- `SharpCoreDB.Data.Provider` — ADO.NET `DbType` mapping for vectors

---

*Document version: 1.0 | Last updated: 2026-02*
