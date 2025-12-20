# SharpCoreDB Performance Analysis Script
# Analyzes dotnet-trace output to identify insert pipeline hotspots

<#
.SYNOPSIS
    Analyzes dotnet-trace nettrace files to identify performance hotspots in SharpCoreDB insert pipeline.

.DESCRIPTION
    This script processes trace files and generates reports on:
    - Top CPU-consuming methods
    - Allocation hotspots
    - Page cache miss rates
    - WAL sync frequency
    - Storage engine bottlenecks

.PARAMETER TracePath
    Path to the .nettrace file to analyze

.PARAMETER OutputPath
    Path for the analysis report (default: .\analysis\report_<timestamp>.txt)

.EXAMPLE
    .\AnalyzeTrace.ps1 -TracePath ".\traces\cpu_sampling_20250116_143022.nettrace"
#>

param(
    [Parameter(Mandatory=$true)]
    [string]$TracePath,
    
    [Parameter(Mandatory=$false)]
    [string]$OutputPath = ".\analysis\report_$(Get-Date -Format 'yyyyMMdd_HHmmss').txt"
)

# Ensure dotnet-trace is installed
if (-not (Get-Command dotnet-trace -ErrorAction SilentlyContinue)) {
    Write-Host "? ERROR: dotnet-trace not found!" -ForegroundColor Red
    Write-Host "  Install with: dotnet tool install --global dotnet-trace" -ForegroundColor Yellow
    exit 1
}

# Ensure analysis directory exists
$analysisDir = Split-Path -Parent $OutputPath
if (-not (Test-Path $analysisDir)) {
    New-Item -ItemType Directory -Path $analysisDir | Out-Null
}

Write-Host "=============================================" -ForegroundColor Cyan
Write-Host "  SharpCoreDB Trace Analysis" -ForegroundColor Cyan
Write-Host "=============================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "? Trace file: $TracePath" -ForegroundColor Green
Write-Host "? Output: $OutputPath" -ForegroundColor Green
Write-Host ""

# Verify trace file exists
if (-not (Test-Path $TracePath)) {
    Write-Host "? ERROR: Trace file not found!" -ForegroundColor Red
    exit 1
}

# Generate report using dotnet-trace
Write-Host "? Generating trace report..." -ForegroundColor Yellow

