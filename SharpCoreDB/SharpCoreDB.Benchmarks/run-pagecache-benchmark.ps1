# PageCache Performance Benchmark Script
# Run dit script om de PageCache performance te testen

Write-Host "=====================================" -ForegroundColor Cyan
Write-Host "  PageCache Performance Benchmark" -ForegroundColor Cyan
Write-Host "=====================================" -ForegroundColor Cyan
Write-Host ""

# Navigeer naar benchmark directory
$benchmarkDir = "D:\source\repos\MPCoreDeveloper\SharpCoreDB\SharpCoreDB.Benchmarks"
Set-Location $benchmarkDir

Write-Host "Building benchmark project..." -ForegroundColor Yellow
dotnet build -c Release

if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed!" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "Running PageCache benchmarks..." -ForegroundColor Green
Write-Host "Dit kan enkele minuten duren..." -ForegroundColor Gray
Write-Host ""

# Run alleen de PageCache benchmarks
dotnet run -c Release --filter "*PageCache*"

Write-Host ""
Write-Host "=====================================" -ForegroundColor Cyan
Write-Host "Benchmark complete!" -ForegroundColor Green
Write-Host "=====================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Results zijn opgeslagen in:" -ForegroundColor Yellow
Write-Host "  BenchmarkDotNet.Artifacts/results/" -ForegroundColor White
Write-Host ""
Write-Host "Open de HTML report voor gedetailleerde analyse:" -ForegroundColor Yellow
Write-Host "  BenchmarkDotNet.Artifacts/results/SharpCoreDB.Benchmarks.PageCacheBenchmarks-report.html" -ForegroundColor White
