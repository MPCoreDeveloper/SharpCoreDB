#Requires -Version 7.0

<#
.SYNOPSIS
    Advanced NuGet build script using .nuspec for fine-grained control
.DESCRIPTION
    This script builds SharpCoreDB with platform-specific optimizations and packages using nuspec
.EXAMPLE
    .\build-nuget-advanced.ps1
.EXAMPLE
    .\build-nuget-advanced.ps1 -Version "1.0.1" -SkipTests
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
    [switch]$SkipTests,
    
    [Parameter(Mandatory=$false)]
    [switch]$CleanOnly
)

$ErrorActionPreference = 'Stop'

# Define runtime identifiers
$runtimes = @(
    @{ RID = 'win-x64'; Name = 'Windows x64'; Optimize = '/p:EnableAVX2=true' },
    @{ RID = 'win-arm64'; Name = 'Windows ARM64'; Optimize = '/p:EnableNeonIntrinsics=true' },
    @{ RID = 'linux-x64'; Name = 'Linux x64'; Optimize = '/p:EnableAVX2=true' },
    @{ RID = 'linux-arm64'; Name = 'Linux ARM64'; Optimize = '/p:EnableNeonIntrinsics=true' },
    @{ RID = 'osx-x64'; Name = 'macOS x64'; Optimize = '/p:EnableAVX2=true' },
    @{ RID = 'osx-arm64'; Name = 'macOS ARM64 (Apple Silicon)'; Optimize = '/p:EnableNeonIntrinsics=true' }
)

function Write-Header {
    param([string]$Text)
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host $Text -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan
}

function Write-Success {
    param([string]$Text)
    Write-Host "✓ $Text" -ForegroundColor Green
}

function Write-Info {
    param([string]$Text)
    Write-Host "→ $Text" -ForegroundColor Yellow
}

function Write-Step {
    param([string]$Text)
    Write-Host "  • $Text" -ForegroundColor Gray
}

Write-Header "SharpCoreDB Advanced NuGet Build"
Write-Info "Configuration: $Configuration"
Write-Info "Version: $Version"
Write-Info "Output: $OutputPath"
Write-Host ""

# Clean
Write-Header "Cleaning Build Artifacts"
$cleanPaths = @($OutputPath, ".\bin", ".\obj")
foreach ($path in $cleanPaths) {
    if (Test-Path $path) {
        Write-Step "Removing $path"
        Remove-Item -Path $path -Recurse -Force
    }
}
New-Item -ItemType Directory -Path $OutputPath -Force | Out-Null
Write-Success "Clean completed"

if ($CleanOnly) {
    Write-Info "Clean-only mode, exiting"
    exit 0
}

# Restore
Write-Header "Restoring Dependencies"
dotnet restore --verbosity quiet
if ($LASTEXITCODE -ne 0) {
    throw "Restore failed"
}
Write-Success "Dependencies restored"

# Build AnyCPU (fallback)
Write-Header "Building AnyCPU (Fallback)"
dotnet build `
    --configuration $Configuration `
    --no-restore `
    /p:Version=$Version `
    /p:GenerateDocumentationFile=true `
    --verbosity quiet

if ($LASTEXITCODE -ne 0) {
    throw "AnyCPU build failed"
}
Write-Success "AnyCPU build completed"

# Build platform-specific
Write-Header "Building Platform-Specific Assemblies"
$buildResults = @()

foreach ($runtime in $runtimes) {
    $rid = $runtime.RID
    $name = $runtime.Name
    $optimize = $runtime.Optimize
    
    Write-Step "Building $name ($rid)..."
    
    try {
        dotnet build `
            --configuration $Configuration `
            --runtime $rid `
            --no-restore `
            /p:Version=$Version `
            /p:SelfContained=false `
            /p:PublishSingleFile=false `
            /p:Optimize=true `
            $optimize `
            --verbosity quiet
        
        if ($LASTEXITCODE -eq 0) {
            $buildResults += @{ RID = $rid; Name = $name; Success = $true }
            Write-Host "    ✓ Success" -ForegroundColor Green
        } else {
            $buildResults += @{ RID = $rid; Name = $name; Success = $false }
            Write-Host "    ✗ Failed" -ForegroundColor Red
        }
    }
    catch {
        $buildResults += @{ RID = $rid; Name = $name; Success = $false }
        Write-Host "    ✗ Failed: $_" -ForegroundColor Red
    }
}

# Summary of platform builds
Write-Host ""
Write-Info "Platform Build Summary:"
foreach ($result in $buildResults) {
    $status = if ($result.Success) { "✓" } else { "✗" }
    $color = if ($result.Success) { "Green" } else { "Red" }
    Write-Host "  $status $($result.Name) ($($result.RID))" -ForegroundColor $color
}

# Run tests
if (-not $SkipTests) {
    Write-Header "Running Tests"
    Write-Info "Skipping tests (no test project configured)"
    # Uncomment when tests are available:
    # dotnet test --configuration $Configuration --no-build --verbosity quiet
}

# Create NuGet package using nuspec
Write-Header "Creating NuGet Package"

# Verify required files exist
$requiredFiles = @(
    "SharpCoreDB.jpg",
    "README.md",
    "SharpCoreDB.nuspec"
)

foreach ($file in $requiredFiles) {
    if (-not (Test-Path $file)) {
        throw "Required file not found: $file"
    }
}

Write-Step "Packing with nuspec..."
dotnet pack `
    --configuration $Configuration `
    --no-build `
    --output $OutputPath `
    /p:NuspecFile=SharpCoreDB.nuspec `
    /p:NuspecProperties="version=$Version" `
    --verbosity quiet

if ($LASTEXITCODE -eq 0) {
    Write-Success "NuGet package created"
} else {
    Write-Warning "Package creation failed, trying alternative method..."
    
    # Alternative: use nuget.exe if available
    if (Get-Command nuget -ErrorAction SilentlyContinue) {
        nuget pack SharpCoreDB.nuspec -Version $Version -OutputDirectory $OutputPath -Properties Configuration=$Configuration
        if ($LASTEXITCODE -eq 0) {
            Write-Success "NuGet package created (via nuget.exe)"
        }
    }
}

# Results
Write-Header "Build Complete"
Write-Host ""

if (Test-Path $OutputPath) {
    $packages = Get-ChildItem -Path $OutputPath -Filter "*.nupkg"
    if ($packages) {
        Write-Success "Packages created in: $OutputPath"
        foreach ($pkg in $packages) {
            $sizeMB = [math]::Round($pkg.Length / 1MB, 2)
            Write-Step "$($pkg.Name) ($sizeMB MB)"
        }
        
        Write-Host ""
        Write-Info "To publish to NuGet.org:"
        Write-Host "  dotnet nuget push `"$OutputPath\SharpCoreDB.$Version.nupkg`" --source https://api.nuget.org/v3/index.json --api-key YOUR_API_KEY" -ForegroundColor Gray
        
        Write-Host ""
        Write-Info "To test locally:"
        Write-Host "  dotnet nuget add source $((Resolve-Path $OutputPath).Path) --name LocalSharpCoreDB" -ForegroundColor Gray
        Write-Host "  dotnet add package SharpCoreDB --version $Version --source LocalSharpCoreDB" -ForegroundColor Gray
    } else {
        Write-Warning "No packages found in output directory"
    }
} else {
    Write-Warning "Output directory not found"
}

Write-Host ""
