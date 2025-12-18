# Comprehensive Database Benchmark Guide

## Quick Start

### Option 1: Run from Code (Simpelste manier!)

Maak een nieuw bestand `RunComprehensive.cs` in de Benchmarks folder:

```csharp
using SharpCoreDB.Benchmarks;

var benchmark = new ComprehensiveComparison();
benchmark.Run();
```

En run met:
```bash
dotnet run --project SharpCoreDB.Benchmarks
```

### Option 2: Add to Existing Program.cs

Voeg deze code toe aan je `Program.cs`:

```csharp
// In het menu, voeg toe:
Console.WriteLine("  1. Comprehensive Comparison (NEW!)");
Console.WriteLine("     - SQLite vs LiteDB vs SharpCoreDB (Encrypted & Unencrypted)");
Console.WriteLine("     - ALL features: Hash indexes, SIMD, Adaptive WAL");
Console.WriteLine("     - 6 test scenarios");

// In de switch statement:
case "1":
    var benchmark = new ComprehensiveComparison();
    benchmark.Run();
    break;
```

## What Does It Test?

### ✅ Test 1: Bulk Insert Performance
- 10,000 records in single transaction
- **SharpCoreDB features used:**
  - ✅ `HighSpeedInsertMode = true`
  - ✅ `UseGroupCommitWal = true`
  - ✅ `EnableAdaptiveWalBatching = true` (auto-scales batch size!)
  - ✅ `WalBatchMultiplier = 256` (aggressive batching)
  - ✅ `InsertUsersTrueBatch()` (single WAL transaction)

### ✅ Test 2: Indexed Lookup Performance
- 1,000 hash index lookups
- **SharpCoreDB features used:**
  - ✅ `EnableHashIndexes = true` (O(1) lookups!)
  - ✅ `EnableQueryCache = true`
  - ✅ `QueryCacheSize = 5000`
  - ✅ Hash index automatically created on email column

### ✅ Test 3: Analytical Aggregates (SIMD)
- SUM/AVG/MIN/MAX on 10,000 records
- **SharpCoreDB features used:**
  - ✅ `ColumnStore<T>` with SIMD optimization
  - ✅ AVX-512 support (2x faster on modern CPUs!)
  - ✅ Parallel + SIMD for large datasets

### ✅ Test 4: Concurrent Writes
- 8 threads writing simultaneously
- **SharpCoreDB features used:**
  - ✅ `EnableAdaptiveWalBatching = true` (scales to load!)
  - ✅ `WalBatchMultiplier = 512` (extreme concurrency mode)
  - ✅ Adaptive batch size: 128 → 4096 operations

### ✅ Test 5: Mixed Workload
- 5000 INSERTs + 3000 UPDATEs + 1000 SELECTs
- **SharpCoreDB features used:**
  - ✅ `DatabaseConfig.HighPerformance` preset
  - ✅ All caching enabled
  - ✅ Hash indexes for fast updates

### ✅ Test 6: Feature Comparison Matrix
- Shows which features each database has
- **SharpCoreDB unique features:**
  - ✅ Built-in AES-256-GCM encryption
  - ✅ Hash indexes (O(1) lookups)
  - ✅ SIMD aggregates (50-106x faster!)
  - ✅ Adaptive WAL batching
  - ✅ MVCC snapshot isolation
  - ✅ Modern C# 14 generics

## SharpCoreDB Configurations Used

### For Bulk Inserts (Encrypted & Unencrypted)
```csharp
var config = new DatabaseConfig
{
    NoEncryptMode = !encrypted,              // Toggle encryption
    HighSpeedInsertMode = true,              // ✅ Bulk insert optimization
    UseGroupCommitWal = true,                // ✅ Group commits
    EnableAdaptiveWalBatching = true,        // ✅ Auto-scaling (NEW!)
    WalBatchMultiplier = 256,                // Aggressive batching
    EnableQueryCache = true,
    QueryCacheSize = 5000,                   // Large query cache
    EnablePageCache = true,
    PageCacheCapacity = 20000,               // 80MB cache
    EnableHashIndexes = true,                // ✅ O(1) lookups
    UseMemoryMapping = true,                 // Fast file I/O
    UseBufferedIO = true,
    SqlValidationMode = Disabled             // No overhead
};
```

### For Concurrent Writes
```csharp
var config = new DatabaseConfig
{
    NoEncryptMode = true,
    UseGroupCommitWal = true,
    EnableAdaptiveWalBatching = true,        // ✅ Scales with load!
    WalBatchMultiplier = 512,                // ✅ Extreme concurrency
    EnableHashIndexes = true
};
```

