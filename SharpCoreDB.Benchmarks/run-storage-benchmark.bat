@echo off
REM SharpCoreDB Storage Engine Benchmark Runner
REM Quick and simple benchmark execution

echo ================================================================
echo   SharpCoreDB Storage Engine Benchmark
echo ================================================================
echo.

cd /d "%~dp0"

:menu
echo Choose benchmark mode:
echo.
echo   1. XUnit Test (Fast - Recommended)
echo   2. BenchmarkDotNet (Detailed - Slower)
echo   3. Exit
echo.

set /p choice="Enter choice (1-3): "

if "%choice%"=="1" goto xunit
if "%choice%"=="2" goto benchmark
if "%choice%"=="3" goto end
goto menu

:xunit
echo.
echo ================================================================
echo   Running XUnit Comparison Test
echo ================================================================
echo.

cd ..\SharpCoreDB.Tests
dotnet test --filter "FullyQualifiedName~StorageEngineComparisonTest"

if %ERRORLEVEL% EQU 0 (
    echo.
    echo ? Test completed successfully!
    echo.
    
    if exist "STORAGE_ENGINE_COMPARISON.md" (
        echo Report generated: STORAGE_ENGINE_COMPARISON.md
        echo.
        set /p view="View report? (Y/n): "
        if /i "!view!"=="Y" (
            type STORAGE_ENGINE_COMPARISON.md | more
        )
    )
) else (
    echo.
    echo ? Test failed!
    echo.
)

cd "%~dp0"
pause
goto menu

:benchmark
echo.
echo ================================================================
echo   Running BenchmarkDotNet Benchmarks
echo ================================================================
echo.
echo Note: This may take 10-20 minutes
echo.

set /p confirm="Continue? (Y/n): "
if /i not "%confirm%"=="Y" goto menu

echo.
echo Building in Release mode...
dotnet build -c Release --nologo

if %ERRORLEVEL% NEQ 0 (
    echo ? Build failed!
    pause
    goto menu
)

echo.
echo Running benchmarks...
echo.

dotnet run -c Release --no-build

if %ERRORLEVEL% EQU 0 (
    echo.
    echo ? Benchmarks completed!
    echo.
    echo Results: BenchmarkDotNet.Artifacts\results\
    echo.
    
    set /p open="Open results directory? (Y/n): "
    if /i "!open!"=="Y" (
        start explorer.exe "BenchmarkDotNet.Artifacts\results"
    )
) else (
    echo.
    echo ? Benchmarks failed!
    echo.
)

pause
goto menu

:end
echo.
echo Goodbye!
echo.
exit /b 0
