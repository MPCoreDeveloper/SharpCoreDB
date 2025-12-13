@echo off
REM ============================================
REM  SharpCoreDB Benchmark - Open HTML Reports
REM ============================================

echo.
echo ========================================
echo   Opening Benchmark HTML Reports
echo ========================================
echo.

REM Get the most recent HTML reports
set RESULTS_DIR=BenchmarkDotNet.Artifacts\results

echo Looking for HTML reports in: %RESULTS_DIR%
echo.

REM Check if results directory exists
if not exist "%RESULTS_DIR%" (
    echo ERROR: Results directory not found!
    echo Please run benchmarks first: RunBenchmarks.bat
    pause
    exit /b 1
)

REM Find and open all HTML reports (newest first)
set FOUND=0

for /f "delims=" %%f in ('dir /b /o-d "%RESULTS_DIR%\*-report.html" 2^>nul') do (
    set FOUND=1
    echo Opening: %%f
    start "" "%RESULTS_DIR%\%%f"
)

if %FOUND%==0 (
    echo.
    echo No HTML reports found!
    echo.
    echo This could mean:
    echo   1. Benchmarks haven't been run yet
    echo   2. Benchmarks failed during execution
    echo   3. HTML export was disabled
    echo.
    echo Run benchmarks first: RunBenchmarks.bat
    pause
    exit /b 1
)

echo.
echo ========================================
echo   All HTML reports opened in browser!
echo ========================================
echo.
echo Reports are located in:
echo %CD%\%RESULTS_DIR%
echo.

pause
