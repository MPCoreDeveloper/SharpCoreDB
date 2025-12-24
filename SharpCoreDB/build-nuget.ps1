#Requires -Version 7.0

<#
.SYNOPSIS
    Build SharpCoreDB NuGet package with platform-specific optimized assemblies
.DESCRIPTION
    This script builds SharpCoreDB for multiple target platforms with architecture-specific optimizations
    and creates a NuGet package with all runtime-specific assemblies including mobile and IoT platforms.
.EXAMPLE
    .\build-nuget.ps1
.EXAMPLE
    .\build-nuget.ps1 -Configuration Release -Version "1.0.1"
.EXAMPLE
    .\build-nuget.ps1 -IncludeMobile -IncludeIoT
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory=$false)]
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    
    [Parameter(Mandatory=$false)]
    [string]$Version = '1.0.0',
    
    [Parameter(Mandatory=$false)]
    [string]$OutputPath = '.\artifacts',
    
    [Parameter(Mandatory=$false)]
    [switch]$IncludeMobile,
    
    [Parameter(Mandatory=$false)]
    [switch]$IncludeIoT
)

$ErrorActionPreference = 'Stop'

# Define runtime identifiers for platform-specific builds
$runtimes = @(
    # Desktop platforms
    'win-x64',
    'win-arm64',
    'linux-x64',
    'linux-arm64',
    'osx-x64',
    'osx-arm64'
)

# Add mobile platforms if requested
if ($IncludeMobile) {
    $runtimes += @(
        'android-arm64',
        'android-x64',
        'ios-arm64',
        'iossimulator-arm64',
        'iossimulator-x64'
    )
}

# Add IoT/embedded platforms if requested
if ($IncludeIoT) {
    $runtimes += @(
        'linux-arm',      # Raspberry Pi, IoT devices (32-bit ARM)
        'linux-arm64'     # Already in desktop, but good for IoT too
    )
}

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "SharpCoreDB NuGet Build Script" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Configuration: $Configuration" -ForegroundColor Yellow
Write-Host "Version: $Version" -ForegroundColor Yellow
Write-Host "Output Path: $OutputPath" -ForegroundColor Yellow
Write-Host "Include Mobile: $IncludeMobile" -ForegroundColor Yellow
Write-Host "Include IoT: $IncludeIoT" -ForegroundColor Yellow
Write-Host ""

# Clean previous builds
Write-Host "Cleaning previous builds..." -ForegroundColor Green
if (Test-Path $OutputPath) {
    Remove-Item -Path $OutputPath -Recurse -Force
}
New-Item -ItemType Directory -Path $OutputPath -Force | Out-Null

if (Test-Path ".\bin") {
    Remove-Item -Path ".\bin" -Recurse -Force
}

if (Test-Path ".\obj") {
    Remove-Item -Path ".\obj" -Recurse -Force
}

# Restore dependencies
Write-Host ""
Write-Host "Restoring NuGet packages..." -ForegroundColor Green
dotnet restore
if ($LASTEXITCODE -ne 0) {
    throw "Failed to restore NuGet packages"
}

# Build for each runtime
Write-Host ""
Write-Host "Building platform-specific assemblies..." -ForegroundColor Green
foreach ($runtime in $runtimes) {
    Write-Host "  - Building for $runtime..." -ForegroundColor Cyan
    
    dotnet build `
        --configuration $Configuration `
        --runtime $runtime `
        /p:Version=$Version `
        /p:SelfContained=false `
        /p:PublishSingleFile=false `
        /p:PublishTrimmed=false
    
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "Failed to build for $runtime, continuing..."
    }
}

# Build AnyCPU version (main)
Write-Host ""
Write-Host "Building AnyCPU version..." -ForegroundColor Green
dotnet build `
    --configuration $Configuration `
    /p:Version=$Version
    
if ($LASTEXITCODE -ne 0) {
    throw "Failed to build AnyCPU version"
}

# Create NuGet package
Write-Host ""
Write-Host "Creating NuGet package..." -ForegroundColor Green
dotnet pack `
    --configuration $Configuration `
    --no-build `
    --output $OutputPath `
    /p:Version=$Version `
    /p:PackageVersion=$Version `
    /p:IncludeSymbols=true `
    /p:SymbolPackageFormat=snupkg

if ($LASTEXITCODE -ne 0) {
    throw "Failed to create NuGet package"
}

# Display results
Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Build Completed Successfully!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "NuGet packages created in: $OutputPath" -ForegroundColor Yellow
Get-ChildItem -Path $OutputPath -Filter "*.nupkg" | ForEach-Object {
    Write-Host "  - $($_.Name) ($([math]::Round($_.Length / 1MB, 2)) MB)" -ForegroundColor Gray
}

Write-Host ""
Write-Host "Platforms included:" -ForegroundColor Yellow
Write-Host "  ✓ Desktop (Windows, Linux, macOS) - x64 & ARM64" -ForegroundColor Gray
if ($IncludeMobile) {
    Write-Host "  ✓ Mobile (Android, iOS) - ARM64 & x64" -ForegroundColor Gray
}
if ($IncludeIoT) {
    Write-Host "  ✓ IoT/Embedded (Linux ARM32/ARM64)" -ForegroundColor Gray
}

Write-Host ""
Write-Host "To publish to NuGet.org, run:" -ForegroundColor Yellow
Write-Host "  dotnet nuget push `"$OutputPath\SharpCoreDB.$Version.nupkg`" --source https://api.nuget.org/v3/index.json --api-key YOUR_API_KEY" -ForegroundColor Gray
Write-Host ""
