<#
.SYNOPSIS
    Runs SharpCoreDB compatibility smoke tests locally.

.DESCRIPTION
    Builds the server, generates a dev certificate, starts the server with
    the smoke test configuration, executes the Python smoke tests, and
    tears down all resources.

.PARAMETER SkipBuild
    Skip the dotnet build step (useful when already built).

.PARAMETER ServerProject
    Path to the server project (relative to repo root).

.PARAMETER HttpsPort
    HTTPS API port to use (default: 8443).

.PARAMETER PgPort
    PostgreSQL binary protocol port to use (default: 5433).

.PARAMETER Username
    Test admin username (default: smokeadmin).

.PARAMETER Password
    Test admin password (default: admin123).

.PARAMETER Timeout
    Seconds to wait for the server to be ready (default: 60).

.PARAMETER KeepServer
    Do not stop the server after tests (useful for manual inspection).

.PARAMETER Python
    Path to the Python 3 interpreter to use. When omitted, a working Python 3 is
    resolved automatically (the $env:PYTHON variable, then python3, python and py).

.EXAMPLE
    .\run-smoke.ps1
    .\run-smoke.ps1 -SkipBuild -Timeout 90
    .\run-smoke.ps1 -Python C:\Python312\python.exe
#>
[CmdletBinding()]
param(
    [switch]$SkipBuild,
    [string]$ServerProject = "src/SharpCoreDB.Server/SharpCoreDB.Server.csproj",
    [int]$HttpsPort = 8443,
    [int]$PgPort    = 5433,
    [string]$Username = "smokeadmin",
    [string]$Password = "admin123",
    [int]$Timeout = 60,
    [switch]$KeepServer,
    [string]$Python = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$RepoRoot  = Resolve-Path "$PSScriptRoot/../.."
$ResolvedServerProject = (Resolve-Path (Join-Path $RepoRoot $ServerProject)).Path
$SmokeDir  = $PSScriptRoot
$CertDir   = Join-Path $SmokeDir "smoke-certs"
$DataDir   = Join-Path $SmokeDir "smoke-data"
$LogDir    = Join-Path $SmokeDir "smoke-logs"
$CertPath  = Join-Path $CertDir  "smoke.pfx"
$CertPass  = "smoketest"
$ConfigSrc = Join-Path $SmokeDir "appsettings.smoke.json"
$Results   = Join-Path $SmokeDir "smoke-results.json"

$ServerProcess = $null

function Write-Step([string]$msg) {
    Write-Host "`n[smoke] $msg" -ForegroundColor Cyan
}

function Write-Pass([string]$msg) {
    Write-Host "  ✓  $msg" -ForegroundColor Green
}

function Write-Fail([string]$msg) {
    Write-Host "  ✗  $msg" -ForegroundColor Red
}

function Test-PythonCandidate([string]$candidate) {
    # A candidate only counts when it really is a Python 3 interpreter. On Windows
    # `python3.exe` is often a Microsoft Store app-execution alias stub that exits
    # with code 9009 ("Python was not found"), so resolving the name is not enough -
    # the candidate has to be executed.
    try {
        $exe = (Get-Command $candidate -ErrorAction Stop).Source
    } catch {
        return $false
    }
    if ([string]::IsNullOrWhiteSpace($exe)) { return $false }
    if ($exe -like "*\WindowsApps\*") { return $false }

    $probe = & $candidate -c "import sys; print(sys.version_info[0])" 2>$null
    return ($LASTEXITCODE -eq 0 -and "$probe".Trim() -eq "3")
}

function Resolve-PythonInterpreter {
    if (-not [string]::IsNullOrWhiteSpace($Python)) {
        if (Test-PythonCandidate $Python) { return $Python }
        throw "The -Python interpreter '$Python' is not a working Python 3."
    }
    if (-not [string]::IsNullOrWhiteSpace($env:PYTHON) -and (Test-PythonCandidate $env:PYTHON)) {
        return $env:PYTHON
    }
    foreach ($candidate in @("python3", "python", "py")) {
        if (Test-PythonCandidate $candidate) { return $candidate }
    }
    throw "No working Python 3 interpreter found. Install Python 3.10+ or pass -Python <path>."
}

function Stop-SmokeServer {
    if ($null -ne $ServerProcess -and -not $ServerProcess.HasExited) {
        Write-Step "Stopping server (PID $($ServerProcess.Id))..."
        $ServerProcess.Kill($true)
        $ServerProcess.WaitForExit(5000) | Out-Null
        Write-Pass "Server stopped."
    }
}

try {
    Push-Location $RepoRoot

    # ── 1. Build ─────────────────────────────────────────────────────────────
    if (-not $SkipBuild) {
        Write-Step "Building server project..."
        dotnet build $ResolvedServerProject -c Release --nologo -v q
        if ($LASTEXITCODE -ne 0) { throw "Build failed." }
        Write-Pass "Build succeeded."
    } else {
        Write-Step "Skipping build (--SkipBuild specified)."
    }

    # ── 2. Dev certificate ────────────────────────────────────────────────────
    Write-Step "Generating development TLS certificate..."
    New-Item -ItemType Directory -Force -Path $CertDir | Out-Null
    New-Item -ItemType Directory -Force -Path $DataDir | Out-Null
    New-Item -ItemType Directory -Force -Path $LogDir  | Out-Null

    if (Test-Path $CertPath) {
        Remove-Item $CertPath -Force
    }
    dotnet dev-certs https -ep $CertPath -p $CertPass --trust 2>&1 | Out-Null
    if (-not (Test-Path $CertPath)) {
        throw "Certificate generation failed: $CertPath not found."
    }
    Write-Pass "Certificate created at: $CertPath"

    # ── 3. Prepare smoke appsettings ─────────────────────────────────────────
    Write-Step "Preparing server configuration..."
    $config = Get-Content $ConfigSrc | ConvertFrom-Json -Depth 20

    # Patch cert path + data paths to absolute paths
    $config.Server.Security.TlsCertificatePath = $CertPath -replace "\\", "/"
    $config.Server.Databases[0].DatabasePath   = (Join-Path $DataDir "smokedb.scdb") -replace "\\", "/"
    $config.Server.Logging.FilePath            = (Join-Path $LogDir  "smoke.log")    -replace "\\", "/"
    $config.Server.GrpcPort    = 5001
    $config.Server.HttpsApiPort = $HttpsPort
    $config.Server.BinaryProtocolPort = $PgPort

    $patchedConfig = Join-Path $SmokeDir "appsettings.smoke.patched.json"
    $config | ConvertTo-Json -Depth 20 | Set-Content $patchedConfig -Encoding UTF8
    Write-Pass "Patched config written to: $patchedConfig"

    # ── 4. Start server ───────────────────────────────────────────────────────
    Write-Step "Starting SharpCoreDB server in background..."
    $env:ASPNETCORE_ENVIRONMENT = "Production"
    $env:DOTNET_ENVIRONMENT     = "Production"

    # The server's stdout/stderr go to files instead of an undrained pipe: an unread
    # redirected pipe fills after ~4 KB and then blocks the server mid-log-write,
    # which surfaces as a request that times out (observed on this box as the
    # information_schema query hanging while SELECT 1 answered in 93 ms).
    $serverOutLog = Join-Path $LogDir "server-stdout.log"
    $serverErrLog = Join-Path $LogDir "server-stderr.log"
    $serverArgs = "run --project `"$ResolvedServerProject`" --configuration Release --no-build " +
                  "-- --appsettings `"$patchedConfig`""

    $ServerProcess = Start-Process -FilePath "dotnet" -ArgumentList $serverArgs `
        -WorkingDirectory $SmokeDir -NoNewWindow -PassThru `
        -RedirectStandardOutput $serverOutLog -RedirectStandardError $serverErrLog
    Write-Pass "Server started (PID: $($ServerProcess.Id))"
    Write-Host "  Server log: $serverOutLog"

    # ── 5. Wait for server ready ──────────────────────────────────────────────
    Write-Step "Waiting for server to become ready (timeout: ${Timeout}s)..."
    $healthUrl = "https://127.0.0.1:$HttpsPort/api/v1/health"
    $deadline  = [DateTime]::Now.AddSeconds($Timeout)
    $ready     = $false

    while ([DateTime]::Now -lt $deadline) {
        try {
            $resp = Invoke-WebRequest -Uri $healthUrl -SkipCertificateCheck -TimeoutSec 3 -UseBasicParsing -ErrorAction Stop
            if ($resp.StatusCode -eq 200) {
                $ready = $true
                break
            }
        } catch {
            # Not ready yet — keep polling
        }
        Start-Sleep -Seconds 1
    }

    if (-not $ready) {
        throw "Server did not become healthy within ${Timeout}s."
    }
    Write-Pass "Server is healthy."

    # ── 6. Run Python smoke tests ─────────────────────────────────────────────
    Write-Step "Resolving a working Python 3 interpreter..."
    $python = Resolve-PythonInterpreter
    Write-Pass "Using Python interpreter: $python"

    Write-Step "Running Python smoke tests..."
    # The report prints box-drawing and check-mark glyphs. Emit UTF-8 and decode it
    # as UTF-8 here, otherwise a legacy Windows code page turns the run into a
    # UnicodeEncodeError before the first test.
    $env:PYTHONIOENCODING = "utf-8"
    try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }

    & $python -m pip install requests --quiet --disable-pip-version-check 2>&1 | Out-Null

    & $python (Join-Path $SmokeDir "smoke_tests.py") `
        --host 127.0.0.1 `
        --https-port $HttpsPort `
        --pg-port $PgPort `
        --username $Username `
        --password $Password `
        --no-verify-tls `
        --output $Results `
        --timeout 10
    $exitCode = $LASTEXITCODE

    # ── 7. Report ─────────────────────────────────────────────────────────────
    if ($exitCode -eq 0) {
        Write-Pass "All smoke tests passed."
    } else {
        Write-Fail "One or more smoke tests failed (exit code: $exitCode)."
    }

    if (Test-Path $Results) {
        Write-Host "`n  Results: $Results" -ForegroundColor Cyan
    }

    exit $exitCode

} catch {
    Write-Fail "Fatal error: $_"
    exit 1
} finally {
    if (-not $KeepServer) {
        Stop-SmokeServer
    }
    # Clean up patched config
    $patchedConfig = Join-Path $SmokeDir "appsettings.smoke.patched.json"
    if (Test-Path $patchedConfig) {
        Remove-Item $patchedConfig -Force -ErrorAction SilentlyContinue
    }
    Pop-Location
}
