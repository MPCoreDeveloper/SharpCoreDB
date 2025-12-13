# SharpCoreDB Comparative Benchmark Suite

Comprehensive performance comparison between **SharpCoreDB**, **SQLite**, and **LiteDB**.

## Overview

This benchmark suite provides head-to-head performance comparisons across three popular .NET embedded databases:

- **SharpCoreDB** - High-performance embedded database with SIMD optimization
- **SQLite** - Industry-standard embedded database (memory and file modes)
- **LiteDB** - Popular .NET embedded NoSQL database

## Features

? **Automatic README Updates** - Results automatically inserted into root README.md
? **Multiple Test Scenarios** - INSERT, SELECT, UPDATE, DELETE operations
? **Various Data Sizes** - 1, 10, 100, 1K, 10K, 100K records
? **Memory Diagnostics** - Track allocations and GC pressure
? **Performance Charts** - Auto-generated charts using RPlot
? **Statistical Analysis** - Mean, median, standard deviation

## Running Benchmarks

### Run All Benchmarks

```bash
cd SharpCoreDB.Benchmarks
dotnet run -c Release
```

This will:
1. Run all comparative benchmarks
2. Generate detailed results
3. **Automatically update root README.md** with results
4. Copy charts to `docs/benchmarks/` directory

### Run Specific Category

```bash
# Insert benchmarks only
dotnet run -c Release -- --filter Insert

# Select benchmarks only
dotnet run -c Release -- --filter Select

# Update/Delete benchmarks only
dotnet run -c Release -- --filter Update
```

## Benchmark Categories

### 1. Insert Benchmarks (`ComparativeInsertBenchmarks.cs`)

Tests single and bulk insert performance across different record counts.

**Test Sizes**: 1, 10, 100, 1,000, 10,000 records

**Scenarios**:
- SharpCoreDB bulk insert
- SQLite memory bulk insert (baseline)
- SQLite file bulk insert
- LiteDB bulk insert

**Example Results**:
```
| Method                      | Records | Mean        | Allocated  |
|-----------------------------|---------|-------------|------------|
| SQLite_Memory_BulkInsert    | 1000    | 12.5 ms     | 128 KB     |
| SharpCoreDB_BulkInsert      | 1000    | 15.2 ms     | 64 KB      |
| LiteDB_BulkInsert           | 1000    | 18.3 ms     | 256 KB     |
| SQLite_File_BulkInsert      | 1000    | 22.1 ms     | 128 KB     |
```

### 2. Select Benchmarks (`ComparativeSelectBenchmarks.cs`)

Tests query performance for point queries, range filters, and full scans.

**Database Size**: 10,000 pre-populated records

**Scenarios**:
- Point query by ID
- Range query (age 25-35)
- Full table scan (active users)

**Example Results**:
```
| Method                      | Mean        | Allocated  |
|-----------------------------|-------------|------------|
| SQLite_PointQuery           | 45 ?s       | 256 B      |
| LiteDB_PointQuery           | 52 ?s       | 512 B      |
| SharpCoreDB_PointQuery      | 68 ?s       | 1 KB       |
```

### 3. Update/Delete Benchmarks (`ComparativeUpdateDeleteBenchmarks.cs`)

Tests modification and deletion performance.

**Test Sizes**: 1, 10, 100, 1,000 records

**Scenarios**:
- Bulk updates
- Bulk deletes with repopulation

**Example Results**:
```
| Method                      | Records | Mean        |
|-----------------------------|---------|-------------|
| SQLite_Update               | 100     | 2.5 ms      |
| LiteDB_Update               | 100     | 3.2 ms      |
| SharpCoreDB_Update          | 100     | 4.1 ms      |
```

## Output Structure

After running benchmarks, the following artifacts are generated:

```
SharpCoreDB.Benchmarks/
??? BenchmarkDotNet.Artifacts/
?   ??? results/
?   ?   ??? *.html          # HTML reports
?   ?   ??? *.csv           # CSV data
?   ?   ??? *.json          # JSON data
?   ?   ??? *.png           # Performance charts
?   ??? logs/
?       ??? *.log           # Execution logs
??? README.md               # This file

SharpCoreDB/ (root)
??? README.md               # ? AUTOMATICALLY UPDATED with results
??? docs/
    ??? benchmarks/
        ??? *.png           # Copied charts
```

## Automatic README Updates

The benchmark suite automatically updates the root `README.md` with results.

### How It Works

1. **Run benchmarks**: All scenarios execute with statistical analysis
2. **Collect results**: BenchmarkResultAggregator gathers all summaries
3. **Generate markdown**: Results formatted as markdown tables
4. **Update README**: Section between `<!-- BENCHMARK_RESULTS -->` markers is replaced
5. **Copy charts**: Performance charts copied to `docs/benchmarks/`

### README Sections Generated

