# SharpCoreDB SCDB - Production Deployment Guide

**Version:** 2.1  
**Last Updated:** 2026-10-11  
**Target:** Production environments

---

> Every option, method and number in this guide was checked against the shipped source. Quoted figures
> come from measured runs - see [`../BENCHMARK_RESULTS.md`](../BENCHMARK_RESULTS.md) and
> [`../performance/WORKLOG.md`](../performance/WORKLOG.md) - never from an estimate. The option surface
> is [`DatabaseOptions`](../../src/SharpCoreDB/DatabaseOptions.cs) and
> [`DatabaseConfig`](../../src/SharpCoreDB/DatabaseConfig.cs); if a setting is not there, it does not exist.

## 🎯 Overview

This guide covers deploying SharpCoreDB with the **SCDB (SharpCore DataBase)** single-file storage format in production environments.

**SCDB Benefits:**
- ✅ Single `.scdb` file (easy backup/restore) - one file, no sidecar WAL to keep in step
- ✅ WAL for ACID transactions - the WAL lives inside the `.scdb` file
- ✅ Crash recovery - replay on open, pinned by `CrashRecoveryTests`
- ✅ Corruption detection & repair - `CorruptionDetector` / `RepairTool`
- ✅ In-place writes without a per-row index rewrite
- ✅ Ratios against SQLite are measured per release and published with their method - see
  [`../BENCHMARK_RESULTS.md`](../BENCHMARK_RESULTS.md); only the `--pk` arm is like-for-like

---

## 📋 Pre-Deployment Checklist

### System Requirements

| Component | Minimum | Recommended |
|-----------|---------|-------------|
| **.NET Runtime** | .NET 11 | .NET 11+ |
| **RAM** | 512 MB | 2 GB+ |
| **Disk** | SSD (100 MB/s) | NVMe (500 MB/s) |
| **CPU** | 2 cores | 4+ cores |
| **OS** | Windows/Linux/macOS | Any |

### Software Dependencies

```bash
# .NET 11 Runtime (required by the 2.1 line on master)
dotnet --version  # Should show 11.0 or higher

# NuGet Package
dotnet add package SharpCoreDB --version 2.1.0
```

The 2.0.x servicing line on `release/v2.0` targets .NET 10 instead; match the runtime to the package
line you deploy.

---

## 🚀 Quick Start

### 1. Basic Setup

```csharp
using SharpCoreDB;
using Microsoft.Extensions.DependencyInjection;

// Configure services
var services = new ServiceCollection();
services.AddSingleton<ICryptoService, CryptoService>();
var provider = services.BuildServiceProvider();

// The .scdb extension selects single-file storage (DatabaseExtensions.DetectStorageMode), so the
// path is the only switch you need. The master password drives AES-256-GCM encryption.
// CreateSingleFileDefault() sets StorageMode, memory mapping and quick auto-VACUUM; DatabaseConfig
// is optional and stays null here, so the engine's own defaults apply.
var options = DatabaseOptions.CreateSingleFileDefault();

using var db = new Database(
    provider,
    "./data/myapp.scdb",  // ✅ Single file
    "your-master-password",
    isReadOnly: false,
    config: options.DatabaseConfig
);

// Use database
db.ExecuteSQL("CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT)");
db.ExecuteSQL("INSERT INTO users VALUES (1, 'Alice')");
```

The same constructor serves both layouts: a path ending in `.scdb` (or an existing file) opens through
`SingleFileStorageProvider`, a directory path opens through `DirectoryStorageProvider`. See
[`README.md`](./README.md) for the file layout.

---

## ⚙️ Configuration

### Production-Ready Configuration

```csharp
// Storage-level options live on DatabaseOptions ...
var options = new DatabaseOptions
{
    // ========== SCDB storage ==========
    StorageMode = StorageMode.SingleFile,
    PageSize = 4096,                    // 4KB pages (optimal for most workloads)
    CreateImmediately = true,
    FsmSizePages = 4,                   // free-space map pages
    BlockRegistrySizePages = 4,
    TableDirectorySizePages = 4,
    WalBufferSizePages = 1024,          // in-file WAL buffer, in pages

    // ========== Maintenance ==========
    AutoVacuum = true,                  // VACUUM when fragmentation crosses the threshold
    AutoVacuumMode = VacuumMode.Quick,
    FragmentationThreshold = 25,        // percent

    // ========== File behaviour ==========
    FileShareMode = FileShare.ReadWrite, // one Database instance per file, per process
    UseUnbufferedIO = false,             // true bypasses the OS page cache
    EnableMemoryMapping = true,
};

// ... and the durability, cache and write-path knobs live on DatabaseConfig.
var config = new DatabaseConfig
{
    // ========== Durability ==========
    WalDurabilityMode = DurabilityMode.FullSync,  // fsync on commit (the default)
    WalBufferSize = 4 * 1024 * 1024,
    EnableAdaptiveWalBatching = true,

    // ========== Caches ==========
    EnablePageCache = true,
    PageCacheCapacity = 1024,           // pages (4 MB at 4 KB)
    EnableQueryCache = true,
    QueryCacheSize = 1000,
    BufferPoolSize = 32 * 1024 * 1024,

    // ========== Write path ==========
    UseGroupCommitWal = false,
    EnableDeltaUpdates = false,
    WorkloadHint = WorkloadHint.General,
};

using var db = new Database(provider, "./data/myapp.scdb", "your-master-password", config: config);
```

