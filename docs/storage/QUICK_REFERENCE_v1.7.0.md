# Quick Reference: v1.7.0 Improvements

**TL;DR:** Critical bug fixes + 60-80% metadata compression. Upgrade immediately.

---

## 🚨 Critical Fixes

### 1. Database Reopen Issue ✅ FIXED
**Problem:** `InvalidOperationException` when reopening newly created databases.  
**Solution:** Graceful null/empty JSON handling + immediate metadata flush.

### 2. Metadata Compression 📦 NEW
**Benefit:** 60-80% smaller metadata files.  
**Overhead:** <1ms CPU time.  
**Backward Compatible:** ✅ Yes, auto-detects format.

---

## 📊 Quick Stats

| Metric | Before | After |
|--------|--------|-------|
| Metadata size (10 tables) | 2.4 KB | 896 B (-62.7%) |
| Metadata size (100 tables) | 24 KB | 5.8 KB (-75.8%) |
| Database open time | 1.2 ms | 1.5 ms (+0.3ms) |
| Compression overhead | N/A | ~0.5ms |
| Decompression overhead | N/A | ~0.3ms |

---

## 💻 Upgrade Guide

### Step 1: Update NuGet
```bash
dotnet add package SharpCoreDB --version 1.7.0
```

### Step 2: No Code Changes Needed!
```csharp
// Your existing code works as-is
var db = factory.Create("mydb.scdb", "password");
```

### Step 3: (Optional) Configure Compression
```csharp
var options = DatabaseOptions.CreateSingleFileDefault();
options.CompressMetadata = true; // Default: enabled
```

### Step 4: (Optional) Tune Compression Presets 🆕

Fine-tune the trade-off between compression speed and storage efficiency:

```csharp
var options = DatabaseOptions.CreateSingleFileDefault();

// Metadata compression (default: Fastest — minimal CPU, metadata is small)
options.MetadataCompressionLevel = OptionalCompressionLevel.Fastest;

// Block data compression (default: Optimal — balanced for telemetry)
options.BlockCompressionLevel = OptionalCompressionLevel.Optimal;
```

**Available presets:**

| Preset | CPU Cost | Compression Ratio | Best For |
|--------|----------|-------------------|----------|
| `Fastest` | Minimal | Good | Metadata, high-frequency writes |
| `Optimal` | Moderate | Better | Telemetry blocks, general use |
| `SmallestSize` | High | Best | Cold storage, archival workloads |

**Available algorithms:**

| Algorithm | Speed | Ratio | Best For |
|-----------|-------|-------|----------|
| `Brotli` | Moderate (Fastest) to Very Slow (SmallestSize) | Best | Archival, read-heavy workloads |
| `GZip` | Fast | Good | High-frequency writes, individual inserts |
| `Zstd` | Fast to Moderate | Excellent | General-purpose, telemetry, mixed workloads |

**Performance findings (1,000 single-row inserts):**

| Configuration | ms/row | vs None |
|---------------|--------|---------|
| None | 0.517 | Baseline |
| GZip/Fastest | 0.197 | **2.6x faster** |
| GZip/Optimal | 0.220 | **2.3x faster** |
| GZip/SmallestSize | 0.360 | **1.4x faster** |
| Brotli/Fastest | 0.639 | 1.2x slower |
| Brotli/Optimal | 0.793 | 1.5x slower |
| Brotli/SmallestSize | 38.322 | **74x slower** ⚠️ |

**Key insights:**
- GZip is **faster than no compression** for individual inserts due to I/O savings.
- Brotli/SmallestSize is a **trap** for write-heavy workloads — use it only for archival.
- Zstd provides the best balance of speed and ratio for most database workloads.

**Note:** Presets only apply when `BlockCompression` is not `None`. Decoders auto-detect the preset on read — no migration needed.

---

## 🧪 Verify Compression

```csharp
using var provider = SingleFileStorageProvider.Open("mydb.scdb", options);
var metadata = await provider.ReadBlockAsync("sys:metadata");

if (metadata[0..4] == "BROT"u8)
{
    Console.WriteLine("✅ Metadata is compressed");
}
else
{
    Console.WriteLine("⚠️ Metadata is raw JSON");
}
```

---

## 📚 Full Documentation

- **Technical Details:** `docs/storage/METADATA_IMPROVEMENTS_v1.7.0.md`
- **Progression Report:** `docs/PROGRESSION_V1.3.5_TO_v1.7.0.md`
- **Changelog:** `docs/CHANGELOG.md`

---

## ✅ Recommendation

**Upgrade Priority:** 🔴 **IMMEDIATE**

Fixes critical reopen issues and provides significant storage optimization with zero breaking changes.

---

**Version:** 1.7.0  
**Date:** 2026-02-20  
**Status:** ✅ Production Ready
