# SharpCoreDB Insert Pipeline Profiling Scripts
# .NET 10 Performance Analysis with dotnet-trace
# Target: Identify insert degradation hotspots (e.g., cache misses, WAL syncs, page flushes)

# ============================================================
# PREREQUISITES
# ============================================================
# Install dotnet-trace:
#   dotnet tool install --global dotnet-trace
#   dotnet tool update --global dotnet-trace

# Build profiling project:
#   dotnet build ..\SharpCoreDB.Profiling\SharpCoreDB.Profiling.csproj -c Release

# ============================================================
# SCENARIO 1: CPU SAMPLING (High Frequency)
# ============================================================
# Captures CPU hotspots with high sampling rate
# Use for: Finding expensive method calls, tight loops, SIMD inefficiencies

function Start-CpuSampling {
    Write-Host "===== CPU SAMPLING MODE =====" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "? Starting profiling app..." -ForegroundColor Green
    
    # Start app in background
    $process = Start-Process -FilePath "dotnet" `
        -ArgumentList "run --project ..\SharpCoreDB.Profiling\SharpCoreDB.Profiling.csproj -c Release" `
        -PassThru `
        -NoNewWindow
    
    # Wait for process to initialize
    Start-Sleep -Seconds 2
    
    Write-Host "? Process ID: $($process.Id)" -ForegroundColor Yellow
    Write-Host "? Press ENTER in the app window to start profiling..." -ForegroundColor Yellow
    Write-Host ""
    
    # Wait for user to start profiling in app
    Read-Host "Press ENTER here after you've started the profiling in the app window"
    
    Write-Host "? Starting CPU trace collection..." -ForegroundColor Green
    
    # Collect CPU trace with high-frequency sampling
    dotnet-trace collect `
        --process-id $process.Id `
        --providers "Microsoft-Windows-DotNETRuntime:0x1F000080018:5,Microsoft-Windows-DotNETRuntimeRundown:0x1F000080018:5" `
        --output ".\traces\cpu_sampling_$(Get-Date -Format 'yyyyMMdd_HHmmss').nettrace" `
        --format Speedscope
    
    Write-Host ""
    Write-Host "? CPU trace saved! Open with speedscope.app" -ForegroundColor Green
}

# ============================================================
# SCENARIO 2: ALLOCATION TRACKING (GC + Allocations)
# ============================================================
# Captures heap allocations and GC behavior
# Use for: Finding excessive allocations, boxing, large object allocations

function Start-AllocationTracking {
    Write-Host "===== ALLOCATION TRACKING MODE =====" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "? Starting profiling app..." -ForegroundColor Green
    
    $process = Start-Process -FilePath "dotnet" `
        -ArgumentList "run --project ..\SharpCoreDB.Profiling\SharpCoreDB.Profiling.csproj -c Release" `
        -PassThru `
        -NoNewWindow
    
    Start-Sleep -Seconds 2
    
    Write-Host "? Process ID: $($process.Id)" -ForegroundColor Yellow
    Write-Host "? Press ENTER in the app window to start profiling..." -ForegroundColor Yellow
    Write-Host ""
    
    Read-Host "Press ENTER here after you've started the profiling in the app window"
    
    Write-Host "? Starting allocation trace collection..." -ForegroundColor Green
    
    # Collect allocation trace with GC events
    dotnet-trace collect `
        --process-id $process.Id `
        --providers "Microsoft-Windows-DotNETRuntime:0x1DFFFFFFF:5" `
        --output ".\traces\allocations_$(Get-Date -Format 'yyyyMMdd_HHmmss').nettrace" `
        --format Speedscope
    
    Write-Host ""
    Write-Host "? Allocation trace saved! Analyze with PerfView or speedscope.app" -ForegroundColor Green
}

# ============================================================
# SCENARIO 3: FULL DIAGNOSTICS (CPU + Allocations + I/O)
# ============================================================
# Captures comprehensive diagnostics including I/O events
# Use for: Complete profiling session, finding storage I/O bottlenecks

