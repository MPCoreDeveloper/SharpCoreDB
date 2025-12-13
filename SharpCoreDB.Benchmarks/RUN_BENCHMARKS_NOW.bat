@echo off
echo ================================================================
echo   Running SharpCoreDB Benchmarks with Performance Fixes
echo ================================================================
echo.
echo All fixes have been implemented:
echo   - ExecuteBatchSQL now commits entire batch as single WAL entry
echo   - Async durability mode enabled for better performance
echo   - GroupCommitWAL race condition fixed
echo   - Fair SQLite comparison added
echo.
echo Expected improvements:
echo   - SharpCoreDB: 246ms -^> 10-20ms (25x faster!)
echo   - Competitive with SQLite when comparing fairly
echo.
echo This will take 5-10 minutes. Please wait...
echo.

cd /d "%~dp0"
dotnet run -c Release -- --quick

echo.
echo ================================================================
echo   Benchmark Complete!
echo ================================================================
echo.
echo Results saved to: BenchmarkDotNet.Artifacts\results\
echo.
echo View HTML reports:
start BenchmarkDotNet.Artifacts\results\ComparativeInsertBenchmarks-report.html
echo.
pause
