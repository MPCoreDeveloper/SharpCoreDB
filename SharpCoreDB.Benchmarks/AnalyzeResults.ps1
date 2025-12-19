# Analyze Benchmark Results
# Run this AFTER you've run the benchmarks manually

Write-Host "???????????????????????????????????????????????????????" -ForegroundColor Cyan
Write-Host "  SharpCoreDB Benchmark Results Analyzer" -ForegroundColor Cyan
Write-Host "???????????????????????????????????????????????????????" -ForegroundColor Cyan
Write-Host ""

# Check if results exist
if (-not (Test-Path "BenchmarkDotNet.Artifacts\results")) {
    Write-Host "? No results found!" -ForegroundColor Red
    Write-Host ""
    Write-Host "Please run benchmarks first:" -ForegroundColor Yellow
    Write-Host "  1. Start the benchmark app: dotnet run -c Release" -ForegroundColor White
    Write-Host "  2. Choose option 1 or 2" -ForegroundColor White
    Write-Host "  3. Wait for completion" -ForegroundColor White
    Write-Host "  4. Run this script again" -ForegroundColor White
    exit 1
}

# Find most recent markdown files
$mdFiles = Get-ChildItem -Path "BenchmarkDotNet.Artifacts\results" -Filter "*-report-github.md" -File | 
    Sort-Object LastWriteTime -Descending

if ($mdFiles.Count -eq 0) {
    Write-Host "? No markdown result files found!" -ForegroundColor Red
    exit 1
}

Write-Host "Found $($mdFiles.Count) result file(s):" -ForegroundColor Green
Write-Host ""

# Show recent results
foreach ($file in $mdFiles | Select-Object -First 5) {
    Write-Host "?? $($file.Name)" -ForegroundColor Cyan
    Write-Host "   Last modified: $($file.LastWriteTime)" -ForegroundColor Gray
    Write-Host "   Size: $([math]::Round($file.Length / 1KB, 2)) KB" -ForegroundColor Gray
    Write-Host ""
}

# Ask which to analyze
Write-Host "Which benchmark do you want to analyze?" -ForegroundColor Yellow
Write-Host ""
Write-Host "  1) Most recent ($(($mdFiles | Select-Object -First 1).Name))" -ForegroundColor White
Write-Host "  2) Show all recent results" -ForegroundColor White
Write-Host "  3) Open results folder" -ForegroundColor White
Write-Host ""

$choice = Read-Host "Enter choice (1-3)"

switch ($choice) {
    "1" {
        $latest = $mdFiles | Select-Object -First 1
        Write-Host ""
        Write-Host "???????????????????????????????????????????????????????" -ForegroundColor Cyan
        Write-Host "  Analysis: $($latest.Name)" -ForegroundColor Cyan
        Write-Host "???????????????????????????????????????????????????????" -ForegroundColor Cyan
        Write-Host ""
        
        # Read and display content
        $content = Get-Content $latest.FullName -Raw
        
        # Extract table
        $lines = $content -split "`n"
        $inTable = $false
        $tableLines = @()
        
        foreach ($line in $lines) {
            if ($line -match "^\| Method ") {
                $inTable = $true
            }
            
            if ($inTable) {
                $tableLines += $line
                
                if ($line -notmatch "^\|") {
                    break
                }
            }
        }
        
        # Display table
        if ($tableLines.Count -gt 0) {
            Write-Host "?? BENCHMARK RESULTS:" -ForegroundColor Green
            Write-Host ""
            foreach ($line in $tableLines) {
                if ($line -match "Baseline") {
                    Write-Host $line -ForegroundColor Yellow
                }
                elseif ($line -match "Optimized") {
                    Write-Host $line -ForegroundColor Green
                }
                elseif ($line -match "SQLite") {
                    Write-Host $line -ForegroundColor Cyan
                }
                else {
                    Write-Host $line
                }
            }
            Write-Host ""
        }
        
        # Check for issues
        if ($content -match "Benchmarks with issues:") {
            Write-Host "??  WARNING: Some benchmarks had issues!" -ForegroundColor Red
            Write-Host ""
            
            $issuesStart = $content.IndexOf("Benchmarks with issues:")
            $issuesSection = $content.Substring($issuesStart).Split("`n") | Select-Object -First 20
            
            foreach ($line in $issuesSection) {
                if ($line.Trim()) {
                    Write-Host "   $line" -ForegroundColor Yellow
                }
            }
            Write-Host ""
        }
        else {
            Write-Host "? All benchmarks completed successfully!" -ForegroundColor Green
            Write-Host ""
        }
        
        # Offer to open HTML report
        $htmlFile = $latest.FullName -replace "-github\.md$", ".html"
        if (Test-Path $htmlFile) {
            $openHtml = Read-Host "Open interactive HTML report? (Y/N)"
            if ($openHtml -eq "Y" -or $openHtml -eq "y") {
                Start-Process $htmlFile
            }
        }
    }
    
    "2" {
        Write-Host ""
        Write-Host "???????????????????????????????????????????????????????" -ForegroundColor Cyan
        Write-Host "  All Recent Results" -ForegroundColor Cyan
        Write-Host "???????????????????????????????????????????????????????" -ForegroundColor Cyan
        Write-Host ""
        
        foreach ($file in $mdFiles | Select-Object -First 10) {
            Write-Host "?? $($file.Name)" -ForegroundColor Cyan
            Write-Host "   Modified: $($file.LastWriteTime)" -ForegroundColor Gray
            Write-Host "   Path: $($file.FullName)" -ForegroundColor DarkGray
            Write-Host ""
        }
    }
    
    "3" {
        Write-Host ""
        Write-Host "Opening results folder..." -ForegroundColor Cyan
        Start-Process "BenchmarkDotNet.Artifacts\results"
    }
    
    default {
        Write-Host ""
        Write-Host "Invalid choice" -ForegroundColor Red
    }
}

Write-Host ""
Write-Host "???????????????????????????????????????????????????????" -ForegroundColor Cyan
Write-Host "  Analysis Complete" -ForegroundColor Cyan
Write-Host "???????????????????????????????????????????????????????" -ForegroundColor Cyan
Write-Host ""

# Offer to save analysis to file
$saveAnalysis = Read-Host "Save analysis to file? (Y/N)"
if ($saveAnalysis -eq "Y" -or $saveAnalysis -eq "y") {
    $outputFile = "benchmark_analysis_$(Get-Date -Format 'yyyy-MM-dd_HH-mm-ss').txt"
    
    $latest = $mdFiles | Select-Object -First 1
    $content = Get-Content $latest.FullName -Raw
    
    $analysis = @"
???????????????????????????????????????????????????????
  SharpCoreDB Benchmark Analysis
  Generated: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')
???????????????????????????????????????????????????????

Source File: $($latest.Name)
Last Modified: $($latest.LastWriteTime)

$content
"@
    
    $analysis | Out-File -FilePath $outputFile -Encoding UTF8
    Write-Host ""
    Write-Host "? Analysis saved to: $outputFile" -ForegroundColor Green
    Write-Host ""
}
