# SCDB Phase 5: Hardening - Design Document

**Version:** 1.0  
**Date:** 2026-01-28  
**Status:** 📐 Design Complete

---

## 🎯 Phase 5 Goals

1. **CorruptionDetector**: Detect and report database corruption
2. **RepairTool**: Automatically repair corruption where possible
3. **Enhanced Error Handling**: Better error messages and recovery
4. **Production Documentation**: Comprehensive deployment guide

---

## 📐 Architecture Overview

```
┌─────────────────────────────────────────────────────────────┐
│                   SCDB Hardening Layer                       │
├─────────────────────────────────────────────────────────────┤
│  CorruptionDetector     │    RepairTool                     │
│  - Checksum validation  │    - Auto-repair                  │
│  - Structure integrity  │    - Backup creation              │
│  - WAL consistency      │    - Recovery reporting           │
│  - Block validation     │    - Rollback on failure          │
└─────────────────────────────────────────────────────────────┘
                          │
                          ▼
          ┌───────────────────────────────┐
          │  SingleFileStorageProvider    │
          │  (with validation hooks)       │
          └───────────────────────────────┘
```

---

## 📦 Component 1: CorruptionDetector

**Purpose**: Detect database corruption early before data loss occurs

### Class Design

```csharp
/// <summary>
/// Detects corruption in SCDB files using multiple validation strategies.
/// C# 14: Uses modern async patterns and Span<T> for performance.
/// </summary>
public sealed class CorruptionDetector : IDisposable
{
    // Validation modes
    private readonly ValidationMode _mode;
    
    // Storage provider to validate
    private readonly SingleFileStorageProvider _provider;
    
    // Methods:
    // - ValidateAsync() → CorruptionReport
    // - ValidateHeader() → HeaderValidationResult
    // - ValidateBlocks() → BlockValidationResult
    // - ValidateWal() → WalValidationResult
    // - ValidateChecksums() → ChecksumValidationResult
}
```

### Validation Strategies

#### 1. **Header Validation**
```
✅ Magic number (0x53434442 - "SCDB")
✅ Version number (1)
✅ Page size (power of 2, 512-65536)
✅ Block registry offset
✅ WAL offset
✅ File size consistency
```

#### 2. **Block Registry Validation**
```
✅ Block count < max blocks
✅ No overlapping blocks
✅ Blocks within file size
✅ Valid block names
✅ SHA-256 checksums match
```

#### 3. **WAL Validation**
```
✅ WAL header magic
✅ LSN sequence valid
✅ Transaction IDs consistent
✅ Entry checksums valid
✅ No partial entries
```

#### 4. **Free Space Validation**
```
✅ Free space bitmap consistent
✅ Free pages not allocated
✅ Total free space accurate
✅ No double allocations
```

### ValidationMode Enum

```csharp
public enum ValidationMode
{
    Quick,      // Header + block count only (~1ms)
    Standard,   // Header + blocks + checksums (~10ms/MB)
    Deep,       // Full validation including WAL (~50ms/MB)
    Paranoid,   // Re-read and verify all data (~200ms/MB)
}
```

### CorruptionReport

```csharp
public sealed record CorruptionReport
{
    public bool IsCorrupted { get; init; }
    public CorruptionSeverity Severity { get; init; }
    public List<CorruptionIssue> Issues { get; init; }
    public TimeSpan ValidationTime { get; init; }
    public long BytesScanned { get; init; }
    public int BlocksValidated { get; init; }
    
    // Analysis
    public bool IsRepairable { get; init; }
    public string Summary { get; init; }
}

public enum CorruptionSeverity
{
    None,           // No issues
    Warning,        // Minor issues, database still usable
    Moderate,       // Some data loss possible
    Severe,         // Significant corruption, repair needed
    Critical,       // Database unusable
}

public sealed record CorruptionIssue
{
    public IssueType Type { get; init; }
    public string Description { get; init; }
    public long Offset { get; init; }
    public string? BlockName { get; init; }
    public bool IsRepairable { get; init; }
}
```

---

## 📦 Component 2: RepairTool

**Purpose**: Automatically repair corruption where safe to do so

### Class Design

```csharp
/// <summary>
/// Repairs corruption in SCDB files with automatic backup.
/// C# 14: Uses modern async patterns with cancellation support.
/// </summary>
public sealed class RepairTool : IDisposable
{
    // Corruption report to repair
    private readonly CorruptionReport _report;
    
    // Storage provider
    private readonly SingleFileStorageProvider _provider;
    
    // Backup path
    private string? _backupPath;
    
    // Methods:
    // - RepairAsync(options) → RepairResult
    // - CreateBackup() → string (backup path)
    // - RepairHeader() → bool
    // - RepairBlocks() → int (blocks repaired)
    // - RepairWal() → bool
    // - RollbackRepair() → void
}
```

### Repair Strategies

#### 1. **Header Repair**
```
- Restore magic number from known value
- Recalculate offsets from actual data
- Rebuild file size if truncated
- Re-scan and rebuild block registry
```

#### 2. **Block Repair**
```
- Remove blocks with invalid checksums
- Rebuild block registry from scratch
- Defragment file to remove gaps
- Update header with new layout
```

#### 3. **WAL Repair**
```
- Truncate at first invalid entry
- Rebuild LSN sequence
- Discard uncommitted transactions
- Create new checkpoint
```

#### 4. **Free Space Repair**
```
- Rebuild free space bitmap
- Recalculate free space
- Merge adjacent free blocks
- Update header
```

