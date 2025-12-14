# SharpCoreDB Benchmark Runner - SIMPLE VERSION
# Just run benchmarks, no complicated scripts!

Write-Host "═══════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "  SharpCoreDB Benchmarks - Simple Menu" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════════════════════`n" -ForegroundColor Cyan

# Check if we're in the right directory
if (-not (Test-Path "SharpCoreDB.Benchmarks.csproj")) {
    Write-Host "❌ Error: Must run from SharpCoreDB.Benchmarks directory" -ForegroundColor Red
    Write-Host "   Current directory: $(Get-Location)" -ForegroundColor Gray
    Write-Host "   Expected: SharpCoreDB\SharpCoreDB.Benchmarks\" -ForegroundColor Gray
    exit 1
}

# Simple menu
Write-Host "Select benchmark:" -ForegroundColor Yellow
Write-Host ""
Write-Host "  1. Quick 10K Test (RECOMMENDED - Fast!)" -ForegroundColor Green
Write-Host "  2. Full INSERT Benchmarks"
Write-Host "  3. Full SELECT Benchmarks"
Write-Host "  4. Full UPDATE/DELETE Benchmarks"
Write-Host "  5. Run ALL Benchmarks"
Write-Host "  Q. Quit"
Write-Host ""

$choice = Read-Host "Enter choice (1-5 or Q)"

switch ($choice) {
    "1" {
        Write-Host "`n🚀 Running Quick 10K Comparison..." -ForegroundColor Green
        Write-Host "This tests 10,000 record inserts against SQLite and LiteDB" -ForegroundColor Gray
        Write-Host "Duration: ~2-3 minutes`n" -ForegroundColor Gray
        
        dotnet run -c Release --filter "*Quick10k*"
    }
    
    "2" {
        Write-Host "`n🚀 Running INSERT Benchmarks..." -ForegroundColor Green
        dotnet run -c Release --filter "*ComparativeInsert*"
    }
    
    "3" {
        Write-Host "`n🚀 Running SELECT Benchmarks..." -ForegroundColor Green
        dotnet run -c Release --filter "*ComparativeSelect*"
    }
    
    "4" {
        Write-Host "`n🚀 Running UPDATE/DELETE Benchmarks..." -ForegroundColor Green
        dotnet run -c Release --filter "*ComparativeUpdateDelete*"
    }
    
    "5" {
        Write-Host "`n🚀 Running ALL Benchmarks..." -ForegroundColor Green
        Write-Host "⚠️  This will take 20-30 minutes!`n" -ForegroundColor Yellow
        
        $confirm = Read-Host "Continue? (Y/N)"
        if ($confirm -eq "Y" -or $confirm -eq "y") {
            dotnet run -c Release
        } else {
            Write-Host "❌ Cancelled" -ForegroundColor Red
            exit 0
        }
    }
    
    "Q" {
        Write-Host "`n👋 Goodbye!" -ForegroundColor Cyan
        exit 0
    }
    
    default {
        Write-Host "`n❌ Invalid choice" -ForegroundColor Red
        exit 1
    }
}

# Show results
Write-Host "`n═══════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "  Results" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════════════════════`n" -ForegroundColor Cyan

if (Test-Path "BenchmarkDotNet.Artifacts\results") {
    Write-Host "📁 Results saved to: BenchmarkDotNet.Artifacts\results\" -ForegroundColor Green
    Write-Host ""
    
    $viewResults = Read-Host "Open results folder? (Y/N)"
    if ($viewResults -eq "Y" -or $viewResults -eq "y") {
        Start-Process "BenchmarkDotNet.Artifacts\results"
    }
}

Write-Host "`n✅ Done!" -ForegroundColor Green