### Configuration by Workload

#### High-Write Workload (OLTP)
```csharp
var config = new DatabaseConfig
{
    EnableDeltaUpdates = true,          // in-place deltas instead of row rewrites
    UseGroupCommitWal = true,           // batch commits
    GroupCommitSize = 1000,
    EnableAdaptiveWalBatching = true,
    PageCacheCapacity = 2048,           // 8 MB cache
    WorkloadHint = WorkloadHint.WriteHeavy,
};
```

#### High-Read Workload (Analytics)
```csharp
var config = new DatabaseConfig
{
    PageCacheCapacity = 4096,           // 32 MB cache
    EnableQueryCache = true,
    QueryCacheSize = 5000,              // large query cache
    UseMemoryMapping = true,
    WorkloadHint = WorkloadHint.Analytics,
};
```

#### Balanced Workload
```csharp
var config = new DatabaseConfig
{
    PageCacheCapacity = 1024,           // 4 MB cache
    EnableAdaptiveWalBatching = true,
    WalDurabilityMode = DurabilityMode.FullSync,
    WorkloadHint = WorkloadHint.General,
};
```

---

## 🛡️ High Availability

### Backup Strategy

#### Daily Backups
```csharp
// There is no separate WAL file to copy - the .scdb file is the whole database. Flush so the in-file
// WAL is applied, then copy the file.
db.Flush();
File.Copy("./data/myapp.scdb", $"./backups/myapp_{DateTime.UtcNow:yyyyMMdd}.scdb");
```

#### Consistent copy while the database is open
```csharp
// Flush(), then copy. The provider has no BackupAsync() - a page that shows one is out of date.
// Reopening the copy is the only real verification that the backup is good:
db.Flush();
File.Copy("./data/myapp.scdb", "./backups/myapp_hot.scdb", overwrite: true);

using var restored = new Database(provider, "./backups/myapp_hot.scdb", "your-master-password");
restored.ExecuteSQL("SELECT COUNT(*) FROM users");
```

#### Incremental Backups
```csharp
// The WAL lives inside the .scdb file, so there is no sidecar *.wal to ship separately. Keep a
// rotation of full copies (the file is one unit) and shrink it with VACUUM:
var result = await db.VacuumAsync(VacuumMode.Incremental);
Console.WriteLine($"{result.FileSizeBefore:N0} -> {result.FileSizeAfter:N0} bytes, reclaimed {result.BytesReclaimed:N0}");
```

### Backup Retention Policy

```
Daily backups:    Keep 7 days
Weekly backups:   Keep 4 weeks
Monthly backups:  Keep 12 months
```

### Disaster Recovery

```csharp
// `storage` is the SingleFileStorageProvider the database was opened with - CorruptionDetector and
// RepairTool take that provider, so corruption tooling applies to .scdb databases.
// 1. Detect corruption
using var detector = new CorruptionDetector(storage, ValidationMode.Standard);
var report = await detector.ValidateAsync();

if (report.IsCorrupted)
{
    // 2. Attempt repair
    using var repairTool = new RepairTool(report, storage);
    var result = await repairTool.RepairAsync(new RepairOptions
    {
        CreateBackup = true,              // ✅ Always backup first (result.BackupPath records where)
        AllowDataLoss = false,            // conservative repair
        Aggressiveness = RepairAggressiveness.Conservative,
    });
    
    if (!result.Success)
    {
        // 3. Restore from backup
        File.Copy("./backups/latest.scdb", "./data/myapp.scdb", overwrite: true);
    }
}
```

`RepairResult` reports `IssuesRepaired`, `IssuesRemaining`, `BackupPath`, `RepairTime` and `ErrorMessage`;
a conservative repair refuses issues the report marked unrepairable rather than guessing.