### RepairOptions

```csharp
public sealed record RepairOptions
{
    public bool CreateBackup { get; init; } = true;
    public bool AllowDataLoss { get; init; } = false;
    public RepairAggressiveness Aggressiveness { get; init; } = RepairAggressiveness.Conservative;
    public IProgress<RepairProgress>? Progress { get; init; }
}

public enum RepairAggressiveness
{
    Conservative,  // Only safe repairs, no data loss
    Moderate,      // Some data loss acceptable
    Aggressive,    // Maximum repair, data loss likely
}
```

### RepairResult

```csharp
public sealed record RepairResult
{
    public bool Success { get; init; }
    public int IssuesRepaired { get; init; }
    public int IssuesRemaining { get; init; }
    public TimeSpan RepairTime { get; init; }
    public string? BackupPath { get; init; }
    public List<string> RepairLog { get; init; }
    public string? ErrorMessage { get; init; }
}
```

---

## 🛡️ Enhanced Error Handling

### Error Categories

```csharp
public class ScdbCorruptionException : InvalidDataException
{
    public CorruptionSeverity Severity { get; }
    public long? Offset { get; }
    public string? BlockName { get; }
}

public class ScdbRecoverableException : Exception
{
    public string RepairSuggestion { get; }
}

public class ScdbUnrecoverableException : Exception
{
    public string BackupRecommendation { get; }
}
```

### Error Messages

**Before (generic):**
```
"Invalid SCDB file: magic=0x00000000"
```

**After (detailed):**
```
"SCDB file corruption detected at offset 0x00000000:
- Magic number mismatch (expected 0x53434442, got 0x00000000)
- Possible causes: file truncation, write failure, or non-SCDB file
- Repair suggestion: Run CorruptionDetector.ValidateAsync() for full analysis
- Recovery: Use RepairTool.RepairAsync() with backup enabled"
```

---

## 🧪 Test Strategy

### CorruptionDetector Tests

```csharp
// CorruptionDetectorTests.cs
[Fact] Validate_HealthyDatabase_NoCorruption()
[Fact] Validate_CorruptHeader_DetectsCorruption()
[Fact] Validate_CorruptBlock_DetectsCorruption()
[Fact] Validate_CorruptWal_DetectsCorruption()
[Fact] Validate_QuickMode_UnderOneMillisecond()
[Fact] Validate_DeepMode_Thorough()
```

### RepairTool Tests

```csharp
// RepairToolTests.cs
[Fact] Repair_CorruptHeader_Success()
[Fact] Repair_CorruptBlock_RemovesBlock()
[Fact] Repair_CorruptWal_Truncates()
[Fact] Repair_WithBackup_CreatesBackup()
[Fact] Repair_RollbackOnFailure_RestoresOriginal()
```

---

## 📁 File Structure

```
src/SharpCoreDB/Storage/Scdb/
├── CorruptionDetector.cs       (NEW - 400 LOC)
├── RepairTool.cs               (NEW - 350 LOC)
├── ValidationMode.cs           (NEW - 50 LOC)
├── CorruptionReport.cs         (NEW - 100 LOC)
├── RepairResult.cs             (NEW - 50 LOC)
└── ScdbExceptions.cs           (NEW - 100 LOC)

tests/SharpCoreDB.Tests/Storage/
├── CorruptionDetectorTests.cs  (NEW - 300 LOC)
└── RepairToolTests.cs          (NEW - 250 LOC)

docs/scdb/
└── PRODUCTION_GUIDE.md         (NEW - 500 LOC)
```

**Total Estimated:** ~2,100 LOC

---

## ⚡ Performance Targets

| Operation | Target | Notes |
|-----------|--------|-------|
| Validate (Quick) | <1ms | Header only |
| Validate (Standard) | <10ms/MB | With checksums |
| Validate (Deep) | <50ms/MB | Full validation |
| Repair (Header) | <10ms | Header reconstruction |
| Repair (Blocks) | <100ms/MB | Block rebuild |
| Repair (WAL) | <50ms | WAL truncation |

---

## 🔐 Safety Guarantees

1. **Automatic Backup**: Always create backup before repair (unless disabled)
2. **Rollback**: Restore from backup if repair fails
3. **Conservative by Default**: No data loss unless explicitly allowed
4. **Progress Reporting**: Real-time repair progress
5. **Validation After Repair**: Re-validate to ensure success

---

## 📋 Implementation Order

1. **CorruptionDetector** (~2 hours)
   - Core detection logic
   - Validation modes
   - Report generation

2. **RepairTool** (~2 hours)
   - Repair strategies
   - Backup/rollback
   - Progress reporting

3. **Enhanced Errors** (~30 min)
   - Exception types
   - Detailed messages

4. **Tests** (~1 hour)
   - Corruption scenarios
   - Repair validation

5. **PRODUCTION_GUIDE.md** (~30 min)
   - Deployment checklist
   - Monitoring guide
   - Troubleshooting

**Total Estimated:** ~6 hours

---

## ✅ Acceptance Criteria

- [ ] CorruptionDetector detects all corruption types
- [ ] RepairTool repairs without data loss (Conservative mode)
- [ ] Backup created before repair
- [ ] Validation <10ms/MB (Standard mode)
- [ ] All tests passing
- [ ] PRODUCTION_GUIDE.md complete
- [ ] Build successful

---

**Ready for Implementation!** 🚀