```markdown
<!-- BENCHMARK_RESULTS -->
## Benchmark Results (Auto-Generated)

**Generated**: 2024-12-15 10:30:45 UTC

### Executive Summary

| Operation | Winner | Performance Advantage |
|-----------|--------|----------------------|
| Bulk Insert (1K) | **SQLite** | 1.22x faster |
| Point Query | **SQLite** | 1.15x faster |
| Range Query | **LiteDB** | 1.08x faster |
| ...

### Detailed Results
...
<!-- /BENCHMARK_RESULTS -->
```

### Manual Section Markers

To control where results appear in your README, add these markers:

```markdown
# Your Project

## Some sections...

<!-- BENCHMARK_RESULTS -->
<!-- Results will be inserted here automatically -->
<!-- /BENCHMARK_RESULTS -->

## More sections...
```

If markers don't exist, results are appended to the end of README.

## Configuration

### Benchmark Settings

Edit `Infrastructure/BenchmarkConfig.cs`:

```csharp
AddJob(Job.Default
    .WithWarmupCount(3)      // Warmup iterations
    .WithIterationCount(10)  // Measurement iterations
    .WithGcServer(true)      // Server GC mode
    .WithGcForce(true));     // Force GC between runs
```

### Test Data

Edit `Infrastructure/TestDataGenerator.cs`:

```csharp
public class UserRecord
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Email { get; set; }
    public int Age { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsActive { get; set; }
}
```

## Interpreting Results

### Metrics Explained

- **Mean**: Average execution time across all iterations
- **Error**: Standard error of the mean
- **StdDev**: Standard deviation (consistency of results)
- **Allocated**: Memory allocated per operation
- **Rank**: Performance ranking (1 = fastest)

### Performance Tips

**SharpCoreDB Strengths**:
- Low memory allocations (SIMD optimizations)
- Fast bulk operations (pooled buffers)
- Efficient page serialization

**SQLite Strengths**:
- Mature query optimizer
- Excellent B-tree performance
- Fast point queries with indexes

**LiteDB Strengths**:
- Simple API
- Good for document-style data
- Fast bulk operations

## Adding New Benchmarks

1. **Create benchmark class**:

```csharp
[Config(typeof(BenchmarkConfig))]
[MemoryDiagnoser]
public class MyNewBenchmarks
{
    [Benchmark]
    public void MyTest()
    {
        // Your benchmark code
    }
}
```

2. **Add to Program.cs**:

```csharp
var summary = BenchmarkRunner.Run<MyNewBenchmarks>(config);
aggregator.AddSummary(summary);
```

3. **Run benchmarks**:

```bash
dotnet run -c Release
```

## CI/CD Integration

### GitHub Actions Example

```yaml
name: Benchmarks

on:
  push:
    branches: [main]
  schedule:
    - cron: '0 0 * * 0'  # Weekly

jobs:
  benchmark:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v3
      
      - name: Setup .NET
        uses: actions/setup-dotnet@v3
        with:
          dotnet-version: '10.0.x'
      
      - name: Run Benchmarks
        run: |
          cd SharpCoreDB.Benchmarks
          dotnet run -c Release
      
      - name: Commit README
        run: |
          git config --local user.email "action@github.com"
          git config --local user.name "GitHub Action"
          git add README.md docs/benchmarks/
          git diff --quiet && git diff --staged --quiet || \
            git commit -m "Update benchmark results [skip ci]"
          git push
```

## Troubleshooting

### Benchmarks Take Too Long

Reduce test sizes in benchmark files:

```csharp
[Params(1, 10, 100)]  // Instead of 1, 10, 100, 1000, 10000
public int RecordCount { get; set; }
```

### README Not Updated

Check:
1. README.md exists in root directory
2. Benchmark completed successfully
3. No file permission errors in console output

### Charts Not Generated

Ensure RPlotExporter is working:
- Install R (optional, but recommended)
- Charts still generated as basic plots without R

## Performance Baselines

As of December 2025 (.NET 10):

| Database | Insert (1K) | Query (10K) | Update (1K) | Memory |
|----------|-------------|-------------|-------------|--------|
| SQLite   | ~12ms       | ~45?s       | ~2.5ms      | 128KB  |
| SharpCoreDB | ~15ms    | ~68?s       | ~4.1ms      | 64KB   |
| LiteDB   | ~18ms       | ~52?s       | ~3.2ms      | 256KB  |

*Results may vary based on hardware and configuration*

## Contributing

To add new benchmark scenarios:

1. Create benchmark class in `Comparative/` directory
2. Follow existing patterns (setup, benchmark methods, cleanup)
3. Add to `Program.cs` runner
4. Update this README with description
5. Run benchmarks to verify
6. Submit PR with results

## License

Same as SharpCoreDB project.

---

**Note**: Always run benchmarks on the same hardware for consistent comparisons. 
Results shown here are examples and will vary based on your environment.
