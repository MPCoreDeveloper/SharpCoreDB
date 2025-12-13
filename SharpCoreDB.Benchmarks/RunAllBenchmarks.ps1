# RunAllBenchmarks.ps1
# Complete benchmark execution script for SharpCoreDB

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  SharpCoreDB Complete Benchmark Suite" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""

# Build in Release mode
Write-Host "[1/8] Building in Release mode..." -ForegroundColor Yellow
dotnet build -c Release --nologo --verbosity quiet
if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed!" -ForegroundColor Red
    exit 1
}
Write-Host "? Build successful" -ForegroundColor Green
Write-Host ""

# Create results directory
$resultsDir = "BenchmarkResults_$(Get-Date -Format 'yyyy-MM-dd_HH-mm-ss')"
New-Item -ItemType Directory -Path $resultsDir -Force | Out-Null

# Array to store all results
$allResults = @()

# Function to run a single benchmark
function Run-Benchmark {
    param(
        [string]$Name,
        [string]$Filter,
        [int]$Step,
        [int]$Total
    )
    
    Write-Host "[$Step/$Total] Running $Name..." -ForegroundColor Yellow
    
    $outputFile = Join-Path $resultsDir "$Name.txt"
    
    $startTime = Get-Date
    try {
        # Run benchmark with timeout of 30 minutes
        $process = Start-Process -FilePath "dotnet" `
            -ArgumentList "run -c Release --no-build -- --filter `"*$Filter*`" --exporters json,html,csv" `
            -NoNewWindow -PassThru -RedirectStandardOutput $outputFile -RedirectStandardError "$outputFile.err"
        
        $timeout = 1800 # 30 minutes
        if ($process.WaitForExit($timeout * 1000)) {
            $endTime = Get-Date
            $duration = ($endTime - $startTime).TotalSeconds
            
            if ($process.ExitCode -eq 0) {
                Write-Host "? $Name completed in $([math]::Round($duration, 1))s" -ForegroundColor Green
                
                # Parse results
                $content = Get-Content $outputFile -Raw
                if ($content -match "Mean.*?(\d+\.?\d*)\s*(ns|?s|ms|s)") {
                    $script:allResults += @{
                        Name = $Name
                        Duration = $duration
                        Status = "Success"
                        BestTime = "$($matches[1]) $($matches[2])"
                    }
                } else {
                    $script:allResults += @{
                        Name = $Name
                        Duration = $duration
                        Status = "Success"
                        BestTime = "N/A"
                    }
                }
                return $true
            } else {
                Write-Host "? $Name failed with exit code $($process.ExitCode)" -ForegroundColor Red
                $script:allResults += @{
                    Name = $Name
                    Duration = $duration
                    Status = "Failed"
                    BestTime = "N/A"
                }
                return $false
            }
        } else {
            $process.Kill()
            Write-Host "? $Name timed out after 30 minutes" -ForegroundColor Red
            $script:allResults += @{
                Name = $Name
                Duration = $timeout
                Status = "Timeout"
                BestTime = "N/A"
            }
            return $false
        }
    } catch {
        Write-Host "? $Name crashed: $($_.Exception.Message)" -ForegroundColor Red
        $script:allResults += @{
            Name = $Name
            Duration = 0
            Status = "Crashed"
            BestTime = "N/A"
        }
        return $false
    }
    Write-Host ""
}

# Run all benchmarks
$benchmarks = @(
    @{Name="ComparativeInsert"; Filter="ComparativeInsertBenchmarks"; Step=2; Total=8},
    @{Name="ComparativeSelect"; Filter="ComparativeSelectBenchmarks"; Step=3; Total=8},
    @{Name="ComparativeUpdateDelete"; Filter="ComparativeUpdateDeleteBenchmarks"; Step=4; Total=8},
    @{Name="CryptoBenchmarks"; Filter="CryptoBenchmarks"; Step=5; Total=8},
    @{Name="IndexBenchmarks"; Filter="IndexBenchmarks"; Step=6; Total=8},
    @{Name="SqlParsing"; Filter="SqlParsingBenchmarks"; Step=7; Total=8},
    @{Name="TimeTracking"; Filter="TimeTrackingBenchmarks"; Step=8; Total=8}
)