function Start-FullDiagnostics {
    Write-Host "===== FULL DIAGNOSTICS MODE =====" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "? Starting profiling app..." -ForegroundColor Green
    
    $process = Start-Process -FilePath "dotnet" `
        -ArgumentList "run --project ..\SharpCoreDB.Profiling\SharpCoreDB.Profiling.csproj -c Release" `
        -PassThru `
        -NoNewWindow
    
    Start-Sleep -Seconds 2
    
    Write-Host "? Process ID: $($process.Id)" -ForegroundColor Yellow
    Write-Host "? Press ENTER in the app window to start profiling..." -ForegroundColor Yellow
    Write-Host ""
    
    Read-Host "Press ENTER here after you've started the profiling in the app window"
    
    Write-Host "? Starting full diagnostic trace collection..." -ForegroundColor Green
    
    # Collect full diagnostic trace
    dotnet-trace collect `
        --process-id $process.Id `
        --providers "Microsoft-Windows-DotNETRuntime:0xC0000FFF:5,Microsoft-DotNETCore-SampleProfiler" `
        --output ".\traces\full_diagnostics_$(Get-Date -Format 'yyyyMMdd_HHmmss').nettrace" `
        --format Speedscope
    
    Write-Host ""
    Write-Host "? Full diagnostic trace saved!" -ForegroundColor Green
}

# ============================================================
# SCENARIO 4: PAGE CACHE MISS ANALYSIS
# ============================================================
# Focused on cache behavior and memory access patterns
# Use for: Analyzing page cache effectiveness, memory bottlenecks

function Start-CacheAnalysis {
    Write-Host "===== PAGE CACHE MISS ANALYSIS =====" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "? This mode tracks memory access patterns and cache behavior" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "? Starting profiling app..." -ForegroundColor Green
    
    $process = Start-Process -FilePath "dotnet" `
        -ArgumentList "run --project ..\SharpCoreDB.Profiling\SharpCoreDB.Profiling.csproj -c Release" `
        -PassThru `
        -NoNewWindow
    
    Start-Sleep -Seconds 2
    
    Write-Host "? Process ID: $($process.Id)" -ForegroundColor Yellow
    Write-Host "? Press ENTER in the app window to start profiling..." -ForegroundColor Yellow
    Write-Host ""
    
    Read-Host "Press ENTER here after you've started the profiling in the app window"
    
    Write-Host "? Starting cache analysis trace..." -ForegroundColor Green
    
    # Collect trace with GC and allocation events for cache analysis
    dotnet-trace collect `
        --process-id $process.Id `
        --providers "Microsoft-Windows-DotNETRuntime:0x1DFFFFFFF:5,Microsoft-Windows-DotNETRuntimeRundown:0x1F000080018:5" `
        --output ".\traces\cache_analysis_$(Get-Date -Format 'yyyyMMdd_HHmmss').nettrace" `
        --format Speedscope
    
    Write-Host ""
    Write-Host "? Cache analysis trace saved!" -ForegroundColor Green
    Write-Host "? Look for:" -ForegroundColor Yellow
    Write-Host "  - High Gen2 collections (indicates memory pressure)" -ForegroundColor White
    Write-Host "  - Large object allocations (>85KB)" -ForegroundColor White
    Write-Host "  - Frequent PageCache.GetPage calls" -ForegroundColor White
}

# ============================================================
# SCENARIO 5: WAL SYNC OVERHEAD ANALYSIS
# ============================================================
# Focused on WAL write performance and flush behavior
# Use for: Analyzing WAL sync frequency, I/O batching effectiveness

function Start-WalSyncAnalysis {
    Write-Host "===== WAL SYNC OVERHEAD ANALYSIS =====" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "? This mode tracks WAL write operations and flush frequency" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "? Starting profiling app with continuous mode..." -ForegroundColor Green
    Write-Host "? Select option 4 (Continuous profiling) in the app" -ForegroundColor Yellow
    Write-Host ""
    
    $process = Start-Process -FilePath "dotnet" `
        -ArgumentList "run --project ..\SharpCoreDB.Profiling\SharpCoreDB.Profiling.csproj -c Release" `
        -PassThru `
        -NoNewWindow
    
    Start-Sleep -Seconds 2
    
    Write-Host "? Process ID: $($process.Id)" -ForegroundColor Yellow
    Write-Host ""
    
    Read-Host "Press ENTER here after selecting continuous mode in the app"
    
    Write-Host "? Starting WAL sync trace (30 seconds)..." -ForegroundColor Green
    
    # Collect trace with I/O events
    dotnet-trace collect `
        --process-id $process.Id `
        --providers "Microsoft-Windows-DotNETRuntime:0x1F000080018:5" `
        --output ".\traces\wal_sync_$(Get-Date -Format 'yyyyMMdd_HHmmss').nettrace" `
        --format Speedscope `
        --duration 00:00:30
    
    Write-Host ""
    Write-Host "? WAL sync trace saved!" -ForegroundColor Green
    Write-Host "? Look for:" -ForegroundColor Yellow
    Write-Host "  - WalManager.WriteEntry* calls" -ForegroundColor White
    Write-Host "  - FileStream.Flush operations" -ForegroundColor White
    Write-Host "  - GroupCommitWAL.CommitAsync latency" -ForegroundColor White
}

# ============================================================
# SCENARIO 6: COMPARATIVE ANALYSIS (Before/After)
# ============================================================
# Runs both PAGE_BASED and COLUMNAR modes for comparison
# Use for: Comparing storage engine performance, identifying regressions

