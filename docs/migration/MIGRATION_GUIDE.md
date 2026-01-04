# Database Migration Guide - SharpCoreDB

## Overview

SharpCoreDB supports **bidirectional migration** between two storage formats:

1. **Directory Mode** (Legacy) - Multi-file format with one file per block
2. **Single-File Mode** (New) - `.scdb` format with all data in one file

Both formats are **fully supported** and maintained for backward compatibility. Migration is **completely optional**.

---

## 🎯 When to Migrate

### Migrate TO Single-File (.scdb) if you want:
- ✅ **Faster startup** (10ms vs 100ms for 100 files)
- ✅ **Easier backups** (copy 1 file instead of directory)
- ✅ **Better SSD performance** (page-aligned I/O)
- ✅ **Fewer file handles** (1 vs 100+)
- ✅ **Incremental VACUUM** (defragmentation without full rewrite)

### Stay with Directory Mode if you have:
- ✅ **Very large databases** (>10GB)
- ✅ **Need for parallel I/O** across multiple files
- ✅ **Existing tooling** that works with multi-file format
- ✅ **Legacy systems** that don't need migration

---

## 📦 Migration API

### Static Methods (Standalone)

```csharp
using SharpCoreDB.Migration;

// Migrate directory → .scdb
var result = await DatabaseMigrator.MigrateToSingleFileAsync(
    sourceDirectoryPath: "mydb/",
    targetScdbPath: "mydb.scdb",
    password: "masterPassword",
    options: null, // Use defaults
    progress: new Progress<double>(p => Console.WriteLine($"Progress: {p:P0}")),
    cancellationToken: default
);

// Migrate .scdb → directory
var result = await DatabaseMigrator.MigrateToDirectoryAsync(
    sourceScdbPath: "mydb.scdb",
    targetDirectoryPath: "mydb_restored/",
    password: "masterPassword",
    options: null,
    progress: new Progress<double>(p => Console.WriteLine($"Progress: {p:P0}")),
    cancellationToken: default
);

// Validate migration
var validation = await DatabaseMigrator.ValidateMigrationAsync(
    path1: "mydb/",
    path2: "mydb.scdb",
    password: "masterPassword"
);

if (validation.IsValid)
{
    Console.WriteLine($"✅ Migration verified! {validation.BlocksValidated} blocks match.");
}
else
{
    foreach (var diff in validation.Differences)
    {
        Console.WriteLine($"❌ {diff}");
    }
}
```

### Instance Methods (on Database)

```csharp
using SharpCoreDB;

// Open existing database
var db = factory.Create("mydb/", "masterPassword");

// Migrate to single-file
var result = await db.MigrateToSingleFileAsync(
    targetScdbPath: "mydb.scdb",
    masterPassword: "masterPassword", // Required for encryption
    progress: new Progress<double>(p => Console.WriteLine($"{p:P0}"))
);

// Validate
var validation = await db.ValidateAgainstAsync(
    otherDatabasePath: "mydb.scdb",
    masterPassword: "masterPassword"
);

// Check current format
Console.WriteLine($"Current mode: {db.CurrentStorageMode}");
```

---

## 🔄 Complete Migration Workflow

### Example 1: Migrate Directory → Single-File

