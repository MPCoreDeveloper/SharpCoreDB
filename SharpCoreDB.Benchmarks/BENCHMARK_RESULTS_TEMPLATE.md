# ?? SharpCoreDB Performance Benchmarks

## Fair Comparison: 10,000 Inserts

This benchmark provides a fair comparison between SharpCoreDB, SQLite, and LiteDB for bulk insert operations.

### Test Configuration

**SQLite:**
- `PRAGMA journal_mode=WAL` - Write-Ahead Logging for better concurrency
- `PRAGMA synchronous=NORMAL` - Balanced durability (not FULL which is significantly slower)
- `PRAGMA page_size=4096` - Match SharpCoreDB page size
- Single transaction wrapping all inserts for optimal batch performance

**LiteDB:**
- Default configuration with file stream
- `InsertBulk()` method for batch operations
- No special tuning applied

**SharpCoreDB:**
Three configurations tested to show performance characteristics:
1. **Default (Encrypted + Columnar)**: Production-ready with AES encryption and columnar storage
2. **HighSpeedInsert**: Optimized mode for bulk insert scenarios (`HighSpeedInsertMode = true`)
3. **No Encryption**: Encryption temporarily disabled (`NoEncryptMode = true`) for fair comparison

All three use:
- GroupCommit WAL with 4MB buffer
- Page cache (1024 pages)
- Memory-mapped files
- True batch insert API (`InsertUsersTrueBatch`)

### Threading Tests

- **Single-threaded**: Sequential inserts (baseline)
- **8 threads**: Parallel inserts with 8 worker threads (typical multi-core)
- **16 threads**: Parallel inserts with 16 worker threads (high-end multi-core)

### Results Template

| Configuration | Threads | SharpCoreDB (ms) | SQLite (ms) | LiteDB (ms) | Winner | Inserts/sec |
|---------------|---------|------------------|-------------|-------------|--------|-------------|
| Default (Encrypted + Columnar) | 1 | — | — | — | — | — |
| HighSpeedInsert | 1 | — | — | — | — | — |
| No Encryption | 1 | — | — | — | — | — |
| SQLite WAL | 1 | — | — | — | — | — |
| LiteDB Default | 1 | — | — | — | — | — |
| | | | | | | |
| Default (Encrypted + Columnar) | 8 | — | — | — | — | — |
| HighSpeedInsert | 8 | — | — | — | — | — |
| No Encryption | 8 | — | — | — | — | — |
| SQLite WAL | 8 | — | — | — | — | — |
| LiteDB Default | 8 | — | — | — | — | — |
| | | | | | | |
| Default (Encrypted + Columnar) | 16 | — | — | — | — | — |
| HighSpeedInsert | 16 | — | — | — | — | — |
| No Encryption | 16 | — | — | — | — | — |
| SQLite WAL | 16 | — | — | — | — | — |
| LiteDB Default | 16 | — | — | — | — | — |

### ?? Database File Sizes

Storage efficiency comparison:

| Database | Configuration | Threads | DB Size | WAL Size | Total |
|----------|---------------|---------|---------|----------|-------|
| SharpCoreDB | Default | 1 | — | — | — |
| SharpCoreDB | HighSpeedInsert | 1 | — | — | — |
| SharpCoreDB | No Encryption | 1 | — | — | — |
| SQLite | WAL + Normal | 1 | — | — | — |
| LiteDB | Default | 1 | — | — | — |

*Note: File sizes shown after completing all inserts. WAL sizes indicate write overhead.*

### Key Metrics Explained

**Time (ms)**: Total time to insert 10,000 records
**Inserts/sec**: Throughput (records per second)
**DB Size**: Main database file size
**WAL Size**: Write-Ahead Log file size (SharpCoreDB and SQLite only)
**Winner**: ?? indicates fastest for that thread configuration

### Analysis Notes

**Expected Results:**

1. **Single-threaded**:
   - SQLite typically fastest due to mature optimization
   - SharpCoreDB No Encryption competitive
   - SharpCoreDB Default slower due to AES encryption overhead

2. **Multi-threaded (8/16 threads)**:
   - SharpCoreDB HighSpeedInsert should excel (designed for concurrency)
   - SQLite may serialize writes despite WAL mode
   - LiteDB performance varies with thread count

3. **File Sizes**:
   - SharpCoreDB columnar storage typically smallest
   - SQLite row-based storage larger
   - LiteDB BSON format typically largest

4. **WAL Growth**:
   - Indicates write pattern efficiency
   - Lower WAL size = better batch optimization
   - SharpCoreDB GroupCommit should minimize WAL overhead

### Running the Benchmark

```bash
dotnet run --project SharpCoreDB.Benchmarks -c Release
```

Or run the `FairComparisonRunner` directly:

```bash
dotnet run --project SharpCoreDB.Benchmarks -c Release -- FairComparison
```

### Interpreting Results

**What to look for:**

? **Winner per thread count**: Shows which database excels at different concurrency levels
? **Inserts/sec scaling**: How performance improves with more threads
? **File size efficiency**: Storage overhead for 10k records
? **WAL growth pattern**: Write optimization effectiveness

**Fair comparison considerations:**

- All databases use batch/transaction mode (no individual commits)
- SQLite configured with WAL + NORMAL sync (production-typical settings)
- SharpCoreDB tested in three modes to show encryption/optimization tradeoffs
- Same test data generated for all databases
- File sizes measured after test completion

### Hardware Notes

Results will vary based on:
- CPU cores and speed (affects multi-threaded performance)
- Disk type (SSD vs HDD, NVMe vs SATA)
- RAM availability (affects caching)
- Operating system (file I/O characteristics differ)

Benchmark your specific hardware to get accurate numbers for your use case.

---

**Legend:**
- ?? = Fastest for this configuration
- — = Not applicable / not tested
- ms = milliseconds
- Inserts/sec = Records inserted per second

