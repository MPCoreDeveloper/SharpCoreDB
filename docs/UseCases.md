# SharpCoreDB Use Cases & Ideal Settings

This guide lists recommended configuration per use case. Copy settings into your setup (DatabaseConfig or DatabaseOptions).

## 1. Web App (Concurrent Reads + OLTP Writes)
- Storage: PageBased
- DatabaseConfig:
  - `EnablePageCache = true`
  - `PageCacheCapacity = 20000`
  - `UseMemoryMapping = true`
  - `UseGroupCommitWal = true`
  - `WalMaxBatchDelayMs = 5`
  - `WalDurabilityMode = DurabilityMode.FullSync`
- Connection: pool connections (Cache=Shared), prefer short transactions.

## 2. Reporting / Read-Heavy API
- Storage: SingleFile
- DatabaseOptions:
  - `EnableMemoryMapping = true`
  - `FileShareMode = FileShare.Read`
  - `CreateImmediately = true`
- DatabaseConfig:
  - `EnablePageCache = true`
  - `PageCacheCapacity = 25000`
- Notes: Open as read-only where possible.

## 3. Bulk Import (ETL)
- Storage: PageBased
- DatabaseConfig:
  - `HighSpeedInsertMode = true`
  - `UseGroupCommitWal = true`
  - `EnableAdaptiveWalBatching = true`
  - `WalBatchMultiplier = 512`
  - `WalDurabilityMode = DurabilityMode.Async`
  - `EnablePageCache = true`
  - `PageCacheCapacity = 1000`
- DatabaseOptions:
  - `WalBufferSizePages = 4096`
- Notes: Disable encryption during import if safe; re-enable after.

## 4. Analytics / BI
- Storage: Columnar (auto)
- DatabaseConfig:
  - `EnableQueryCache = true`
  - `QueryCacheSize = 5000`
  - `EnablePageCache = true`
  - `PageCacheCapacity = 20000`
  - `UseMemoryMapping = true`
- Notes: Build indexes for filter columns; rely on SIMD aggregates.

## 5. Desktop App (Single-User)
- Storage: SingleFile
- DatabaseOptions:
  - `EnableMemoryMapping = true`
  - `FileShareMode = FileShare.None`
  - `CreateImmediately = true`
  - `WalBufferSizePages = 2048`
- DatabaseConfig:
  - `EnablePageCache = true`
  - `PageCacheCapacity = 10000`

## 6. High-Concurrency API (Writes)
- Storage: PageBased
- DatabaseConfig:
  - `UseGroupCommitWal = true`
  - `EnableAdaptiveWalBatching = true`
  - `WalBatchMultiplier = 256`
  - `WalDurabilityMode = DurabilityMode.Async`
- Notes: Shard large write streams; avoid long transactions.

---

## Quick Code Examples

### SingleFile (Read-Heavy)
```csharp
var opts = DatabaseOptions.CreateSingleFileDefault();
opts.EnableMemoryMapping = true;
opts.FileShareMode = FileShare.Read;
opts.WalBufferSizePages = 2048;
opts.CreateImmediately = true;

var cfg = new DatabaseConfig
{
    EnablePageCache = true,
    PageCacheCapacity = 25000,
    UseMemoryMapping = true
};

var db = factory.CreateWithOptions(path + ".scdb", "password", opts);
```

### PageBased (OLTP)
```csharp
var cfg = new DatabaseConfig
{
    EnablePageCache = true,
    PageCacheCapacity = 20000,
    UseMemoryMapping = true,
    UseGroupCommitWal = true,
    WalMaxBatchDelayMs = 5,
    WalDurabilityMode = DurabilityMode.FullSync
};

var db = (Database)factory.Create(dirPath, "password", false, cfg);
```
