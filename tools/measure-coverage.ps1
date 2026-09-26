<#
.SYNOPSIS
    Measures line coverage of the shipped SharpCoreDB assemblies and fails when it is below the floor.

.DESCRIPTION
    `dotnet test` cannot measure coverage in this repository. xUnit v3 runs on Microsoft.Testing.Platform,
    which rejects the VSTest target on .NET 10 SDK and later, so `--collect "XPlat Code Coverage"` never
    reaches the test host; Microsoft.Testing.Extensions.CodeCoverage registers its `--coverage` switch only
    inside an MTP host, and this branch's test host is xUnit's own in-process runner (its `--help` has no
    coverage option and `--coverage` answers `error: unknown option: --coverage`). The route that does work
    is the standalone `dotnet-coverage` collector wrapped around the test host CI already runs:

        dotnet-coverage collect -f cobertura -o <report> -- <test-host> -filterVSTest "<filter>"

    `dotnet-coverage` is pinned in .config/dotnet-tools.json (`dotnet tool restore` provides it; this script
    finds it through `dotnet tool run dotnet-coverage`, with PATH as the fallback), so the measurement is
    reproducible locally and in CI from one version pin.

    The script builds the given test projects, collects one Cobertura report per project, merges the
    per-assembly line counts, prints a per-assembly table plus the merged rate, and fails when the merged
    line rate is below -Floor. Only packages whose name starts with `SharpCoreDB` and does not end with
    `.Tests` are counted: that is the shipped `src/` surface, which is what codecov.yml counts too (`ignore:`
    excludes tests/**, tools/**, Examples/** and the benchmark/Demo code). The test assembly itself is
    collected as well and deliberately left out of the verdict. Two things about the counting are deliberate
    and match the uploads: a line is counted once per (assembly, source file, line number) and it counts as
    covered when any report covered it - cobertura repeats a line once per class that owns it and every test
    project's bin directory holds its own copy of every product assembly, so counting raw entries inflated
    the denominator threefold across the three suites (a line owned by two classes counts once, the same file
    in two reports counts once; both are self-tested below). Generated sources that codecov.yml ignores
    (**/*.g.cs, **/*.g.i.cs, **/*.Designer.cs, GlobalUsings.cs) are skipped here too.

    Exit codes — the job log states the number and the floor it was compared against either way:

        0  measured, at or above the floor
        1  measured, below the floor
        2  a test suite failed (coverage verdict withheld)
        3  no measurement possible (no report, no product assembly, collector unavailable)

    Slow by nature: it runs the suites a second time under instrumentation. Keep it in the dedicated
    coverage job (see .github/workflows/ci-net11.yml), never in the build matrix on every commit.

.PARAMETER Floor
    Minimum acceptable line rate as a fraction: 0.18 is 18%. Default 0.18, the floor in codecov.yml.

.PARAMETER Filter
    VSTest filter expression handed to every test host. Default the CI filter
    (Category!=Debug&Category!=Manual&Category!=Performance).

.PARAMETER Projects
    Test projects to measure, relative to the repository root. Default the three suites codecov.yml counts
    (SharpCoreDB.Tests, SharpCoreDB.VectorSearch.Tests, SharpCoreDB.EntityFrameworkCore.Tests).

.PARAMETER OutputDirectory
    Where the Cobertura reports are written, relative to the repository root. Default artifacts/coverage.

.PARAMETER Configuration
    Build configuration. Default Release.

.PARAMETER Framework
    Target framework to run. Default net11.0, the only framework this branch ships.

.PARAMETER SkipBuild
    Measure the binaries already in bin/<Configuration>/<Framework> instead of building first.

.PARAMETER SelfTest
    Exercises the merge and floor logic against synthetic reports (above, at and below the floor, coverage
    that only the test assembly would bring, and a missing report) without building anything. Run it after
    editing this script, the way tools/check-banned-simd-api.ps1 self-tests its detector.

.EXAMPLE
    pwsh -NoProfile -File tools/measure-coverage.ps1

.EXAMPLE
    pwsh -NoProfile -File tools/measure-coverage.ps1 -SelfTest
