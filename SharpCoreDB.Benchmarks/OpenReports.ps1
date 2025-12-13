# ============================================
#  SharpCoreDB Benchmark - Open HTML Reports
# ============================================

Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "  Opening Benchmark HTML Reports" -ForegroundColor Cyan
Write-Host "========================================`n" -ForegroundColor Cyan

$resultsDir = "BenchmarkDotNet.Artifacts\results"

# Check if results directory exists
if (-not (Test-Path $resultsDir)) {
    Write-Host "ERROR: Results directory not found!" -ForegroundColor Red
    Write-Host "Please run benchmarks first: .\RunBenchmarks.bat`n" -ForegroundColor Yellow
    Read-Host "Press Enter to exit"
    exit 1
}

# Find all HTML reports
$htmlReports = Get-ChildItem -Path $resultsDir -Filter "*-report.html" -File | 
               Sort-Object LastWriteTime -Descending

if ($htmlReports.Count -eq 0) {
    Write-Host "`nNo HTML reports found!`n" -ForegroundColor Red
    Write-Host "This could mean:" -ForegroundColor Yellow
    Write-Host "  1. Benchmarks haven't been run yet" -ForegroundColor Yellow
    Write-Host "  2. Benchmarks failed during execution" -ForegroundColor Yellow
    Write-Host "  3. HTML export was disabled`n" -ForegroundColor Yellow
    Write-Host "Run benchmarks first: .\RunBenchmarks.bat`n" -ForegroundColor Cyan
    Read-Host "Press Enter to exit"
    exit 1
}

Write-Host "Found $($htmlReports.Count) HTML report(s):`n" -ForegroundColor Green

# Display and open each report
foreach ($report in $htmlReports) {
    $age = (Get-Date) - $report.LastWriteTime
    $ageText = if ($age.TotalMinutes -lt 60) {
        "$([int]$age.TotalMinutes) minutes ago"
    } elseif ($age.TotalHours -lt 24) {
        "$([int]$age.TotalHours) hours ago"
    } else {
        "$([int]$age.TotalDays) days ago"
    }
    
    Write-Host "  ? $($report.Name)" -ForegroundColor Green
    Write-Host "    Generated: $ageText" -ForegroundColor Gray
    
    # Open in default browser
    Start-Process $report.FullName
    Start-Sleep -Milliseconds 500  # Small delay between opens
}

Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "  All reports opened in browser!" -ForegroundColor Cyan
Write-Host "========================================`n" -ForegroundColor Cyan

Write-Host "Reports location:" -ForegroundColor Yellow
Write-Host "  $((Get-Location).Path)\$resultsDir`n" -ForegroundColor White

Read-Host "Press Enter to exit"