---

## 📊 Monitoring

### Health Checks

```csharp
public class DatabaseHealthCheck : IHealthCheck
{
    private readonly SingleFileStorageProvider _storage;

    public DatabaseHealthCheck(SingleFileStorageProvider storage) => _storage = storage;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Quick validation (header + WAL, no block scans)
            using var detector = new CorruptionDetector(_storage, ValidationMode.Quick);
            var report = await detector.ValidateAsync(cancellationToken);

            if (report.IsCorrupted)
            {
                return HealthCheckResult.Unhealthy($"Corruption detected: {report.Summary}");
            }

            return HealthCheckResult.Healthy("Database is healthy");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy($"Health check failed: {ex.Message}");
        }
    }
}
```

`CorruptionDetector` takes the **single-file provider** (`SingleFileStorageProvider`), not a DI service
provider - keep the instance the database was opened with and reuse it here. Validation modes: `Quick`
(header and WAL), `Standard` (registry and block checksums), `Deep` (full byte scan), `Paranoid`
(everything, re-verified).

### Performance Metrics

```csharp
// Storage statistics (single-file mode)
StorageStatistics stats = db.GetStorageStatistics();

Console.WriteLine($"Total Size: {stats.TotalSize:N0} bytes");
Console.WriteLine($"Used: {stats.UsedSpace:N0} / Free: {stats.FreeSpace:N0} bytes");
Console.WriteLine($"Pages: {stats.PageCount}, free pages: {stats.FreePages}");
Console.WriteLine($"Blocks: {stats.BlockCount} ({stats.DirtyBlocks} dirty)");
Console.WriteLine($"Fragmentation: {stats.FragmentationPercent:F1}%");
Console.WriteLine($"WAL size: {stats.WalSize:N0} bytes");
Console.WriteLine($"Last VACUUM: {stats.LastVacuum:u}");

// Cache statistics - the engine's live performance counters
var (qHits, qMisses, qHitRate, qCount) = db.GetQueryCacheStatistics();
Console.WriteLine($"Query cache: {qHitRate:P1} hit rate over {qCount} entries");

var pageCache = db.GetPageCacheStatistics();
Console.WriteLine($"Page cache: {pageCache.HitRate:P1} hit rate, {pageCache.CurrentSize}/{pageCache.Capacity} pages");

// Anything else the engine exposes as name/value pairs
foreach (var (key, value) in db.GetDatabaseStatistics())
{
    Console.WriteLine($"{key}: {value}");
}
```

There is no `db.GetStatistics()` and no `db.GetPerformanceMetrics()`: storage numbers come from
`GetStorageStatistics()`, caches from `GetQueryCacheStatistics()` / `GetPageCacheStatistics()`. Per-stage
write timings come from the profiler in `src/SharpCoreDB/Diagnostics/WritePathProfiler.cs`
(the `WritePathProfilerTests` harness), and end-to-end ratios from the benchmark suite - not from a
database-level metrics API. Fragmentation is also exposed per extent by `FreeSpaceManager.GetDetailedStatistics()`.

### Logging

```csharp
// Register logging on the same service provider the database is opened with
// (`Microsoft.Extensions.Logging` for AddLogging/AddConsole).
services.AddLogging(builder => builder.AddConsole());
var provider = services.BuildServiceProvider();

// Log signals that exist:
logger.LogInformation("Database opened: {Path}", dbPath);

var stats = db.GetStorageStatistics();
logger.LogWarning("Fragmentation {Percent:F1}%, WAL {WalSize} bytes",
    stats.FragmentationPercent, stats.WalSize);

using var detector = new CorruptionDetector(storage, ValidationMode.Quick);
var report = await detector.ValidateAsync();
logger.LogError("Corruption detected: {Severity} - {Summary}", report.Severity, report.Summary);
```

---

## 🔧 Maintenance

### Regular Maintenance Tasks

#### Daily Tasks
```bash
# 1. Backup database
./backup.sh

# 2. Check disk space
df -h

# 3. Monitor logs
tail -f logs/sharpcoredb.log
```

#### Weekly Tasks
```csharp
// 1. Run VACUUM to defragment (SQL form; db.VacuumAsync(VacuumMode.Quick) is the API form)
db.ExecuteSQL("VACUUM");

// 2. Validate integrity - the report carries BytesScanned/BlocksValidated/ValidationTime,
//    so the cost is measured, not assumed
using var detector = new CorruptionDetector(storage, ValidationMode.Standard);
var report = await detector.ValidateAsync();
Console.WriteLine(report.Summary);

// 3. Review the counters that exist
var stats = db.GetStorageStatistics();
var (hits, misses, hitRate, count) = db.GetQueryCacheStatistics();
// Alert on: FragmentationPercent growth, WalSize growth, query-cache hit-rate collapse
```