```csharp
using SharpCoreDB;
using SharpCoreDB.Migration;

var factory = serviceProvider.GetRequiredService<DatabaseFactory>();
var password = "myMasterPassword";

// Step 1: Open original database
using var dbOriginal = factory.Create("mydb/", password);

// Step 2: Perform migration
Console.WriteLine("Starting migration...");
var result = await DatabaseMigrator.MigrateToSingleFileAsync(
    "mydb/",
    "mydb.scdb",
    password,
    progress: new Progress<double>(p => Console.WriteLine($"Progress: {p:P0}"))
);

// Step 3: Check result
if (result.Success)
{
    Console.WriteLine($"✅ Migration complete!");
    Console.WriteLine($"   Files migrated: {result.FilesMigrated}");
    Console.WriteLine($"   Bytes: {result.BytesMigrated:N0}");
    Console.WriteLine($"   Duration: {result.Duration.TotalSeconds:F1}s");
    Console.WriteLine($"   Throughput: {result.ThroughputMBps:F1} MB/s");
    
    if (result.VacuumDuration.HasValue)
    {
        Console.WriteLine($"   VACUUM: {result.VacuumDuration.Value.TotalMilliseconds:F0}ms");
    }
}
else
{
    Console.WriteLine($"❌ Migration failed: {result.ErrorMessage}");
    return;
}

// Step 4: Validate migration
Console.WriteLine("\nValidating migration...");
var validation = await DatabaseMigrator.ValidateMigrationAsync(
    "mydb/",
    "mydb.scdb",
    password
);

if (validation.IsValid)
{
    Console.WriteLine($"✅ Validation passed! {validation.BlocksValidated} blocks verified.");
    
    // Step 5: Test new database
    using var dbNew = factory.Create("mydb.scdb", password);
    var stats = dbNew.GetDatabaseStatistics();
    Console.WriteLine($"New database: {stats["TablesCount"]} tables");
    
    Console.WriteLine("\n✅ Migration complete and verified!");
    Console.WriteLine("You can now:");
    Console.WriteLine("  - Use mydb.scdb for all operations");
    Console.WriteLine("  - Keep mydb/ as backup");
    Console.WriteLine("  - Delete mydb/ after testing");
}
else
{
    Console.WriteLine($"❌ Validation failed!");
    foreach (var diff in validation.Differences)
    {
        Console.WriteLine($"   - {diff}");
    }
}
```

### Example 2: Migrate Single-File → Directory

```csharp
using SharpCoreDB.Migration;

// Restore .scdb back to directory format
var result = await DatabaseMigrator.MigrateToDirectoryAsync(
    "mydb.scdb",
    "mydb_restored/",
    "masterPassword",
    progress: new Progress<double>(p => Console.WriteLine($"{p:P0}"))
);

if (result.Success)
{
    Console.WriteLine($"✅ Restored to directory format!");
    
    // Validate
    var validation = await DatabaseMigrator.ValidateMigrationAsync(
        "mydb.scdb",
        "mydb_restored/",
        "masterPassword"
    );
    
    Console.WriteLine(validation.IsValid 
        ? "✅ Restoration verified!" 
        : "❌ Restoration has differences!");
}
```

---

## 🛠️ Migration Features

### 1. **Incremental Migration with Checkpoints**

If migration is interrupted (power failure, crash), it can resume from checkpoint:

```csharp
// First attempt (crashes at 50%)
await DatabaseMigrator.MigrateToSingleFileAsync("mydb/", "mydb.scdb", "password");
// Creates: mydb.scdb.checkpoint

// Second attempt (resumes from 50%)
await DatabaseMigrator.MigrateToSingleFileAsync("mydb/", "mydb.scdb", "password");
// Reads checkpoint, skips already-migrated blocks
// ✅ Completes from 50% to 100%
```

**Checkpoint Format:**
```json
{
  "MigratedBlocks": [
    "table:users:data",
    "table:orders:data",
    ...
  ],
  "LastUpdate": "2026-01-XX..."
}
```

### 2. **SHA-256 Checksum Verification**

Every block is checksummed before and after migration:

```csharp
// Automatic verification during migration
var blockData = await source.ReadBlockAsync(blockName);
var checksumBefore = SHA256.HashData(blockData);

await target.WriteBlockAsync(blockName, blockData);

var verifyData = await target.ReadBlockAsync(blockName);
var checksumAfter = SHA256.HashData(verifyData);

if (!checksumBefore.SequenceEqual(checksumAfter))
{
    throw new InvalidDataException($"Checksum mismatch for block '{blockName}'");
}
```

### 3. **Automatic VACUUM After Migration**

Single-file migrations automatically run VACUUM Full for optimal compaction:

```csharp
var result = await DatabaseMigrator.MigrateToSingleFileAsync(...);

// result.VacuumDuration contains the optimization time
Console.WriteLine($"VACUUM: {result.VacuumDuration.Value.TotalMilliseconds}ms");
```

### 4. **Progress Reporting**

Real-time progress for large databases:

```csharp
var progress = new Progress<double>(p => 
{
    Console.Write($"\rMigrating: {p:P0} ");
});

await DatabaseMigrator.MigrateToSingleFileAsync(
    "mydb/",
    "mydb.scdb",
    "password",
    progress: progress
);

// Output:
// Migrating: 0%
// Migrating: 25%
// Migrating: 50%
// Migrating: 75%
// Migrating: 100%
```

### 5. **Encryption Handling**

Migration automatically handles encryption/decryption:

```csharp
// Source encrypted with password "old123"
// Target encrypted with password "new456"

// Decrypt from source, re-encrypt to target
var result = await DatabaseMigrator.MigrateToSingleFileAsync(
    "mydb/",
    "mydb.scdb",
    password: "old123" // Used for both decryption and encryption
);
```

### 6. **Size Estimation**

Estimate target file size before migration:

```csharp
var estimatedBytes = DatabaseMigrator.EstimateMigratedSize("mydb/", "password");
Console.WriteLine($"Estimated .scdb size: {estimatedBytes / 1024.0 / 1024.0:F1} MB");

// Check available disk space
var drive = new DriveInfo(Path.GetPathRoot("C:\\"));
if (drive.AvailableFreeSpace < estimatedBytes * 1.2)
{
    Console.WriteLine("⚠️  WARNING: Low disk space!");
}
```

---

## 📊 Migration Result

The `MigrationResult` class provides detailed statistics:

```csharp
public sealed class MigrationResult
{
    public string SourcePath { get; init; }        // "mydb/"
    public string TargetPath { get; init; }        // "mydb.scdb"
    public StorageMode SourceFormat { get; init; } // Directory
    public StorageMode TargetFormat { get; init; } // SingleFile
    public bool Success { get; set; }              // true
    public int TotalFiles { get; set; }            // 100
    public int FilesMigrated { get; set; }         // 100
    public int FilesSkipped { get; set; }          // 0
    public long BytesMigrated { get; set; }        // 52428800 (50MB)
    public DateTime StartTime { get; set; }        // 2026-01-XX...
    public DateTime EndTime { get; set; }          // 2026-01-XX...
    public TimeSpan Duration { get; set; }         // 00:00:12
    public TimeSpan? VacuumDuration { get; set; }  // 00:00:02
    public string? ErrorMessage { get; set; }      // null
    public double ThroughputMBps { get; }          // 4.2 MB/s
}
```

---

## 🧪 Testing Migration

### Unit Test Example

```csharp
[Fact]
public async Task Migration_DirectoryToSingleFile_Success()
{
    // Arrange
    var factory = serviceProvider.GetRequiredService<DatabaseFactory>();
    var db = factory.Create("test_db/", "password");
    db.ExecuteSQL("CREATE TABLE users (id INTEGER, name TEXT)");
    db.ExecuteSQL("INSERT INTO users VALUES (1, 'Alice')");
    db.Dispose();

    // Act
    var result = await DatabaseMigrator.MigrateToSingleFileAsync(
        "test_db/",
        "test_db.scdb",
        "password"
    );

    // Assert
    Assert.True(result.Success);
    Assert.Equal(1, result.FilesMigrated); // At least metadata
    Assert.True(result.BytesMigrated > 0);
    
    // Verify
    var validation = await DatabaseMigrator.ValidateMigrationAsync(
        "test_db/",
        "test_db.scdb",
        "password"
    );
    Assert.True(validation.IsValid);
}
```

---

## ⚠️ Important Notes

### 1. **Original Database Preserved**

Migration **never modifies** the source database:

```csharp
// Before:
mydb/
  ├── .meta
  ├── table_users.dat
  └── table_orders.dat

// After migration:
mydb/              ← UNCHANGED, safe to delete after verification
  ├── .meta
  ├── table_users.dat
  └── table_orders.dat
mydb.scdb          ← NEW FILE

```

