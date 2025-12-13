# SharpCoreDB Benchmarks

## Running Benchmarks

### Quick Start

```bash
cd SharpCoreDB.Benchmarks
dotnet run -c Release
```

### Run Specific Benchmark Categories

```bash
# Database comparison benchmarks (SQLite, LiteDB, SharpCoreDB)
dotnet run -c Release -- --filter *DatabaseComparison*

# Aggregate benchmarks (SUM, AVG, MIN, MAX with SIMD)
dotnet run -c Release -- --filter *AggregateComparison*

# Sequential insert comparison
dotnet run -c Release -- --filter *SequentialInsert*

# Batch insert comparison
dotnet run -c Release -- --filter *BatchInsert*

# Select benchmarks
dotnet run -c Release -- --filter *Select*

# Update benchmarks
dotnet run -c Release -- --filter *Update*

# Delete benchmarks
dotnet run -c Release -- --filter *Delete*
```

### Generate HTML Reports

```bash
dotnet run -c Release -- --exporters html
```

Reports will be generated in `BenchmarkDotNet.Artifacts/results/`

---

## Benchmark Categories

### 1. Database Comparison Benchmarks

Located in: `Comparison/DatabaseComparisonBenchmark.cs`

**Tests:**
- Sequential Insert (10,000 records)
- Batch Insert (10,000 records)
- Select by ID (1,000 queries)
- Update (1,000 records)
- Delete (1,000 records)

**Compared:**
- SQLite (Microsoft.Data.Sqlite)
- LiteDB
- SharpCoreDB (No Encryption)
- SharpCoreDB (Encrypted)

### 2. Aggregate Comparison Benchmarks

Located in: `Comparison/AggregateComparisonBenchmark.cs`

**Tests:**
- SUM (100,000 rows)
- AVG (100,000 rows)
- MIN (100,000 rows)
- MAX (100,000 rows)
- COUNT (100,000 rows)
- Full Table Scan (100,000 rows)
- Filtered Scan (WHERE clause)

**Compared:**
- SQLite native aggregates
- LiteDB LINQ aggregates
- SharpCoreDB table aggregates
- SharpCoreDB ColumnStore with SIMD

---

## Understanding Results

### Key Metrics

**Mean**: Average execution time (lower is better)
**Error**: Statistical error margin
**StdDev**: Standard deviation
**Rank**: Relative ranking (1 = fastest)
**Gen0/Gen1/Gen2**: GC collections
**Allocated**: Memory allocated

### Example Output

```
| Method                                    | Mean      | Rank | Allocated |
|------------------------------------------ |----------:|-----:|----------:|
| SharpCoreDB_Sum_ColumnStore               |  1.23 ms  |    1 |    0.5 KB |
| SQLite_Sum                                | 12.45 ms  |    2 |    2.1 KB |
| LiteDB_Sum                                | 45.67 ms  |    3 |   15.3 KB |
```

**Interpretation:**
- SharpCoreDB ColumnStore is 10x faster than SQLite
- SharpCoreDB uses less memory (0.5 KB vs 2.1 KB)
- Rank 1 = fastest

---

## Benchmark Fair

ness

### Equal Conditions

1. **Same Hardware**: All benchmarks run on same machine
2. **Same Dataset**: Identical data for all databases
3. **Warm Caches**: Multiple runs to eliminate cold start
4. **Proper Indexing**: Indexes created where appropriate
5. **Release Mode**: All tests in Release configuration

### Configuration Details

**SQLite**:
- WAL mode enabled
- Transaction batching for bulk operations
- B-tree indexes

**LiteDB**:
- Default configuration
- Bulk insert for batch operations
- Automatic indexing

**SharpCoreDB**:
- GroupCommitWAL enabled for batching
- Hash indexes for lookups
- Both encrypted and non-encrypted tested

---

## Expected Results

Based on our comprehensive testing:

### SharpCoreDB Wins

- ✅ **Indexed Lookups**: 46% faster than SQLite (O(1) hash index)
- ✅ **SIMD Aggregates**: 8-10x faster than SQLite (AVX-512)
- ✅ **Analytics Queries**: Dominant with ColumnStore

### SQLite Wins

- ✅ **Sequential Inserts**: 21x faster than SharpCoreDB
- ✅ **Batch Inserts**: 36x faster than SharpCoreDB
- ✅ **Updates**: 3.4x faster than SharpCoreDB
- ✅ **Deletes**: 2.4x faster than SharpCoreDB

### LiteDB Position

- Generally slower than both SQLite and SharpCoreDB
- Good for document-oriented scenarios
- Simpler API than SQL

---

## Interpreting for Your Use Case

### Choose SharpCoreDB if:
- Analytics/BI workloads (frequent aggregates)
- Key-value lookups dominate
- Built-in encryption required
- .NET-native preferred
- SIMD acceleration needed

### Choose SQLite if:
- Write-heavy workloads
- SQL compliance required
- Mature ecosystem needed
- Cross-platform critical

### Choose LiteDB if:
- Document-oriented data
- Simple API preferred
- Small to medium datasets

---

## Troubleshooting Benchmarks

### "OutOfMemoryException"

Reduce dataset size in benchmark constants:
```csharp
private const int RecordCount = 10_000; // Was 100_000
```

### "Benchmark took too long"

Use shorter job:
```bash
dotnet run -c Release -- --job short
```

### "Results vary significantly"

Increase warmup count:
```bash
dotnet run -c Release -- --warmupCount 10
```

### "Permission denied" on cleanup

Close all database connections properly in `[GlobalCleanup]`

---

## Contributing Benchmarks

### Adding New Benchmarks

1. Create class in `Comparison/` folder
2. Inherit base attributes:
   ```csharp
   [MemoryDiagnoser]
   [Orderer(SummaryOrderPolicy.FastestToSlowest)]
   [RankColumn]
   ```
3. Add `[Benchmark]` methods
4. Include all three databases for fair comparison
5. Document expected results

### Benchmark Guidelines

- ✅ Use realistic data sizes
- ✅ Clean up between runs
- ✅ Use same data for all DBs
- ✅ Warm up caches properly
- ✅ Document methodology

---

## Full Documentation

**Complete Analysis**: [📊 Database Comparison](../docs/benchmarks/DATABASE_COMPARISON.md)

Includes:
- Detailed results for all scenarios
- Analysis of why each DB wins/loses
- Performance tuning tips
- Honest recommendations

---

*Benchmarks maintained by MPCoreDeveloper*  
*Last updated: January 2025*