#>
[CmdletBinding()]
param(
    [double]$Floor = 0.18,
    [string]$Filter = 'Category!=Debug&Category!=Manual&Category!=Performance',
    [string[]]$Projects = @(
        'tests/SharpCoreDB.Tests/SharpCoreDB.Tests.csproj',
        'tests/SharpCoreDB.VectorSearch.Tests/SharpCoreDB.VectorSearch.Tests.csproj',
        'tests/SharpCoreDB.EntityFrameworkCore.Tests/SharpCoreDB.EntityFrameworkCore.Tests.csproj'
    ),
    [string]$OutputDirectory = 'artifacts/coverage',
    [string]$Configuration = 'Release',
    [string]$Framework = 'net11.0',
    [switch]$SkipBuild,
    [switch]$SelfTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Exit codes, exactly as documented in the header above.
$ExitOk = 0
$ExitBelowFloor = 1
$ExitSuiteFailed = 2
$ExitNoMeasurement = 3

$RepoRoot = Split-Path -Parent $PSScriptRoot
$Invariant = [System.Globalization.CultureInfo]::InvariantCulture

# True for the assemblies the verdict is about: product code, not a test assembly. Everything else the
# collector picks up (xunit, Moq, FluentMigrator, the suite itself) is noise for this number.
function Test-ProductPackage {
    param([string]$Name)
    return ($Name -like 'SharpCoreDB*') -and ($Name -notlike '*.Tests')
}

# codecov.yml's `ignore:` list, for the file patterns that decide this number: generated sources
# (**/*.g.cs, **/*.g.i.cs, **/*.Designer.cs) and GlobalUsings.cs are not counted there either.
function Test-CoveredFile {
    param([string]$Path)

    if ($Path -like '*.g.cs') { return $false }
    if ($Path -like '*.g.i.cs') { return $false }
    if ($Path -like '*.Designer.cs') { return $false }
    if ($Path -like '*GlobalUsings.cs') { return $false }
    return $true
}

# One entry per (assembly, source file, line number) with its covered flag, for one report. Counting the raw
# <line> elements instead multiplies both sides of the rate twice over: cobertura lists a line once per class
# that owns it, so a file holding several classes reports the same line several times inside a single report,
# and every test project's bin directory holds its own copy of every product assembly, so three suites made
# the core assembly look like 273270 lines instead of 91090. Deduplicating by (assembly, file, line) and
# OR-ing the hits is what codecov does with the uploaded reports, so the floor is compared with the same
# quantity the status check reports.
function Get-CoverageLineMap {
    param([string]$ReportPath)

    if (-not (Test-Path -LiteralPath $ReportPath)) { return $null }

    $document = New-Object System.Xml.XmlDocument
    $document.Load($ReportPath)

    $lines = [System.Collections.Generic.Dictionary[string, bool]]::new()
    foreach ($package in $document.SelectNodes('/coverage/packages/package')) {
        $name = $package.GetAttribute('name')
        if (-not (Test-ProductPackage -Name $name)) { continue }

        foreach ($class in $package.SelectNodes('.//class')) {
            $filename = $class.GetAttribute('filename')
            if (-not (Test-CoveredFile -Path $filename)) { continue }

            foreach ($line in $class.SelectNodes('.//line')) {
                $key = '{0}|{1}|{2}' -f $name, $filename, $line.GetAttribute('number')
                $covered = $line.GetAttribute('hits') -ne '0'
                if ($lines.ContainsKey($key)) {
                    if ($covered -and -not $lines[$key]) { $lines[$key] = $true }
                }
                else {
                    $lines[$key] = $covered
                }
            }
        }
    }
    return $lines
}


# One report per suite covers different lines of the same assemblies, so the maps are unioned: a line is
# covered when any suite covered it, and it is counted once however many reports mention it.
function Merge-CoverageLineMaps {
    param([System.Collections.IDictionary[]]$Maps)

    $merged = [System.Collections.Generic.Dictionary[string, bool]]::new()
    foreach ($map in $Maps) {
        if ($null -eq $map) { continue }
        foreach ($key in $map.Keys) {
            if ($merged.ContainsKey($key)) {
                if ($map[$key] -and -not $merged[$key]) { $merged[$key] = $true }
            }
            else {
                $merged[$key] = $map[$key]
            }
        }
    }
    return $merged
}

# Per-assembly and total line counts out of a merged map. The assembly is the part of the key before the
# first '|', which is why the key starts with the package name.
function Get-CoverageSummary {
    param([System.Collections.IDictionary]$Lines)

    $assemblies = @{}
    $total = @{ Valid = 0; Covered = 0 }
    foreach ($key in $Lines.Keys) {
        $name = $key.Substring(0, $key.IndexOf('|'))
        if (-not $assemblies.ContainsKey($name)) { $assemblies[$name] = @{ Valid = 0; Covered = 0 } }
        $assemblies[$name]['Valid']++
        $total['Valid']++
        if ($Lines[$key]) {
            $assemblies[$name]['Covered']++
            $total['Covered']++
        }
    }
    return @{ Assemblies = $assemblies; Total = $total }
}

# The verdict for one summary. An empty or line-less set is $ExitNoMeasurement, never a pass: a report that
# carries no product assembly did not come from a SharpCoreDB suite, and a silently green job would be worse
# than a red one.
function Test-CoverageFloor {
    param([hashtable]$Summary, [double]$Minimum)

    if ($Summary.Total.Valid -le 0) { return $ExitNoMeasurement }
    if (($Summary.Total.Covered / [double]$Summary.Total.Valid) -lt $Minimum) { return $ExitBelowFloor }
    return $ExitOk
}

function Format-Percent {
    param([double]$Fraction)
    return ([string]::Format($Invariant, '{0:0.00}%', 100.0 * $Fraction))
}

function Format-Rate {
    param([int]$Covered, [int]$Valid)
    if ($Valid -le 0) { return 'n/a' }
    return (Format-Percent -Fraction ($Covered / [double]$Valid))
}

# The pinned collector from .config/dotnet-tools.json, run as `dotnet <tool>.dll`. `dotnet tool run` is not
# used on purpose: it re-binds the tool's options, so `collect -f cobertura -o <path> -- <host> ...` arrives
# at dotnet-coverage as a duplicated `-f`, which answers "Option '-f' expects a single argument but 2 were
# provided" and never runs the suite. A PATH install (dotnet tool install --global) is the fallback.
function Resolve-CoverageCommand {
    $manifestPath = Join-Path $RepoRoot '.config/dotnet-tools.json'
    if (Test-Path -LiteralPath $manifestPath) {
        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
        $version = $manifest.tools.'dotnet-coverage'.version
        $packageRoot = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $HOME '.nuget/packages' }
        $toolRoot = Join-Path $packageRoot ('dotnet-coverage/{0}' -f $version)
        $toolDll = Get-ChildItem -LiteralPath $toolRoot -Recurse -Filter 'dotnet-coverage.dll' -File -ErrorAction SilentlyContinue |
            Select-Object -First 1
        if ($null -ne $toolDll) {
            # The package ships the collector built for an older runtime than this branch targets; roll it
            # forward so the machine needs one installed runtime, not the exact one the tool was built for.
            $env:DOTNET_ROLL_FORWARD = 'Major'
            return , @('dotnet', $toolDll.FullName)
        }
    }

    $onPath = Get-Command dotnet-coverage -ErrorAction SilentlyContinue
    if ($null -ne $onPath) { return , @($onPath.Source) }

    Write-Host 'ERROR no coverage collector available.'
    Write-Host '      Fix: dotnet tool restore   (pins dotnet-coverage in .config/dotnet-tools.json)'
    exit $ExitNoMeasurement
}


