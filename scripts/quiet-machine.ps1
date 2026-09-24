#!/usr/bin/env pwsh
# SharpCoreDB - measurement-environment check ("is this box quiet enough to benchmark?")
#
# WHY THIS EXISTS
# The write-path gate refuses to record a baseline on a loaded machine (brief section 11). Measured on
# the campaign's own dev laptop (i7-10850H, 32 GB, NVMe), a `--dual-mode` run showed a 3,9x within-run
# UPDATE spread while the machine was simultaneously:
#   - NOT thermally throttled  (% of Maximum Frequency held at 100 % for the whole run)
#   - NOT CPU-saturated        (total CPU 2-22 %)
#   - NOT disk-saturated       (physical-disk queue length 0,00; ~1 MB/s)
# and it WAS showing MsMpEng (Defender real-time) at 0-17 % CPU, correlated with the benchmark's own
# phases. The engine does thousands of tiny `FileOptions.WriteThrough` opens, so every one of them is a
# filter-driver callback and the workload is latency-bound, not bandwidth-bound. That is the signature
# of this machine's noise: per-I/O interference, not raw load.
#
# So "quiet" here means low per-I/O interference, and every check below is evidence for that.
#
# USAGE
#   pwsh scripts/quiet-machine.ps1                        # diagnose only (read-only; no admin needed)
#   pwsh scripts/quiet-machine.ps1 -Apply                 # also apply the fixes it can (NEEDS ADMIN)
#   pwsh scripts/quiet-machine.ps1 -Apply -StopServices    # ...and stop Search/SysMain/DiagTrack
#
# Exits 0 when the verdict is QUIET, 1 when NOISY, 2 on error - so it can gate a session:
#   pwsh scripts/quiet-machine.ps1; if ($LASTEXITCODE -ne 0) { 'do not record a baseline' }

[CmdletBinding(SupportsShouldProcess)]
param(
    # Apply the mitigations it is allowed to (Defender exclusions, build-server shutdown). Admin needed.
    [switch]$Apply,
    # With -Apply: also stop Windows Search, SysMain and DiagTrack for this session. Intrusive - opt in.
    [switch]$StopServices,
    # Directory the benchmark's data files land in. Narrowing this keeps the Defender exclusion small
    # instead of excluding all of %TEMP%.
    [string]$BenchTempDir = ''
)

$ErrorActionPreference = 'Stop'

$script:noisy = @()

function Add-Finding {
    param([string]$Reason, [string]$Detail)
    $script:noisy += [pscustomobject]@{ Reason = $Reason; Detail = $Detail }
}

function Get-CounterSafe {
    param([string]$Path, [string]$Match)
    try {
        $s = Get-Counter -Counter $Path -SampleInterval 1 -MaxSamples 3 -ErrorAction Stop
        $vals = $s.CounterSamples | Where-Object { $_.Path -like "*$Match*" } | Select-Object -ExpandProperty CookedValue
        if (-not $vals) { return $null }
        return [math]::Round(($vals | Measure-Object -Average).Average, 1)
    } catch { return $null }
}

Write-Host "SharpCoreDB - measurement environment check" -ForegroundColor Cyan
Write-Host ""

# ---- 1. Power posture ------------------------------------------------------
$battery = Get-CimInstance Win32_Battery -ErrorAction SilentlyContinue
$onAc    = ($battery | Select-Object -First 1).BatteryStatus -ne 1
$plan    = (powercfg /getactivescheme) -replace '.*\(|\)', ''
Write-Host ("Power          : plan='{0}'  on AC={1}" -f $plan, $onAc)
if (-not $onAc) { Add-Finding 'On battery' 'DC power caps the CPU and adds latency variance; plug in.' }
if ($plan -notmatch 'High performance|Ultimate') {
    Add-Finding 'Power plan' "Active plan is '$plan'; 'High performance' removes core parking."
}

# ---- 2. Thermal / power throttling -----------------------------------------
$maxFreq = Get-CounterSafe -Path '\Processor Information(_Total)\% of Maximum Frequency' -Match 'Maximum Frequency'
Write-Host ("MaxFreq        : {0} %   (100 = no throttle; <95 = throttling)" -f $maxFreq)
if ($null -ne $maxFreq -and $maxFreq -lt 95) {
    Add-Finding 'Thermal/power throttle' "Achieved $maxFreq % of maximum clock - cool the machine or cap run length."
}

