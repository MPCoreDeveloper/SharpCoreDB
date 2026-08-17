# ============================================================================
# SharpCoreDB WebViewer — Cross-platform launcher (Windows PowerShell)
# Builds (if needed) and starts the WebViewer, then opens the browser.
# Usage:
#   .\launch-viewer.ps1                    # build + run + open browser
#   .\launch-viewer.ps1 -NoBuild           # run existing build
#   .\launch-viewer.ps1 -Port 5443         # custom port
#   .\launch-viewer.ps1 -OpenPath C:\data  # connect to a database on startup
# ============================================================================
[CmdletBinding()]
param(
    [switch]$NoBuild,
    [int]$Port = 5443,
    [string]$OpenPath = '',
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$Project = Join-Path $PSScriptRoot 'SharpCoreDB.WebViewer.csproj'

Write-Host '=== SharpCoreDB WebViewer ===' -ForegroundColor Cyan

# 1. Build
if (-not $NoBuild) {
    Write-Host "[1/3] Building WebViewer ($Configuration)..."
    dotnet build $Project -c $Configuration --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
}

# 2. Launch with settings
Write-Host "[2/3] Starting WebViewer on https://localhost:$Port ..."

$env:WebViewer__HttpsPort = "$Port"
if ($OpenPath) {
    $env:WebViewer__InitialDatabasePath = $OpenPath
}

$job = Start-Job -ScriptBlock {
    param($proj)
    dotnet run --project $proj --no-build -c $env:Configuration
} -ArgumentList $Project

# 3. Open browser once port responds
Write-Host "[3/3] Opening browser..."
$url = "https://localhost:$Port"
Start-Sleep -Seconds 2

$deadline = (Get-Date).AddSeconds(20)
$ready = $false
while ((Get-Date) -lt $deadline) {
    try {
        $resp = Invoke-WebRequest -Uri $url -SkipCertificateCheck -TimeoutSec 2 -UseBasicParsing
        if ($resp.StatusCode -eq 200) { $ready = $true; break }
    } catch { Start-Sleep -Milliseconds 500 }
}

if ($ready) {
    Start-Process $url
    Write-Host "WebViewer running at $url" -ForegroundColor Green
} else {
    Write-Warning 'WebViewer did not respond in time. Check console output above.'
}

# Keep process alive in foreground
Receive-Job $job -Wait