# Name test for the test host. The bin directory also holds argument files the coverage tooling writes for
# the target - `CoverletSourceRootsMapping_<suite>.Tests` (coverlet.collector) and, after a collection,
# `.msCoverageSourceRootsMapping_<suite>.Tests` / `.msCoverageExtensionSourceRootsMapping_<suite>.Tests`.
# Their names end in `.Tests` too and they sort *before* the real host, so a resolver that only matches the
# suffix hands a text file to `dotnet-coverage`, which answers "The specified executable is not a valid
# application for this OS platform" and never runs the suite. The host is matched explicitly instead.
function Test-TestHostName {
    param([string]$Name)

    if ($Name.StartsWith('.')) { return $false }
    if ($Name -like '*SourceRootsMapping*') { return $false }
    return $Name -match '\.Tests(\.exe)?$'
}

# The test host CI runs: the xUnit v3 in-process runner, `<suite>.exe` on Windows and `<suite>` elsewhere.
# The exact name is probed first; the scan is the fallback (a renamed assembly output). Returning $null
# records a suite failure for the caller.
function Resolve-TestHost {
    param([string]$BinDirectory, [string]$Suite)

    if (-not (Test-Path -LiteralPath $BinDirectory)) {
        Write-Host ("FAIL  {0}: no build output in {1}" -f $Suite, $BinDirectory)
        return $null
    }

    $exactName = if ($IsWindows) { '{0}.exe' -f $Suite } else { $Suite }
    $exactPath = Join-Path $BinDirectory $exactName
    if (Test-Path -LiteralPath $exactPath -PathType Leaf) { return (Get-Item -LiteralPath $exactPath) }

    $testHost = Get-ChildItem -LiteralPath $BinDirectory -File -ErrorAction SilentlyContinue |
        Where-Object { Test-TestHostName -Name $_.Name } |
        Select-Object -First 1
    if ($null -eq $testHost) {
        Write-Host ("FAIL  {0}: no test host (*.Tests / *.Tests.exe) in {1}" -f $Suite, $BinDirectory)
    }
    return $testHost
}


