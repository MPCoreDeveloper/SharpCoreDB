# SharpCoreDB Profiling in Visual Studio 2026

## ?? Quick Start (30 seconds)

1. **Open Solution** in Visual Studio 2026
2. **Right-click** `SharpCoreDB.Profiling` project ? Set as Startup Project
3. **Press Alt+F2** (Debug ? Performance Profiler)
4. **Select tools:**
   - ? CPU Usage
   - ? .NET Object Allocation Tracking
   - ? File I/O (optional)
5. **Select launch profile:** "Profiling - PAGE_BASED"
6. **Click "Start"** button
7. **Wait** for 10K inserts to complete (~3 seconds)
8. **Report opens automatically** in Visual Studio

## ?? Understanding the Results

### CPU Usage Report

**Hot Path View** (default):
```
Method                              Inclusive   Exclusive   Call Count
??????????????????????????????????????????????????????????????????????
PageManager.InsertRecord            35.2%       12.5%       10,000
?? Storage.AppendBytes              22.7%       7.1%        10,000  ? HOTSPOT!
?  ?? FileStream.Write              15.6%       15.6%       10,000
?? BinaryPrimitives.WriteInt32      8.3%        8.3%        60,000
```

**What this means:**
- ? **Storage.AppendBytes called 10K times** ? Should use AppendBytesMultiple (1 call)
- ? **FileStream.Write 10K times** ? Indicates missing batching
- ? **BinaryPrimitives fast** ? Serialization is efficient

### Memory Allocation Report

**Top Allocations:**
```
Type                    Instances   Total Size   Avg Size
????????????????????????????????????????????????????????????
byte[]                  10,523      45.2 MB      4,400 bytes  ? Row buffers
Dictionary<string,obj>  10,000      15.3 MB      1,530 bytes  ? Row data
List<byte[]>            152         5.1 MB       33 KB        ? Batch lists
```

**What this means:**
- ? **ArrayPool working** ? byte[] should be reused (check rental count)
- ?? **15MB dictionaries** ? Consider struct-based rows
- ? **Low GC pressure** ? No Gen2 collections expected

### File I/O Report (if enabled)

**File Operations:**
```
Operation          Count    Total Time   Avg Time
??????????????????????????????????????????????????
FileStream.Write   10,000   1,250 ms     0.125 ms  ? TOO MANY!
FileStream.Flush   10,000   500 ms       0.050 ms  ? TOO MANY!
```

**What this means:**
- ? **10K flushes** ? Should be ~10 (one per batch of 1000)
- ? **Each flush 50?s** ? WAL syncing too frequently
- ?? **Target:** <100 total flushes for 10K inserts

## ?? Visual Studio Profiler Features

### 1. Flame Graph View

**How to access:**
- CPU Usage report ? Click "Flame Graph" button
- Or: Performance Profiler ? CPU Usage ? Flame Graph tab

**What to look for:**
- **Wide bars** = Hot methods (high CPU time)
- **Tall stacks** = Deep call chains (potential optimization)
- **Red bars** = Methods consuming >10% CPU

**Example:**
```
???????????????????? BulkInsertAsync (100%)
  ?????????????? InsertBatch (70%)
    ???????? AppendBytes (40%)  ? HOTSPOT: Called in loop!
      ???? FileStream.Write (25%)
    ?? SerializeRow (10%)
```

### 2. Call Tree View

**How to access:**
- CPU Usage report ? Click "Call Tree" tab

**Useful for:**
- Finding indirect hotspots
- Understanding method invocation hierarchy
- Identifying redundant calls

**Sort by:**
- **Exclusive %** ? Methods doing work themselves
- **Inclusive %** ? Methods + their callees
- **Call Count** ? Frequently called methods

### 3. Allocation Timeline

**How to access:**
- .NET Object Allocation report ? Timeline view

**What to look for:**
- **Gen2 collections** ? Memory pressure (should be zero)
- **Large spikes** ? Object[] or byte[] allocations >85KB
- **Sawtooth pattern** ? Good (Gen0/Gen1 GC working)

