# ULTRA SIMPLE runner - just call the benchmark directly!

Write-Host "Starting Comprehensive Benchmark..." -ForegroundColor Green
Write-Host ""

# Just compile and run with the ComprehensiveComparison class
# The class will handle everything automatically

dotnet build --configuration Release
if ($LASTEXITCODE -eq 0) {
    Write-Host "Build successful! Running benchmark..." -ForegroundColor Green
    
    # You can run it by adding to Program.cs menu, or create simple runner
    Write-Host ""
    Write-Host "To run the benchmark, add this to your Program.cs:" -ForegroundColor Yellow
    Write-Host ""
    Write-Host 'var benchmark = new ComprehensiveComparison();' -ForegroundColor Cyan
    Write-Host 'benchmark.Run();' -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Or use the RunComprehensiveBenchmark.cs file provided!" -ForegroundColor Yellow
} else {
    Write-Host "Build failed!" -ForegroundColor Red
}
