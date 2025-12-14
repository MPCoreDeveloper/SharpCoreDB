# SharpCoreDB Comprehensive Benchmark Runner
# Runs all comparative benchmarks and generates reports

Write-Host "═══════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "  SharpCoreDB Comprehensive Benchmark Suite" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════════════════════`n" -ForegroundColor Cyan

# Check if we're in the right directory
if (-not (Test-Path "SharpCoreDB.Benchmarks.csproj")) {
    Write-Host "❌ Error: Must run from SharpCoreDB.Benchmarks directory" -ForegroundColor Red
    exit 1
}

# Menu
Write-Host "Select benchmark mode:" -ForegroundColor Yellow
Write-Host ""
Write-Host "  1. Quick 10K Test (2-3 minutes) - RECOMMENDED" -ForegroundColor Green
Write-Host "  2. Comparative Insert Benchmarks (5-10 minutes)"
Write-Host "  3. Comparative Select Benchmarks (5-10 minutes)"
Write-Host "  4. Comparative Update/Delete Benchmarks (5-10 minutes)"
Write-Host "  5. Full Comparative Suite (20-30 minutes)"
Write-Host "  6. Column Store SIMD Benchmarks (2-3 minutes)"
Write-Host "  7. All Benchmarks (30-45 minutes)"
Write-Host "  Q. Quit"
Write-Host ""

$choice = Read-Host "Enter choice (1-7 or Q)"

switch ($choice) {
    "1" {
        Write-Host "`n🚀 Running Quick 10K Test..." -ForegroundColor Green
        Write-Host "This will benchmark 10,000 record inserts against SQLite and LiteDB`n" -ForegroundColor Gray
        
        Push-Location ..\SharpCoreDB.Quick10kBenchmark
        dotnet run -c Release
        Pop-Location
        
        Write-Host "`n✅ Quick test complete!" -ForegroundColor Green
        Write-Host "📊 Results displayed above" -ForegroundColor Cyan
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
        Write-Host "`n🚀 Running Full Comparative Suite..." -ForegroundColor Green
        Write-Host "⚠️  This will take 20-30 minutes!`n" -ForegroundColor Yellow
        
        $confirm = Read-Host "Continue? (Y/N)"
        if ($confirm -eq "Y" -or $confirm -eq "y") {
            dotnet run -c Release --filter "*Comparative*"
        } else {
            Write-Host "❌ Cancelled" -ForegroundColor Red
            exit 0
        }
    }
    
    "6" {
        Write-Host "`n🚀 Running Column Store SIMD Benchmarks..." -ForegroundColor Green
        Write-Host "Testing SIMD-accelerated aggregates on columnar storage`n" -ForegroundColor Gray
        
        Push-Location ..\SharpCoreDB.Tests
        dotnet test --filter "FullyQualifiedName~ColumnStoreTests" --logger "console;verbosity=detailed"
        Pop-Location
        
        Write-Host "`n✅ Column Store tests complete!" -ForegroundColor Green
    }
    
    "7" {
        Write-Host "`n🚀 Running ALL Benchmarks..." -ForegroundColor Green
        Write-Host "⚠️  This will take 30-45 minutes!`n" -ForegroundColor Yellow
        
        $confirm = Read-Host "Continue? (Y/N)"
        if ($confirm -eq "Y" -or $confirm -eq "y") {
            # Run quick test
            Write-Host "`n📊 1/3: Quick 10K Test..." -ForegroundColor Cyan
            Push-Location ..\SharpCoreDB.Quick10kBenchmark
            dotnet run -c Release
            Pop-Location
            
            # Run column store tests
            Write-Host "`n📊 2/3: Column Store SIMD Tests..." -ForegroundColor Cyan
            Push-Location ..\SharpCoreDB.Tests
            dotnet test --filter "FullyQualifiedName~ColumnStoreTests" --logger "console;verbosity=minimal"
            Pop-Location
            
            # Run full comparative suite
            Write-Host "`n📊 3/3: Full Comparative Suite..." -ForegroundColor Cyan
            dotnet run -c Release --filter "*Comparative*"
            
            Write-Host "`n✅ All benchmarks complete!" -ForegroundColor Green
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

# Show results location
Write-Host "`n═══════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "  Benchmark Results" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════════════════════`n" -ForegroundColor Cyan

if (Test-Path "BenchmarkDotNet.Artifacts\results") {
    Write-Host "📁 Results saved to:" -ForegroundColor Green
    Write-Host "   BenchmarkDotNet.Artifacts\results\`n" -ForegroundColor Gray
    
    Write-Host "📊 Available formats:" -ForegroundColor Cyan
    Write-Host "   • HTML reports (interactive charts)"
    Write-Host "   • CSV files (Excel-compatible)"
    Write-Host "   • JSON data (programmatic access)"
    Write-Host "   • Markdown tables (GitHub-ready)`n"
    
    $viewResults = Read-Host "Open results folder? (Y/N)"
    if ($viewResults -eq "Y" -or $viewResults -eq "y") {
        Start-Process "BenchmarkDotNet.Artifacts\results"
    }
}

Write-Host "`n✅ Done!" -ForegroundColor Green
Write-Host ""
