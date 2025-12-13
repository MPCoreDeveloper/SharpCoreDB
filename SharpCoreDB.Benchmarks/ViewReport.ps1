# ============================================
#  SharpCoreDB Benchmark - View Specific Report
# ============================================

param(
    [Parameter(Mandatory=$false)]
    [ValidateSet("Insert", "Select", "UpdateDelete", "GroupCommitWAL", "All", "Latest")]
    [string]$Type = "Latest"
)

Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "  Benchmark Report Viewer" -ForegroundColor Cyan
Write-Host "========================================`n" -ForegroundColor Cyan

$resultsDir = "BenchmarkDotNet.Artifacts\results"

# Check if results directory exists
if (-not (Test-Path $resultsDir)) {
    Write-Host "ERROR: Results directory not found!" -ForegroundColor Red
    Write-Host "Please run benchmarks first.`n" -ForegroundColor Yellow
    exit 1
}

# Find reports based on type
$filter = switch ($Type) {
    "Insert"        { "*ComparativeInsert*-report.html" }
    "Select"        { "*ComparativeSelect*-report.html" }
    "UpdateDelete"  { "*ComparativeUpdateDelete*-report.html" }
    "GroupCommitWAL" { "*GroupCommitWAL*-report.html" }
    "Latest"        { "*-report.html" }
    "All"           { "*-report.html" }
}

$htmlReports = Get-ChildItem -Path $resultsDir -Filter $filter -File | 
               Sort-Object LastWriteTime -Descending

if ($Type -eq "Latest" -and $htmlReports.Count -gt 0) {
    $htmlReports = $htmlReports | Select-Object -First 1
}

if ($htmlReports.Count -eq 0) {
    Write-Host "No $Type reports found!`n" -ForegroundColor Red
    Write-Host "Available benchmark types:" -ForegroundColor Yellow
    Write-Host "  - Insert          : INSERT performance benchmarks" -ForegroundColor Gray
    Write-Host "  - Select          : SELECT performance benchmarks" -ForegroundColor Gray
    Write-Host "  - UpdateDelete    : UPDATE/DELETE benchmarks" -ForegroundColor Gray
    Write-Host "  - GroupCommitWAL  : WAL configuration benchmarks" -ForegroundColor Gray
    Write-Host "  - Latest          : Most recent benchmark" -ForegroundColor Gray
    Write-Host "  - All             : All available benchmarks`n" -ForegroundColor Gray
    exit 1
}

Write-Host "Opening $($htmlReports.Count) $Type report(s):`n" -ForegroundColor Green

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
    Write-Host "    Last modified: $ageText" -ForegroundColor Gray
    Write-Host "    Size: $([math]::Round($report.Length / 1KB, 2)) KB`n" -ForegroundColor Gray
    
    Start-Process $report.FullName
    Start-Sleep -Milliseconds 300
}

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Reports opened in browser!" -ForegroundColor Cyan
Write-Host "========================================`n" -ForegroundColor Cyan

# Also show available markdown reports
$mdReports = Get-ChildItem -Path $resultsDir -Filter "*-report-github.md" -File | 
             Sort-Object LastWriteTime -Descending | 
             Select-Object -First 5

if ($mdReports.Count -gt 0) {
    Write-Host "Available Markdown Reports:" -ForegroundColor Yellow
    foreach ($md in $mdReports) {
        Write-Host "  • $($md.Name)" -ForegroundColor Gray
    }
    Write-Host "`nView in editor: code `"$resultsDir\<report-name>.md`"`n" -ForegroundColor Cyan
}
