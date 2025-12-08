# GroupCommitWAL File Locking Issue - Analysis & Solutions 🔍

## Problem Statement

**Error**: `IOException: The process cannot access the file 'wal.log' because it is being used by another process`

**Root Cause**: Multiple database instances trying to use the same WAL file simultaneously in benchmarks.

---

## 🔬 Deep Analysis

### Current Architecture

```
Database Instance 1  ──┐
Database Instance 2  ──┼──> SAME wal.log file ❌ FILE LOCK CONFLICT!
Database Instance 3  ──┘
```

### File Locking Chain

1. **GroupCommitWAL constructor** opens `wal.log` in `FileMode.Append`
2. **FileStream** keeps handle open for entire lifetime (performance optimization)
3. **Background worker** continuously writes to the file
4. **Windows file locking** prevents second instance from opening same file

### Why This Happens

**In GroupCommitWAL.cs**:
```csharp
// Line 69-78: FileStream opened and kept alive
this.fileStream = new FileStream(
    this.logPath,  // SAME PATH for all instances at same dbPath!
    FileMode.Append,
    FileAccess.Write,
    FileShare.Read,  // ⚠️ Only allows READ sharing, not WRITE!
    bufferSize: 64 * 1024,
    options);
```

**Key Issue**: `FileShare.Read` means:
- ✅ Multiple readers can access the file
- ❌ Only ONE writer can have it open
- ❌ Second Database instance cannot create its own writer

---

## 💡 Solution Options (Ranked by Quality)

### Option 1: Instance-Specific WAL Files (RECOMMENDED) 🥇

**Approach**: Each database instance gets its own unique WAL file using a GUID.

**Pros**:
- ✅ Complete isolation between instances
- ✅ No file locking conflicts
- ✅ True concurrent benchmarks
- ✅ Simpler recovery logic
- ✅ Thread-safe by design

**Cons**:
- ⚠️ Multiple WAL files in same directory (minor)
- ⚠️ Cleanup needed to remove instance-specific WALs

**Implementation Complexity**: LOW ⭐⭐

---

### Option 2: Shared WAL with FileShare.Write 🥈

**Approach**: Allow multiple writers by changing `FileShare.Read` to `FileShare.ReadWrite`.

**Pros**:
- ✅ Single WAL file (traditional)
- ✅ Minimal code changes

**Cons**:
- ❌ Race conditions on concurrent writes
- ❌ Corrupted WAL if multiple instances write simultaneously
- ❌ Complex synchronization needed (defeats GroupCommitWAL purpose)
- ❌ Performance degradation due to cross-process locks

**Implementation Complexity**: HIGH ⭐⭐⭐⭐⭐
**VERDICT**: ❌ **NOT RECOMMENDED** - defeats the purpose of GroupCommitWAL

---

### Option 3: WAL Pooling/Manager Service 🥉

**Approach**: Singleton WAL manager that coordinates all instances.

**Pros**:
- ✅ Single WAL file
- ✅ Centralized coordination
- ✅ Can share background worker

**Cons**:
- ⚠️ Requires DI infrastructure changes
- ⚠️ Complex lifetime management
- ⚠️ Benchmark setup becomes harder
- ⚠️ Not suitable for true isolation

**Implementation Complexity**: MEDIUM-HIGH ⭐⭐⭐⭐

---

### Option 4: Process-Level WAL (Advanced)

**Approach**: One WAL per process, shared across all Database instances in that process.

**Pros**:
- ✅ Efficient resource usage
- ✅ Natural process boundary

**Cons**:
- ⚠️ Complex static/singleton management
- ⚠️ Breaks encapsulation
- ⚠️ Hard to test

**Implementation Complexity**: HIGH ⭐⭐⭐⭐⭐

---

## 🎯 Recommended Solution: Option 1 (Instance-Specific WAL)

### Design

```
Database Instance 1  ──> wal-{guid1}.log
Database Instance 2  ──> wal-{guid2}.log
Database Instance 3  ──> wal-{guid3}.log
```

### Benefits

1. **Zero Conflicts**: Each instance has exclusive file access
2. **True Concurrency**: Benchmarks can run in parallel
3. **Simple Recovery**: Each instance recovers its own WAL
4. **Easy Cleanup**: Delete WAL when Database is disposed
5. **No Shared State**: No cross-instance coordination needed

### Implementation Strategy

#### Step 1: Add Instance ID to GroupCommitWAL

```csharp
private readonly string instanceId;

public GroupCommitWAL(
    string dbPath,
    DurabilityMode durabilityMode = DurabilityMode.FullSync,
    int maxBatchSize = 100,
    int maxBatchDelayMs = 10,
    string? instanceId = null)  // NEW: Optional instance ID
{
    this.instanceId = instanceId ?? Guid.NewGuid().ToString("N");
    
    // Generate instance-specific WAL filename
    this.logPath = Path.Combine(dbPath, $"wal-{this.instanceId}.log");
    
    // ... rest of constructor
}
```

#### Step 2: Update Database.cs to Pass Instance ID

```csharp
// NEW: Generate unique instance ID for this Database
private readonly string _instanceId = Guid.NewGuid().ToString("N");

// In constructor:
if (this.config.UseGroupCommitWal && !isReadOnly)
{
    this.groupCommitWal = new GroupCommitWAL(
        this._dbPath,
        this.config.WalDurabilityMode,
        this.config.WalMaxBatchSize,
        this.config.WalMaxBatchDelayMs,
        this._instanceId);  // Pass instance ID
        
    // Recovery looks for instance-specific WAL
    var recoveredOps = this.groupCommitWal.CrashRecovery();
    // ... recovery logic
}
```