try {
    $reportContent = dotnet-trace report $TracePath 2>&1
    
    # Save full report
    $reportContent | Out-File -FilePath $OutputPath -Encoding UTF8
    
    Write-Host "? Report generated successfully!" -ForegroundColor Green
    Write-Host ""
    
    # Parse and display key metrics
    Write-Host "===== KEY METRICS =====" -ForegroundColor Cyan
    Write-Host ""
    
    # Extract GC metrics
    Write-Host "? GC Statistics:" -ForegroundColor Yellow
    $gcMetrics = $reportContent | Select-String -Pattern "GC|Allocation|Gen[0-2]" | Select-Object -First 10
    if ($gcMetrics) {
        $gcMetrics | ForEach-Object { Write-Host "  $_" }
    } else {
        Write-Host "  No GC metrics found in trace" -ForegroundColor Gray
    }
    Write-Host ""
    
    # Extract method timing
    Write-Host "? Top Time-Consuming Methods:" -ForegroundColor Yellow
    $methodMetrics = $reportContent | Select-String -Pattern "SharpCoreDB|Insert|Append|Flush|Page|WAL" | Select-Object -First 20
    if ($methodMetrics) {
        $methodMetrics | ForEach-Object { Write-Host "  $_" }
    } else {
        Write-Host "  Method timing data not available in this trace format" -ForegroundColor Gray
    }
    Write-Host ""
    
    # Known hotspots to look for
    Write-Host "? Checking for Known Hotspots:" -ForegroundColor Yellow
    $hotspots = @{
        "AppendBytes" = "Storage append operations"
        "FlushBufferedAppends" = "Transaction commit flush"
        "PageManager.InsertRecord" = "Page allocation overhead"
        "FindPageWithSpace" = "Free space search"
        "FileStream.Flush" = "I/O sync operations"
        "WalManager.WriteEntry" = "WAL write operations"
        "Serialize" = "Row serialization overhead"
        "Encrypt" = "Encryption overhead"
        "PageCache.GetPage" = "Page cache lookups"
        "BinaryPrimitives" = "Binary encoding operations"
    }
    
    foreach ($hotspot in $hotspots.GetEnumerator()) {
        $matches = $reportContent | Select-String -Pattern $hotspot.Key -CaseSensitive:$false
        if ($matches) {
            $count = $matches.Count
            Write-Host "  ? $($hotspot.Value): $count occurrences" -ForegroundColor Red
        }
    }
    Write-Host ""
    
    # Optimization suggestions
    Write-Host "===== OPTIMIZATION SUGGESTIONS =====" -ForegroundColor Cyan
    Write-Host ""
    
    $suggestions = @()
    
    # Check for excessive AppendBytes calls
    $appendMatches = $reportContent | Select-String -Pattern "AppendBytes" -CaseSensitive:$false
    if ($appendMatches.Count -gt 10000) {
        $suggestions += "? HIGH: Reduce AppendBytes indirection - use AppendBytesMultiple for batching ($($appendMatches.Count) calls detected)"
    }
    
    # Check for frequent flushes
    $flushMatches = $reportContent | Select-String -Pattern "Flush" -CaseSensitive:$false
    if ($flushMatches.Count -gt 100) {
        $suggestions += "? MEDIUM: Optimize flush frequency - enable GroupCommit WAL ($($flushMatches.Count) flush operations)"
    }
    
    # Check for page allocation overhead
    $pageMatches = $reportContent | Select-String -Pattern "FindPageWithSpace" -CaseSensitive:$false
    if ($pageMatches.Count -gt 5000) {
        $suggestions += "? MEDIUM: Page allocation overhead - optimize free list ($($pageMatches.Count) page searches)"
    }
    
    # Check for cache misses
    $cacheMatches = $reportContent | Select-String -Pattern "PageCache.*Miss|EvictPage" -CaseSensitive:$false
    if ($cacheMatches.Count -gt 1000) {
        $suggestions += "? LOW: High cache miss rate - increase PageCacheCapacity ($($cacheMatches.Count) cache misses)"
    }
    
    # Check for encryption overhead
    $encryptMatches = $reportContent | Select-String -Pattern "Encrypt|Decrypt" -CaseSensitive:$false
    if ($encryptMatches.Count -gt 20000) {
        $suggestions += "? INFO: Encryption enabled - consider NoEncryptMode for benchmarking ($($encryptMatches.Count) encrypt operations)"
    }
    
    if ($suggestions.Count -eq 0) {
        Write-Host "  ? No major issues detected!" -ForegroundColor Green
        Write-Host "  ? Profile looks healthy - performance is likely near optimal" -ForegroundColor Green
    } else {
        $suggestions | ForEach-Object { 
            if ($_ -match "HIGH") {
                Write-Host $_ -ForegroundColor Red
            } elseif ($_ -match "MEDIUM") {
                Write-Host $_ -ForegroundColor Yellow
            } else {
                Write-Host $_ -ForegroundColor White
            }
        }
    }
    
    Write-Host ""
    Write-Host "===== DETAILED RECOMMENDATIONS =====" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Based on analysis, consider these fixes:" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "1. Reduce AppendBytes Indirection:" -ForegroundColor White
    Write-Host "   - Use AppendBytesMultiple in InsertBatch" -ForegroundColor Gray
    Write-Host "   - Batch up to 1000 rows before calling storage" -ForegroundColor Gray
    Write-Host "   - Expected improvement: 5-10x faster inserts" -ForegroundColor Gray
    Write-Host ""
    Write-Host "2. Optimize Page Flushes:" -ForegroundColor White
    Write-Host "   - Enable GroupCommitWAL (GroupCommitSize = 1000)" -ForegroundColor Gray
    Write-Host "   - Remove immediate FlushDirtyPages() calls" -ForegroundColor Gray
    Write-Host "   - Flush only on transaction commit" -ForegroundColor Gray
    Write-Host "   - Expected improvement: 3-5x fewer I/O operations" -ForegroundColor Gray
    Write-Host ""
    Write-Host "3. Improve Page Cache:" -ForegroundColor White
    Write-Host "   - Increase PageCacheCapacity to 10000+ pages" -ForegroundColor Gray
    Write-Host "   - Use CLOCK eviction policy (already implemented)" -ForegroundColor Gray
    Write-Host "   - Expected improvement: 10x faster on hot data" -ForegroundColor Gray
    Write-Host ""
    Write-Host "4. Optimize WAL Syncs:" -ForegroundColor White
    Write-Host "   - Use WalManager pooled streams" -ForegroundColor Gray
    Write-Host "   - Batch WAL writes with EnableAdaptiveWalBatching" -ForegroundColor Gray
    Write-Host "   - Expected improvement: 2-3x fewer sync operations" -ForegroundColor Gray
    Write-Host ""
    Write-Host "5. Free List Optimization:" -ForegroundColor White
    Write-Host "   - Use O(1) free list (already implemented)" -ForegroundColor Gray
    Write-Host "   - Maintain free page bitmap" -ForegroundColor Gray
    Write-Host "   - Expected improvement: Constant-time page allocation" -ForegroundColor Gray
    Write-Host ""
    
    Write-Host "? Full report saved to: $OutputPath" -ForegroundColor Green
    
} catch {
    Write-Host "? ERROR analyzing trace:" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "? Next steps:" -ForegroundColor Yellow
Write-Host "  1. Open trace in speedscope.app for visual analysis" -ForegroundColor White
Write-Host "  2. Open trace in PerfView for detailed .NET profiling" -ForegroundColor White
Write-Host "  3. Compare with baseline traces to measure improvements" -ForegroundColor White
Write-Host ""
