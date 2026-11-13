<#
.SYNOPSIS
    Runs the mutation-testing slice matrix defined in scripts/mutation-matrix.json.

.DESCRIPTION
    Slice = (project under test, test host, mutate scope). Each slice runs Stryker.NET with a
    temporary config generated from the manifest, writes its reports under
    artifacts/mutation/<slice>/, and the results are aggregated into
    artifacts/mutation/summary.{json,md}.

    The score is informational by default: the script fails only when a slice could not run
    (non-zero Stryker exit, missing report, timeout). Pass -BreakAt <n> to also fail when a
    slice's mutation score is below n.

.PARAMETER Tier
    pr (fast modules), nightly (engine slices), or all.

.PARAMETER Slice
    Run only these slice names (overrides -Tier).

.PARAMETER BreakAt
    Fail when a slice scores below this value. Default: 0 (never).

.PARAMETER AggregateOnly
    Skip execution and only aggregate the reports already under artifacts/mutation/.

.PARAMETER List
    Print the slices and exit.
#>
[CmdletBinding()]
param(
    [ValidateSet('pr', 'nightly', 'all')]
    [string] $Tier = 'pr',

    [string[]] $Slice = @(),

    [int] $BreakAt = -1,

    [switch] $AggregateOnly,

    [switch] $List
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$manifestPath = Join-Path $PSScriptRoot 'mutation-matrix.json'
$artifactRoot = Join-Path $repoRoot 'artifacts/mutation'

if (-not (Test-Path $manifestPath)) { throw "Manifest not found: $manifestPath" }
$manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
$defaults = $manifest.defaults
$allSlices = @($manifest.slices)

# NOTE: the loop variable below must not be named $slice -- PowerShell variable names are
# case-insensitive, so it would alias the [string[]] $Slice parameter and coerce every
# manifest entry to a string.
if ($Slice.Count -gt 0) {
    $selected = @($allSlices | Where-Object { $Slice -contains $_.name })
}
elseif ($Tier -eq 'all') {
    $selected = @($allSlices)
}
else {
    $selected = @($allSlices | Where-Object { $_.tier -eq $Tier })
}

if ($List) {
    $selected | ForEach-Object {
        $flag = if ($_.PSObject.Properties.Name -contains 'enabled' -and $_.enabled -eq $false) { 'disabled' } else { 'enabled' }
        '{0,-24} {1,-8} {2,-9} {3}' -f $_.name, $_.tier, $flag, $_.project
    }
    return
}
if ($selected.Count -eq 0) { throw "No slice matched (Tier=$Tier, Slice=$($Slice -join ','))." }

# A slice with "enabled": false is declared for the roadmap but has not been validated end to end
# (for example: an engine folder with no test filter yet, which would hit the coverage-relay
# timeout storm). It is skipped, and named in the summary so it cannot be mistaken for a pass.
$skipped = @($selected | Where-Object { $_.PSObject.Properties.Name -contains 'enabled' -and $_.enabled -eq $false })
$selected = @($selected | Where-Object { -not ($_.PSObject.Properties.Name -contains 'enabled' -and $_.enabled -eq $false) })
if ($selected.Count -eq 0 -and $skipped.Count -gt 0) {
    Write-Host ("All selected slices are disabled: {0}" -f (($skipped | ForEach-Object { $_.name }) -join ', ')) -ForegroundColor Yellow
    return
}

function Get-SliceSetting {
    param($SliceDef, [string] $Name, $Fallback)
    if ($SliceDef.PSObject.Properties.Name -contains $Name) { return $SliceDef.$Name }
    if ($defaults.PSObject.Properties.Name -contains $Name) { return $defaults.$Name }
    return $Fallback
}

function ConvertTo-MatchPattern {
    param([string] $Glob)
    $g = $Glob.Replace('\', '/').Replace('/**/', '/')
    if ($g.StartsWith('**/')) { $g = $g.Substring(3) }
    return $g.Replace('**', '*')
}

function Test-InScope {
    param([string] $Path, [string[]] $Patterns)
    if (-not $Patterns -or $Patterns.Count -eq 0) { return $true }
    $p = $Path.Replace('\', '/')
    foreach ($glob in $Patterns) {
        if ($glob.StartsWith('!')) { continue }
        if ($p -like ('*' + (ConvertTo-MatchPattern $glob))) { return $true }
    }
    return $false
}

function Get-SliceCounts {
    param([string] $ReportPath, [string[]] $MutatePatterns = @())
    $counts = [ordered]@{
        killed = 0; survived = 0; timeout = 0; runtimeError = 0
        noCoverage = 0; compileError = 0; ignored = 0; candidates = 0; score = $null
    }
    if (-not (Test-Path $ReportPath)) { return $counts }

    $report = Get-Content $ReportPath -Raw | ConvertFrom-Json
    foreach ($file in $report.files.PSObject.Properties) {
        # Stryker's report contains every mutant in the assembly; only the slice's own files are
        # its result, otherwise a one-file slice reports the whole project's compile errors.
        if (-not (Test-InScope -Path $file.Name -Patterns $MutatePatterns)) { continue }
        foreach ($m in $file.Value.mutants) {
            switch ($m.status) {
                'Killed'       { $counts.killed++ }
                'Survived'     { $counts.survived++ }
                'Timeout'      { $counts.timeout++ }
                'RuntimeError' { $counts.runtimeError++ }
                'NoCoverage'   { $counts.noCoverage++ }
                'CompileError' { $counts.compileError++ }
                default        { $counts.ignored++ }
            }
        }
    }
    $counts.candidates = $counts.killed + $counts.survived + $counts.timeout + $counts.runtimeError + $counts.noCoverage
    if ($counts.candidates -gt 0) {
        $counts.score = [math]::Round(100.0 * ($counts.killed + $counts.timeout + $counts.runtimeError) / $counts.candidates, 2)
    }
    return $counts
}

function Invoke-Slice {
    param($SliceDef)

    $sliceDir = Join-Path $artifactRoot $SliceDef.name
    $configDir = Join-Path $artifactRoot 'configs'
    New-Item -ItemType Directory -Force -Path $sliceDir, $configDir | Out-Null

    $runFrom = Join-Path $repoRoot $SliceDef.'run-from'
    if (-not (Test-Path $runFrom)) { throw "run-from path missing for '$($SliceDef.name)': $runFrom" }

    $cfg = [ordered]@{
        'project'                       = $SliceDef.project
        'test-projects'                 = @($SliceDef.'test-projects')
        'test-runner'                   = Get-SliceSetting -SliceDef $SliceDef -Name 'test-runner' -Fallback 'mtp'
        'configuration'                 = Get-SliceSetting -SliceDef $SliceDef -Name 'configuration' -Fallback 'Release'
        'target-framework'              = Get-SliceSetting -SliceDef $SliceDef -Name 'target-framework' -Fallback 'net11.0'
        'mutation-level'                = Get-SliceSetting -SliceDef $SliceDef -Name 'mutation-level' -Fallback 'Standard'
        'coverage-analysis'             = Get-SliceSetting -SliceDef $SliceDef -Name 'coverage-analysis' -Fallback 'perTest'
        'concurrency'                   = Get-SliceSetting -SliceDef $SliceDef -Name 'concurrency' -Fallback 2
        'break-on-initial-test-failure' = Get-SliceSetting -SliceDef $SliceDef -Name 'break-on-initial-test-failure' -Fallback $true
        'reporters'                     = @(Get-SliceSetting -SliceDef $SliceDef -Name 'reporters' -Fallback @('json', 'cleartext'))
        'thresholds'                    = Get-SliceSetting -SliceDef $SliceDef -Name 'thresholds' -Fallback ([ordered]@{ high = 80; low = 60; break = 0 })
        'mutate'                        = @($SliceDef.mutate)
    }
    if ($SliceDef.PSObject.Properties.Name -contains 'test-case-filter') {
        $cfg.'test-case-filter' = $SliceDef.'test-case-filter'
    }

    $configPath = Join-Path $configDir ("{0}.json" -f $SliceDef.name)
    [ordered]@{ 'stryker-config' = $cfg } | ConvertTo-Json -Depth 8 | Set-Content -Path $configPath -Encoding utf8

    $configRel = [IO.Path]::GetRelativePath($runFrom, $configPath)
    $outputRel = [IO.Path]::GetRelativePath($runFrom, $sliceDir)
    $logPath = Join-Path $sliceDir 'run.log'
    $errPath = Join-Path $sliceDir 'run.err.log'

    Write-Host ("[{0}] running (timeout {1} min) ..." -f $SliceDef.name, $SliceDef.'timeout-minutes') -ForegroundColor Cyan
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $proc = Start-Process -FilePath 'dotnet' `
        -ArgumentList 'stryker', '--config-file', $configRel, '--output', $outputRel, '--verbosity', 'info', '--skip-version-check' `
        -WorkingDirectory $runFrom -NoNewWindow -PassThru `
        -RedirectStandardOutput $logPath -RedirectStandardError $errPath

    $timedOut = -not $proc.WaitForExit([int]$SliceDef.'timeout-minutes' * 60 * 1000)
    if ($timedOut) {
        try { $proc.Kill($true) } catch { }
        Write-Host ("[{0}] TIMEOUT after {1} min" -f $SliceDef.name, $SliceDef.'timeout-minutes') -ForegroundColor Red
    }
    $sw.Stop()

    $reportPath = Join-Path $sliceDir 'reports/mutation-report.json'
    $counts = Get-SliceCounts -ReportPath $reportPath -MutatePatterns @($SliceDef.mutate)

    $status = 'ok'
    if ($timedOut) { $status = 'timeout' }
    elseif ($proc.HasExited -and $proc.ExitCode -ne 0) { $status = 'error' }
    elseif (-not (Test-Path $reportPath)) { $status = 'no-report' }

    $result = [ordered]@{
        name         = $SliceDef.name
        tier         = $SliceDef.tier
        project      = $SliceDef.project
        status       = $status
        score        = $counts.score
        killed       = $counts.killed
        survived     = $counts.survived
        timeout      = $counts.timeout
        runtimeError = $counts.runtimeError
        noCoverage   = $counts.noCoverage
        compileError = $counts.compileError
        ignored      = $counts.ignored
        candidates   = $counts.candidates
        duration_s   = [math]::Round($sw.Elapsed.TotalSeconds, 1)
        report       = if (Test-Path $reportPath) { [IO.Path]::GetRelativePath($repoRoot, $reportPath) } else { $null }
        log          = [IO.Path]::GetRelativePath($repoRoot, $logPath)
    }

    $color = if ($status -ne 'ok') { 'Red' } elseif ($null -ne $counts.score -and $counts.score -lt 60) { 'Yellow' } else { 'Green' }
    $scoreText = if ($null -eq $counts.score) { 'n/a' } else { [string]::Format([Globalization.CultureInfo]::InvariantCulture, '{0:N2}', $counts.score) }
    Write-Host ("[{0}] {1} score={2} killed={3} survived={4} noCoverage={5} compileError={6} {7}s" -f `
        $SliceDef.name, $status, $scoreText, $counts.killed, $counts.survived, $counts.noCoverage, $counts.compileError, $result.duration_s) -ForegroundColor $color

    return [pscustomobject]$result
}


$results = @()
if (-not $AggregateOnly) {
    foreach ($def in $selected) {
        try {
            $results += Invoke-Slice -SliceDef $def
        }
        catch {
            Write-Host ("[{0}] FAILED: {1}" -f $def.name, $_.Exception.Message) -ForegroundColor Red
            $results += [pscustomobject][ordered]@{
                name = $def.name; tier = $def.tier; project = $def.project; status = 'error'
                score = $null; killed = 0; survived = 0; timeout = 0; runtimeError = 0; noCoverage = 0
                compileError = 0; ignored = 0; candidates = 0; duration_s = 0; report = $null; log = $null
            }
        }
    }
}
else {
    foreach ($def in $selected) {
        $reportPath = Join-Path (Join-Path $artifactRoot $def.name) 'reports/mutation-report.json'
        if (-not (Test-Path $reportPath)) { continue }
        $counts = Get-SliceCounts -ReportPath $reportPath -MutatePatterns @($def.mutate)
        $results += [pscustomobject][ordered]@{
            name = $def.name; tier = $def.tier; project = $def.project; status = 'ok'
            score = $counts.score; killed = $counts.killed; survived = $counts.survived
            timeout = $counts.timeout; runtimeError = $counts.runtimeError
            noCoverage = $counts.noCoverage; compileError = $counts.compileError
            ignored = $counts.ignored; candidates = $counts.candidates; duration_s = 0
            report = [IO.Path]::GetRelativePath($repoRoot, $reportPath); log = $null
        }
    }
}

if ($results.Count -eq 0) { throw 'No results to aggregate (nothing under artifacts/mutation).' }

# ---- aggregate ----
New-Item -ItemType Directory -Force -Path $artifactRoot | Out-Null
$summaryJson = Join-Path $artifactRoot 'summary.json'
$summaryMd = Join-Path $artifactRoot 'summary.md'
$results | ConvertTo-Json -Depth 5 | Set-Content -Path $summaryJson -Encoding utf8

$lines = @()
$lines += '# Mutation-testing slice matrix'
$lines += ''
$lines += ('Run: {0} UTC' -f (Get-Date).ToUniversalTime().ToString('yyyy-MM-dd HH:mm'))
$lines += ''
$lines += '| Slice | Tier | Status | Score | Killed | Survived | NoCoverage | CompileError | Wall |'
$lines += '|---|---|---|---|---|---|---|---|---|'
$inv = [Globalization.CultureInfo]::InvariantCulture
foreach ($r in $results) {
    $score = if ($null -eq $r.score) { 'n/a' } else { [string]::Format($inv, '{0:N2} %', $r.score) }
    $lines += ('| {0} | {1} | {2} | {3} | {4} | {5} | {6} | {7} | {8}s |' -f $r.name, $r.tier, $r.status, $score, $r.killed, $r.survived, $r.noCoverage, $r.compileError, $r.duration_s)
}

$scored = @($results | Where-Object { $null -ne $_.score })
if ($skipped.Count -gt 0) {
    $lines += ''
    $lines += '## Declared but disabled'
    $lines += ''
    $lines += 'These slices are in the manifest but not validated end to end, so they are skipped rather than'
    $lines += 'silently scored. Enable one only after a single run has completed on the target machine.'
    $lines += ''
    foreach ($s in $skipped) {
        $reason = if ($s.PSObject.Properties.Name -contains 'reason') { $s.reason } else { 'not validated' }
        $lines += ('- `{0}` — {1}' -f $s.name, $reason)
    }
}
if ($scored.Count -gt 0) {
    $totalKilled = ($scored | Measure-Object -Property killed -Sum).Sum
    $totalCandidates = ($scored | Measure-Object -Property candidates -Sum).Sum
    if ($totalCandidates -gt 0) {
        $lines += ''
        $aggregate = [string]::Format([Globalization.CultureInfo]::InvariantCulture, '{0:N2}', (100.0 * $totalKilled / $totalCandidates))
        $lines += ('**Aggregate (scored slices): {0} % - {1} killed of {2} mutants in scope.**' -f $aggregate, $totalKilled, $totalCandidates)
    }
}
$lines += ''
$lines += 'Score = (killed + timeout + runtimeError) / (killed + survived + timeout + runtimeError + noCoverage).'
$lines += 'CompileError and ignored mutants are outside the score. NoCoverage counts as *not detected* on purpose:'
$lines += 'a line that no test reaches must not improve the score.'
$lines += 'A `timeout` row means the slice did not finish inside its budget: its score is **unknown**, not zero,'
$lines += 'and it is excluded from the aggregate.'
$lines | Set-Content -Path $summaryMd -Encoding utf8

Get-Content $summaryMd | ForEach-Object { Write-Host $_ }
Write-Host ''
Write-Host ("Summary: {0}" -f $summaryMd)

if ($env:GITHUB_STEP_SUMMARY) {
    Get-Content $summaryMd | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Append -Encoding utf8
}

# ---- exit code ----
$broken = @($results | Where-Object { $_.status -ne 'ok' -and $_.status -ne 'timeout' })
if ($broken.Count -gt 0) {
    Write-Host ("Slices that did not run: {0}" -f (($broken | ForEach-Object { $_.name }) -join ', ')) -ForegroundColor Red
    exit 1
}
$timedOut = @($results | Where-Object { $_.status -eq 'timeout' })
if ($timedOut.Count -gt 0) {
    Write-Host ("Slices that hit their budget (score unknown, not zero): {0} - raise timeout-minutes or narrow mutate." -f `
        (($timedOut | ForEach-Object { $_.name }) -join ', ')) -ForegroundColor Yellow
}
$threshold = if ($BreakAt -ge 0) { $BreakAt } else { 0 }
if ($threshold -gt 0) {
    $low = @($results | Where-Object { $null -ne $_.score -and $_.score -lt $threshold })
    if ($low.Count -gt 0) {
        Write-Host ("Below break-at {0}: {1}" -f $threshold, (($low | ForEach-Object { "$($_.name)=$($_.score)%" }) -join ', ')) -ForegroundColor Red
        exit 1
    }
}
exit 0

