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

.EXAMPLE
    .\run-smoke.ps1
    .\run-smoke.ps1 -SkipBuild -Timeout 90
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
    [switch]$KeepServer
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

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName  = "dotnet"
    $startInfo.Arguments = "run --project $ResolvedServerProject --configuration Release " +
                           "--no-build -- " +
                           "--appsettings $patchedConfig"
    $startInfo.UseShellExecute  = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError  = $true
    $startInfo.WorkingDirectory = $SmokeDir

    $ServerProcess = [System.Diagnostics.Process]::Start($startInfo)
    Write-Pass "Server started (PID: $($ServerProcess.Id))"

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
    Write-Step "Running Python smoke tests..."
    $python = if (Get-Command python3 -ErrorAction SilentlyContinue) { "python3" } else { "python" }

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