# ---- 3. Total CPU headroom -------------------------------------------------
$procPct = Get-CounterSafe -Path '\Processor Information(_Total)\% Processor Time' -Match 'Processor Time'
Write-Host ("Total CPU      : {0} %   (idle baseline; keep under ~25 %)" -f $procPct)
if ($null -ne $procPct -and $procPct -gt 25) {
    Add-Finding 'CPU busy' "Total CPU at $procPct % before the benchmark starts."
}

# ---- 4. THE main suspect: Defender's real-time filter ----------------------
$mp = Get-CounterSafe -Path '\Process(MsMpEng)\% Processor Time' -Match 'MsMpEng'
Write-Host ("MsMpEng        : {0} %   (Defender real-time; the #1 per-I/O noise source here)" -f $mp)
if ($null -ne $mp -and $mp -gt 2) {
    # Threshold is 2 %, not 5 %: an idle Defender should be near zero, and this machine measured
    # MsMpEng at 0-17 % *during* a run while the box was otherwise idle. An idle reading above ~2 %
    # means a background scan is in flight, which is exactly when a measurement is least trustworthy.
    Add-Finding 'Defender real-time active' "MsMpEng at $mp % while idle - a background scan is in flight; each tiny write-through open is a filter callback."
}
foreach ($svc in 'NisSrv', 'SearchIndexer') {
    $v = Get-CounterSafe -Path "\Process($svc)\% Processor Time" -Match $svc
    if ($null -ne $v -and $v -gt 5) { Add-Finding "$svc busy" "$svc at $v %." }
}

# ---- 5. Disk saturation ----------------------------------------------------
$queue = Get-CounterSafe -Path '\PhysicalDisk(_Total)\Avg. Disk Queue Length' -Match 'Disk Queue'
Write-Host ("Disk queue     : {0}   (>2 means the I/O path is saturated)" -f $queue)
if ($null -ne $queue -and $queue -gt 2) { Add-Finding 'Disk saturated' "Average disk queue $queue." }

# ---- 6. Free space (NVMe write latency degrades as the drive fills) --------
# Filter to real volume letters: Get-PSDrive also reports aliases ('Temp'), which is not a volume.
foreach ($d in (Get-PSDrive -PSProvider FileSystem | Where-Object { $_.Name -match '^[A-Za-z]$' -and $_.Free -gt 0 })) {
    $freeGb = [math]::Round($d.Free / 1GB, 1)
    Write-Host ("Drive {0}:        {1} GB free" -f $d.Name, $freeGb)
    if ($freeGb -lt 15) {
        Add-Finding "Low free space on $($d.Name)" "$freeGb GB free - a nearly-full NVMe has worse write latency and more GC variance."
    }
}

# ---- 7. Competing background workloads -------------------------------------
$services = @('WSearch', 'SysMain', 'DiagTrack') | ForEach-Object { Get-Service $_ -ErrorAction SilentlyContinue } |
    Where-Object Status -eq 'Running'
if ($services) {
    Write-Host ("Running        : {0}" -f (($services.Name) -join ', '))
    # Windows Search is a finding: it indexes the repo and %TEMP%, which is real added I/O on the
    # exact paths the benchmark hammers. SysMain is pointless on an NVMe and DiagTrack is small, so
    # those two stay informational rather than flipping the verdict.
    if ($services.Name -contains 'WSearch') {
        Add-Finding 'Windows Search indexing' 'WSearch is running and indexes the repo and %TEMP%; stop it for a measurement session.'
    }
}

$buildServers = @(Get-Process -Name 'MSBuild', 'VBCSCompiler' -ErrorAction SilentlyContinue)
if ($buildServers.Count -gt 0) {
    Add-Finding 'Build servers alive' "$($buildServers.Count) MSBuild/VBCSCompiler processes are idling and waking on timers."
}

$ideProcs = @(Get-Process -Name 'Code', 'devenv' -ErrorAction SilentlyContinue)
if ($ideProcs.Count -gt 0) {
    Write-Host ("IDE            : {0} VS Code / devenv processes (Cline runs inside these; expected, not a finding)" -f $ideProcs.Count)
}

