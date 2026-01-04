# Migration Documentation

This directory contains guides for migrating SharpCoreDB databases between storage formats.

---

## 📁 Files

### **[MIGRATION_GUIDE.md](./MIGRATION_GUIDE.md)** ⭐
Complete guide for database migration.

**Contents:**
- Bidirectional migration (Directory ↔ Single-File)
- Migration API documentation
- Step-by-step examples
- Progress reporting
- Checksum verification
- Best practices
- Performance expectations

---

## 🔄 Supported Migrations

### 1. Directory → Single-File (.scdb)

**When to use:**
- Want faster startup (10ms vs 100ms)
- Need easier backups (1 file instead of directory)
- Prefer SSD-optimized storage
- Database size < 10GB

**Example:**
```csharp
using SharpCoreDB.Migration;

var result = await DatabaseMigrator.MigrateToSingleFileAsync(
    "mydb/",           // Source directory
    "mydb.scdb",       // Target .scdb file
    "masterPassword"
);
```

### 2. Single-File (.scdb) → Directory

**When to use:**
- Need to restore from .scdb backup
- Require multi-file format for tooling
- Database growing beyond 10GB

**Example:**
```csharp
using SharpCoreDB.Migration;

var result = await DatabaseMigrator.MigrateToDirectoryAsync(
    "mydb.scdb",       // Source .scdb file
    "mydb_restored/",  // Target directory
    "masterPassword"
);
```

---

## ✅ Key Features

### Incremental Migration with Checkpoints
- **Crash recovery** - Resume from interruption
- **Checkpoint files** - Track progress
- **Automatic cleanup** - Remove checkpoint on success

### Integrity Verification
- **SHA-256 checksums** - Verify each block
- **Validation API** - Compare source and target
- **Error detection** - Mismatch alerts

### Progress Reporting
- **Real-time progress** - `IProgress<double>` callback
- **Performance metrics** - Throughput tracking
- **Result statistics** - Detailed migration report

### Safety First
- **Non-destructive** - Source database unchanged
- **Atomic operations** - File swapping where possible
- **Backup recommendations** - Always backup first

---

## 📊 Migration Performance

| Database Size | File Count | Typical Time | Throughput |
|---------------|------------|--------------|------------|
| 10 MB | 50 files | ~3 seconds | ~3 MB/s |
| 100 MB | 100 files | ~25 seconds | ~4 MB/s |
| 1 GB | 500 files | ~4 minutes | ~4 MB/s |
| 10 GB | 1000 files | ~40 minutes | ~4 MB/s |

*Performance varies based on hardware and block count.*

---

## 🚀 Quick Start

### Static API (Standalone)

```csharp
using SharpCoreDB.Migration;

// Migrate
var result = await DatabaseMigrator.MigrateToSingleFileAsync(
    sourceDirectoryPath: "mydb/",
    targetScdbPath: "mydb.scdb",
    password: "masterPassword",
    options: null, // Use defaults
    progress: new Progress<double>(p => Console.WriteLine($"{p:P0}")),
    cancellationToken: default
);

// Validate
var validation = await DatabaseMigrator.ValidateMigrationAsync(
    "mydb/",
    "mydb.scdb",
    "masterPassword"
);

Console.WriteLine(validation.IsValid ? "✅ Success!" : "❌ Failed!");
```

### Instance API (on Database)

```csharp
using SharpCoreDB;

var db = factory.Create("mydb/", "masterPassword");

// Migrate
var result = await db.MigrateToSingleFileAsync(
    "mydb.scdb",
    "masterPassword"
);

// Check result
if (result.Success)
{
    Console.WriteLine($"Migrated {result.FilesMigrated} files in {result.Duration.TotalSeconds:F1}s");
}
```

---

## 📖 Detailed Guide

For complete examples, edge cases, and best practices, see:
**[MIGRATION_GUIDE.md](./MIGRATION_GUIDE.md)**

Topics covered:
- ✅ Complete workflow examples
- ✅ Error handling
- ✅ Progress monitoring
- ✅ Size estimation
- ✅ Encryption handling
- ✅ Post-migration validation
- ✅ Cleanup procedures
- ✅ Testing strategies

---

## ⚠️ Important Notes

### 1. Source Database Preserved
Migration **never modifies** the source database. The original remains intact.

### 2. Password Required
You must provide the master password (not stored in Database class).

### 3. Backup First
Always backup before migration:
```bash
tar -czf mydb_backup.tar.gz mydb/
```

### 4. Test First
Test migration on a copy before production:
```csharp
Directory.Copy("mydb/", "mydb_test/", recursive: true);
await DatabaseMigrator.MigrateToSingleFileAsync("mydb_test/", "test.scdb", "password");
```

---

## 🧪 Testing Migration

```csharp
[Fact]
public async Task Migration_DirectoryToSingleFile_Success()
{
    // Create test database
    var db = factory.Create("test_db/", "password");
    db.ExecuteSQL("CREATE TABLE users (id INTEGER, name TEXT)");
    db.ExecuteSQL("INSERT INTO users VALUES (1, 'Alice')");
    db.Dispose();

    // Migrate
    var result = await DatabaseMigrator.MigrateToSingleFileAsync(
        "test_db/",
        "test_db.scdb",
        "password"
    );

    // Assert
    Assert.True(result.Success);
    
    // Validate
    var validation = await DatabaseMigrator.ValidateMigrationAsync(
        "test_db/",
        "test_db.scdb",
        "password"
    );
    Assert.True(validation.IsValid);
}
```

---

## 📝 See Also

- [SCDB Format](../scdb/README.md) - Single-file format documentation
- [Implementation Status](../scdb/IMPLEMENTATION_STATUS.md) - Feature status
- [Contributing](../CONTRIBUTING.md) - How to contribute

---

**Last Updated:** 2026-01-XX  
**Status:** ✅ Production Ready  
**License:** MIT
