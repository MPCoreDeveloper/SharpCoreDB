# SCDB Phase 3: WAL & Recovery Design

**Created:** 2026-01-28  
**Status:** 📝 Design Phase

---

## 🎯 Phase 3 Overview

**Current Status:** WalManager ~60% complete  
**Goal:** 100% functional WAL with crash recovery

---

## 📊 **What We Have (60%)**

### ✅ WalManager - Implemented
1. **Transaction Management**
   - `BeginTransaction()` ✅
   - `CommitTransactionAsync()` ✅
   - `RollbackTransaction()` ✅

2. **Logging API**
   - `LogWriteAsync()` ✅
   - `LogDeleteAsync()` ✅
   - `CheckpointAsync()` ✅

3. **Structures** (ScdbStructures.cs)
   - `WalHeader` (64 bytes) ✅
   - `WalEntry` (4096 bytes) ✅
   - `WalOperation` enum ✅

4. **Basic Features**
   - In-memory queue (`_pendingEntries`) ✅
   - Transaction tracking ✅
   - LSN management ✅

---

## ❌ **What We Need (40%)**

### 1. WalManager - Missing Features

#### A. Circular Buffer Persistence
```csharp
// Current: In-memory queue only
// Need: Write to disk at _walOffset with wraparound

private async Task FlushWalAsync(CancellationToken ct)
{
    // ❌ TODO: Write to circular buffer
    // ❌ TODO: Update head/tail pointers
    // ❌ TODO: Handle wraparound
    // ❌ TODO: Persist WalHeader
}
```

#### B. WAL Loading
```csharp
// ❌ Missing: Load WAL from disk on startup
public void LoadWal()
{
    // Read WalHeader
    // Scan circular buffer
    // Identify incomplete transactions
}
```

#### C. Entry Persistence
```csharp
// ❌ Need: Serialize WalEntry to disk
private async Task WriteEntryAsync(WalEntry entry)
{
    // Convert to binary
    // Calculate checksum
    // Write to circular buffer position
}
```

---

### 2. RecoveryManager - NEW Component

**Purpose:** Replay WAL after crash to restore consistent state.

**Architecture:**
```csharp
public sealed class RecoveryManager
{
    private readonly SingleFileStorageProvider _provider;
    private readonly WalManager _walManager;
    
    // Redo operations from WAL
    public async Task RecoverAsync(CancellationToken ct)
    {
        // 1. Load WAL entries since last checkpoint
        // 2. Replay committed transactions
        // 3. Rollback uncommitted transactions
        // 4. Update database state
    }
    
    // Analyze WAL for recovery info
    public RecoveryInfo AnalyzeWal()
    {
        // Find last checkpoint
        // Identify committed/uncommitted transactions
        // Calculate recovery time estimate
    }
}
```

---

## 🏗️ **Implementation Plan**

### Step 3: Complete WalManager Persistence (~2 hours)

**Tasks:**
1. Implement circular buffer write
   - Calculate buffer position
   - Handle wraparound
   - Update head/tail pointers

2. Serialize WalEntry
   - Convert WalLogEntry → WalEntry
   - Calculate SHA-256 checksum
   - Write to file at calculated offset

3. Persist WalHeader
   - Update CurrentLsn, LastCheckpoint
   - Update HeadOffset, TailOffset
   - Write to _walOffset

4. Load WAL on startup
   - Read WalHeader
   - Validate magic/version
   - Scan for entries since last checkpoint

**Files:**
- `src/SharpCoreDB/Storage/WalManager.cs` (complete persistence)

---

### Step 4: Implement RecoveryManager (~3 hours)

**Tasks:**
1. Create RecoveryManager class
   - Constructor with dependencies
   - Recovery state tracking

2. WAL Analysis
   - Scan entries since checkpoint
   - Build transaction map
   - Identify commit/rollback

3. Redo Logic
   - Replay committed transactions
   - Apply operations in LSN order
   - Update block registry

4. Undo Logic
   - Rollback uncommitted transactions
   - Restore previous state

5. Integration
   - Call from SingleFileStorageProvider.Open()
   - Automatic recovery on startup

**Files:**
- `src/SharpCoreDB/Storage/Scdb/RecoveryManager.cs` (new)
- `src/SharpCoreDB/Storage/Scdb/RecoveryInfo.cs` (new)

---

### Step 5: Checkpoint Coordination (~1 hour)

