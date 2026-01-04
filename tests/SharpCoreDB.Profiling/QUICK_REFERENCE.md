# Visual Studio 2026 Profiling - Quick Reference Card

## ?? One-Click Profiling

### Method 1: Launch Profiles (Fastest)
```
1. Ctrl+Shift+X (Test Explorer)
2. Select: "Profiling - PAGE_BASED" from dropdown
3. Click ?? button
4. Wait 3 seconds
5. Report opens automatically ?
```

### Method 2: Performance Profiler (Most Features)
```
1. Alt+F2 (Debug ? Performance Profiler)
2. Select tools:
   ? CPU Usage
   ? .NET Object Allocation
3. Click "Start"
4. Report opens after completion ?
```

### Method 3: Diagnostic Tools (Live View)
```
1. F5 (Start Debugging)
2. Ctrl+Alt+F2 (Show Diagnostic Tools)
3. Watch metrics in real-time:
   - Memory graph
   - CPU spikes
   - GC collections
```

---

## ?? What to Look For (30-Second Checklist)

### ? Good Signs
- [ ] No single method >10% CPU time
- [ ] Only Gen0/Gen1 GC collections
- [ ] <100 FileStream operations
- [ ] Call tree depth <15 levels
- [ ] Total time <500ms for 10K inserts

### ? Bad Signs (Need Fixing!)
- [ ] ?? Storage.AppendBytes called >1000 times
- [ ] ?? FlushDirtyPages called >100 times  
- [ ] ?? Gen2 GC collections present
- [ ] ?? Memory usage >50MB
- [ ] ?? Total time >2000ms

---

## ?? Known Hotspots (Priority Order)

### 1?? AppendBytes Loop (5-10x impact)
```
Location: DataStructures\Table.CRUD.cs:200
Symptom: Called 10,000 times in CPU report
Fix: Replace loop with AppendBytesMultiple
Time: 30 minutes
```

### 2?? Page Flush After Every Insert (3-5x impact)
```
Location: Storage\Engines\PageBasedEngine.cs:60
Symptom: FlushDirtyPages in CPU hot path
Fix: Remove immediate flush, only flush on commit
Time: 1 hour
```

### 3?? Excessive WAL Syncs (2-3x impact)
```
Location: DatabaseConfig instantiation
Symptom: 10K FileStream.Flush in File I/O report
Fix: Enable GroupCommitWAL, GroupCommitSize=1000
Time: 5 minutes
```

### 4?? Small Page Cache (10x on hot data)
```
Location: DatabaseConfig instantiation
Symptom: High PageCache miss rate in Allocation report
Fix: PageCacheCapacity = 10000
Time: 2 minutes
```

### 5?? Linear Page Search (100x improvement)
```
Location: Storage\Hybrid\PageManager.cs:150
Symptom: FindPageWithSpace high CPU time
Fix: Replace O(n) search with O(1) bitmap
Time: 4 hours
```

---

## ?? Interpreting CPU Report

### Hot Path View (Default)
```
Method Name               | Inclusive | Exclusive | Calls
?????????????????????????????????????????????????????????
PageManager.InsertRecord  | 35.2%     | 12.5%     | 10,000
?? AppendBytes            | 22.7%     | 7.1%      | 10,000  ?
?  ?? FileStream.Write    | 15.6%     | 15.6%     | 10,000  ?
?? SerializeRow           | 8.3%      | 8.3%      | 10,000  ?
```

**What this means:**
- ? **AppendBytes: 10K calls** ? Use batch API
- ? **FileStream.Write: 10K** ? Missing batching
- ? **SerializeRow: OK** ? Efficient enough

---

## ?? Interpreting Memory Report

### Top Allocations
```
Type                | Instances | Size    | Avg Size
???????????????????????????????????????????????????????
byte[]              | 10,523    | 45 MB   | 4.4 KB  ? Check ArrayPool
Dictionary<...>     | 10,000    | 15 MB   | 1.5 KB  ? Row data
List<byte[]>        | 152       | 5 MB    | 33 KB   ? Batch lists
```

**What this means:**
- ? **45MB byte[]** ? Should be reused via ArrayPool
- ?? **15MB dictionaries** ? Consider struct rows
- ? **5MB lists** ? Acceptable overhead

### GC Timeline
```
Good (sawtooth):        Bad (spikes):
 
Memory                  Memory
? /\  /\  /\            ?     /\
?/  \/  \/  \           ?    /  \
??????????????          ????/????\????
 Gen0 GC ?             Gen2 GC ?
```

---

## ?? Visual Studio Shortcuts

| Shortcut | Action |
|----------|--------|
| **Alt+F2** | Open Performance Profiler |
| **Ctrl+Alt+F2** | Show Diagnostic Tools |
| **F5** | Start Debugging |
| **Ctrl+Shift+B** | Build Solution |
| **Ctrl+M, O** | Collapse All Methods |
| **Ctrl+M, L** | Expand All Methods |
| **Ctrl+,** | Go To All (find methods) |

---

## ?? Report File Locations

```
Profile runs saved to:
  SharpCoreDB.Profiling\bin\Release\net10.0\

Report files (.diagsession):
  - Baseline_PageBased.diagsession
  - After_Fix1_PageBased.diagsession
  - After_Fix2_PageBased.diagsession
  ...

To compare:
  Performance Profiler ? Compare Reports
  Select: Baseline vs After_Fix1
```

---

## ?? Quick Fixes Configuration

### Enable All Performance Analyzers
```
1. Right-click SharpCoreDB project
2. Properties ? Code Analysis
3. Set "Analysis Level" to "All"
4. Build ? Warnings will show performance issues
```

### Set Startup Project
```
1. Right-click SharpCoreDB.Profiling in Solution Explorer
2. Set as Startup Project
3. Launch profile dropdown appears in toolbar
```

### Enable PerfTips
```
1. Tools ? Options ? Debugging ? General
2. ? Show elapsed time PerfTip while debugging
3. F5 ? See timing tooltips while stepping
```

---

## ?? Common Issues

### "No data collected"
? Build in **Release mode** (not Debug)

### "Only System.* methods shown"
? Project Properties ? Debug symbols: **Full**

### "Report takes long to load"
? Use shorter runs (1K records instead of 10K)

### "Gen2 GC collections appearing"
? Check allocations >85KB, use ArrayPool

---

## ?? Visual Studio Resources

- **Performance Profiler:** Alt+F2
- **Diagnostic Tools:** Ctrl+Alt+F2 (while debugging)
- **IntelliTrace:** Debug ? IntelliTrace (Enterprise only)
- **Code Lens:** Shows perf metrics above methods
- **PerfTips:** Hover over code while debugging

---

## ? Success Criteria

After profiling and optimization:

```
? CPU Usage:
   - No method >10% exclusive time
   - AppendBytes call count: 1 (was 10,000)
   
? Memory:
   - Total allocations <20MB (was 68MB)
   - No Gen2 collections
   
? File I/O:
   - Total flushes <100 (was 10,000)
   - Batch size >500 records
   
? Performance:
   - 10K inserts <300ms (was 2,776ms)
   - Throughput >33 records/ms (was 3.6)
```

---

**Print this page** and keep it next to your monitor during profiling sessions! ???

---

**Last Updated:** 2025-01-16  
**Visual Studio:** 2026  
**Framework:** .NET 10