#### Monthly Tasks
```csharp
// 1. Deep validation (full byte scan - measure it on your data, do not assume a rate)
using var detector = new CorruptionDetector(storage, ValidationMode.Deep);
var report = await detector.ValidateAsync();

// 2. Analyse growth
var stats = db.GetStorageStatistics();
// Track stats.TotalSize, stats.BlockCount, stats.FragmentationPercent, stats.WalSize over time

// 3. Test restore from backup - and prove it by querying the copy
File.Copy("./backups/latest.scdb", "./test/restore.scdb");
using var restored = new Database(provider, "./test/restore.scdb", "your-master-password");
restored.ExecuteSQL("SELECT COUNT(*) FROM users");
```

### VACUUM Strategy

```csharp
// When to VACUUM:
// - FragmentationPercent above your threshold (AutoVacuum + FragmentationThreshold make it automatic)
// - After large DELETE operations
// - Monthly maintenance

var stats = db.GetStorageStatistics();
if (stats.FragmentationPercent > 20)
{
    Console.WriteLine("Running VACUUM...");
    var result = await db.VacuumAsync(VacuumMode.Full);
    Console.WriteLine($"Reclaimed {result.BytesReclaimed:N0} bytes: " +
        $"{result.FragmentationBefore:F1}% -> {result.FragmentationAfter:F1}%, {result.DurationMs}ms");
}
```

---

## ⚠️ Troubleshooting

### Common Issues

#### Issue: "SCDB file is locked"
**Cause:** Another process has the file open  
**Solution:**
```csharp
// Ensure only one Database instance per file
using var db = new Database(...);  // ✅ Use using statement
// db.Dispose() called automatically
```

#### Issue: "Corruption detected"
**Cause:** Crash, disk failure, or power loss  
**Solution:**
```csharp
// 1. Run repair
using var detector = new CorruptionDetector(provider, ValidationMode.Standard);
var report = await detector.ValidateAsync();

using var repairTool = new RepairTool(report, provider);
var result = await repairTool.RepairAsync();

// 2. If repair fails, restore from backup
if (!result.Success)
{
    File.Copy("./backups/latest.scdb", "./data/myapp.scdb", overwrite: true);
}
```

#### Issue: "Slow queries"
**Cause:** No indexes or cache disabled  
**Solution:**
```csharp
// 1. Create indexes
db.ExecuteSQL("CREATE INDEX idx_users_email ON users(email)");

// 2. Enable caches (these live on DatabaseConfig, not DatabaseOptions)
var config = new DatabaseConfig
{
    EnableQueryCache = true,
    QueryCacheSize = 4096,
    EnablePageCache = true,
    PageCacheCapacity = 2048,  // increase cache
    UseMemoryMapping = true,
};
```

#### Issue: "WAL file growing too large"
**Cause:** A long write burst against a WAL region sized below it, or file growth from fragmentation — the WAL is applied by the provider and there is no checkpoint switch to disable  
**Solution:**
```csharp
// The WAL is a region inside the .scdb file, not a sidecar file, and it is applied by the provider.
// There is no CHECKPOINT SQL statement and no EnableWalCheckpointing option.
// 1. Size the buffer for the burst and keep full durability
var config = new DatabaseConfig
{
    WalDurabilityMode = DurabilityMode.FullSync,
    WalBufferSize = 4 * 1024 * 1024,
    EnableAdaptiveWalBatching = true,
};

var options = new DatabaseOptions
{
    StorageMode = StorageMode.SingleFile,
    WalBufferSizePages = 2048,   // in-file WAL region, in pages
};

// 2. Flush pending work into the file (the API form)
db.Flush();

// 3. Shrink the file when fragmentation is the real cause
var result = await db.VacuumAsync(VacuumMode.Quick);
Console.WriteLine($"File after VACUUM: {result.FileSizeAfter:N0} bytes");
```

---

## 🔐 Security

### Encryption

```csharp
// Master password encryption (AES-256-GCM). The password is the key source: without it the file
// cannot be opened, and there is no plaintext mode for a password-protected database.
var db = new Database(
    provider,
    "./data/secure.scdb",
    "strong-master-password-here",  // ✅ Use strong password
    config: new DatabaseConfig { /* DatabaseConfig, not DatabaseOptions */ }
);

// Password requirements:
// - Minimum 12 characters
// - Mix of uppercase, lowercase, numbers, symbols
// - Not a dictionary word
// - Store securely (Azure Key Vault, AWS Secrets Manager, etc.)
```

