@echo off
REM Simpelste Storage Engine Benchmark Runner
REM Dubbelklik om te runnen

echo.
echo ==================================================
echo   SharpCoreDB Storage Engine Benchmark
echo ==================================================
echo.

cd /d "D:\source\repos\MPCoreDeveloper\SharpCoreDB\SharpCoreDB.Tests"

echo Running test...
echo.

dotnet test --filter "FullyQualifiedName~StorageEngineComparisonTest"

echo.
echo ==================================================
echo.

if exist "STORAGE_ENGINE_COMPARISON.md" (
    echo Report generated: STORAGE_ENGINE_COMPARISON.md
    echo.
    echo Opening report...
    start notepad "STORAGE_ENGINE_COMPARISON.md"
) else (
    echo No report file found
)

echo.
pause
