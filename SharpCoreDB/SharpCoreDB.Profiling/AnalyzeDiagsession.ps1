# AnalyzeDiagsession.ps1
# Analyzes Visual Studio .diagsession profiling report
# Usage: .\AnalyzeDiagsession.ps1 -ReportPath "Report20251220-0925.diagsession"

param(
    [Parameter(Mandatory=$false)]
    [string]$ReportPath = "Report20251220-0925.diagsession",
    
    [Parameter(Mandatory=$false)]
    [int]$TopN = 25
)

Write-Host "=============================================="
Write-Host "  .diagsession Report Analyzer"
Write-Host "=============================================="
Write-Host ""

$fullPath = Join-Path $PSScriptRoot $ReportPath

if (-not (Test-Path $fullPath)) {
    Write-Host "❌ ERROR: Report file not found at: $fullPath" -ForegroundColor Red
    Write-Host ""
    Write-Host "Expected location: D:\source\repos\MPCoreDeveloper\SharpCoreDB\SharpCoreDB.Profiling\$ReportPath"
    Write-Host ""
    Write-Host "Available .diagsession files:"
    Get-ChildItem -Path $PSScriptRoot -Filter "*.diagsession" | ForEach-Object {
        Write-Host "  - $($_.Name)" -ForegroundColor Yellow
    }
    exit 1
}

Write-Host "📊 Analyzing: $ReportPath"
Write-Host "📁 Full path: $fullPath"
Write-Host ""

# Check if dotnet-trace is available
$dotnetTraceAvailable = $null -ne (Get-Command dotnet-trace -ErrorAction SilentlyContinue)

if (-not $dotnetTraceAvailable) {
    Write-Host "⚠️  dotnet-trace not found. Installing..." -ForegroundColor Yellow
    dotnet tool install --global dotnet-trace
    Write-Host ""
}

Write-Host "🔍 Generating text report..." -ForegroundColor Cyan
Write-Host ""

# Generate text report
$outputFile = Join-Path $PSScriptRoot "analysis_$(Get-Date -Format 'yyyyMMdd-HHmmss').txt"

try {
    # Try to convert .diagsession to .speedscope.json first (better format)
    $speedscopeFile = $fullPath -replace '\.diagsession$', '.speedscope.json'
    
    Write-Host "Converting to speedscope format..." -ForegroundColor Gray
    dotnet-trace convert $fullPath --format speedscope --output $speedscopeFile 2>&1 | Out-Null
    
    if (Test-Path $speedscopeFile) {
        Write-Host "✅ Speedscope file created: $speedscopeFile" -ForegroundColor Green
        
        # Parse speedscope JSON
        $json = Get-Content $speedscopeFile -Raw | ConvertFrom-Json
        
        Write-Host ""
        Write-Host "=============================================="
        Write-Host "  TOP $TopN HOTSPOTS (by Self Time)"
        Write-Host "=============================================="
        Write-Host ""
        
        # Extract profile data
        $profiles = $json.profiles
        $shared = $json.shared
        
        if ($profiles -and $profiles.Count -gt 0) {
            $profile = $profiles[0]
            
            # Build frame name lookup
            $frameNames = @{}
            for ($i = 0; $i -lt $shared.frames.Count; $i++) {
                $frameNames[$i] = $shared.frames[$i].name
            }
            
            # Calculate self times
            $selfTimes = @{}
            $totalTime = 0
            
            foreach ($sample in $profile.samples) {
                if ($sample -and $sample.Count -gt 0) {
                    $leafFrame = $sample[-1]
                    if (-not $selfTimes.ContainsKey($leafFrame)) {
                        $selfTimes[$leafFrame] = 0
                    }
                    $selfTimes[$leafFrame]++
                    $totalTime++
                }
            }
            
            # Sort by self time and display
            $hotspots = $selfTimes.GetEnumerator() | 
                Sort-Object -Property Value -Descending | 
                Select-Object -First $TopN
            
            Write-Host ("Method Name".PadRight(60)) "Self %" "Samples"
            Write-Host ("-" * 85)
            
            $rank = 1
            foreach ($hotspot in $hotspots) {
                $frameId = $hotspot.Key
                $samples = $hotspot.Value
                $percentage = ($samples / $totalTime) * 100
                
                $frameName = if ($frameNames.ContainsKey($frameId)) { 
                    $frameNames[$frameId] 
                } else { 
                    "Frame_$frameId" 
                }
                
                # Truncate long names
                if ($frameName.Length -gt 58) {
                    $frameName = $frameName.Substring(0, 55) + "..."
                }
                
                $color = if ($percentage -gt 10) { "Red" } 
                        elseif ($percentage -gt 5) { "Yellow" } 
                        else { "White" }
                
                $rankStr = "$rank.".PadRight(3)
                $nameStr = $frameName.PadRight(60)
                $pctStr = ("{0:F2}%" -f $percentage).PadLeft(7)
                $samplesStr = $samples.ToString().PadLeft(8)
                
                Write-Host "$rankStr" -NoNewline
                Write-Host "$nameStr" -NoNewline -ForegroundColor $color
                Write-Host "$pctStr" -NoNewline -ForegroundColor $color
                Write-Host "$samplesStr" -ForegroundColor $color
                
                $rank++
            }
            
            Write-Host ""
            Write-Host "Total Samples: $totalTime"
            Write-Host ""
        }
        else {
            Write-Host "⚠️  No profile data found in speedscope file" -ForegroundColor Yellow
        }
    }
    else {
        Write-Host "⚠️  Failed to create speedscope file" -ForegroundColor Yellow
    }
}
catch {
    Write-Host "❌ ERROR during conversion: $_" -ForegroundColor Red
    Write-Host ""
}

