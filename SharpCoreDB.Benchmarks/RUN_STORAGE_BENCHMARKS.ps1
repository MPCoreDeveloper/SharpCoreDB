# ?? Run Storage Engine Benchmarks

Write-Host "=====================================================" -ForegroundColor Cyan
Write-Host "  SharpCoreDB Storage Engine Benchmark Suite" -ForegroundColor Cyan
Write-Host "=====================================================" -ForegroundColor Cyan
Write-Host ""

# Check if running in benchmark directory
if (-not (Test-Path "SharpCoreDB.Benchmarks.csproj")) {
    Write-Host "? ERROR: Run this script from SharpCoreDB.Benchmarks directory" -ForegroundColor Red
    exit 1
}

Write-Host "?? Available Benchmark Suites:" -ForegroundColor Yellow
Write-Host ""
Write-Host "  1. PAGE_BASED Before/After   - Validate 3-5x optimization impact" -ForegroundColor Green
Write-Host "  2. Cross-Engine Comparison   - SharpCore vs SQLite vs LiteDB" -ForegroundColor Green
Write-Host "  3. Full Suite (All)          - Run everything (60-90 minutes)" -ForegroundColor Green
Write-Host ""

$choice = Read-Host "Select benchmark (1-3)"

Write-Host ""
Write-Host "??  Building in Release mode..." -ForegroundColor Yellow
dotnet build -c Release --framework net9.0

if ($LASTEXITCODE -ne 0) {
    Write-Host "? Build failed!" -ForegroundColor Red
    exit 1
}

Write-Host "? Build successful!" -ForegroundColor Green
Write-Host ""

# Run selected benchmark
switch ($choice) {
    "1" {
        Write-Host "?? Running PAGE_BASED Before/After Benchmarks..." -ForegroundColor Cyan
        Write-Host "   Expected: 3-5x speedup across all operations" -ForegroundColor Gray
        Write-Host ""
        dotnet run -c Release --filter *PageBasedStorage* --framework net9.0 -- --exporters json markdown
    }
    "2" {
        Write-Host "?? Running Cross-Engine Comparison Benchmarks..." -ForegroundColor Cyan
        Write-Host "   Comparing: AppendOnly, PAGE_BASED, SQLite, LiteDB" -ForegroundColor Gray
        Write-Host ""
        dotnet run -c Release --filter *StorageEngineComparison* --framework net9.0 -- --exporters json markdown
    }
    "3" {
        Write-Host "?? Running Full Benchmark Suite..." -ForegroundColor Cyan
        Write-Host "   ??  This will take 60-90 minutes!" -ForegroundColor Yellow
        Write-Host ""
        dotnet run -c Release --framework net9.0 -- --exporters json markdown html
    }
    default {
        Write-Host "? Invalid choice!" -ForegroundColor Red
        exit 1
    }
}

if ($LASTEXITCODE -eq 0) {
    Write-Host ""
    Write-Host "=====================================================" -ForegroundColor Cyan
    Write-Host "? Benchmarks Complete!" -ForegroundColor Green
    Write-Host "=====================================================" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "?? Results saved to:" -ForegroundColor Yellow
    Write-Host "   - BenchmarkDotNet.Artifacts/results/*.md   (Markdown table)" -ForegroundColor Gray
    Write-Host "   - BenchmarkDotNet.Artifacts/results/*.json (Raw data)" -ForegroundColor Gray
    Write-Host ""
    Write-Host "?? Compare against expected results in:" -ForegroundColor Yellow
    Write-Host "   docs/benchmarks/STORAGE_BENCHMARK_RESULTS.md" -ForegroundColor Gray
    Write-Host ""
} else {
    Write-Host ""
    Write-Host "? Benchmark failed!" -ForegroundColor Red
    exit 1
}