foreach ($benchmark in $benchmarks) {
    Run-Benchmark -Name $benchmark.Name -Filter $benchmark.Filter -Step $benchmark.Step -Total $benchmark.Total
    Write-Host ""
}

# Generate summary report
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  BENCHMARK SUMMARY" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""

$summaryFile = Join-Path $resultsDir "SUMMARY.md"
$summaryContent = @"
# SharpCoreDB Benchmark Results Summary
**Generated:** $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')

## Results Overview

| Benchmark | Status | Duration (s) | Best Time |
|-----------|--------|--------------|-----------|
"@

foreach ($result in $allResults) {
    $statusEmoji = switch ($result.Status) {
        "Success" { "?" }
        "Failed" { "?" }
        "Timeout" { "?" }
        "Crashed" { "??" }
    }
    
    $summaryContent += "`n| $($result.Name) | $statusEmoji $($result.Status) | $([math]::Round($result.Duration, 1)) | $($result.BestTime) |"
    
    Write-Host "$statusEmoji $($result.Name): $($result.Status)" -ForegroundColor $(
        switch ($result.Status) {
            "Success" { "Green" }
            "Failed" { "Red" }
            "Timeout" { "Yellow" }
            "Crashed" { "Magenta" }
        }
    )
}

$summaryContent += @"


## Total Statistics

- **Total Benchmarks:** $($allResults.Count)
- **Successful:** $($allResults | Where-Object { $_.Status -eq "Success" } | Measure-Object | Select-Object -ExpandProperty Count)
- **Failed:** $($allResults | Where-Object { $_.Status -eq "Failed" } | Measure-Object | Select-Object -ExpandProperty Count)
- **Timeouts:** $($allResults | Where-Object { $_.Status -eq "Timeout" } | Measure-Object | Select-Object -ExpandProperty Count)
- **Crashed:** $($allResults | Where-Object { $_.Status -eq "Crashed" } | Measure-Object | Select-Object -ExpandProperty Count)
- **Total Duration:** $([math]::Round(($allResults | Measure-Object -Property Duration -Sum).Sum, 1)) seconds

## Detailed Results

Detailed results for each benchmark can be found in:
- JSON: `BenchmarkDotNet.Artifacts/results/*.json`
- HTML: `BenchmarkDotNet.Artifacts/results/*.html`
- CSV: `BenchmarkDotNet.Artifacts/results/*.csv`

## Performance Analysis

### Top 5 Areas for Improvement

Based on the benchmark results, analyze the following:

1. **Comparative Performance** - How does SharpCoreDB compare to SQLite and LiteDB?
2. **Encryption Overhead** - What is the cost of encryption on performance?
3. **Memory Allocations** - Are there excessive allocations causing GC pressure?
4. **Index Performance** - Are indexes being used effectively?
5. **SQL Parsing** - Is the parser a bottleneck?

### Recommendations

After reviewing the detailed results:

1. Check for outliers (operations taking much longer than expected)
2. Review memory allocation patterns (anything > 1KB per operation needs attention)
3. Identify GC pressure (Gen 0/1/2 collection counts)
4. Compare with baseline (SQLite memory mode is fastest reference)
5. Look for regression (compare with previous benchmark runs)
"@

$summaryContent | Out-File -FilePath $summaryFile -Encoding UTF8

Write-Host ""
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  BENCHMARK COMPLETE" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Results saved to: $resultsDir" -ForegroundColor Green
Write-Host "Summary: $summaryFile" -ForegroundColor Green
Write-Host ""
Write-Host "Next Steps:" -ForegroundColor Yellow
Write-Host "1. Review summary: $summaryFile" -ForegroundColor White
Write-Host "2. Analyze HTML reports in: BenchmarkDotNet.Artifacts/results/" -ForegroundColor White
Write-Host "3. Compare with previous runs" -ForegroundColor White
Write-Host "4. Identify performance bottlenecks" -ForegroundColor White
Write-Host ""
