# SharpCoreDB SCDB - Production Deployment Guide

**Version:** 2.0  
**Last Updated:** 2026-01-28  
**Target:** Production environments

---

## 🎯 Overview

This guide covers deploying SharpCoreDB with the **SCDB (SharpCore DataBase)** single-file storage format in production environments.

**SCDB Benefits:**
- ✅ Single `.scdb` file (easy backup/restore)
- ✅ WAL for ACID transactions
- ✅ Crash recovery
- ✅ Corruption detection & repair
- ✅ 7,765x faster analytics (SIMD)
- ✅ 1.37x faster INSERTs vs SQLite

---

## 📋 Pre-Deployment Checklist

### System Requirements

| Component | Minimum | Recommended |
|-----------|---------|-------------|
| **.NET Runtime** | .NET 10 | .NET 10+ |
| **RAM** | 512 MB | 2 GB+ |
| **Disk** | SSD (100 MB/s) | NVMe (500 MB/s) |
| **CPU** | 2 cores | 4+ cores |
| **OS** | Windows/Linux/macOS | Any |

### Software Dependencies

```bash
# .NET 10 Runtime (required)
dotnet --version  # Should show 10.0 or higher

# NuGet Package
dotnet add package SharpCoreDB --version 2.0.0
```

---

## 🚀 Quick Start

### 1. Basic Setup

```csharp
using SharpCoreDB;
using SharpCoreDB.Storage;
using Microsoft.Extensions.DependencyInjection;

// Configure services
var services = new ServiceCollection();
services.AddSingleton<ICryptoService, CryptoService>();
var provider = services.BuildServiceProvider();

// Create SCDB database
var options = new DatabaseOptions
{
    StorageMode = StorageMode.SingleFile,  // ✅ Use SCDB format
    PageSize = 4096,                        // 4KB pages (default)
    CreateImmediately = true,
    EnableWalCheckpointing = true,          // ✅ Auto-checkpoint
    WalCheckpointIntervalMs = 30000,        // Checkpoint every 30s
};

using var db = new Database(
    provider,
    "./data/myapp.scdb",  // ✅ Single file!
    "your-master-password",
    isReadOnly: false,
    config: options
);

// Use database
db.ExecuteSQL("CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT)");
db.ExecuteSQL("INSERT INTO users VALUES (1, 'Alice')");
```

---

## ⚙️ Configuration

### Production-Ready Configuration

```csharp
var config = new DatabaseOptions
{
    // ========== SCDB Storage ==========
    StorageMode = StorageMode.SingleFile,
    PageSize = 4096,                    // 4KB pages (optimal for most workloads)
    CreateImmediately = true,
    
    // ========== WAL Configuration ==========
    EnableWalCheckpointing = true,      // ✅ CRITICAL: Enable auto-checkpoint
    WalCheckpointIntervalMs = 30000,    // Checkpoint every 30 seconds
    WalMaxBatchSize = 100,              // Batch up to 100 operations
    WalMaxBatchDelayMs = 10,            // Max 10ms batch delay
    WalDurabilityMode = WalDurabilityMode.Fsync,  // ✅ Maximum durability
    
    // ========== Performance ==========
    EnableQueryCache = true,
    QueryCacheSize = 1000,
    EnablePageCache = true,
    PageCacheCapacity = 1024,           // 4 MB cache (1024 * 4KB)
    
    // ========== Monitoring ==========
    EnablePerformanceMetrics = true,
};
```

### Configuration by Workload

#### High-Write Workload (OLTP)
```csharp
var config = new DatabaseOptions
{
    PageSize = 4096,                    // Smaller pages for updates
    WalMaxBatchSize = 50,               // Smaller batches for lower latency
    WalCheckpointIntervalMs = 60000,    // Checkpoint every minute
    PageCacheCapacity = 2048,           // 8 MB cache
};
```

#### High-Read Workload (Analytics)
```csharp
var config = new DatabaseOptions
{
    PageSize = 8192,                    // Larger pages for scans
    EnableQueryCache = true,
    QueryCacheSize = 5000,              // Large query cache
    PageCacheCapacity = 4096,           // 32 MB cache
};
```

#### Balanced Workload
```csharp
var config = new DatabaseOptions
{
    PageSize = 4096,                    // Standard pages
    WalCheckpointIntervalMs = 30000,    // 30s checkpoint
    PageCacheCapacity = 1024,           // 4 MB cache
};
```

---

## 🛡️ High Availability

### Backup Strategy

#### Daily Backups
```csharp
// Simple file copy (database must be checkpointed first)
db.ExecuteSQL("CHECKPOINT");  // Flush WAL
File.Copy("./data/myapp.scdb", $"./backups/myapp_{DateTime.UtcNow:yyyyMMdd}.scdb");
```

#### Hot Backup (No Downtime)
```csharp
// Use SCDB's built-in backup
await db.BackupAsync("./backups/myapp_hot.scdb");
```

#### Incremental Backups
```csharp
// Copy WAL files separately
var walFiles = Directory.GetFiles("./data", "*.wal");
foreach (var wal in walFiles)
{
    File.Copy(wal, Path.Combine("./wal_backups", Path.GetFileName(wal)));
}
```

### Backup Retention Policy

```
Daily backups:    Keep 7 days
Weekly backups:   Keep 4 weeks
Monthly backups:  Keep 12 months
```

### Disaster Recovery

```csharp
// 1. Detect corruption
using var detector = new CorruptionDetector(provider, ValidationMode.Standard);
var report = await detector.ValidateAsync();

if (report.IsCorrupted)
{
    // 2. Attempt repair
    using var repairTool = new RepairTool(report, provider);
    var result = await repairTool.RepairAsync(new RepairOptions
    {
        CreateBackup = true,              // ✅ Always backup first
        AllowDataLoss = false,            // Conservative repair
        Aggressiveness = RepairAggressiveness.Conservative,
    });
    
    if (!result.Success)
    {
        // 3. Restore from backup
        File.Copy("./backups/latest.scdb", "./data/myapp.scdb", overwrite: true);
    }
}
```

