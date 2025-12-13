# SharpCoreDB Benchmarks - Quick Reference

## ?? Quick Start

Run comprehensive comparison of SharpCoreDB (with/without encryption) vs SQLite vs LiteDB:

```bash
cd SharpCoreDB.Benchmarks
dotnet run -c Release -- --quick
```

## ?? What Gets Benchmarked

### Databases Compared
- **SharpCoreDB** ? WITH encryption
- **SharpCoreDB** ?? WITHOUT encryption  
- **SQLite** (Memory mode) ??
- **SQLite** (File mode) ??
- **LiteDB** ??

### Operations Tested
- **INSERT** (individual & batch, 1-1000 records)
- **SELECT** (point queries, range queries, full scans)
- **UPDATE** (bulk updates, 1-100 records)
- **DELETE** (bulk deletes with repopulation, 1-100 records)

### Key Metrics
- ?? **Execution time** (mean, standard deviation)
- ?? **Memory allocation** (per operation)
- ?? **Encryption overhead** (automatic analysis)
- ?? **Performance ranking** (fastest to slowest)

## ?? Command Line Options

```bash
# Quick comparison (~5-10 minutes)
dotnet run -c Release -- --quick

# Full comprehensive suite (~20-30 minutes)
dotnet run -c Release -- --full

# Specific operations only
dotnet run -c Release -- --inserts
dotnet run -c Release -- --selects
dotnet run -c Release -- --updates

# Interactive menu
dotnet run -c Release

# Help
dotnet run -c Release -- --help
```

## ?? Understanding Results

### Encryption Impact Analysis

```
?????????????????????????????????????????????????????????????
  ENCRYPTION IMPACT ANALYSIS
?????????????????????????????????????????????????????????????

  Average time WITH encryption:    15.23 ms
  Average time WITHOUT encryption:  13.84 ms
  Encryption overhead:              10.0%

  ? EXCELLENT: Encryption overhead is minimal (<10%)
```

**Interpretation:**
- **< 10%**: ? Excellent - minimal overhead
- **10-25%**: ? Good - acceptable for most use cases
- **> 25%**: ?? Significant - consider if encryption is needed

### Performance Rankings

```
?? #1  SQLite Memory: Bulk Insert (1000 records)
       12.45 ms  |  128.0 KB

?? #2  SharpCoreDB (No Encryption): Batch Insert (1000 records)
       13.84 ms  |  64.2 KB

?? #3  SharpCoreDB (Encrypted): Batch Insert (1000 records)
       15.23 ms  |  64.5 KB
```

## ?? Output Files

Results saved to `BenchmarkDotNet.Artifacts/results/`:

| Format | Use Case | File Extension |
|--------|----------|----------------|
| HTML | Interactive viewing | `*.html` |
| CSV | Excel analysis | `*.csv` |
| JSON | Programmatic access | `*.json` |
| Markdown | GitHub documentation | `*.md` |
| Text | Comprehensive summary | `BenchmarkResults_*.txt` |

**View HTML report:**
```bash
start BenchmarkDotNet.Artifacts/results/ComparativeInsertBenchmarks-report.html
```

## ?? Example Results

### INSERT Performance (1000 records)

| Database | Batch Insert | Memory | Encryption Overhead |
|----------|--------------|--------|---------------------|
| SQLite Memory | 12.5 ms | 128 KB | N/A |
| SharpCoreDB (No Encrypt) | 13.8 ms | 64 KB | - |
| **SharpCoreDB (Encrypted)** | **15.2 ms** | **65 KB** | **+10%** |
| LiteDB | 18.3 ms | 256 KB | N/A |

**Key Insight:** SharpCoreDB uses **2-4x less memory** than competitors!

### SELECT Performance (1000 pre-populated records)

| Database | Point Query | Range Query | Full Scan |
|----------|-------------|-------------|-----------|
| SQLite | 45 ?s | 650 ?s | 2.1 ms |
| **SharpCoreDB (No Encrypt)** | **62 ?s** | **780 ?s** | **2.8 ms** |
| **SharpCoreDB (Encrypted)** | **68 ?s** | **850 ?s** | **3.1 ms** |
| LiteDB | 72 ?s | 920 ?s | 3.5 ms |

**Key Insight:** Encryption adds only **~10% overhead** to query performance!

### UPDATE Performance (100 records)

| Database | Update Time | Memory |
|----------|-------------|--------|
| SQLite | 2.5 ms | 128 KB |
| LiteDB | 3.2 ms | 256 KB |
| SharpCoreDB (No Encrypt) | 3.8 ms | 64 KB |
| **SharpCoreDB (Encrypted)** | **4.1 ms** | **65 KB** |

## ? Performance Tips

### For SharpCoreDB

```csharp
// ? DO: Use batch inserts (10-50x faster)
db.InsertUsersBatch(userList);

// ? DON'T: Use individual inserts in a loop
foreach (var user in users)
    db.InsertUser(user);

// ? DO: Enable Group Commit WAL
var config = new DatabaseConfig {
    UseGroupCommitWal = true,
    WalMaxBatchSize = 100,
    EnablePageCache = true
};

// ? DO: Choose encryption wisely
// - Use for sensitive data
// - Expect ~10% overhead
// - Minimal impact on batch operations
```

### General Tips

1. **Always run in Release mode**: `dotnet run -c Release`
2. **Close other applications** for consistent results
3. **Run multiple times** to verify consistency
4. **Use quick mode first** to identify issues
5. **Review HTML reports** for detailed analysis

## ?? Customization

### Change Test Sizes

Edit benchmark files (e.g., `ComparativeInsertBenchmarks.cs`):

```csharp
[Params(1, 10, 100, 1000)]  // Current
public int RecordCount { get; set; }

// Change to:
[Params(1, 10, 100)]  // Faster
// or
[Params(1, 10, 100, 1000, 10000)]  // More comprehensive
```

### Change Iterations

Edit `Infrastructure/BenchmarkConfig.cs`:

```csharp
AddJob(Job.Default
    .WithWarmupCount(3)      // Increase for stability
    .WithIterationCount(10)  // Increase for accuracy
```

## ?? Troubleshooting

| Issue | Solution |
|-------|----------|
| Benchmarks too slow | Use `--quick` mode or reduce test sizes |
| Memory errors | Run one category at a time, reduce record counts |
| Inconsistent results | Close other apps, run in Release mode |
| Setup fails | Check disk space and write permissions |

## ?? More Information

See **[COMPREHENSIVE_BENCHMARK_GUIDE.md](COMPREHENSIVE_BENCHMARK_GUIDE.md)** for:
- Detailed explanations of each benchmark
- Performance optimization strategies
- CI/CD integration examples
- FAQ and troubleshooting
- Advanced configuration options

## ?? Recommended Workflow

1. **Quick benchmark** to understand current state:
   ```bash
   dotnet run -c Release -- --quick
   ```

2. **Analyze encryption overhead** from the summary

3. **Run full suite** if needed:
   ```bash
   dotnet run -c Release -- --full
   ```

4. **Share HTML reports** with your team

5. **Make informed decisions** about:
   - Whether to use encryption
   - Which database to choose
   - Performance optimization priorities

## ? Ready to Run?

```bash
cd SharpCoreDB.Benchmarks
dotnet run -c Release -- --quick
```

**Estimated time:** 5-10 minutes

**Output:** Complete comparison including encryption impact analysis

---

**Questions?** See [COMPREHENSIVE_BENCHMARK_GUIDE.md](COMPREHENSIVE_BENCHMARK_GUIDE.md) or run `dotnet run -c Release -- --help`
