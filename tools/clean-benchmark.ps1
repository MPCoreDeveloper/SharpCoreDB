<#
.SYNOPSIS
    Clean benchmark runner for SharpCoreDB. Clears environment, prepares a quiet measurement,
    runs the comparative harness with recommended flags, and reports regime clearly.

    This implements the canonical protocol from docs/performance/INSERT_UPDATE_PERFORMANCE_PLAN.md
    (Phase 0 + §11 + §2).

    Usage examples:
      .\clean-benchmark.ps1 --pk --dual-mode
      .\clean-benchmark.ps1 --pk --dual-mode --gate
      .\clean-benchmark.ps1 --pk-default
      .\clean-benchmark.ps1 --multirowinsert --SHARPCOREDB_MULTIROW_ROWS=1

    The script always clears SHARPCOREDB_* variables first, runs in Release, discards cold JIT
    implicitly via multiple reps, and prints the full [diag] line for reproducibility.
#>

param(
    [Parameter(ValueFromRemainingArguments=$true)]
    [string[]]$BenchmarkArgs = @("--pk", "--dual-mode")
)

$repoRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$benchmarkProject = Join-Path $repoRoot "tests\benchmarks\SharpCoreDB.Benchmarks.Comparative"

Write-Host "=== SharpCoreDB Clean Benchmark Runner ===" -ForegroundColor Cyan
Write-Host "Repository root : $repoRoot" -ForegroundColor Gray
Write-Host "Project         : $benchmarkProject" -ForegroundColor Gray
Write-Host "Arguments       : $($BenchmarkArgs -join ' ')" -ForegroundColor Gray
Write-Host ""

# 1. Clear environment variables that leak between runs (biggest source of "default" contamination)
$envVarsToClear = @(
    "SHARPCOREDB_BUFFERED_APPENDS",
    "SHARPCOREDB_WAL_DURABILITY",
    "SHARPCOREDB_QUERY_CACHE",
    "SHARPCOREDB_WRITE_PROFILE",
    "SHARPCOREDB_MULTIROW_ROWS",
    "SHARPCOREDB_PK_AB_ARM_A",
    "SHARPCOREDB_PK_AB_ARM_B",
    "SHARPCOREDB_BENCH_REPS"
)

foreach ($var in $envVarsToClear) {
    if (Test-Path "env:$var") {
        Remove-Item "env:$var"
        Write-Host "Cleared env:$var" -ForegroundColor Yellow
    }
}

Write-Host "Environment cleared. Running in a clean shell.`n" -ForegroundColor Green

# 2. Optional: remind user of quiet-machine best practices
Write-Host "=== Pre-flight recommendations for minimal noise (from performance plan) ===" -ForegroundColor Magenta
Write-Host "• Reboot if session is long-running" -ForegroundColor Gray
Write-Host "• Close browser tabs, SCDMS, VS debugging, OneDrive, antivirus scans" -ForegroundColor Gray
Write-Host "• Set Power Plan to High Performance" -ForegroundColor Gray
Write-Host "• In Task Manager → Details: set pwsh.exe + dotnet.exe to High priority" -ForegroundColor Gray
Write-Host "• Run this script in a fresh pwsh window (pwsh -NoProfile recommended)" -ForegroundColor Gray
Write-Host "• For absolute lowest noise: launch as detached process or redirect output to file`n" -ForegroundColor Gray

# 3. Build & run the benchmark (Release is mandatory for meaningful numbers)
Write-Host "Building and running benchmark (Release configuration)..." -ForegroundColor Cyan

$runCommand = "dotnet run -c Release --project `"$benchmarkProject`" -- $($BenchmarkArgs -join ' ')"

Write-Host "Executing: $runCommand`n" -ForegroundColor White

# Execute — let the harness print its own [diag] line, JSON output, min-median-max, etc.
Invoke-Expression $runCommand

Write-Host "`n=== Benchmark completed ===" -ForegroundColor Green
Write-Host "Check the JSON in tests/benchmarks/SharpCoreDB.Benchmarks.Comparative/results/" -ForegroundColor Gray
Write-Host "Use median values. If rep spread >2.5× the gate will report INCONCLUSIVE." -ForegroundColor Gray
Write-Host "Re-run on a completely quiet machine (after reboot if needed) for baseline recording." -ForegroundColor Gray