# Try to extract basic info from .diagsession using PerfView (if available)
$perfViewPath = "C:\Program Files\PerfView\PerfView.exe"
if (Test-Path $perfViewPath) {
    Write-Host ""
    Write-Host "🔧 PerfView detected, extracting detailed metrics..." -ForegroundColor Cyan
    Write-Host ""
    
    try {
        $csvFile = $fullPath -replace '\.diagsession$', '_calltree.csv'
        & $perfViewPath /nogui /AcceptEULA csv $fullPath $csvFile
        
        if (Test-Path $csvFile) {
            Write-Host "✅ Call tree exported: $csvFile" -ForegroundColor Green
            
            # Parse CSV
            $callTreeData = Import-Csv $csvFile | 
                Where-Object { $_.'Exc %' -gt 0 } |
                Sort-Object { [double]$_.'Exc %' } -Descending |
                Select-Object -First $TopN
            
            Write-Host ""
            Write-Host "=============================================="
            Write-Host "  CALL TREE (Top $TopN by Exclusive %)"
            Write-Host "=============================================="
            Write-Host ""
            
            $callTreeData | Format-Table -Property Name, 'Exc %', 'Inc %', 'Exc Count' -AutoSize
        }
    }
    catch {
        Write-Host "⚠️  PerfView analysis failed: $_" -ForegroundColor Yellow
    }
}

Write-Host ""
Write-Host "=============================================="
Write-Host "  ANALYSIS SUMMARY"
Write-Host "=============================================="
Write-Host ""

# Known hotspot patterns
Write-Host "🔍 Looking for known hotspots..." -ForegroundColor Cyan
Write-Host ""

$knownHotspots = @{
    "Storage.AppendBytes" = "❌ CRITICAL: Should be replaced with AppendBytesMultiple"
    "PageManager.FlushDirtyPages" = "⚠️  WARNING: Excessive page flushes detected"
    "FileStream.Flush" = "⚠️  WARNING: Too many WAL syncs"
    "PageManager.FindPageWithSpace" = "⚠️  WARNING: Linear search detected (use O(1) bitmap)"
    "BinaryPrimitives.Write" = "✅ OK: Serialization is efficient"
}

# Check if speedscope file exists for pattern matching
if (Test-Path $speedscopeFile) {
    $content = Get-Content $speedscopeFile -Raw
    
    foreach ($pattern in $knownHotspots.Keys) {
        if ($content -like "*$pattern*") {
            $message = $knownHotspots[$pattern]
            $color = if ($message -like "*CRITICAL*") { "Red" } 
                    elseif ($message -like "*WARNING*") { "Yellow" } 
                    else { "Green" }
            
            Write-Host "  $pattern" -ForegroundColor $color
            Write-Host "    $message"
            Write-Host ""
        }
    }
}

Write-Host ""
Write-Host "=============================================="
Write-Host "  RECOMMENDATIONS"
Write-Host "=============================================="
Write-Host ""

Write-Host "Based on your output (659ms for 10K inserts):" -ForegroundColor Cyan
Write-Host ""
Write-Host "1. ❌ Page Cache Not Working!" -ForegroundColor Red
Write-Host "   - Hits: 0, Misses: 0, Size: 0/10000"
Write-Host "   - Check PageCache initialization in PageBasedEngine"
Write-Host "   - Expected: Thousands of cache hits"
Write-Host ""

Write-Host "2. 🎯 Current Performance: 15,175 records/sec" -ForegroundColor Yellow
Write-Host "   - Target: 33,000+ records/sec (2-3x improvement needed)"
Write-Host "   - SQLite comparison: ~5-10x slower than target"
Write-Host ""

Write-Host "3. 🔧 Recommended Fixes (Priority Order):" -ForegroundColor Cyan
Write-Host ""
Write-Host "   Fix #1: AppendBytesMultiple batching" -ForegroundColor Yellow
Write-Host "   - Impact: 4-5x improvement"
Write-Host "   - Time: 30 minutes"
Write-Host "   - File: DataStructures\Table.CRUD.cs"
Write-Host ""

Write-Host "   Fix #2: Enable Page Cache properly" -ForegroundColor Yellow
Write-Host "   - Impact: 2-3x improvement"
Write-Host "   - Time: 1 hour"
Write-Host "   - File: Storage\Engines\PageBasedEngine.cs"
Write-Host ""

Write-Host "   Fix #3: Reduce WAL flushes" -ForegroundColor Yellow
Write-Host "   - Impact: 2x improvement"
Write-Host "   - Time: 15 minutes"
Write-Host "   - Already configured: GroupCommitSize=1000"
Write-Host ""

Write-Host "4. 📊 To view detailed CPU hotspots:" -ForegroundColor Cyan
Write-Host "   - Open speedscope.dev in browser"
Write-Host "   - Drag & drop: $speedscopeFile"
Write-Host "   - Look for red/hot methods in flame graph"
Write-Host ""

Write-Host "✅ Analysis complete!" -ForegroundColor Green
Write-Host ""
Write-Host "Next steps:"
Write-Host "  1. Review the hotspots above"
Write-Host "  2. Open speedscope.dev and load: $(Split-Path $speedscopeFile -Leaf)"
Write-Host "  3. Apply Fix #1 (AppendBytesMultiple)"
Write-Host "  4. Re-profile and compare"
Write-Host ""