**Example:**
```
Memory (MB)
?
?    /\    /\    /\    ? Gen0 collections (good)
?   /  \  /  \  /  \
?  /    \/    \/    \
??????????????????????? Time
   0s   1s   2s   3s
```

### 4. Hot Path Annotations

**Automatic in code editor:**
- Red squiggles under slow lines
- Tooltip shows execution time
- Click to see detailed breakdown

**Example:**
```csharp
public void InsertBatch(List<...> rows)
{
    foreach (var row in rows)  // ? 2,500ms (90% of method time)
    {
        storage.AppendBytes(...);  // ?? HOT PATH: Called 10K times
    }
}
```

## ?? Visual Studio Launch Profiles

### Profile 1: PAGE_BASED Mode
```
Profile: Profiling - PAGE_BASED
Args: page-based
Records: 10,000
Expected time: 2,500-3,000ms (before optimization)
```

**Use for:**
- Baseline profiling
- Validating page-based optimizations
- OLTP workload analysis

### Profile 2: COLUMNAR Mode
```
Profile: Profiling - COLUMNAR
Args: columnar
Records: 10,000
Expected time: 1,200-1,500ms (before optimization)
```

**Use for:**
- Append-only storage profiling
- OLAP workload analysis
- Comparing vs PAGE_BASED

### Profile 3: Continuous Mode
```
Profile: Profiling - Continuous
Args: continuous
Batch size: 1,000 records/iteration
Duration: Until Ctrl+C
```

**Use for:**
- Long-running profiling
- Memory leak detection
- Steady-state performance

### Profile 4: Comparative Mode
```
Profile: Profiling - Comparative
Args: compare
Runs: Both PAGE_BASED and COLUMNAR
Total records: 20,000
```

**Use for:**
- Side-by-side comparison
- Validating storage engine choice
- A/B testing configurations

## ?? Step-by-Step Optimization Workflow

### Week 1: Baseline + Fix #1

**Monday: Baseline Profiling**
1. Set `SharpCoreDB.Profiling` as startup project
2. Select launch profile: **Profiling - PAGE_BASED**
3. Alt+F2 ? CPU Usage + Allocations ? Start
4. Save report: File ? Export ? "Baseline_PageBased.diagsession"

**Tuesday: Analyze Results**
1. Open "Baseline_PageBased.diagsession"
2. CPU Usage ? Hot Path view
3. Look for `Storage.AppendBytes` in Call Tree
4. Note: Called 10,000 times ? **This is the problem!**

**Wednesday: Apply Fix #1**
1. Open `DataStructures\Table.CRUD.cs`
2. Find `InsertBatch()` method
3. Replace loop with single `AppendBytesMultiple` call
4. Build ? Rebuild Solution (Ctrl+Shift+B)

**Thursday: Validate Fix**
1. Alt+F2 ? CPU Usage + Allocations ? Start (same profile)
2. Save report: "After_Fix1_PageBased.diagsession"
3. Performance Profiler ? Compare Reports
4. Select: Baseline vs After_Fix1
5. **Expected:** 4-5x improvement, AppendBytes count: 10,000 ? 1

**Friday: Document Results**
1. Screenshot comparison report
2. Add to CHANGELOG.md
3. Commit changes
4. Tag: `perf/fix1-appendbytes-batching`

### Week 2: Fix #2 (Page Flush)

Repeat same workflow for next optimization...

## ?? Success Metrics

### Visual Studio Report Indicators

**? Good Profile:**
- No methods >10% exclusive CPU time
- Allocation timeline shows only Gen0/Gen1 GCs
- File I/O shows <100 operations for 10K inserts
- Call tree depth <15 levels

**? Bad Profile:**
- Single method >30% exclusive time
- Gen2 GC collections visible
- File I/O shows >1000 operations
- Deep call stacks (>20 levels)

### Performance Targets

