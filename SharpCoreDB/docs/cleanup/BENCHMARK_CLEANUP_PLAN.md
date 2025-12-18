# 🧹 Benchmark Cleanup - Files to Remove

## ✅ KEEP (New Working Benchmarks)
- ✅ `PageBasedStorageBenchmark.cs` - NEW: Before/After optimization validation
- ✅ `StorageEngineComparisonBenchmark.cs` - NEW: Cross-engine comparison
- ✅ `Program.cs` - NEW: Interactive menu
- ✅ `Infrastructure/` folder - Reusable utilities

## ❌ REMOVE (Obsolete/Duplicate Benchmarks)

### Duplicate/Old Entry Points
- ❌ `BenchmarkMenuProgram.cs` - Duplicate of new Program.cs
- ❌ `DebugQuick10kTest.cs` - Debug test, not benchmark
- ❌ `RunStorageEngineComparison.cs` - Old runner, replaced by Program.cs
- ❌ `ComprehensiveBenchmarkRunner.cs` - Old runner
- ❌ `GroupCommitComparisonRunner.cs` - Old runner

### Obsolete Benchmarks (Pre-Optimization)
- ❌ `SimpleBenchmark.cs` - Basic test, no longer relevant
- ❌ `SimpleFairBenchmark.cs` - Replaced by StorageEngineComparisonBenchmark
- ❌ `ComprehensiveComparison.cs` - No [Benchmark] methods (broken)
- ❌ `FairComparisonBenchmark.cs` - No [Benchmark] methods (broken)
- ❌ `RealisticWorkloadBenchmark.cs` - No [Benchmark] methods (broken)
- ❌ `InsertOptimizationBenchmark.cs` - No [Benchmark] methods (broken)

### Specific Feature Benchmarks (Superseded)
- ❌ `OptimizationsBenchmark.cs` - Covered by PageBasedStorageBenchmark
- ❌ `StorageEngineBenchmark.cs` - Old version, replaced
- ❌ `IndexBenchmarks.cs` - Index-specific (not core storage)
- ❌ `HybridStorageBenchmark.cs` - Hybrid engine deprecated
- ❌ `ModernizationBenchmark.cs` - Language modernization complete
- ❌ `UpdatePerformanceBenchmark.cs` - Covered by PageBasedStorageBenchmark
- ❌ `InsertPerformanceBenchmark.cs` - Covered by PageBasedStorageBenchmark
- ❌ `InsertAllocationBenchmark.cs` - Memory-specific (covered)
- ❌ `CryptoBenchmarks.cs` - Crypto-specific (not core)
- ❌ `SqlParsingBenchmarks.cs` - Parser-specific (not storage)
- ❌ `TimeTrackingBenchmarks.cs` - Profiling, not benchmark
- ❌ `PageSerializationBenchmarks.cs` - Serialization-specific
- ❌ `MemoryMappedFilesBenchmark.cs` - Feature-specific

### Quick Comparison Tests (Obsolete)
- ❌ `Simple/SimpleQuick10kComparison.cs` - Replaced by new benchmarks
- ❌ `QuickPerformanceComparison.cs` - Replaced
- ❌ `QuickValidationBench.cs` - Replaced
- ❌ `StorageEngineComparisonTest.cs` - Old test version

### Comparative Folder (Old Structure)
- ❌ `Comparative/Quick10kComparison.cs` - Replaced
- ❌ `Comparative/ComparativeInsertBenchmarks.cs` - Covered
- ❌ `Comparative/ComparativeUpdateDeleteBenchmarks.cs` - Covered
- ❌ `Comparative/ComparativeSelectBenchmarks.cs` - Covered
- ❌ `Comparative/GroupCommitWALBenchmarks.cs` - Feature-specific

### Special Tests
- ❌ `NoEncryptionPerformanceTest.cs` - Config-based now
- ❌ `HighSpeedInsertBenchmarks.cs` - Covered by PageBasedStorageBenchmark

---

## 📊 Summary

| Category | Count |
|----------|-------|
| **KEEP** | 2 benchmarks + Program.cs + Infrastructure |
| **REMOVE** | ~35 obsolete files |

---

## 🎯 Result After Cleanup

```
SharpCoreDB.Benchmarks/
├── PageBasedStorageBenchmark.cs          ✅ NEW
├── StorageEngineComparisonBenchmark.cs   ✅ NEW
├── Program.cs                            ✅ NEW
└── Infrastructure/                       ✅ KEEP
    ├── BenchmarkConfig.cs
    ├── TestDataGenerator.cs
    ├── BenchmarkDatabaseHelper.cs
    ├── StorageMetricsCollector.cs
    ├── BenchmarkResultAggregator.cs
    └── ReadmeUpdater.cs
```

**Total**: ~10 files (down from 45+)

---

## ✅ Benefits

1. ✅ **Cleaner codebase** - Only working benchmarks
2. ✅ **No broken tests** - Removed all files without [Benchmark] attributes
3. ✅ **No duplicates** - Single entry point (Program.cs)
4. ✅ **Modern structure** - Aligns with new benchmark suite
5. ✅ **Faster builds** - 35 fewer files to compile

---

**Next Step**: Execute removal of all files marked ❌
