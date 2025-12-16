# Simpelste Storage Engine Benchmark Runner
# Gewoon dubbelklikken of "powershell .\RunStorageBenchmark.ps1"

$ErrorActionPreference = "Stop"

Write-Host ""
Write-Host "==================================================" -ForegroundColor Cyan
Write-Host "  SharpCoreDB Storage Engine Benchmark" -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Cyan
Write-Host ""

# Ga naar de juiste directory
Set-Location "D:\source\repos\MPCoreDeveloper\SharpCoreDB\SharpCoreDB.Tests"

Write-Host "Running test..." -ForegroundColor Yellow
Write-Host ""

# Run de test
dotnet test --filter "FullyQualifiedName~StorageEngineComparisonTest" --logger "console;verbosity=detailed"

Write-Host ""
Write-Host "==================================================" -ForegroundColor Cyan
Write-Host ""

# Check voor report
$reportFile = "STORAGE_ENGINE_COMPARISON.md"
if (Test-Path $reportFile) {
    Write-Host "Report generated: $reportFile" -ForegroundColor Green
    Write-Host ""
    
    # Toon de eerste 50 regels
    Get-Content $reportFile -Head 50
    
    Write-Host ""
    Write-Host "Full report: D:\source\repos\MPCoreDeveloper\SharpCoreDB\SharpCoreDB.Tests\$reportFile" -ForegroundColor Cyan
} else {
    Write-Host "No report file found" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "Press Enter to exit..."
Read-Host