# Synthetic Cobertura reports for the self-test. Only the shape the aggregation reads is built: one package
# per entry, with exactly <Valid> <line> elements of which <Covered> carry hits > 0.
function New-SyntheticReport {
    param([string]$Path, [hashtable[]]$Packages)

    $packagesXml = New-Object System.Text.StringBuilder
    $packageIndex = 0
    foreach ($package in $Packages) {
        $packageIndex++
        # Which classes own a file is what makes the deduplication testable: cobertura writes a second class
        # with the same filename for a file holding several classes, and its hit pattern differs. A package
        # with `SecondClassCovered` produces exactly that.
        $classSpecs = @(@{ Cover = $package.Covered; Suffix = '' })
        if ($package.ContainsKey('SecondClassCovered')) {
            $classSpecs += @{ Cover = $package.SecondClassCovered; Suffix = 'B' }
        }

        [void]$packagesXml.AppendLine(('    <package name="{0}" line-rate="0" branch-rate="0" complexity="1" version="1.0.0">' -f $package.Name))
        [void]$packagesXml.AppendLine('      <classes>')
        foreach ($classSpec in $classSpecs) {
            [void]$packagesXml.AppendLine(('        <class name="{0}.Synthetic{1}{2}" filename="src/{0}/Synthetic{1}.cs" line-rate="0" branch-rate="0" complexity="1">' -f $package.Name, $packageIndex, $classSpec.Suffix))
            [void]$packagesXml.AppendLine('          <methods />')
            [void]$packagesXml.AppendLine('          <lines>')
            for ($lineNumber = 1; $lineNumber -le $package.Valid; $lineNumber++) {
                $hits = if ($lineNumber -le $classSpec.Cover) { 1 } else { 0 }
                [void]$packagesXml.AppendLine(('            <line number="{0}" hits="{1}" branch="false" />' -f $lineNumber, $hits))
            }
            [void]$packagesXml.AppendLine('          </lines>')
            [void]$packagesXml.AppendLine('        </class>')
        }
        [void]$packagesXml.AppendLine('      </classes>')
        [void]$packagesXml.AppendLine('    </package>')
    }


    $document = @"
<?xml version="1.0" encoding="utf-8"?>
<coverage line-rate="0" branch-rate="0" version="1.9" timestamp="0">
  <packages>
$($packagesXml.ToString())  </packages>
</coverage>
"@
    Set-Content -LiteralPath $Path -Value $document -Encoding utf8
}

