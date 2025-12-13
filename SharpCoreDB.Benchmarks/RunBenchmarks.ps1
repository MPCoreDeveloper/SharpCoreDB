# SharpCoreDB Benchmark Runner (PowerShell)
# Quick access to all benchmark modes with result viewing

function Show-Menu {
    Clear-Host
    Write-Host "================================================================" -ForegroundColor Cyan
    Write-Host "  SharpCoreDB Benchmark Suite - Quick Runner" -ForegroundColor Cyan
    Write-Host "================================================================" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Select benchmark mode:" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "  1. Quick Comparison (5-10 min, RECOMMENDED)" -ForegroundColor Green
    Write-Host "  2. Full Comprehensive Suite (20-30 min)" -ForegroundColor White
    Write-Host "  3. INSERT Benchmarks Only" -ForegroundColor White
    Write-Host "  4. SELECT Benchmarks Only" -ForegroundColor White
    Write-Host "  5. UPDATE/DELETE Benchmarks Only" -ForegroundColor White
    Write-Host "  6. View Latest HTML Reports" -ForegroundColor Cyan
    Write-Host "  7. Open Results Directory" -ForegroundColor Cyan
    Write-Host "  8. Clean Results Directory" -ForegroundColor Red
    Write-Host "  Q. Quit" -ForegroundColor Gray
    Write-Host ""
}

function Run-Benchmark {
    param(
        [string]$Mode,
        [string]$Description,
        [string]$EstimatedTime
    )
    
    Write-Host ""
    Write-Host "================================================================" -ForegroundColor Cyan
    Write-Host "  $Description" -ForegroundColor Cyan
    Write-Host "  Estimated time: $EstimatedTime" -ForegroundColor Yellow
    Write-Host "================================================================" -ForegroundColor Cyan
    Write-Host ""
    
    $startTime = Get-Date
    
    dotnet run -c Release -- $Mode
    
    $endTime = Get-Date
    $duration = $endTime - $startTime
    
    Write-Host ""
    Write-Host "================================================================" -ForegroundColor Green
    Write-Host "  Benchmark Complete!" -ForegroundColor Green
    Write-Host "  Actual time: $($duration.ToString('mm\:ss'))" -ForegroundColor Green
    Write-Host "================================================================" -ForegroundColor Green
    Write-Host ""
    
    Show-Results
}

function Show-Results {
    Write-Host "Results saved to: BenchmarkDotNet.Artifacts\results\" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Available formats:" -ForegroundColor Yellow
    Write-Host "  - HTML (interactive reports with charts)" -ForegroundColor White
    Write-Host "  - CSV (Excel-compatible)" -ForegroundColor White
    Write-Host "  - JSON (programmatic access)" -ForegroundColor White
    Write-Host "  - Markdown (GitHub-ready)" -ForegroundColor White
    Write-Host ""
    
    $viewResults = Read-Host "View HTML reports now? (Y/N)"
    if ($viewResults -eq "Y" -or $viewResults -eq "y") {
        Open-HtmlReports
    }
    
    Write-Host ""
    Write-Host "Press any key to continue..." -ForegroundColor Gray
    $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
}

function Open-HtmlReports {
    Write-Host ""
    Write-Host "Opening HTML reports..." -ForegroundColor Cyan
    
    $reportsDir = "BenchmarkDotNet.Artifacts\results"
    
    if (Test-Path $reportsDir) {
        # Find and open all HTML reports
        $htmlFiles = Get-ChildItem -Path $reportsDir -Filter "*.html" -ErrorAction SilentlyContinue
        
        if ($htmlFiles.Count -gt 0) {
            foreach ($file in $htmlFiles) {
                Start-Process $file.FullName
            }
            Start-Sleep -Seconds 1
            # Also open results directory
            Start-Process explorer.exe $reportsDir
        } else {
            Write-Host "No HTML reports found yet. Run a benchmark first." -ForegroundColor Yellow
        }
    } else {
        Write-Host "Results directory not found. Run a benchmark first." -ForegroundColor Yellow
    }
}

function Open-ResultsDirectory {
    $reportsDir = "BenchmarkDotNet.Artifacts\results"
    
    if (Test-Path $reportsDir) {
        Start-Process explorer.exe $reportsDir
        Write-Host "Results directory opened." -ForegroundColor Green
    } else {
        Write-Host "Results directory not found. Run a benchmark first." -ForegroundColor Yellow
    }
    
    Write-Host ""
    Write-Host "Press any key to continue..." -ForegroundColor Gray
    $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
}

function Clean-Results {
    Write-Host ""
    Write-Host "================================================================" -ForegroundColor Red
    Write-Host "  Cleaning Results Directory" -ForegroundColor Red
    Write-Host "================================================================" -ForegroundColor Red
    Write-Host ""
    
    $confirm = Read-Host "Delete all benchmark results? (Y/N)"
    
    if ($confirm -eq "Y" -or $confirm -eq "y") {
        if (Test-Path "BenchmarkDotNet.Artifacts") {
            Write-Host "Deleting BenchmarkDotNet.Artifacts..." -ForegroundColor Yellow
            Remove-Item -Path "BenchmarkDotNet.Artifacts" -Recurse -Force
            Write-Host "Done! Results directory cleaned." -ForegroundColor Green
        } else {
            Write-Host "No results directory found." -ForegroundColor Yellow
        }
    } else {
        Write-Host "Cancelled." -ForegroundColor Gray
    }
    
    Write-Host ""
    Write-Host "Press any key to continue..." -ForegroundColor Gray
    $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
}

# Main script
while ($true) {
    Show-Menu
    $choice = Read-Host "Enter choice (1-8 or Q)"
    
    switch ($choice) {
        "1" {
            Run-Benchmark -Mode "--quick" `
                         -Description "Running QUICK Comparison" `
                         -EstimatedTime "5-10 minutes"
        }
        "2" {
            Write-Host ""
            Write-Host "WARNING: Full suite may take 20-30 minutes!" -ForegroundColor Yellow
            $confirm = Read-Host "Are you sure? (Y/N)"
            
            if ($confirm -eq "Y" -or $confirm -eq "y") {
                Run-Benchmark -Mode "--full" `
                             -Description "Running FULL Comprehensive Suite" `
                             -EstimatedTime "20-30 minutes"
            }
        }
        "3" {
            Run-Benchmark -Mode "--inserts" `
                         -Description "Running INSERT Benchmarks Only" `
                         -EstimatedTime "3-5 minutes"
        }
        "4" {
            Run-Benchmark -Mode "--selects" `
                         -Description "Running SELECT Benchmarks Only" `
                         -EstimatedTime "3-5 minutes"
        }
        "5" {
            Run-Benchmark -Mode "--updates" `
                         -Description "Running UPDATE/DELETE Benchmarks Only" `
                         -EstimatedTime "2-4 minutes"
        }
        "6" {
            Open-HtmlReports
            Write-Host ""
            Write-Host "Press any key to continue..." -ForegroundColor Gray
            $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
        }
        "7" {
            Open-ResultsDirectory
        }
        "8" {
            Clean-Results
        }
        {$_ -eq "Q" -or $_ -eq "q"} {
            Write-Host ""
            Write-Host "Thank you for using SharpCoreDB Benchmarks!" -ForegroundColor Green
            Write-Host ""
            exit
        }
        default {
            Write-Host ""
            Write-Host "Invalid choice. Please try again." -ForegroundColor Red
            Write-Host ""
            Start-Sleep -Seconds 1
        }
    }
}
