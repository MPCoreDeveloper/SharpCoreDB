@echo off
REM Quick Benchmark Runner for SharpCoreDB
REM This script provides easy access to all benchmark modes

echo ================================================================
echo   SharpCoreDB Benchmark Suite - Quick Runner
echo ================================================================
echo.

:MENU
echo Select benchmark mode:
echo.
echo   1. Quick Comparison (5-10 min, RECOMMENDED)
echo   2. Full Comprehensive Suite (20-30 min)
echo   3. INSERT Benchmarks Only
echo   4. SELECT Benchmarks Only
echo   5. UPDATE/DELETE Benchmarks Only
echo   6. View Latest HTML Reports
echo   7. Clean Results Directory
echo   Q. Quit
echo.
set /p choice="Enter choice (1-7 or Q): "

if "%choice%"=="1" goto QUICK
if "%choice%"=="2" goto FULL
if "%choice%"=="3" goto INSERTS
if "%choice%"=="4" goto SELECTS
if "%choice%"=="5" goto UPDATES
if "%choice%"=="6" goto VIEW
if "%choice%"=="7" goto CLEAN
if /i "%choice%"=="Q" goto END

echo Invalid choice. Please try again.
echo.
goto MENU

:QUICK
echo.
echo ================================================================
echo   Running QUICK Comparison
echo   Estimated time: 5-10 minutes
echo ================================================================
echo.
dotnet run -c Release -- --quick
goto RESULTS

:FULL
echo.
echo ================================================================
echo   Running FULL Comprehensive Suite
echo   Estimated time: 20-30 minutes
echo   WARNING: This will take a while!
echo ================================================================
echo.
set /p confirm="Are you sure? (Y/N): "
if /i not "%confirm%"=="Y" goto MENU
echo.
dotnet run -c Release -- --full
goto RESULTS

:INSERTS
echo.
echo ================================================================
echo   Running INSERT Benchmarks Only
echo   Estimated time: 3-5 minutes
echo ================================================================
echo.
dotnet run -c Release -- --inserts
goto RESULTS

:SELECTS
echo.
echo ================================================================
echo   Running SELECT Benchmarks Only
echo   Estimated time: 3-5 minutes
echo ================================================================
echo.
dotnet run -c Release -- --selects
goto RESULTS

:UPDATES
echo.
echo ================================================================
echo   Running UPDATE/DELETE Benchmarks Only
echo   Estimated time: 2-4 minutes
echo ================================================================
echo.
dotnet run -c Release -- --updates
goto RESULTS

:RESULTS
echo.
echo ================================================================
echo   Benchmark Complete!
echo ================================================================
echo.
echo Results saved to: BenchmarkDotNet.Artifacts\results\
echo.
echo Available formats:
echo   - HTML (interactive reports with charts)
echo   - CSV (Excel-compatible)
echo   - JSON (programmatic access)
echo   - Markdown (GitHub-ready)
echo.
set /p viewresults="View HTML reports now? (Y/N): "
if /i "%viewresults%"=="Y" goto VIEW
echo.
goto MENU

:VIEW
echo.
echo Opening HTML reports...
start BenchmarkDotNet.Artifacts\results\ComparativeInsertBenchmarks-report.html 2>nul
start BenchmarkDotNet.Artifacts\results\ComparativeSelectBenchmarks-report.html 2>nul
start BenchmarkDotNet.Artifacts\results\ComparativeUpdateDeleteBenchmarks-report.html 2>nul
timeout /t 2 >nul
explorer BenchmarkDotNet.Artifacts\results
echo.
goto MENU

:CLEAN
echo.
echo ================================================================
echo   Cleaning Results Directory
echo ================================================================
echo.
set /p confirmclean="Delete all benchmark results? (Y/N): "
if /i not "%confirmclean%"=="Y" goto MENU
echo.
if exist "BenchmarkDotNet.Artifacts" (
    echo Deleting BenchmarkDotNet.Artifacts...
    rmdir /s /q BenchmarkDotNet.Artifacts
    echo Done!
) else (
    echo No results directory found.
)
echo.
goto MENU

:END
echo.
echo Thank you for using SharpCoreDB Benchmarks!
echo.
pause