| Metric | Before | Target | Status |
|--------|--------|--------|--------|
| **10K INSERT time** | 2,776ms | 200-300ms | ? In progress |
| **AppendBytes calls** | 10,000 | 1 | ? Fix #1 pending |
| **Page flushes** | 10,000 | 10 | ? Fix #2 pending |
| **Memory usage** | 68 MB | 15-20 MB | ? Fix #1 pending |
| **Gen2 GC count** | 0 ? | 0 | ? Already optimal |

## ?? Advanced Profiling

### PerfTips (Inline Performance)

**Enable:**
1. Tools ? Options ? Debugging ? General
2. ? Show elapsed time PerfTip while debugging

**Use:**
- Set breakpoint at start of `InsertBatch()`
- F5 (Start Debugging)
- F10 (Step Over) ? See elapsed time appear as tooltip
- Identify slow lines immediately

### Diagnostic Tools Window

**Enable:**
1. F5 (Start Debugging)
2. Debug ? Windows ? Show Diagnostic Tools (Ctrl+Alt+F2)

**Live monitoring:**
- Memory usage graph (real-time)
- CPU usage spikes
- Events timeline
- Breakpoints with time stamps

### IntelliTrace (VS Enterprise only)

**If you have VS Enterprise:**
1. Debug ? IntelliTrace ? IntelliTrace Events
2. Start debugging (F5)
3. After profiling run completes:
   - View ? Other Windows ? IntelliTrace
   - Browse historical events
   - Step backward through execution
   - See parameter values at any point

**Useful for:**
- Understanding why certain code paths execute
- Debugging race conditions
- Analyzing transaction lifecycle

## ?? Deliverables Checklist

After each optimization iteration:

- [ ] Performance report saved (.diagsession file)
- [ ] Comparison report generated (before vs after)
- [ ] Screenshots captured
- [ ] Metrics documented in OPTIMIZATION_ROADMAP.md
- [ ] Code changes committed to Git
- [ ] Unit tests updated
- [ ] Benchmark results added to BenchmarkDotNet reports

## ?? Troubleshooting

### Issue: "No performance data collected"

**Cause:** Profiler not attached properly

**Fix:**
1. Ensure project is set as Startup Project
2. Build in Release mode (not Debug)
3. Disable "Enable Just My Code" in Options
4. Try: Debug ? Attach to Process ? Select running process

### Issue: "Report shows only System.* methods"

**Cause:** Symbols not loaded

**Fix:**
1. Project Properties ? Build ? Debug symbols: Full
2. Build ? Rebuild Solution
3. Tools ? Options ? Debugging ? Symbols
4. Add symbol cache location

### Issue: "Visual Studio hangs during profiling"

**Cause:** Too much data collected

**Fix:**
1. Use shorter test runs (1000 records instead of 10,000)
2. Disable unnecessary profilers (only CPU Usage)
3. Increase VS memory limit:
   - Tools ? Options ? Environment ? Memory
   - Increase "Maximum memory for Visual Studio"

### Issue: "Gen2 collections appearing"

**Cause:** Memory pressure from large objects

**Fix:**
1. Check .NET Object Allocation report
2. Find allocations >85KB
3. Use ArrayPool for large buffers
4. Consider object pooling

## ?? Additional Resources

- **VS Performance Profiler docs:** [learn.microsoft.com/visualstudio/profiling](https://learn.microsoft.com/en-us/visualstudio/profiling/)
- **Diagnostic Tools:** [learn.microsoft.com/visualstudio/debugger/diagnostic-tools](https://learn.microsoft.com/en-us/visualstudio/debugger/diagnostic-tools-debugger)
- **PerfTips:** [learn.microsoft.com/visualstudio/profiling/perftips](https://learn.microsoft.com/en-us/visualstudio/profiling/perftips)
- **IntelliTrace:** [learn.microsoft.com/visualstudio/debugger/intellitrace](https://learn.microsoft.com/en-us/visualstudio/debugger/intellitrace)

---

**Last Updated:** 2025-01-16  
**Visual Studio Version:** 2026  
**Target Framework:** .NET 10  
**Status:** ? Ready to use
