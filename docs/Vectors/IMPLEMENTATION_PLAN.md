# Implementation Plan: Vector Search & Storage

> **Target Version:** 1.2.0  
> **Estimated Effort:** 4-6 weeks  
> **Priority:** High (AI/RAG is the #1 requested feature)  
> **Risk Level:** Low (100% optional, zero breaking changes)

---

## Prerequisites

- [x] Review and approve `TECHNICAL_SPEC.md`
- [x] Review and approve `README.md` (user-facing docs)
- [ ] Create feature branch: `feature/vector-search`
- [x] Create `SharpCoreDB.VectorSearch` project in solution
- [ ] Create `SharpCoreDB.VectorSearch.Tests` project in solution

---

## Phase 1: Core Extension Points ✅ COMPLETE

> **Goal:** Add minimal hooks in SharpCoreDB core to support external type/function providers.  
> **Risk:** LOW — additive changes only, all with backward-compatible defaults.

### 1.1 DataType Enum Extension

- [x] **`src/SharpCoreDB/DataTypes.cs`** — Add `Vector` to DataType enum
- [x] Verify all `switch` expressions over `DataType` have a `_` default case
- [x] No other DataType switches need updating (Vector stores as BLOB internally)

### 1.2 Extension Interfaces

- [x] **`src/SharpCoreDB/Interfaces/ICustomFunctionProvider.cs`** — CanHandle, Evaluate, GetFunctionNames
- [x] **`src/SharpCoreDB/Interfaces/ICustomTypeProvider.cs`** — CanHandle, GetStorageType, Serialize, Deserialize

### 1.3 Core Integration Points

- [x] **`src/SharpCoreDB/Services/SqlFunctions.cs`** — EvaluateFunction with provider fallback
- [x] **`src/SharpCoreDB/DataStructures/Table.cs`** → `ParseDataType()` with VECTOR support
- [x] **`src/SharpCoreDB/Services/SqlParser.Helpers.cs`** → `ParseValue()` with Vector case + `ParseVectorValue()`
- [x] **`src/SharpCoreDB/Services/SqlAst.DML.cs`** → `ColumnDefinition.Dimensions` property
- [x] **`src/SharpCoreDB/DatabaseExtensions.cs`** — Provider registration support

### 1.4 DDL Parsing for VECTOR(N)

- [x] **`src/SharpCoreDB/Services/SqlParser.DDL.cs`** → VECTOR(N) type parsing
- [ ] **`src/SharpCoreDB/Services/SqlParser.DML.cs`** → `CREATE VECTOR INDEX` dispatch (deferred to Phase 5)

### 1.5 Verification

- [x] Build succeeds
- [x] No new warnings introduced
- [x] `DataType.Vector` unreachable unless explicitly used in CREATE TABLE

---

## Phase 2: Vector Module Project Setup ✅ COMPLETE

> **Goal:** Create the `SharpCoreDB.VectorSearch` project with proper structure.

### 2.1 Project Creation

- [x] **`src/SharpCoreDB.VectorSearch/SharpCoreDB.VectorSearch.csproj`** — CREATED
  ```xml
  <Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup>
      <TargetFramework>net10.0</TargetFramework>
      <LangVersion>14.0</LangVersion>
      <ImplicitUsings>enable</ImplicitUsings>
      <Nullable>enable</Nullable>
      <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
      <PackageId>SharpCoreDB.VectorSearch</PackageId>
      <Description>Vector search extension for SharpCoreDB</Description>
    </PropertyGroup>
    <ItemGroup>
      <ProjectReference Include="..\SharpCoreDB\SharpCoreDB.csproj" />
    </ItemGroup>
  </Project>
  ```

- [x] Add project to solution
- [x] Create folder structure:
  ```
  src/SharpCoreDB.VectorSearch/
    VectorSearchExtensions.cs         ← DI registration
    VectorSearchOptions.cs            ← Configuration
    VectorFunctionProvider.cs         ← ICustomFunctionProvider impl
    VectorTypeProvider.cs             ← ICustomTypeProvider impl
    VectorSerializer.cs               ← float[] ↔ byte[] serialization
    Distance/
      DistanceMetrics.cs              ← SIMD cosine, L2, dot product
      DistanceFunction.cs             ← Enum: Cosine, Euclidean, DotProduct
    Index/
      VectorIndexType.cs              ← Enum: Flat, Hnsw
      IVectorIndex.cs                 ← Index interface
      FlatIndex.cs                    ← Brute-force exact search
      HnswIndex.cs                    ← HNSW approximate search
      HnswNode.cs                     ← Graph node structure
      HnswConfig.cs                   ← HNSW parameters
    Quantization/
      QuantizationType.cs             ← Enum: None, Scalar8, Binary
      IQuantizer.cs                   ← Quantization interface
      ScalarQuantizer.cs              ← float32 → int8 (Phase 3)
      BinaryQuantizer.cs              ← float32 → bit (Phase 3)
    Storage/
      VectorStorageFormat.cs          ← Binary format constants
      HnswPersistence.cs             ← Index serialization (Phase 2)
  ```

### 2.2 Test Project Creation

- [ ] **`tests/SharpCoreDB.VectorSearch.Tests/SharpCoreDB.VectorSearch.Tests.csproj`** — NEW
  ```xml
  <ItemGroup>
    <ProjectReference Include="..\..\src\SharpCoreDB.VectorSearch\SharpCoreDB.VectorSearch.csproj" />
    <ProjectReference Include="..\..\src\SharpCoreDB\SharpCoreDB.csproj" />
  </ItemGroup>
  ```

- [ ] Add test project to solution
- [ ] Create test structure mirroring source

---

## Phase 3: Core Vector Operations ✅ COMPLETE

> **Goal:** Implement vector serialization, distance metrics, and SQL functions.

### 3.1 Vector Serialization

- [x] **`VectorSerializer.cs`**
  - `Serialize(ReadOnlySpan<float> vector) → byte[]` via `MemoryMarshal.AsBytes()`
  - `Deserialize(byte[] data) → float[]` via `MemoryMarshal.Cast<byte, float>()`
  - `DeserializeSpan(byte[] data) → ReadOnlySpan<float>` (zero-copy)
  - `FromJson(string json) → float[]` via `JsonSerializer.Deserialize<float[]>()`
  - `ToJson(ReadOnlySpan<float> vector) → string`
  - Header: magic bytes + version + dimensions + flags
  - Unit tests: round-trip, edge cases (empty, max dims, NaN rejection)

### 3.2 Distance Metrics (SIMD)

- [x] **`DistanceMetrics.cs`**
  - `CosineDistance(ReadOnlySpan<float> a, ReadOnlySpan<float> b) → float`
  - `EuclideanDistanceSquared(ReadOnlySpan<float> a, ReadOnlySpan<float> b) → float`
  - `EuclideanDistance(ReadOnlySpan<float> a, ReadOnlySpan<float> b) → float`
  - `DotProduct(ReadOnlySpan<float> a, ReadOnlySpan<float> b) → float`
  - `NegativeDotProduct(...)` for ORDER BY compatibility
  - All use `System.Numerics.Vector<float>` + scalar tail
  - All have `[MethodImpl(MethodImplOptions.AggressiveOptimization)]`
  - Unit tests: known values, edge cases (zero vector, identical vectors, orthogonal)
  - Correctness tests: SIMD result == scalar result (within float epsilon)

### 3.3 Vector Function Provider

- [x] **`VectorFunctionProvider.cs`** — implements `ICustomFunctionProvider`
  - `vec_distance_cosine(a, b)` → `DistanceMetrics.CosineDistance()`
  - `vec_distance_l2(a, b)` → `DistanceMetrics.EuclideanDistance()`
  - `vec_distance_dot(a, b)` → `DistanceMetrics.NegativeDotProduct()`
  - `vec_from_float32(json_string)` → `VectorSerializer.FromJson()`
  - `vec_to_json(vector)` → `VectorSerializer.ToJson()`
  - `vec_normalize(vector)` → L2 normalize
  - `vec_dimensions(vector)` → dimension count
  - Argument type coercion: `float[]`, `byte[]`, `string` → float array
  - Unit tests: each function with various input types

### 3.4 Vector Type Provider

- [x] **`VectorTypeProvider.cs`** — implements `ICustomTypeProvider`
  - Parse `VECTOR(1536)` → DataType.Vector + dimensions = 1536
  - Serialize `float[]` → header + binary float data
  - Deserialize → float[] with dimension validation
  - Reject dimension mismatch on INSERT
  - Unit tests: CREATE TABLE round-trip, dimension validation

### 3.5 DI Registration

- [x] **`VectorSearchExtensions.cs`**
  - `AddVectorSupport(Action<VectorSearchOptions>? configure = null)`
  - Register `VectorFunctionProvider` as `ICustomFunctionProvider`
  - Register `VectorTypeProvider` as `ICustomTypeProvider`
  - Register `VectorSearchOptions` as singleton

### 3.6 Integration Tests

- [ ] CREATE TABLE with VECTOR(1536) column
- [ ] INSERT with `vec_from_float32()` and parameter binding
- [ ] SELECT with `vec_distance_cosine()` in ORDER BY
- [ ] End-to-end: create → insert 100 vectors → search → verify top-k correctness
- [ ] Verify non-vector queries are completely unaffected
- [ ] Verify database without vector module loaded (functions throw NotSupportedException)
- [ ] Test with both Directory and SingleFile storage modes

---

## Phase 4: Flat (Exact) Search Index ✅ COMPLETE

> **Goal:** Brute-force search that's simple, correct, and works for small datasets.

### 4.1 Flat Index

- [x] **`FlatIndex.cs`** — implements `IVectorIndex`
  - `Search(ReadOnlySpan<float> query, int k, DistanceFunction metric) → List<(long Id, float Distance)>`
  - Scans all vectors, computes distance, returns top-k via min-heap
  - Uses `ArrayPool<float>` for temporary buffers
  - Thread-safe (read-only access to immutable vector data)
  - Unit tests: correctness, k > count, empty index

### 4.2 Top-K Selection

- [x] Max-heap implementation for efficient top-k selection (`TopKHeap.cs`)
  - Avoids sorting all N distances (O(N) instead of O(N log N))
  - Uses `Span<T>` for zero-allocation heap operations

---

## Phase 5: HNSW Approximate Index (IN PROGRESS)

> **Goal:** High-performance approximate nearest neighbor search for large datasets.

### 5.1 HNSW Core ✅

- [x] **`HnswNode.cs`** — Graph node structure
  - ID, Vector (copied), MaxLayer, volatile Neighbors (immutable-swap for thread safety)
  - EstimatedBytes property for memory tracking

- [x] **`HnswConfig.cs`** — Configuration
  - M, EfConstruction, EfSearch, Dimensions, DistanceFunction, LevelMultiplier
  - Presets: Default, HighRecall, LowMemory
  - Validate() method

- [x] **`HnswIndex.cs`** — Main index (implements `IVectorIndex`)
  - `Add()` — 2-phase: greedy traverse from top → layer-by-layer insert with bidirectional connections
  - `Search()` — greedy traverse to layer 1 → beam search on layer 0 with ef = max(efSearch, k)
  - `Remove()` — remove from all neighbor lists + entry point reassignment
  - `RandomLevel()` — standard HNSW distribution: floor(-ln(uniform) × 1/ln(M))
  - Thread safety: Lock for writes, ConcurrentDictionary + volatile neighbor swap for reads
  - Deterministic seed option for reproducible testing

### 5.2 HNSW Tests

- [ ] Insert/search correctness (compare with exact brute-force)
- [ ] Recall@10 measurement (should be > 95% with default params)
- [ ] Thread safety: concurrent reads during insert
- [ ] Parameterized tests: various M, efConstruction values
- [ ] Edge cases: duplicate vectors, zero vectors, single element

### 5.3 DDL Syntax ✅

- [x] Parse `CREATE VECTOR INDEX idx ON table(col) USING HNSW`
- [x] Parse `CREATE VECTOR INDEX idx ON table(col) USING FLAT`
- [x] Parse `DROP VECTOR INDEX idx ON table`
- [x] Store index metadata in table schema via `ITable.Metadata` dictionary
- [x] Added `ITable.SetMetadata/GetMetadata/RemoveMetadata` to core interface
- [x] Validated column is VECTOR type before creating index

### 5.4 Query Planner Integration

- [ ] Detect `ORDER BY vec_distance_*(col, query) LIMIT k` pattern
- [ ] Route to HNSW index when available (skip full table scan)
- [ ] Fallback to flat search if no index exists
- [ ] EXPLAIN output shows "Vector Index Scan (HNSW)" vs "Vector Full Scan (Exact)"

### 5.5 Index Persistence ✅

- [x] Serialize HNSW graph to binary format (`HnswPersistence.Serialize()`)
- [x] Deserialize on database open (`HnswPersistence.Deserialize()`)
- [x] `HnswIndex.GetSnapshot()` / `RestoreNode()` for persistence access
- [x] `HnswSnapshot` / `HnswNodeSnapshot` record structs
- [ ] Integrate with WAL for crash recovery (deferred — requires core changes)

---

## Phase 6: Memory Optimization ✅ COMPLETE

> **Goal:** Make vector search viable on memory-constrained devices.

### 6.1 Memory Tracking ✅

- [x] Track memory usage per vector index (`IVectorIndex.EstimatedMemoryBytes`)
- [x] `VectorMemoryInfo` record struct for diagnostics
- [x] `VectorStorageFormat` constants for binary format metadata
- [ ] Enforce `MaxMemoryMB` limit at index creation (deferred — requires index registry)

### 6.2 Lazy Index Loading

- [x] `VectorSearchOptions.LazyIndexLoading` flag configured
- [ ] Implementation in index manager (deferred — requires integration layer)

### 6.3 Scalar Quantization ✅

- [x] **`ScalarQuantizer.cs`** — float32 → uint8 (4× memory reduction)
  - Calibrate min/max per dimension from sample vectors
  - `Quantize()` / `Dequantize()` round-trip
  - Asymmetric distance: query float32 × DB uint8 (cosine, L2, dot product)
  - Calibration serialization / deserialization

### 6.4 Binary Quantization ✅

- [x] **`BinaryQuantizer.cs`** — float32 → 1 bit (32× memory reduction)
  - Sign-based encoding: positive → 1, negative/zero → 0
  - Hamming distance via hardware `BitOperations.PopCount` (ulong batched)
  - Calibration serialization / deserialization

---

## Phase 7: Testing & Quality (~3 days)

### 7.1 Unit Tests (per component)

- [ ] `VectorSerializerTests` — round-trip, edge cases, NaN rejection
- [ ] `DistanceMetricsTests` — known values, SIMD vs scalar consistency
- [ ] `VectorFunctionProviderTests` — all 7 functions
- [ ] `VectorTypeProviderTests` — type parsing, dimension validation
- [ ] `FlatIndexTests` — correctness, empty, overflow
- [ ] `HnswIndexTests` — insert, search, recall, concurrent
- [ ] `ScalarQuantizerTests` — round-trip accuracy
- [ ] `BinaryQuantizerTests` — hamming distance correctness

### 7.2 Integration Tests

- [ ] End-to-end SQL workflow (CREATE → INSERT → SELECT)
- [ ] Multi-vector-column tables
- [ ] Hybrid search (vector + WHERE filter)
- [ ] Both storage modes (Directory + SingleFile)
- [ ] Encrypted database with vector columns
- [ ] Prepared statements with vector parameters
- [ ] ExecuteBatchSQL with vector inserts

### 7.3 Regression Tests

- [ ] Run FULL existing test suite — zero failures
- [ ] Benchmark existing operations — zero performance regression
- [ ] Database file format — existing .scdb files open without issues

### 7.4 Performance Tests

- [ ] Distance computation throughput (floats/second per platform)
- [ ] HNSW build time (vectors/second)
- [ ] HNSW search latency (queries/second at various ef_search)
- [ ] Memory usage verification against estimates
- [ ] Compare: SharpCoreDB vs sqlite-vec vs pgvector (where possible)

---

## Phase 8: Documentation & Release (~2 days)

### 8.1 Documentation

- [ ] Update `README_NUGET.md` with vector search mention
- [ ] Update `docs/CHANGELOG.md` for v1.2.0
- [ ] API XML documentation on all public types
- [ ] Migration guide from sqlite-vec
- [ ] Performance tuning guide

### 8.2 Examples

- [ ] `Examples/SharpCoreDB.Examples.VectorSearch/` — standalone example
  - RAG pattern with mock embeddings
  - Hybrid search (vector + SQL filter)
  - HNSW index creation and tuning

### 8.3 NuGet Package

- [ ] `SharpCoreDB.VectorSearch.csproj` package metadata
- [ ] Package README for NuGet gallery
- [ ] Dependency: only `SharpCoreDB` (no other packages)

### 8.4 Release

- [ ] Merge `feature/vector-search` → `master`
- [ ] Tag `v1.2.0`
- [ ] Publish `SharpCoreDB 1.2.0` to NuGet
- [ ] Publish `SharpCoreDB.VectorSearch 1.2.0` to NuGet
- [ ] GitHub Release notes

---

## File Summary: All New & Modified Files

### New Files (SharpCoreDB.VectorSearch project)

| File | Purpose | Phase |
|------|---------|-------|
| `SharpCoreDB.VectorSearch.csproj` | Project file | 2 |
| `VectorSearchExtensions.cs` | DI registration | 3 |
| `VectorSearchOptions.cs` | Configuration | 3 |
| `VectorFunctionProvider.cs` | SQL function impl | 3 |
| `VectorTypeProvider.cs` | Type handling impl | 3 |
| `VectorSerializer.cs` | Serialization | 3 |
| `Distance/DistanceMetrics.cs` | SIMD distance | 3 |
| `Distance/DistanceFunction.cs` | Enum | 3 |
| `Index/VectorIndexType.cs` | Enum | 4 |
| `Index/IVectorIndex.cs` | Interface | 4 |
| `Index/FlatIndex.cs` | Exact search | 4 |
| `Index/HnswIndex.cs` | Approximate search | 5 |
| `Index/HnswNode.cs` | Graph node | 5 |
| `Index/HnswConfig.cs` | HNSW params | 5 |
| `Quantization/QuantizationType.cs` | Enum | 6 |
| `Quantization/IQuantizer.cs` | Interface | 6 |
| `Quantization/ScalarQuantizer.cs` | SQ8 | 6 |
| `Quantization/BinaryQuantizer.cs` | BQ1 | 6 |
| `Storage/VectorStorageFormat.cs` | Binary format | 3 |
| `Storage/HnswPersistence.cs` | Index persistence | 5 |

### New Files (Core — interfaces only)

| File | Purpose | Phase |
|------|---------|-------|
| `Interfaces/ICustomFunctionProvider.cs` | Extension interface | 1 |
| `Interfaces/ICustomTypeProvider.cs` | Extension interface | 1 |

### Modified Files (Core — minimal changes)

| File | Change | LOC | Phase |
|------|--------|-----|-------|
| `DataTypes.cs` | Add `Vector = 10` | 3 | 1 |
| `Services/SqlFunctions.cs` | Provider fallback | 15 | 1 |
| `DataStructures/Table.cs` | ParseDataType case | 5 | 1 |
| `Services/SqlParser.Helpers.cs` | ParseValue case | 5 | 1 |
| `Services/SqlAst.DML.cs` | ColumnDefinition.Dimensions | 5 | 1 |
| `DatabaseExtensions.cs` | Provider registration | 10 | 1 |
| `Services/SqlParser.DML.cs` | CREATE VECTOR INDEX dispatch | 5 | 5 |
| `Services/SqlParser.DDL.cs` | VECTOR(N) parsing | 15 | 1 |

### New Test Files

| File | Phase |
|------|-------|
| `tests/SharpCoreDB.VectorSearch.Tests/VectorSerializerTests.cs` | 3 |
| `tests/SharpCoreDB.VectorSearch.Tests/DistanceMetricsTests.cs` | 3 |
| `tests/SharpCoreDB.VectorSearch.Tests/VectorFunctionProviderTests.cs` | 3 |
| `tests/SharpCoreDB.VectorSearch.Tests/FlatIndexTests.cs` | 4 |
| `tests/SharpCoreDB.VectorSearch.Tests/HnswIndexTests.cs` | 5 |
| `tests/SharpCoreDB.VectorSearch.Tests/IntegrationTests.cs` | 3 |
| `tests/SharpCoreDB.VectorSearch.Tests/RegressionTests.cs` | 7 |

---

## Risk Mitigation

| Risk | Probability | Impact | Mitigation |
|------|------------|--------|------------|
| Core changes break existing tests | Low | High | Phase 1 changes are additive with defaults; run full test suite |
| HNSW recall too low | Medium | Medium | Extensive parameterized tests; tune defaults from literature |
| Memory limits hard to enforce | Medium | Medium | Use GC.AddMemoryPressure() + monitoring |
| SIMD gives different results than scalar | Low | High | Correctness tests comparing SIMD vs scalar (within epsilon) |
| Vector serialization backward compat | Low | High | Version header in binary format enables future migration |
| NativeAOT breaks with new DI registrations | Low | Medium | Test with `dotnet publish -r win-x64` early in Phase 2 |

---

## Success Criteria

### Phase 1 (Core Extension Points)
- ✅ All existing tests pass
- ✅ Zero performance regression in non-vector queries
- ✅ `DataType.Vector` enum value exists but is unreachable without vector module

### Phase 3 (Core Operations)
- ✅ Can CREATE TABLE with VECTOR column
- ✅ Can INSERT and SELECT vectors
- ✅ `vec_distance_cosine()` returns correct results
- ✅ Exact search works via ORDER BY + LIMIT

### Phase 5 (HNSW)
- ✅ Recall@10 > 95% with default parameters
- ✅ Search latency < 2ms for 100K vectors (1536-dim, AVX2)
- ✅ Concurrent reads don't block during insert
- ✅ Index persists across database restart

### Phase 7 (Release Ready)
- ✅ All tests pass
- ✅ Zero regression in existing benchmarks
- ✅ Documentation complete
- ✅ NuGet package builds and installs correctly

---

*Document version: 1.0 | Last updated: 2026-02*
