# Row Overflow Documentation

This directory contains technical documentation for SharpCoreDB's row overflow implementation.

## 📁 Contents

- **[DESIGN.md](DESIGN.md)**: Complete technical design specification
- **[COMPRESSION_ANALYSIS.md](COMPRESSION_ANALYSIS.md)**: Compression algorithm comparison and benchmarks
- **[IMPLEMENTATION_GUIDE.md](IMPLEMENTATION_GUIDE.md)**: Step-by-step implementation checklist
- **[PERFORMANCE_TUNING.md](PERFORMANCE_TUNING.md)**: Performance considerations and optimization strategies

## 🎯 Overview

Row overflow enables SharpCoreDB to store rows larger than the configured page size (default 4KB) by chaining overflow pages together. This is similar to SQLite's overflow mechanism but with modern improvements:

- **Configurable threshold** (not hardcoded like SQLite)
- **Doubly-linked chains** (bi-directional traversal)
- **Optional Brotli compression** (better ratio than Gzip)
- **Integration with FreeSpaceManager** (O(log n) allocation)
- **WAL-aware** (crash recovery for overflow pages)
- **Backward compatible** (feature flag in file header)

## 🚀 Quick Start

### Enable Row Overflow

```csharp
var options = new DatabaseOptions
{
    EnableRowOverflow = true,                         // Feature flag
    RowOverflowThresholdPercent = 75,                // 75% of page size (3KB for 4KB pages)
    CompressOverflowPages = true,                    // Enable Brotli compression
    OverflowCompressionAlgorithm = CompressionAlgorithm.Brotli,
    OverflowChainMode = OverflowChainMode.DoublyLinked
};

var db = Database.Open("mydb.scdb", options);
```

### Use Cases

**✅ Good candidates for overflow:**
- Large text fields (JSON documents, logs, descriptions)
- BLOBs (images, files, serialized objects)
- Wide tables (100+ columns with many NULLs)

**❌ Not recommended:**
- Small rows (< 1KB) - overhead not worth it
- Frequently updated rows - overflow rewrite is expensive
- Hot path queries - prefer normalization

## 🔗 Related Documentation

- [Binary Serialization Guide](../serialization/SERIALIZATION_AND_STORAGE_GUIDE.md)
- [SCDB File Format](../scdb/FILE_FORMAT.md)
- [Page Manager Design](../architecture/PAGE_MANAGER.md)
- [Performance Testing](../testing/PERFORMANCE_TESTING.md)

---

**Status**: Design phase (not yet implemented)  
**Target Release**: Phase 5 (after B-Tree optimizations)  
**Last Updated**: 2025-01-28