$benchRunning = @(Get-Process -Name 'SharpCoreDB.Benchmarks.Comparative', 'SharpCoreDB.Tests' -ErrorAction SilentlyContinue)
if ($benchRunning.Count -gt 0) {
    Add-Finding 'Benchmark already running' "$($benchRunning.Count) harness/test process(es) active - nothing measured now is valid."
}

# ---- Verdict ---------------------------------------------------------------
Write-Host ""
if ($script:noisy.Count -eq 0) {
    Write-Host "VERDICT: QUIET - no known noise source is active. A gate/baseline run is defensible." -ForegroundColor Green
} else {
    Write-Host "VERDICT: NOISY - $($script:noisy.Count) finding(s):" -ForegroundColor Yellow
    foreach ($f in $script:noisy) { Write-Host ("  - {0}: {1}" -f $f.Reason, $f.Detail) -ForegroundColor Yellow }
    Write-Host ""
    Write-Host "  Do not record a baseline. Fix, then re-run this check." -ForegroundColor Yellow
}

# ---- Optional mitigation ---------------------------------------------------
if (-not $Apply) {
    if ($script:noisy.Count -gt 0) {
        Write-Host ""
        Write-Host "Re-run with -Apply to fix what can be fixed (Defender exclusions + build servers)." -ForegroundColor Cyan
    }
    exit $(if ($script:noisy.Count -eq 0) { 0 } else { 1 })
}

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)

Write-Host ""
Write-Host "Applying mitigations..." -ForegroundColor Cyan
if (-not $isAdmin) {
    Write-Host "  NOT ELEVATED - the Defender and service steps need admin. Run this from an elevated shell:" -ForegroundColor Red
    Write-Host "    pwsh scripts/quiet-machine.ps1 -Apply" -ForegroundColor Red
}

# Always available: drop the idle compiler servers.
if ($PSCmdlet.ShouldProcess('dotnet build servers', 'shutdown')) {
    & dotnet build-server shutdown | Out-Null
    Write-Host "  [ok] build servers shut down" -ForegroundColor Green
}

if ($isAdmin) {
    $paths = @((Split-Path -Parent $PSScriptRoot))   # the repo
    if ($BenchTempDir) { $paths += $BenchTempDir }
    $exePath = Join-Path $PSScriptRoot '..\tests\benchmarks\SharpCoreDB.Benchmarks.Comparative\bin\Release\net11.0\SharpCoreDB.Benchmarks.Comparative.exe'

    foreach ($p in $paths) {
        if ($PSCmdlet.ShouldProcess($p, 'Add-MpPreference -ExclusionPath')) {
            Add-MpPreference -ExclusionPath $p -ErrorAction SilentlyContinue
            Write-Host "  [ok] Defender path exclusion: $p" -ForegroundColor Green
        }
    }
    if (Test-Path $exePath) {
        if ($PSCmdlet.ShouldProcess('SharpCoreDB.Benchmarks.Comparative.exe', 'Add-MpPreference -ExclusionProcess')) {
            Add-MpPreference -ExclusionProcess 'SharpCoreDB.Benchmarks.Comparative.exe' -ErrorAction SilentlyContinue
            Write-Host "  [ok] Defender process exclusion: SharpCoreDB.Benchmarks.Comparative.exe" -ForegroundColor Green
        }
    }

    if ($StopServices) {
        foreach ($name in 'WSearch', 'SysMain', 'DiagTrack') {
            $svc = Get-Service $name -ErrorAction SilentlyContinue
            if ($svc -and $svc.Status -eq 'Running' -and $PSCmdlet.ShouldProcess($name, 'Stop-Service')) {
                Stop-Service $name -Force -ErrorAction SilentlyContinue
                Write-Host "  [ok] stopped $name (restart with: Start-Service $name)" -ForegroundColor Green
            }
        }
    } else {
        Write-Host "  [--] Windows Search / SysMain / DiagTrack left running (-StopServices to stop them)" -ForegroundColor DarkGray
    }
}

Write-Host ""
Write-Host "Re-run without -Apply to confirm the verdict is QUIET." -ForegroundColor Cyan
exit 1
