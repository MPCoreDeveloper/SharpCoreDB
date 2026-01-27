# Test Verification Plan - IterationCleanup Fix

## Overview
This document outlines the testing strategy to verify the IterationCleanup fix is working correctly.

## Changes Summary
1. **Benchmark Fix**: Removed dispose/recreate logic from `IterationCleanup()`
2. **Database Fix**: Enhanced `ForceSave()` to persist schema metadata via `TableDirectoryManager.Flush()`

## Test Categories

### 1. Immediate Verification (Manual)

#### Test 1: Run Benchmark Without Crash
**Goal:** Verify the "Table bench_records does not exist" exception is resolved

**Steps:**
```bash
cd tests\SharpCoreDB.Benchmarks
dotnet run -c Release -- --filter *Insert*
```

**Expected Result:**
- ✅ No `InvalidOperationException` thrown
- ✅ All Insert benchmarks complete successfully
- ✅ No "[IterationCleanup] Warning" messages in output

**Failure Indicators:**
- ❌ Exception: "Table bench_records does not exist"
- ❌ Exception during database creation/reopening
- ❌ Benchmark crashes mid-iteration

#### Test 2: Full Benchmark Suite
**Goal:** Verify all benchmark categories work across iterations

**Steps:**
```bash
dotnet run -c Release -- --filter *SCDB_Single*
```

**Expected Result:**
- ✅ Insert, Update, Select benchmarks all pass
- ✅ Multiple iterations complete without errors
- ✅ Consistent performance (no degradation over iterations)

### 2. Schema Persistence Verification (Unit Test)

#### Test 3: Schema Survives ForceSave + Reopen
**Create Test:** `tests\SharpCoreDB.Tests\SingleFileSchemaPersistenceTest.cs`

```csharp
[Fact]
public void ForceSave_Should_Persist_Schema_Metadata()
{
    // Arrange
    var dbPath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.scdb");
    var factory = new DatabaseFactory(services);
    var options = DatabaseOptions.CreateSingleFileDefault(enableEncryption: false);
    
    // Act - Create table and force save
    var db1 = factory.CreateWithOptions(dbPath, "password", options);
    db1.ExecuteSQL("CREATE TABLE test_table (id INTEGER PRIMARY KEY, name TEXT)");
    db1.ForceSave();
    ((IDisposable)db1).Dispose();
    
    // Reopen database
    options.CreateImmediately = false;
    var db2 = factory.CreateWithOptions(dbPath, "password", options);
    
    // Assert - Table should exist in reopened database
    Assert.True(db2.Tables.ContainsKey("test_table"));
    
    // Cleanup
    ((IDisposable)db2).Dispose();
    File.Delete(dbPath);
}
```

**Expected Result:**
- ✅ Table exists after reopen
- ✅ No exception thrown
- ✅ Schema metadata was successfully persisted

### 3. Crash Recovery Verification (Integration Test)

#### Test 4: Simulate Process Crash After ForceSave
**Goal:** Verify database can recover from unexpected shutdown

**Steps:**
```csharp
[Fact]
public void Database_Should_Recover_After_Crash_Following_ForceSave()
{
    var dbPath = Path.Combine(Path.GetTempPath(), $"crash_test_{Guid.NewGuid()}.scdb");
    var factory = new DatabaseFactory(services);
    var options = DatabaseOptions.CreateSingleFileDefault(enableEncryption: false);
    
    // Phase 1: Create and populate database
    var db1 = factory.CreateWithOptions(dbPath, "password", options);
    db1.ExecuteSQL("CREATE TABLE crash_test (id INTEGER PRIMARY KEY, value TEXT)");
    db1.ExecuteSQL("INSERT INTO crash_test (id, value) VALUES (1, 'test')");
    db1.ForceSave(); // Persist everything
    
    // Simulate crash - dispose without graceful shutdown
    ((IDisposable)db1).Dispose();
    
    // Phase 2: Reopen and verify recovery
    options.CreateImmediately = false;
    var db2 = factory.CreateWithOptions(dbPath, "password", options);
    
    Assert.True(db2.Tables.ContainsKey("crash_test"));
    var rows = db2.ExecuteQuery("SELECT * FROM crash_test WHERE id = 1");
    Assert.Single(rows);
    Assert.Equal("test", rows[0]["value"]);
    
    // Cleanup
    ((IDisposable)db2).Dispose();
    File.Delete(dbPath);
}
```

**Expected Result:**
- ✅ Database reopens successfully
- ✅ Schema is intact
- ✅ Data is intact
- ✅ No corruption detected

### 4. Performance Regression Test

#### Test 5: IterationCleanup Performance
**Goal:** Verify simplified cleanup doesn't degrade performance

**Baseline:**
- Previous dispose/recreate: ~5-10ms overhead per iteration

**Expected:**
- New flush-only: <1ms overhead per iteration
- Net improvement: ~5-9ms faster per iteration