---

## 📊 Monitoring

### Health Checks

```csharp
public class DatabaseHealthCheck : IHealthCheck
{
    private readonly Database _db;
    
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Quick validation (header only, <1ms)
            using var provider = GetStorageProvider();
            using var detector = new CorruptionDetector(provider, ValidationMode.Quick);
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

### Performance Metrics

```csharp
// Get statistics
var stats = db.GetStatistics();

Console.WriteLine($"Total Size: {stats.TotalSize:N0} bytes");
Console.WriteLine($"Block Count: {stats.BlockCount}");
Console.WriteLine($"Fragmentation: {stats.FragmentationPercentage:F1}%");
Console.WriteLine($"Free Space: {stats.FreeSpaceBytes:N0} bytes");

// Performance metrics
var metrics = db.GetPerformanceMetrics();
Console.WriteLine($"Avg INSERT: {metrics.AvgInsertMs:F2}ms");
Console.WriteLine($"Avg SELECT: {metrics.AvgSelectMs:F2}ms");
Console.WriteLine($"Cache Hit Rate: {metrics.CacheHitRate:F1}%");
```

### Logging

```csharp
// Enable structured logging
var logger = LoggerFactory.Create(builder =>
{
    builder.AddConsole();
    builder.AddFile("logs/sharpcoredb.log");
}).CreateLogger<Database>();

// Log critical operations
logger.LogInformation("Database opened: {Path}", dbPath);
logger.LogWarning("Checkpoint took {Ms}ms", checkpointTime);
logger.LogError("Corruption detected: {Severity}", report.Severity);
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
// 1. Run VACUUM to defragment
db.ExecuteSQL("VACUUM");

// 2. Validate integrity (Standard mode, ~10ms/MB)
using var detector = new CorruptionDetector(provider, ValidationMode.Standard);
var report = await detector.ValidateAsync();
Console.WriteLine(report.Summary);

// 3. Review performance metrics
var metrics = db.GetPerformanceMetrics();
// Alert if INSERT time > 10ms or cache hit rate < 80%
```

#### Monthly Tasks
```csharp
// 1. Deep validation (50ms/MB)
using var detector = new CorruptionDetector(provider, ValidationMode.Deep);
var report = await detector.ValidateAsync();

// 2. Analyze growth
var stats = db.GetStatistics();
// Track totalSize, blockCount, fragmentation over time

// 3. Test restore from backup
File.Copy("./backups/latest.scdb", "./test/restore.scdb");
// Verify can open and query
```

### VACUUM Strategy

```csharp
// When to VACUUM:
// - Fragmentation > 20%
// - After large DELETE operations
// - Monthly maintenance

var stats = db.GetStatistics();
if (stats.FragmentationPercentage > 20)
{
    Console.WriteLine("Running VACUUM...");
    var result = await db.VacuumAsync(VacuumMode.Full);
    Console.WriteLine($"Reclaimed {result.BytesReclaimed:N0} bytes");
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

// 2. Enable caches
var config = new DatabaseOptions
{
    EnableQueryCache = true,
    EnablePageCache = true,
    PageCacheCapacity = 2048,  // Increase cache
};
```

#### Issue: "WAL file growing too large"
**Cause:** Checkpointing disabled or too infrequent  
**Solution:**
```csharp
// 1. Enable auto-checkpoint
var config = new DatabaseOptions
{
    EnableWalCheckpointing = true,
    WalCheckpointIntervalMs = 30000,  // Checkpoint every 30s
};

// 2. Manual checkpoint
db.ExecuteSQL("CHECKPOINT");
```

---

## 🔐 Security

### Encryption

```csharp
// Master password encryption (AES-256)
var db = new Database(
    provider,
    "./data/secure.scdb",
    "strong-master-password-here",  // ✅ Use strong password
    config: new DatabaseOptions { /* ... */ }
);

// Password requirements:
// - Minimum 12 characters
// - Mix of uppercase, lowercase, numbers, symbols
// - Not a dictionary word
// - Store securely (Azure Key Vault, AWS Secrets Manager, etc.)
```

### Access Control

```csharp
// User authentication
db.CreateUser("admin", "admin-password");
db.CreateUser("readonly", "readonly-password");

// Login before operations
if (db.Login("admin", "admin-password"))
{
    // Perform admin operations
}
```

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
- [ ] **WAL checkpointing** configured appropriately
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
// Target: <10ms (100k ops/sec)
```

---

## 🎓 Best Practices

### DO ✅

1. **Use `using` statements** to ensure Dispose() is called
2. **Enable WAL checkpointing** in production
3. **Create backups** before VACUUM or repair
4. **Run validation** weekly (Standard mode)
5. **Monitor fragmentation** and VACUUM when needed
6. **Use batch operations** for bulk inserts
7. **Create indexes** on frequently queried columns
8. **Enable caches** for better performance

### DON'T ❌

1. **Don't** open multiple Database instances for same file
2. **Don't** disable WAL checkpointing in production
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

- [ ] .NET 10 runtime installed
- [ ] Configuration reviewed and tested
- [ ] Backup strategy in place
- [ ] Health checks configured
- [ ] Monitoring and logging enabled
- [ ] Security reviewed (encryption, access control)
- [ ] Performance benchmarked
- [ ] Disaster recovery plan tested
- [ ] Team trained on maintenance procedures

---

**Last Updated:** 2026-01-28  
**Version:** 2.0 (SCDB Phase 5 Complete)

🎉 **You're ready for production!**