**Tasks:**
1. Flush coordination
   - BlockRegistry flush ✅ (already done)
   - FreeSpaceManager flush ✅ (already done)
   - WAL checkpoint (new)

2. Checkpoint triggers
   - Time-based (every 60s)
   - Size-based (every 1000 transactions)
   - Manual (on demand)

**Files:**
- `src/SharpCoreDB/Storage/SingleFileStorageProvider.cs` (enhance)

---

### Step 6: Crash Recovery Tests (~2 hours)

**Tests:**
1. **BasicRecoveryTest**
   - Write data in transaction
   - Simulate crash (no flush)
   - Reopen and verify data

2. **MultiTransactionRecovery**
   - Multiple transactions
   - Some committed, some not
   - Verify only committed data persists

3. **CheckpointRecovery**
   - Write before checkpoint
   - Write after checkpoint
   - Verify recovery only replays after checkpoint

4. **CorruptedWalEntry**
   - Corrupt checksum
   - Verify graceful handling
   - Continue recovery with valid entries

**Files:**
- `tests/SharpCoreDB.Tests/Storage/CrashRecoveryTests.cs` (new)
- `tests/SharpCoreDB.Tests/Storage/WalManagerTests.cs` (new)

---

### Step 7: Performance Benchmarks (~1 hour)

**Benchmarks:**
1. **WAL Write Performance**
   - Single entry: <100µs
   - 1000 entries: <5ms
   - 10000 entries: <50ms

2. **Recovery Performance**
   - 1000 transactions: <100ms
   - 10000 transactions: <1s
   - Checkpoint impact: <10ms

**Files:**
- `tests/SharpCoreDB.Tests/Storage/WalBenchmarks.cs` (new)

---

### Step 8: Documentation (~30 min)

**Documents:**
- `docs/scdb/PHASE3_COMPLETE.md`
- Update `docs/scdb/IMPLEMENTATION_STATUS.md`
- WAL format documentation

---

## 📐 **Key Design Decisions**

### 1. Circular Buffer Strategy

**Choice:** Fixed-size circular buffer with wraparound  
**Reason:** PostgreSQL-inspired, bounded memory, predictable performance

```
WAL Layout:
[WalHeader(64B)] [Entry0(4096B)] [Entry1(4096B)] ... [EntryN(4096B)]

Head ──> Oldest entry (to be overwritten next)
Tail ──> Newest entry (last written)

Wraparound: tail = (tail + 1) % maxEntries
```

---

### 2. Checksum Strategy

**Choice:** SHA-256 per entry  
**Reason:** Strong corruption detection, hardware-accelerated on modern CPUs

```csharp
// Checksum calculation
using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
sha256.AppendData(headerBytes);
sha256.AppendData(dataBytes);
var checksum = sha256.GetHashAndReset();
```

---

### 3. Recovery Strategy

**Choice:** REDO-only (no UNDO)  
**Reason:** Simpler, faster, sufficient for write-ahead guarantee

**Recovery Algorithm:**
```
1. Load WalHeader
2. If no checkpoint → start from entry 0
3. If checkpoint → start from LastCheckpoint + 1
4. Scan entries:
   - TransactionBegin → mark transaction as active
   - TransactionCommit → mark transaction as committed
   - Update/Insert/Delete → queue operation
5. Replay:
   - For each committed transaction:
     - Apply operations in LSN order
     - Update block registry
     - Update FSM
6. Discard:
   - Uncommitted transactions (no action needed)
```

---

## 🎯 **Success Criteria**

### Performance
- [x] WAL write <5ms per 1000 entries
- [ ] Recovery <100ms per 1000 transactions
- [ ] Checkpoint <10ms overhead

### Reliability
- [ ] Zero data loss on crash
- [ ] Corrupt entry detection (checksum)
- [ ] Graceful degradation

### Testing
- [ ] 10+ crash recovery scenarios
- [ ] 5+ performance benchmarks
- [ ] 100% code coverage for critical paths

---

## 🔮 **Phase 6 Integration**

**Row Overflow** will benefit from WAL:
- Overflow chain allocation logged
- Compression operations logged
- Chain traversal recovery

**Example:**
```csharp
// Phase 6: OverflowPageManager uses WAL
await _walManager.LogWriteAsync(
    blockName: $"overflow_chain_{chainId}",
    offset: extentStart,
    data: compressedData
);
```

---

**Status:** Design complete, ready for implementation  
**Next:** Step 3 - Complete WalManager persistence