**Measurement:**
```bash
# Run benchmark with detailed timing
dotnet run -c Release -- --filter *SCDB_Single_Unencrypted_Insert* --iterationTime 5000
```

**Metrics to Check:**
- Mean iteration time
- Allocation/iteration
- Gen 0/1/2 collections

### 5. Edge Case Testing

#### Test 6: Multiple Tables Schema Persistence
**Goal:** Verify multiple tables are all persisted correctly

```csharp
[Fact]
public void ForceSave_Should_Persist_Multiple_Tables()
{
    var dbPath = Path.Combine(Path.GetTempPath(), $"multi_{Guid.NewGuid()}.scdb");
    var factory = new DatabaseFactory(services);
    var options = DatabaseOptions.CreateSingleFileDefault(enableEncryption: false);
    
    var db1 = factory.CreateWithOptions(dbPath, "password", options);
    
    // Create multiple tables
    db1.ExecuteSQL("CREATE TABLE table1 (id INTEGER PRIMARY KEY)");
    db1.ExecuteSQL("CREATE TABLE table2 (id INTEGER PRIMARY KEY)");
    db1.ExecuteSQL("CREATE TABLE table3 (id INTEGER PRIMARY KEY)");
    db1.ForceSave();
    ((IDisposable)db1).Dispose();
    
    // Reopen
    options.CreateImmediately = false;
    var db2 = factory.CreateWithOptions(dbPath, "password", options);
    
    Assert.Equal(3, db2.Tables.Count);
    Assert.True(db2.Tables.ContainsKey("table1"));
    Assert.True(db2.Tables.ContainsKey("table2"));
    Assert.True(db2.Tables.ContainsKey("table3"));
    
    ((IDisposable)db2).Dispose();
    File.Delete(dbPath);
}
```

#### Test 7: ForceSave Called Multiple Times
**Goal:** Verify idempotency of ForceSave

```csharp
[Fact]
public void ForceSave_Multiple_Calls_Should_Be_Safe()
{
    var dbPath = Path.Combine(Path.GetTempPath(), $"multi_save_{Guid.NewGuid()}.scdb");
    var factory = new DatabaseFactory(services);
    var options = DatabaseOptions.CreateSingleFileDefault(enableEncryption: false);
    
    var db = factory.CreateWithOptions(dbPath, "password", options);
    db.ExecuteSQL("CREATE TABLE test (id INTEGER PRIMARY KEY)");
    
    // Call ForceSave multiple times - should not throw
    db.ForceSave();
    db.ForceSave();
    db.ForceSave();
    
    // Verify still functional
    db.ExecuteSQL("INSERT INTO test (id) VALUES (1)");
    var rows = db.ExecuteQuery("SELECT * FROM test");
    Assert.Single(rows);
    
    ((IDisposable)db).Dispose();
    File.Delete(dbPath);
}
```

## Success Criteria

### Critical (Must Pass)
- ✅ Test 1: Benchmark runs without "Table does not exist" exception
- ✅ Test 3: Schema persists after ForceSave + reopen
- ✅ Test 4: Crash recovery works correctly

### Important (Should Pass)
- ✅ Test 2: Full benchmark suite completes
- ✅ Test 5: Performance doesn't degrade
- ✅ Test 6: Multiple tables persist correctly

### Nice to Have (Good to Verify)
- ✅ Test 7: ForceSave is idempotent
- ✅ No memory leaks during repeated ForceSave calls
- ✅ File size remains stable (no unnecessary growth)

## Rollback Plan

If any critical test fails:

1. **Revert benchmark change only:**
   ```bash
   git checkout HEAD -- tests/SharpCoreDB.Benchmarks/StorageEngineComparisonBenchmark.cs
   ```

2. **Revert database layer change:**
   ```bash
   git checkout HEAD -- src/SharpCoreDB/DatabaseExtensions.cs
   ```

3. **Revert both:**
   ```bash
   git checkout HEAD -- tests/SharpCoreDB.Benchmarks/StorageEngineComparisonBenchmark.cs src/SharpCoreDB/DatabaseExtensions.cs
   ```

## Next Steps

1. **Run manual tests** (Test 1 & 2) to verify immediate fix
2. **Create unit tests** (Test 3-7) for regression prevention
3. **Monitor production** benchmarks for any unexpected behavior
4. **Document findings** in ITERATION_CLEANUP_FIX.md

## Automated Test Execution

```bash
# Run all SharpCoreDB tests
dotnet test tests/SharpCoreDB.Tests --filter "FullyQualifiedName~SingleFileSchemaPersis"

# Run benchmarks in verification mode
dotnet run --project tests/SharpCoreDB.Benchmarks -c Release -- --filter *Insert* --job dry
```

---

**Status:** Ready for Testing  
**Priority:** High  
**Estimated Time:** 30-60 minutes for full verification
