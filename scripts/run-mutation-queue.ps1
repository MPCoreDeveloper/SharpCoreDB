<#
.SYNOPSIS
    Runs a list of mutation slices sequentially and is safe to re-run.

.DESCRIPTION
    Wraps scripts/run-mutation-matrix.ps1 so an unattended, multi-hour batch can be launched once and
    collected later, instead of being supervised by polling:

      pwsh scripts/run-mutation-queue.ps1 -Slice graph-advanced,engine-storage
      # ... later, from any shell:
      pwsh scripts/run-mutation-queue.ps1 -Status

    A slice that already has reports/mutation-report.json is skipped unless -Force is passed, so re-running
    the same command resumes rather than restarts. Every step is appended to
    artifacts/mutation/queue.log, and artifacts/mutation/queue.done is written when the batch finishes -
    the marker is the thing to check, not the process.

.PARAMETER Slice
    Slice names from scripts/mutation-matrix.json, in the order they should run.

.PARAMETER Force
    Re-run slices that already have a report.

.PARAMETER Status
    Do not run anything; print the per-slice state from disk and exit.

.EXAMPLE
    pwsh scripts/run-mutation-queue.ps1 -Slice graph-advanced,engine-datastructures,engine-storage
#>
[CmdletBinding()]
param(
    [string[]] $Slice = @(),

    [switch] $Force,

    [switch] $Status
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# `pwsh -File script.ps1 -Slice a,b,c` binds the whole list as ONE string, so the documented comma form
# would arrive as a single unknown name. Split it here so the shell form and the in-PowerShell array form
# behave the same.
$Slice = @($Slice | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$artifactRoot = Join-Path $repoRoot 'artifacts/mutation'
$logPath = Join-Path $artifactRoot 'queue.log'
$markerPath = Join-Path $artifactRoot 'queue.done'
$runner = Join-Path $PSScriptRoot 'run-mutation-matrix.ps1'
$manifestPath = Join-Path $PSScriptRoot 'mutation-matrix.json'

$manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
$known = @($manifest.slices | ForEach-Object { $_.name })

function Get-SliceState {
    param([string] $Name)
    $report = Join-Path $artifactRoot "$Name/reports/mutation-report.json"
    if (Test-Path $report) { return 'scored' }
    $sliceDef = $manifest.slices | Where-Object { $_.name -eq $Name }
    if ($sliceDef -and $sliceDef.PSObject.Properties.Name -contains 'enabled' -and $sliceDef.enabled -eq $false) {
        return 'disabled'
    }
    return 'pending'
}

if ($Status) {
    $names = if ($Slice.Count -gt 0) { $Slice } else { $known }
    foreach ($name in $names) {
        '{0,-26} {1}' -f $name, (Get-SliceState -Name $name)
    }
    if (Test-Path $markerPath) { "marker: $markerPath" } else { 'marker: (absent - batch not finished)' }
    return
}

if ($Slice.Count -eq 0) { throw 'Pass -Slice <names...> (or -Status to inspect).' }

$unknown = @($Slice | Where-Object { $known -notcontains $_ })
if ($unknown.Count -gt 0) { throw "Unknown slice(s): $($unknown -join ', ')" }

New-Item -ItemType Directory -Force -Path $artifactRoot | Out-Null
Remove-Item $markerPath -Force -ErrorAction SilentlyContinue

"=== queue start $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')  slices: $($Slice -join ', ') ===" |
    Out-File $logPath -Encoding utf8

$results = @()
foreach ($name in $Slice) {
    $state = Get-SliceState -Name $name
    if ($state -eq 'scored' -and -not $Force) {
        "[$name] skip - report already exists" | Out-File $logPath -Append -Encoding utf8
        $results += [pscustomobject]@{ Slice = $name; Outcome = 'skipped' }
        continue
    }

    "[$name] run  $(Get-Date -Format 'HH:mm:ss')" | Out-File $logPath -Append -Encoding utf8
    pwsh -NoProfile -File $runner -Slice $name 2>&1 | Out-File $logPath -Append -Encoding utf8

    $after = Get-SliceState -Name $name
    "[$name] done $(Get-Date -Format 'HH:mm:ss')  state=$after" | Out-File $logPath -Append -Encoding utf8
    $results += [pscustomobject]@{ Slice = $name; Outcome = $after }
}

'=== aggregate ===' | Out-File $logPath -Append -Encoding utf8
pwsh -NoProfile -File $runner -AggregateOnly -Tier all 2>&1 | Out-File $logPath -Append -Encoding utf8

$results | Format-Table -AutoSize | Out-String -Width 120 | Out-File $logPath -Append -Encoding utf8
$results | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $artifactRoot 'queue.json') -Encoding utf8
"finished $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" | Out-File $markerPath -Encoding utf8

$results | ForEach-Object { '{0,-26} {1}' -f $_.Slice, $_.Outcome }
"log:    $logPath"
"marker: $markerPath"