function Start-ComparativeAnalysis {
    Write-Host "===== COMPARATIVE ANALYSIS (PAGE_BASED vs COLUMNAR) =====" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "? This will collect traces for both storage modes" -ForegroundColor Yellow
    Write-Host ""
    
    # PAGE_BASED trace
    Write-Host "? Step 1/2: PAGE_BASED mode" -ForegroundColor Green
    Write-Host "? Starting profiling app..." -ForegroundColor Green
    Write-Host "? Select option 1 (PAGE_BASED) in the app" -ForegroundColor Yellow
    Write-Host ""
    
    $process1 = Start-Process -FilePath "dotnet" `
        -ArgumentList "run --project ..\SharpCoreDB.Profiling\SharpCoreDB.Profiling.csproj -c Release" `
        -PassThru `
        -NoNewWindow
    
    Start-Sleep -Seconds 2
    
    Write-Host "? Process ID: $($process1.Id)" -ForegroundColor Yellow
    Write-Host ""
    
    Read-Host "Press ENTER after selecting PAGE_BASED mode"
    
    Write-Host "? Collecting PAGE_BASED trace..." -ForegroundColor Green
    
    dotnet-trace collect `
        --process-id $process1.Id `
        --providers "Microsoft-Windows-DotNETRuntime:0x1F000080018:5" `
        --output ".\traces\pagebased_$(Get-Date -Format 'yyyyMMdd_HHmmss').nettrace" `
        --format Speedscope
    
    $process1.WaitForExit()
    
    Write-Host ""
    Write-Host "? PAGE_BASED trace complete!" -ForegroundColor Green
    Write-Host ""
    Start-Sleep -Seconds 2
    
    # COLUMNAR trace
    Write-Host "? Step 2/2: COLUMNAR mode" -ForegroundColor Green
    Write-Host "? Starting profiling app..." -ForegroundColor Green
    Write-Host "? Select option 2 (COLUMNAR) in the app" -ForegroundColor Yellow
    Write-Host ""
    
    $process2 = Start-Process -FilePath "dotnet" `
        -ArgumentList "run --project ..\SharpCoreDB.Profiling\SharpCoreDB.Profiling.csproj -c Release" `
        -PassThru `
        -NoNewWindow
    
    Start-Sleep -Seconds 2
    
    Write-Host "? Process ID: $($process2.Id)" -ForegroundColor Yellow
    Write-Host ""
    
    Read-Host "Press ENTER after selecting COLUMNAR mode"
    
    Write-Host "? Collecting COLUMNAR trace..." -ForegroundColor Green
    
    dotnet-trace collect `
        --process-id $process2.Id `
        --providers "Microsoft-Windows-DotNETRuntime:0x1F000080018:5" `
        --output ".\traces\columnar_$(Get-Date -Format 'yyyyMMdd_HHmmss').nettrace" `
        --format Speedscope
    
    $process2.WaitForExit()
    
    Write-Host ""
    Write-Host "? COLUMNAR trace complete!" -ForegroundColor Green
    Write-Host ""
    Write-Host "? Both traces saved! Compare in speedscope.app" -ForegroundColor Green
}

# ============================================================
# MAIN MENU
# ============================================================

function Show-Menu {
    Clear-Host
    Write-Host "=============================================" -ForegroundColor Cyan
    Write-Host "  SharpCoreDB Insert Pipeline Profiler" -ForegroundColor Cyan
    Write-Host "  dotnet-trace Collection Scripts" -ForegroundColor Cyan
    Write-Host "=============================================" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Select profiling scenario:" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "  1) CPU Sampling (Find hot methods)" -ForegroundColor White
    Write-Host "  2) Allocation Tracking (Find memory leaks)" -ForegroundColor White
    Write-Host "  3) Full Diagnostics (Complete profile)" -ForegroundColor White
    Write-Host "  4) Page Cache Analysis (Cache misses)" -ForegroundColor White
    Write-Host "  5) WAL Sync Analysis (I/O bottlenecks)" -ForegroundColor White
    Write-Host "  6) Comparative Analysis (Before/After)" -ForegroundColor White
    Write-Host "  0) Exit" -ForegroundColor White
    Write-Host ""
}

# Ensure traces directory exists
if (-not (Test-Path ".\traces")) {
    New-Item -ItemType Directory -Path ".\traces" | Out-Null
}

# Main loop
do {
    Show-Menu
    $choice = Read-Host "Enter choice [1-6, 0 to exit]"
    Write-Host ""
    
    switch ($choice) {
        "1" { Start-CpuSampling }
        "2" { Start-AllocationTracking }
        "3" { Start-FullDiagnostics }
        "4" { Start-CacheAnalysis }
        "5" { Start-WalSyncAnalysis }
        "6" { Start-ComparativeAnalysis }
        "0" { Write-Host "Exiting..." -ForegroundColor Green; break }
        default { Write-Host "Invalid choice!" -ForegroundColor Red }
    }
    
    if ($choice -ne "0") {
        Write-Host ""
        Read-Host "Press ENTER to return to menu"
    }
} while ($choice -ne "0")
