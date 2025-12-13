# ?? BENCHMARK QUICK REFERENCE CARD

## ? Run Benchmarks (Choose One)

```powershell
# Option 1: Interactive GUI (easiest)
.\RunBenchmarks.ps1

# Option 2: Quick comparison (5-10 min, recommended)
dotnet run -c Release -- --quick

# Option 3: Full suite (20-30 min)
dotnet run -c Release -- --full
```

## ?? View Results

```powershell
# Open HTML reports (after benchmark completes)
start BenchmarkDotNet.Artifacts\results\*.html

# Or navigate to results folder
explorer BenchmarkDotNet.Artifacts\results
```

## ?? Key Metrics to Check

| Metric | Target | What It Means |
|--------|--------|---------------|
| **Encryption Overhead** | < 15% | Cost of encryption (should be minimal) |
| **Memory (1K ops)** | < 100KB | SharpCoreDB should be 2-4x better |
| **Insert (1K batch)** | < 20ms | Batch should be 10-50x faster than individual |
| **Point Query** | < 100?s | Query response time |

## ? Expected Results

```
Encryption overhead:     ~10% ? EXCELLENT
Memory efficiency:       2-4x better ? BEST
Insert performance:      Within 1.5x of SQLite ? GOOD
Query performance:       < 100?s ? EXCELLENT
Batch speedup:          10-50x faster ? CRITICAL
```

## ?? Quick Decisions

### Use Encryption?
- Overhead < 15% ? **YES** ? (typical: ~10%)
- Overhead > 25% ? Investigate ??

### Choose SharpCoreDB?
- ? Memory critical (2-4x better!)
- ? Need encryption (minimal overhead)
- ? Batch operations (excellent performance)
- ? .NET native (no interop overhead)

### Must-Do Optimizations
1. **Always use batch operations** (50x faster!)
2. **Enable Group Commit WAL** (already enabled)
3. **Use encryption** (only ~10% cost)

## ?? File Locations

```
Results:     BenchmarkDotNet.Artifacts\results\*.html
CSV Data:    BenchmarkDotNet.Artifacts\results\*.csv
Summary:     BenchmarkDotNet.Artifacts\results\BenchmarkResults_*.txt
```

## ?? Troubleshooting

| Problem | Solution |
|---------|----------|
| Too slow | Use `--quick` mode |
| Memory error | Run one category at a time |
| No results | Check `BenchmarkDotNet.Artifacts\results\` |
| Inconsistent | Close other apps, use Release mode |

## ?? Documentation

- **Quick Start**: `RUN_AND_EVALUATE.md`
- **Detailed**: `COMPREHENSIVE_BENCHMARK_GUIDE.md`
- **Reference**: `README_BENCHMARKS.md`

## ? TL;DR

```powershell
cd SharpCoreDB.Benchmarks
dotnet run -c Release -- --quick
# Wait 5-10 minutes
start BenchmarkDotNet.Artifacts\results\*.html
```

**Expected:** ~10% encryption overhead, 2-4x better memory, competitive performance

---

**Print this card and keep it handy!** ??