### 2. **Password Required**

You must provide the master password for migration (not stored in Database class):

```csharp
// ❌ This won't compile - password required
await db.MigrateToSingleFileAsync("mydb.scdb");

// ✅ Correct
await db.MigrateToSingleFileAsync("mydb.scdb", "masterPassword");
```

### 3. **Backup Recommendation**

Always backup before migration:

```bash
# Linux/Mac
tar -czf mydb_backup.tar.gz mydb/

# Windows
Compress-Archive -Path mydb\ -DestinationPath mydb_backup.zip
```

### 4. **No In-Place Migration**

Migration creates a NEW file/directory, doesn't modify source:

```csharp
// Migrating mydb/ → mydb.scdb
await DatabaseMigrator.MigrateToSingleFileAsync("mydb/", "mydb.scdb", "password");

// Now you have BOTH:
// - mydb/      (original, can be deleted)
// - mydb.scdb  (new)
```

---

## 🚀 Performance Expectations

### Directory → Single-File

| Database Size | File Count | Migration Time | Throughput |
|---------------|------------|----------------|------------|
| 10 MB | 50 files | ~3 seconds | ~3 MB/s |
| 100 MB | 100 files | ~25 seconds | ~4 MB/s |
| 1 GB | 500 files | ~4 minutes | ~4 MB/s |
| 10 GB | 1000 files | ~40 minutes | ~4 MB/s |

### Single-File → Directory

Similar performance, no VACUUM overhead.

---

## 📝 Best Practices

### 1. **Test Migration First**

```csharp
// Test on a copy
Directory.Copy("mydb/", "mydb_test/", recursive: true);
await DatabaseMigrator.MigrateToSingleFileAsync("mydb_test/", "test.scdb", "password");

// Verify test migration
var validation = await DatabaseMigrator.ValidateMigrationAsync("mydb_test/", "test.scdb", "password");
if (validation.IsValid)
{
    // Now migrate production
    await DatabaseMigrator.MigrateToSingleFileAsync("mydb/", "mydb.scdb", "password");
}
```

### 2. **Monitor Progress**

```csharp
var progress = new Progress<double>(p => 
{
    if (p % 0.1 < 0.01) // Every 10%
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Progress: {p:P0}");
    }
});

await DatabaseMigrator.MigrateToSingleFileAsync(..., progress: progress);
```

### 3. **Handle Errors Gracefully**

```csharp
try
{
    var result = await DatabaseMigrator.MigrateToSingleFileAsync(...);
    
    if (!result.Success)
    {
        Console.WriteLine($"Migration failed: {result.ErrorMessage}");
        // Cleanup partial file
        if (File.Exists("mydb.scdb"))
        {
            File.Delete("mydb.scdb");
        }
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Migration error: {ex.Message}");
    // Original database unchanged
}
```

### 4. **Cleanup After Verification**

```csharp
// Migrate
var result = await DatabaseMigrator.MigrateToSingleFileAsync("mydb/", "mydb.scdb", "password");

// Validate
var validation = await DatabaseMigrator.ValidateMigrationAsync("mydb/", "mydb.scdb", "password");

// Test new database
using (var dbNew = factory.Create("mydb.scdb", "password"))
{
    // Run some queries to verify functionality
    dbNew.ExecuteSQL("SELECT * FROM users");
}

// If all good, cleanup old database
if (validation.IsValid)
{
    Directory.Delete("mydb/", recursive: true);
    Console.WriteLine("✅ Old database removed");
}
```

---

## 📖 See Also

- [SCDB File Format Design](./SCDB_FILE_FORMAT_DESIGN.md)
- [SCDB Implementation Status](./SCDB_IMPLEMENTATION_STATUS.md)
- [SCDB Phase 1 Implementation](./SCDB_PHASE1_IMPLEMENTATION.md)

---

**License:** MIT  
**Status:** ✅ Production Ready  
**Version:** 1.0.0
