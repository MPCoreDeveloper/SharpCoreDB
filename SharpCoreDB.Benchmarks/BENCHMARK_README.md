# SharpCoreDB Storage Engine Benchmarks

## Quick Start

### Option 1: Interactive Scripts (Easiest)

**Windows PowerShell:**
```powershell
.\run-storage-benchmark.ps1
```

**Windows Batch:**
```cmd
run-storage-benchmark.bat
```

### Option 2: Direct Commands

**Fast XUnit Test (Recommended):**
```bash
cd ..\SharpCoreDB.Tests
dotnet test --filter "StorageEngineComparisonTest"
```

**Detailed BenchmarkDotNet:**
```bash
cd SharpCoreDB.Benchmarks
dotnet run -c Release
```

## What Gets Tested

### Storage Modes
1. **AppendOnly** - Pure append-only writes
2. **PageBased** - 8KB pages with in-place updates
3. **Hybrid** - WAL + page-based compaction

### Operations Benchmarked
- ? 10,000 single inserts
- ? Bulk insert (10k batch)
- ? 10,000 updates
- ? 10,000 random reads
- ? Full table scan
- ? VACUUM operation (Hybrid only)

### Metrics Collected
- **Performance**: Insert/Update/Read times (microseconds)
- **File Sizes**: Data, WAL, Pages (with overhead %)
- **Comparison**: Winner per operation
- **Recommendations**: Best mode per workload type

## Output

### XUnit Test Output
- Console results with metrics
- `STORAGE_ENGINE_COMPARISON.md` - Full report with recommendations

### BenchmarkDotNet Output
- `BenchmarkDotNet.Artifacts/results/` directory:
  - HTML reports with charts
  - CSV exports (Excel compatible)
  - JSON data files
  - Markdown tables

## Expected Results

### Performance (Typical)
| Operation | AppendOnly | PageBased | Hybrid | Winner |
|-----------|------------|-----------|--------|--------|
| Inserts | ~15 ?s | ~25 ?s | ~16 ?s | Hybrid ? |
| Updates | ~15 ?s | ~8 ?s | ~12 ?s | PageBased ? |
| Reads | ~5 ?s | ~3 ?s | ~4 ?s | PageBased ? |

### File Size (Typical - 10k records × 100 bytes)
| Mode | Data File | WAL | Total | Overhead |
|------|-----------|-----|-------|----------|
| AppendOnly | 1.2 MB | - | 1.2 MB | +20% |
| PageBased | 1.1 MB | - | 1.1 MB | +10% |
| Hybrid | 0.5 MB | 0.6 MB | 1.1 MB | +10% |

## Troubleshooting

### "Filter not working"
? **Don't use:** `dotnet run -c Release -- --filter "*Storage*"`
? **Use instead:** Scripts above or direct `dotnet test`

### "Benchmark takes too long"
- Use XUnit test instead of BenchmarkDotNet
- XUnit: ~30 seconds
- BenchmarkDotNet: ~15 minutes (full statistical analysis)

### "No output file"
- XUnit generates: `SharpCoreDB.Tests\STORAGE_ENGINE_COMPARISON.md`
- BenchmarkDotNet generates: `BenchmarkDotNet.Artifacts\results\`

## Configuration

To test different configurations, edit `StorageEngineComparisonTest.cs`:

```csharp
// Change record count
private const int RecordCount = 10_000; // Try: 1_000, 50_000, 100_000

// Change record size
private const int RecordSize = 100; // Try: 50, 200, 500
```

## Advanced Usage

### Run specific benchmark methods
```bash
dotnet run -c Release --filter "*Insert*"  # Only insert benchmarks
dotnet run -c Release --filter "*Update*"  # Only update benchmarks
```

### Export formats
```bash
dotnet run -c Release -- --exporters html,csv,json
```

### Memory diagnostics
```bash
dotnet run -c Release -- --memory
```

## Related Documentation

- [STORAGE_ENGINE_COMPARISON.md](../../STORAGE_ENGINE_COMPARISON.md) - Full usage guide
- [HYBRID_IMPLEMENTATION_SUMMARY.md](../../HYBRID_IMPLEMENTATION_SUMMARY.md) - Implementation details

## Support

For issues or questions:
- Check console output for error messages
- Review generated reports
- See main documentation in repository root