# The self-test: every case runs the real aggregation and the real comparison against synthetic reports, so
# a change to either is caught here without building anything. Returns the number of failed cases.
function Invoke-SelfTest {
    $tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('sharpcoredb-coverage-selftest-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null

    $cases = @(
        @{
            Name = 'above the floor'
            Reports = @( @(@{ Name = 'SharpCoreDB'; Valid = 100; Covered = 25 }), @(@{ Name = 'SharpCoreDB.Tests'; Valid = 1000; Covered = 1000 }) )
            Expect = $ExitOk
        },
        @{
            Name = 'exactly at the floor'
            Reports = @( @(@{ Name = 'SharpCoreDB'; Valid = 100; Covered = 18 }) )
            Expect = $ExitOk
        },
        @{
            Name = 'one line below the floor'
            Reports = @( @(@{ Name = 'SharpCoreDB'; Valid = 100; Covered = 17 }) )
            Expect = $ExitBelowFloor
        },
        @{
            Name = 'test-assembly coverage does not lift the verdict'
            Reports = @( @(@{ Name = 'SharpCoreDB'; Valid = 100; Covered = 10 }), @(@{ Name = 'SharpCoreDB.Tests'; Valid = 10000; Covered = 10000 }), @(@{ Name = 'SharpCoreDB.Functional.Tests'; Valid = 500; Covered = 500 }) )
            Expect = $ExitBelowFloor
        },
        @{
            Name = 'two reports merge before the comparison'
            Reports = @( @(@{ Name = 'SharpCoreDB'; Valid = 100; Covered = 10 }), @(@{ Name = 'SharpCoreDB'; Valid = 100; Covered = 30 }) )
            Expect = $ExitOk
        },
        @{
            Name = 'a line owned by two classes counts once'
            Reports = @( @(@{ Name = 'SharpCoreDB'; Valid = 100; Covered = 0; SecondClassCovered = 20 }) )
            Expect = $ExitOk
        },
        @{
            Name = 'the same file in two reports counts once'
            Reports = @( @(@{ Name = 'SharpCoreDB'; Valid = 100; Covered = 0 }), @(@{ Name = 'SharpCoreDB'; Valid = 100; Covered = 20 }) )
            Expect = $ExitOk
        },
        @{
            Name = 'missing report is not a pass'
            Reports = @( $null )
            Expect = $ExitNoMeasurement
        }
    )

    $passed = 0
    $failed = 0
    $caseIndex = 0
    foreach ($case in $cases) {
        $caseIndex++
        $reportMaps = @()
        $reportIndex = 0
        foreach ($report in $case.Reports) {
            $reportIndex++
            if ($null -eq $report) {
                $reportMaps += , (Get-CoverageLineMap -ReportPath (Join-Path $tempRoot ('missing-{0}-{1}.xml' -f $caseIndex, $reportIndex)))
                continue
            }
            $path = Join-Path $tempRoot ('report-{0}-{1}.xml' -f $caseIndex, $reportIndex)
            New-SyntheticReport -Path $path -Packages $report
            $reportMaps += , (Get-CoverageLineMap -ReportPath $path)
        }

        $merged = Merge-CoverageLineMaps -Maps $reportMaps
        $actual = Test-CoverageFloor -Summary (Get-CoverageSummary -Lines $merged) -Minimum 0.18
        if ($actual -eq $case.Expect) {
            $passed++
            Write-Host ('SELFTEST ok    {0} -> exit {1}' -f $case.Name, $actual)
        }
        else {
            $failed++
            Write-Host ('SELFTEST FAIL  {0} -> expected exit {1}, got {2}' -f $case.Name, $case.Expect, $actual)
        }
    }

    # The test-host resolver has its own cases: the argument files the coverage tooling leaves in the bin
    # directory end in `.Tests` as well, and one of them sorting first used to be enough to break the
    # measurement (the resolver handed `.msCoverageSourceRootsMapping_*.Tests` to dotnet-coverage).
    $hostCases = @(
        @{ Name = 'host found next to the argument files'; DecoysOnly = $false }
        @{ Name = 'argument files only, no host'; DecoysOnly = $true }
    )
    $hostIndex = 0
    foreach ($hostCase in $hostCases) {
        $hostIndex++
        $hostDirectory = Join-Path $tempRoot ('bin-{0}' -f $hostIndex)
        New-Item -ItemType Directory -Path $hostDirectory -Force | Out-Null
        foreach ($decoy in @(
                '.msCoverageExtensionSourceRootsMapping_MySuite.Tests'
                '.msCoverageSourceRootsMapping_MySuite.Tests'
                'CoverletSourceRootsMapping_MySuite.Tests'
            )) {
            Set-Content -LiteralPath (Join-Path $hostDirectory $decoy) -Value 'mapping' -Encoding utf8
        }

        $expectedLabel = 'no host'
        if (-not $hostCase.DecoysOnly) {
            Set-Content -LiteralPath (Join-Path $hostDirectory 'MySuite.Tests.exe') -Value 'host' -Encoding utf8
            Set-Content -LiteralPath (Join-Path $hostDirectory 'MySuite.Tests') -Value 'host' -Encoding utf8
            $expectedLabel = 'MySuite.Tests'
        }

        $resolved = Resolve-TestHost -BinDirectory $hostDirectory -Suite 'MySuite.Tests' 6>$null
        $actualLabel = 'no host'
        if ($null -ne $resolved) { $actualLabel = $resolved.Name }
        $ok = if ($expectedLabel -eq 'no host') {
            $actualLabel -eq 'no host'
        }
        else {
            $actualLabel -match ('^{0}(\.exe)?$' -f [regex]::Escape($expectedLabel))
        }

        if ($ok) {
            $passed++
            Write-Host ('SELFTEST ok    {0} -> {1}' -f $hostCase.Name, $actualLabel)
        }
        else {
            $failed++
            Write-Host ('SELFTEST FAIL  {0} -> expected {1}, resolved {2}' -f $hostCase.Name, $expectedLabel, $actualLabel)
        }
    }

    Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue

    $verdict = if ($failed -eq 0) { 'PASS' } else { 'FAIL' }
    Write-Host ('SELFTEST {0} — {1} passed, {2} failed ({3} case(s))' -f $verdict, $passed, $failed, ($cases.Count + $hostCases.Count))
    return $failed
}

# --- main -----------------------------------------------------------------------------------------------

if ($SelfTest) {
    if ((Invoke-SelfTest) -gt 0) { exit 1 }
    exit $ExitOk
}

Push-Location $RepoRoot
try {
    $reportDirectory = Join-Path $RepoRoot $OutputDirectory
    New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null

    Write-Host ('Coverage floor : {0}' -f (Format-Percent -Fraction $Floor))
    Write-Host ('VSTest filter  : {0}' -f $Filter)
    Write-Host ('Reports        : {0}' -f $reportDirectory)
    Write-Host ('Projects       : {0}' -f ($Projects -join ', '))

    $coverageCommand = Resolve-CoverageCommand
    $coverageExe = $coverageCommand[0]

    $reportPaths = @()
    $failedSuites = @()
    $collectorFailures = @()

    foreach ($project in $Projects) {
        $projectPath = Join-Path $RepoRoot $project
        $suite = [System.IO.Path]::GetFileNameWithoutExtension($projectPath)
        Write-Host ''
        if (-not (Test-Path -LiteralPath $projectPath)) {
            Write-Host ('SKIP  {0}: {1} is not in this tree' -f $suite, $project)
            continue
        }

        if (-not $SkipBuild) {
            Write-Host ('BUILD {0}' -f $suite)
            & dotnet restore $projectPath --configfile NuGet.Config
            if ($LASTEXITCODE -ne 0) {
                Write-Host ('FAIL  {0}: restore exited {1}' -f $suite, $LASTEXITCODE)
                exit $ExitSuiteFailed
            }
            & dotnet build $projectPath --configuration $Configuration --framework $Framework --no-restore
            if ($LASTEXITCODE -ne 0) {
                Write-Host ('FAIL  {0}: build exited {1}' -f $suite, $LASTEXITCODE)
                exit $ExitSuiteFailed
            }
        }

        $binDirectory = Join-Path (Split-Path -Parent $projectPath) ('bin/{0}/{1}' -f $Configuration, $Framework)
        $testHost = Resolve-TestHost -BinDirectory $binDirectory -Suite $suite
        if ($null -eq $testHost) { $failedSuites += $suite; continue }

        $reportPath = Join-Path $reportDirectory ('{0}.cobertura.xml' -f $suite)
        Write-Host ('COLLECT {0} -> {1}' -f $suite, (Split-Path -Leaf $reportPath))

        $toolArguments = @()
        if ($coverageCommand.Count -gt 1) { $toolArguments += $coverageCommand[1..($coverageCommand.Count - 1)] }
        $toolArguments += @('collect', '-f', 'cobertura', '-o', $reportPath, '--', $testHost.FullName, '-filterVSTest', $Filter)
        & $coverageExe @toolArguments
        $suiteExitCode = $LASTEXITCODE
        $reportWritten = Test-Path -LiteralPath $reportPath

        if (-not $reportWritten) {
            # No report means the collector itself never got as far as running the suite (bad arguments, no
            # runtime for the tool). That is a broken measurement, not a red test run, and it must not be
            # allowed to read as a coverage number.
            Write-Host ('ERROR {0}: no Cobertura report written - the collector exited {1} without running the suite' -f $suite, $suiteExitCode)
            $collectorFailures += $suite
            continue
        }
        if ($suiteExitCode -ne 0) {
            # A red suite is reported as red and the coverage verdict is withheld for the whole run: a suite
            # that died early under-reports coverage in a way that reads like a real number.
            Write-Host ('FAIL  {0}: test host exited {1}' -f $suite, $suiteExitCode)
            $failedSuites += $suite
        }
        $reportPaths += $reportPath

    }

    $reportMaps = @()
    foreach ($path in $reportPaths) { $reportMaps += , (Get-CoverageLineMap -ReportPath $path) }
    $summary = Get-CoverageSummary -Lines (Merge-CoverageLineMaps -Maps $reportMaps)

    Write-Host ''
    Write-Host ('Coverage per shipped assembly (merged over {0} report(s), a line counts once)' -f $reportPaths.Count)
    Write-Host ('  {0,-42} {1,10} {2,10} {3,9}' -f 'Assembly', 'Lines', 'Covered', 'Rate')

    foreach ($name in ($summary.Assemblies.Keys | Sort-Object)) {
        $entry = $summary.Assemblies[$name]
        Write-Host ('  {0,-42} {1,10} {2,10} {3,9}' -f $name, $entry.Valid, $entry.Covered, (Format-Rate -Covered $entry.Covered -Valid $entry.Valid))
    }
    Write-Host ('  {0,-42} {1,10} {2,10} {3,9}' -f 'TOTAL', $summary.Total.Valid, $summary.Total.Covered, (Format-Rate -Covered $summary.Total.Covered -Valid $summary.Total.Valid))
    Write-Host ''

    if ($collectorFailures.Count -gt 0) {
        Write-Host ('MEASUREMENT FAILED: {0} - no report, so the numbers above are incomplete' -f ($collectorFailures -join ', '))
        exit $ExitNoMeasurement
    }
    if ($failedSuites.Count -gt 0) {
        Write-Host ('FAILED SUITES: {0} - coverage verdict withheld' -f ($failedSuites -join ', '))
        exit $ExitSuiteFailed
    }

    $measured = Format-Rate -Covered $summary.Total.Covered -Valid $summary.Total.Valid
    $verdict = Test-CoverageFloor -Summary $summary -Minimum $Floor
    switch ($verdict) {
        $ExitOk {
            Write-Host ('PASS  measured {0}, floor {1}' -f $measured, (Format-Percent -Fraction $Floor))
            exit $ExitOk
        }
        $ExitBelowFloor {
            Write-Host ('FAIL  measured {0}, floor {1} - the merged rate is below the floor' -f $measured, (Format-Percent -Fraction $Floor))
            exit $ExitBelowFloor
        }
        default {
            Write-Host 'ERROR no product assembly in the reports - nothing to compare against the floor'
            exit $ExitNoMeasurement
        }
    }
}
finally {
    Pop-Location
}