#### Step 3: Clean Up on Dispose

```csharp
// In Database.Dispose() or finalizer:
if (groupCommitWal != null)
{
    groupCommitWal.Dispose();
    
    // Delete instance-specific WAL after successful shutdown
    var walPath = Path.Combine(_dbPath, $"wal-{_instanceId}.log");
    if (File.Exists(walPath))
    {
        File.Delete(walPath);
    }
}
```

---

## 🔧 Alternative: Recovery from ANY WAL File

For production scenarios where you want to recover from ALL WAL files (not just instance-specific):

```csharp
public static List<ReadOnlyMemory<byte>> RecoverAll(string dbPath)
{
    var allRecords = new List<ReadOnlyMemory<byte>>();
    
    // Find all WAL files
    var walFiles = Directory.GetFiles(dbPath, "wal-*.log");
    
    foreach (var walFile in walFiles)
    {
        // Read each WAL file
        var records = ReadWalFile(walFile);
        allRecords.AddRange(records);
    }
    
    return allRecords;
}
```

---

## 📊 Performance Impact Analysis

### Before Fix (Shared WAL)
- ❌ Benchmarks FAIL with IOException
- ❌ Cannot test concurrent scenarios
- ❌ False performance data (sequential fallback)

### After Fix (Instance-Specific WAL)
- ✅ Benchmarks RUN successfully
- ✅ True concurrent testing possible
- ✅ Accurate performance measurements
- ⚠️ Minor: More disk I/O (multiple files)
- ⚠️ Minor: Slightly more memory (multiple background workers)

**Expected Overhead**: < 1% (negligible)

---

## 🎯 Implementation Plan

### Phase 1: Core Changes (Immediate)
1. Add `instanceId` parameter to GroupCommitWAL constructor
2. Generate instance-specific filename: `wal-{instanceId}.log`
3. Add instance ID to Database class
4. Pass instance ID when creating GroupCommitWAL

### Phase 2: Lifecycle Management
1. Clean up instance-specific WAL on Database.Dispose()
2. Add `IAsyncDisposable` support for proper async cleanup
3. Handle cleanup failures gracefully

### Phase 3: Recovery Enhancement (Optional)
1. Add `RecoverAll()` static method
2. Support recovering from multiple WAL files
3. Order records by timestamp for correct replay

### Phase 4: Testing
1. Test benchmarks with multiple instances
2. Verify no file locking conflicts
3. Measure performance impact
4. Test crash recovery scenarios

---

## 🚨 Edge Cases to Handle

### 1. Orphaned WAL Files
**Problem**: Database crashes before cleanup  
**Solution**: Cleanup on next startup (find old WAL files)

```csharp
// On Database startup:
CleanupOrphanedWALFiles(dbPath);

private void CleanupOrphanedWALFiles(string dbPath)
{
    var walFiles = Directory.GetFiles(dbPath, "wal-*.log");
    foreach (var walFile in walFiles)
    {
        // Check if file is old (> 1 hour)
        var info = new FileInfo(walFile);
        if (DateTime.Now - info.LastWriteTime > TimeSpan.FromHours(1))
        {
            try
            {
                // Try to recover first
                var records = GroupCommitWAL.ReadWalFile(walFile);
                if (records.Count > 0)
                {
                    // Replay records
                }
                
                // Delete after recovery
                File.Delete(walFile);
            }
            catch
            {
                // Can't delete if in use - that's OK
            }
        }
    }
}
```

### 2. Directory Full of WAL Files
**Problem**: Many concurrent instances = many WAL files  
**Solution**: Regular cleanup + max WAL file age

### 3. Recovery from Multiple WAL Files
**Problem**: Need to determine correct order  
**Solution**: Add timestamp to WAL records OR use file modification time

---

## 📝 Code Changes Required

### Files to Modify

1. ✅ `Services/GroupCommitWAL.cs`
   - Add `instanceId` parameter
   - Change `logPath` to use instance ID
   - Add static recovery helper

2. ✅ `Database.cs`
   - Add `_instanceId` field
   - Pass instance ID to GroupCommitWAL
   - Add cleanup in Dispose()

3. ✅ `Services/WalRecord.cs`
   - Add timestamp field (optional)
   - Support ordering for multi-WAL recovery

4. ✅ `DatabaseConfig.cs`
   - Add `WalInstanceIdMode` enum (optional)
   - Options: Auto (GUID), Manual, Shared (legacy)

---

## ✅ Success Criteria

After implementation, verify:

1. ✅ Multiple Database instances can coexist at same path
2. ✅ Benchmarks run without IOException
3. ✅ Each instance writes to its own WAL file
4. ✅ Crash recovery works for instance-specific WAL
5. ✅ WAL files are cleaned up on dispose
6. ✅ Performance is not significantly impacted (< 1%)
7. ✅ Concurrent benchmarks show correct throughput scaling

---

## 📚 References

- SQLite WAL: Uses rollback journal per connection
- PostgreSQL: Uses shared WAL with process-level coordination
- MongoDB: Uses oplog per replica set member
- **Our choice**: Instance-specific WAL (like SQLite approach)

---

**Recommendation**: Implement **Option 1 (Instance-Specific WAL)** for:
- ✅ Simplicity
- ✅ Correctness
- ✅ Performance
- ✅ Maintainability

**Status**: Ready to implement  
**Estimated Time**: 2-3 hours  
**Risk**: LOW ⭐  
**Impact**: HIGH ✅