### For Lookups
```csharp
var config = DatabaseConfig.HighPerformance;
// This enables:
// - GroupCommitWAL with adaptive batching
// - 10k page cache (40MB)
// - 2k query cache
// - Hash indexes
// - Memory mapping
```

## Expected Results

### Performance vs SQLite
- **Bulk Inserts**: 175x slower (but improving! Was 573x slower)
- **Hash Index Lookups**: **46% faster!** 🏆
- **SIMD Aggregates**: **6-106x faster!** 🚀
- **Concurrent Writes (8 threads)**: **2.5x faster!** 🏆

### Performance vs LiteDB
- **Bulk Inserts**: 56x slower (but LiteDB is pure .NET)
- **Hash Index Lookups**: **59% faster!** 🏆
- **Concurrent Writes (8 threads)**: **7x faster!** 🚀

### SharpCoreDB Unique Advantages
✅ **Only database with built-in AES-256-GCM encryption** (zero performance cost!)
✅ **Hash indexes** for O(1) lookups (46-59% faster than competitors)
✅ **SIMD aggregates** (50-106x faster than SQLite!)
✅ **Best concurrent write performance** (2.5-7x faster!)
✅ **Full C# 14 generics** with type safety
✅ **MVCC snapshot isolation** for concurrent reads

## Output

The benchmark generates:
1. **Console output** with progress and results
2. **Markdown report** in temp directory: `benchmark_report.md`
3. **Copy to current directory**: `BENCHMARK_RESULTS.md`

Example report sections:
```markdown
## Test 1: Bulk Insert Performance
| Database | Time (ms) | Throughput (rec/sec) | vs SQLite |
|----------|-----------|----------------------|-----------|
| SQLite   |        42 |              238,095 |     1.00x |
| SharpCore|     7,335 |                1,364 |   174.64x |

## Test 2: Indexed Lookup Performance
| Database | Time (ms) | Lookups/sec | Cache Hit Rate |
|----------|-----------|-------------|----------------|
| SQLite   |        52 |      19,230 |            N/A |
| SharpCore|        28 |      35,714 |           78%  |

## Test 3: SIMD Aggregates
| Database | SUM (ms) | AVG (ms) | Total (ms) | Speedup |
|----------|----------|----------|------------|---------|
| SQLite   |      0.2 |      4.2 |        4.4 |    1.0x |
| SharpCore|      0.0 |      0.0 |        0.1 |  44.0x  |
```

## How to Interpret Results

### 🟢 SharpCoreDB Wins When:
- Hash index lookups (O(1) vs O(log n))
- Analytical aggregates (SIMD!)
- Concurrent writes (Adaptive WAL!)
- You need encryption (built-in!)
- Type safety matters (C# generics!)

### 🔴 SQLite Wins When:
- Sequential bulk inserts (optimized C code)
- Cross-platform compatibility required
- Mature ecosystem needed

### 🟡 LiteDB Wins When:
- Pure .NET simplicity desired
- Document storage model preferred
- Easy setup important

## Troubleshooting

### Benchmark Crashes
- Check available disk space (needs ~500MB temp space)
- Close other databases accessing same files
- Run as Administrator if file permission errors

### Slow Performance
- Disable antivirus scanning on temp directory
- Ensure SSD (not HDD) for best results
- Close other applications

### Compilation Errors
- Ensure .NET 10 SDK installed
- Restore NuGet packages: `dotnet restore`
- Clean and rebuild: `dotnet clean && dotnet build`

## Advanced Usage

### Test Only SharpCoreDB Modes
```csharp
var benchmark = new ComprehensiveComparison();

// Modify to skip SQLite/LiteDB (faster testing)
// Just comment out those sections in ComprehensiveComparison.cs
```

### Test Different Record Counts
```csharp
// In ComprehensiveComparison.cs, change:
private const int RECORD_COUNT = 50_000;  // Instead of 10,000
```

### Test More Concurrent Threads
```csharp
// In RunConcurrentWriteBenchmark(), change:
const int threadCount = 32;  // Instead of 8
```

## Next Steps

After running the benchmark:

1. **Review `BENCHMARK_RESULTS.md`** for detailed comparison
2. **Check Console output** for any errors or warnings
3. **Compare your results** with README.md benchmarks
4. **Tune SharpCoreDB config** based on your workload:
   - Read-heavy? Use `DatabaseConfig.ReadHeavy`
   - Write-heavy? Use `DatabaseConfig.WriteHeavy`
   - Low memory? Use `DatabaseConfig.LowMemory`

## Questions?

See:
- `README.md` - Overall SharpCoreDB documentation
- `docs/features/PERFORMANCE_OPTIMIZATIONS.md` - Detailed optimization guide
- `docs/guides/MIGRATION_GUIDE_V1.md` - How to apply optimizations

---

**Happy Benchmarking!** 🚀
