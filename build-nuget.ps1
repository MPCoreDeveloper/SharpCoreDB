#!/usr/bin/env pwsh
# Build SharpCoreDB NuGet package with multi-RID support
# Usage: .\build-nuget.ps1

param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$ProjectPath = "src\SharpCoreDB\SharpCoreDB.csproj"
$RIDs = @("win-x64", "win-arm64", "linux-x64", "linux-arm64", "osx-x64", "osx-arm64")

Write-Host "🔧 Building SharpCoreDB NuGet Package" -ForegroundColor Cyan
Write-Host ""

# Step 1: Clean
Write-Host "🧹 Cleaning previous builds..." -ForegroundColor Yellow
dotnet clean $ProjectPath -c $Configuration
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# Step 2: Restore without RID (AnyCPU)
Write-Host "📦 Restoring project..." -ForegroundColor Yellow
dotnet restore $ProjectPath
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# Step 3: Build for each RID
foreach ($rid in $RIDs) {
    Write-Host "🏗️  Building for $rid..." -ForegroundColor Green
    dotnet build $ProjectPath -c $Configuration -r $rid --no-restore
    if ($LASTEXITCODE -ne 0) { 
        Write-Host "❌ Build failed for $rid" -ForegroundColor Red
        exit $LASTEXITCODE 
    }
}

# Step 4: Pack
Write-Host "📦 Creating NuGet package..." -ForegroundColor Green
dotnet pack $ProjectPath -c $Configuration --no-build --no-restore
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host ""
Write-Host "✅ NuGet package created successfully!" -ForegroundColor Green
Write-Host "📁 Location: src\SharpCoreDB\bin\$Configuration\" -ForegroundColor Cyan
