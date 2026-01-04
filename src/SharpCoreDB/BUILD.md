# SharpCoreDB NuGet Build Guide

This guide explains how to build SharpCoreDB as a NuGet package with platform-optimized assemblies.

## Overview

SharpCoreDB supports multiple build configurations:

- **AnyCPU**: Fallback build that works on any platform
- **Platform-specific**: Optimized builds for specific architectures
  - Windows x64 (with optional AVX2 support)
  - Windows ARM64 (with optional NEON support)
  - Linux x64 (with optional AVX2 support)
  - Linux ARM64 (with optional NEON support)
  - macOS x64 (with optional AVX2 support)
  - macOS ARM64/Apple Silicon (with optional NEON support)

## Build Scripts

### Quick Build: `build-nuget.ps1`

Simple script for creating a NuGet package with all platform-specific assemblies.

```powershell
# Basic usage
.\build-nuget.ps1

# Specify version
.\build-nuget.ps1 -Version "1.0.1"

# Debug build
.\build-nuget.ps1 -Configuration Debug
```

**Parameters:**
- `-Configuration`: Build configuration (Debug/Release, default: Release)
- `-Version`: Package version (default: 1.0.0)
- `-OutputPath`: Output directory for packages (default: .\artifacts)

### Advanced Build: `build-nuget-advanced.ps1`

Advanced script with more control and optimization options.

```powershell
# Basic usage
.\build-nuget-advanced.ps1

# With version and skip tests
.\build-nuget-advanced.ps1 -Version "1.0.1" -SkipTests

# Clean only (remove build artifacts)
.\build-nuget-advanced.ps1 -CleanOnly
```

**Parameters:**
- `-Configuration`: Build configuration (Debug/Release, default: Release)
- `-Version`: Package version (default: 1.0.0)
- `-OutputPath`: Output directory (default: .\artifacts)
- `-SkipTests`: Skip running tests
- `-CleanOnly`: Only clean build artifacts without building

## Platform-Specific Optimizations

The build process enables architecture-specific optimizations:

### x64 Platforms (Intel/AMD)
- SIMD instructions enabled
- Optional AVX2 support (set via `/p:EnableAVX2=true`)
- Compiler constant: `X64`, `SIMD_ENABLED`, `AVX2` (if enabled)

### ARM64 Platforms
- SIMD instructions enabled
- Optional NEON intrinsics (set via `/p:EnableNeonIntrinsics=true`)
- Compiler constant: `ARM64`, `SIMD_ENABLED`, `NEON` (if enabled)

## Using Platform-Specific Code

In your code, you can use conditional compilation:

```csharp
#if X64 && AVX2
    // Use AVX2 intrinsics for x64
    using System.Runtime.Intrinsics.X86;
#elif ARM64 && NEON
    // Use NEON intrinsics for ARM64
    using System.Runtime.Intrinsics.Arm;
#elif SIMD_ENABLED
    // Use generic SIMD
    using System.Numerics;
#endif
```

## NuGet Package Structure

The generated package contains:

```
SharpCoreDB.{version}.nupkg
├── lib/net10.0/
│   ├── SharpCoreDB.dll (AnyCPU fallback)
│   └── SharpCoreDB.xml
├── runtimes/
│   ├── win-x64/lib/net10.0/SharpCoreDB.dll
│   ├── win-arm64/lib/net10.0/SharpCoreDB.dll
│   ├── linux-x64/lib/net10.0/SharpCoreDB.dll
│   ├── linux-arm64/lib/net10.0/SharpCoreDB.dll
│   ├── osx-x64/lib/net10.0/SharpCoreDB.dll
│   └── osx-arm64/lib/net10.0/SharpCoreDB.dll
├── build/
│   └── *.props
├── SharpCoreDB.jpg (package icon)
└── README.md
```

## Manual Build Commands

If you prefer manual control:

### 1. Clean
```powershell
dotnet clean
Remove-Item -Path bin, obj, artifacts -Recurse -Force -ErrorAction SilentlyContinue
```

### 2. Restore
```powershell
dotnet restore
```

### 3. Build AnyCPU
```powershell
dotnet build --configuration Release /p:Version=1.0.0
```

### 4. Build Platform-Specific
```powershell
# Windows x64
dotnet build --configuration Release --runtime win-x64 /p:Version=1.0.0

# Windows ARM64
dotnet build --configuration Release --runtime win-arm64 /p:Version=1.0.0

# Linux x64
dotnet build --configuration Release --runtime linux-x64 /p:Version=1.0.0

# Linux ARM64
dotnet build --configuration Release --runtime linux-arm64 /p:Version=1.0.0

# macOS x64
dotnet build --configuration Release --runtime osx-x64 /p:Version=1.0.0

# macOS ARM64
dotnet build --configuration Release --runtime osx-arm64 /p:Version=1.0.0
```

### 5. Create Package
```powershell
dotnet pack --configuration Release --output artifacts /p:Version=1.0.0
```

## Publishing to NuGet.org

1. Get your API key from https://www.nuget.org/account/apikeys

2. Push the package:
```powershell
dotnet nuget push .\artifacts\SharpCoreDB.1.0.0.nupkg `
    --source https://api.nuget.org/v3/index.json `
    --api-key YOUR_API_KEY
```

## Testing Locally

Before publishing, test the package locally:

```powershell
# Add local source
dotnet nuget add source D:\source\repos\MPCoreDeveloper\SharpCoreDB\SharpCoreDB\artifacts `
    --name LocalSharpCoreDB

# Create test project
mkdir TestSharpCoreDB
cd TestSharpCoreDB
dotnet new console
dotnet add package SharpCoreDB --version 1.0.0 --source LocalSharpCoreDB

# Verify correct platform assembly is loaded
dotnet run
```

## Troubleshooting

### Logo not included
Ensure `SharpCoreDB.jpg` exists in the project root and is referenced in the .csproj:
```xml
<PackageIcon>SharpCoreDB.jpg</PackageIcon>
<None Include="SharpCoreDB.jpg" Pack="true" PackagePath="/" />
```

### Platform-specific build fails
Some platforms may not be available on your development machine. The build script will continue with available platforms.

### Package too large
To reduce package size, consider:
- Remove debug symbols: `/p:IncludeSymbols=false`
- Enable trimming: `/p:PublishTrimmed=true` (test carefully!)
- Build only needed platforms

## CI/CD Integration

For GitHub Actions, Azure DevOps, or other CI/CD:

```yaml
- name: Build NuGet Package
  run: |
    pwsh -File build-nuget-advanced.ps1 -Version ${{ github.ref_name }} -SkipTests
  
- name: Publish to NuGet
  run: |
    dotnet nuget push artifacts/*.nupkg --source https://api.nuget.org/v3/index.json --api-key ${{ secrets.NUGET_API_KEY }}
```

## Additional Resources

- [NuGet Package Documentation](https://docs.microsoft.com/en-us/nuget/)
- [Runtime Identifier Catalog](https://docs.microsoft.com/en-us/dotnet/core/rid-catalog)
- [Platform-Specific Code](https://docs.microsoft.com/en-us/dotnet/standard/library-guidance/cross-platform-targeting)