### Access Control

Single-file mode has **no in-database user management**: `CreateUser` / `Login` on
`DatabaseExtensions` throw `NotSupportedException` ("User management is not supported in single-file
mode"). Access control therefore sits at the process and deployment boundary:

- the encryption key comes from the master password, so whoever holds the password holds the data
- file-system ACLs on the `.scdb` file are the access control for local deployments
- for multi-user access, deploy the gRPC server, which serves multiple databases and enforces
  HTTPS/TLS (minimum TLS 1.2) - never a plain-HTTP endpoint
- keep the password in a secrets manager, never in code or a checked-in settings file

### Network Security

```csharp
// If exposing via API:
// 1. Use HTTPS only
// 2. Implement rate limiting
// 3. Validate all SQL inputs
// 4. Use parameterized queries

// ✅ GOOD
db.ExecuteSQL("INSERT INTO users VALUES (@id, @name)", 
    new { id = 1, name = userInput });

// ❌ BAD (SQL injection risk)
db.ExecuteSQL($"INSERT INTO users VALUES ({id}, '{userInput}')");
```

---

## 📈 Performance Tuning

### Optimization Checklist

- [ ] **Indexes created** on frequently queried columns
- [ ] **Page cache enabled** with adequate size
- [ ] **Query cache enabled** for repeated queries
- [ ] **Durability and WAL sized** (`WalDurabilityMode`, `WalBufferSize`, `WalBufferSizePages`)
- [ ] **Batch operations** used for bulk inserts
- [ ] **VACUUM run** monthly or when fragmentation > 20%

### Benchmarking

```csharp
// Measure performance
var sw = Stopwatch.StartNew();

// Batch insert (recommended)
var statements = new List<string>();
for (int i = 0; i < 1000; i++)
{
    statements.Add($"INSERT INTO users VALUES ({i}, 'User{i}')");
}
db.ExecuteBatchSQL(statements);

sw.Stop();
Console.WriteLine($"1000 inserts: {sw.ElapsedMilliseconds}ms");
// Compare against your OWN baseline, and against SQLite measured on this machine - never against a
// published number. Report ratios, not absolutes, and state the machine and posture (see
// ../BENCHMARK_RESULTS.md for the method this repository uses).
```

---

## 🎓 Best Practices

### DO ✅

1. **Use `using` statements** to ensure Dispose() is called
2. **Keep `DurabilityMode.FullSync`** unless the data can be re-created after a crash
3. **Create backups** before VACUUM or repair
4. **Run validation** weekly (Standard mode)
5. **Monitor fragmentation** and VACUUM when needed
6. **Use batch operations** for bulk inserts
7. **Create indexes** on frequently queried columns
8. **Enable caches** for better performance

### DON'T ❌

1. **Don't** open multiple Database instances for same file
2. **Don't** drop to `DurabilityMode.Async` unless losing the last commits is acceptable to you
3. **Don't** skip backups before maintenance
4. **Don't** use string concatenation for SQL (SQL injection)
5. **Don't** run Deep validation on every request (too slow)
6. **Don't** skip VACUUM indefinitely (fragmentation grows)
7. **Don't** store master password in code (use secrets manager)

---

## 📞 Support

### Resources

- **Documentation:** https://github.com/MPCoreDeveloper/SharpCoreDB/docs
- **Issues:** https://github.com/MPCoreDeveloper/SharpCoreDB/issues
- **NuGet:** https://www.nuget.org/packages/SharpCoreDB

### Getting Help

1. Check this guide and documentation
2. Search existing GitHub issues
3. Create new issue with:
   - SharpCoreDB version
   - .NET version
   - OS and hardware
   - Steps to reproduce
   - Error messages and logs

---

## ✅ Production Deployment Checklist

Before going live:

- [ ] .NET 11 runtime installed
- [ ] Configuration reviewed and tested
- [ ] Backup strategy in place
- [ ] Health checks configured
- [ ] Monitoring and logging enabled
- [ ] Security reviewed (encryption, access control)
- [ ] Performance benchmarked
- [ ] Disaster recovery plan tested
- [ ] Team trained on maintenance procedures

---

**Last Updated:** 2026-10-11  
**Version:** 2.1 (single-file format v2 - see [`README.md`](./README.md) and
[`../CHANGELOG.md`](../CHANGELOG.md))

🎉 **You're ready for production!**
