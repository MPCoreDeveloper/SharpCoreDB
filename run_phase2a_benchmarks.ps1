#!/usr/bin/env pwsh

# Phase 2A Optimization Benchmarks Runner
# Measures actual performance improvements for all Week 3 optimizations

Write-Host "╔════════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║       PHASE 2A OPTIMIZATION BENCHMARKS - RUNNING           ║" -ForegroundColor Cyan
Write-Host "╚════════════════════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""

$benchmarkDir = "tests/SharpCoreDB.Benchmarks"
$outputDir = "BenchmarkResults_Phase2A"

# Create output directory
if (!(Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir | Out-Null
    Write-Host "✅ Created output directory: $outputDir" -ForegroundColor Green
}

Write-Host ""
Write-Host "📊 PHASE 2A BENCHMARKS TO RUN:" -ForegroundColor Yellow
Write-Host "  1. WHERE Clause Caching (Mon-Tue)" -ForegroundColor Yellow
Write-Host "  2. SELECT* StructRow Path (Wed)" -ForegroundColor Yellow
Write-Host "  3. Type Conversion Caching (Thu)" -ForegroundColor Yellow
Write-Host "  4. Batch PK Validation (Fri)" -ForegroundColor Yellow
Write-Host "  5. Combined All Optimizations" -ForegroundColor Yellow
Write-Host ""

# Build benchmarks project
Write-Host "🔨 Building benchmarks project..." -ForegroundColor Cyan
cd $benchmarkDir
dotnet build -c Release

if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ Build failed!" -ForegroundColor Red
    exit 1
}

Write-Host "✅ Build successful!" -ForegroundColor Green
Write-Host ""

# Run Phase 2A benchmarks
Write-Host "🚀 Running Phase 2A Optimization Benchmarks..." -ForegroundColor Cyan
Write-Host ""

$benchmarkOutput = dotnet run -c Release -- --filter "*Phase2A*" --exportJson "$outputDir/phase2a-results.json"

if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ Benchmark run failed!" -ForegroundColor Red
    Write-Host $benchmarkOutput
    exit 1
}

Write-Host ""
Write-Host "✅ Benchmarks completed!" -ForegroundColor Green
Write-Host ""

# Check if results file exists
if (Test-Path "$outputDir/phase2a-results.json") {
    Write-Host "📄 Results saved to: $outputDir/phase2a-results.json" -ForegroundColor Green
    
    # Parse and display summary
    Write-Host ""
    Write-Host "📊 BENCHMARK SUMMARY:" -ForegroundColor Cyan
    Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Cyan
    
    # Read JSON results
    $results = Get-Content "$outputDir/phase2a-results.json" | ConvertFrom-Json
    
    foreach ($benchmark in $results.benchmarks) {
        Write-Host ""
        Write-Host "📌 $($benchmark.method)" -ForegroundColor Yellow
        Write-Host "   Description: $($benchmark.descriptor)" -ForegroundColor Gray
        Write-Host "   Mean:        $([math]::Round($benchmark.statistics.mean, 2)) ns" -ForegroundColor Green
        Write-Host "   StdDev:      $([math]::Round($benchmark.statistics.stdDev, 2)) ns" -ForegroundColor Gray
        Write-Host "   Memory:      $([math]::Round($benchmark.allocatedBytes / 1024, 2)) KB" -ForegroundColor Magenta
    }
    
    Write-Host ""
    Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Cyan
}

Write-Host ""
Write-Host "✅ Phase 2A benchmarks complete!" -ForegroundColor Green
Write-Host ""

cd ../..
