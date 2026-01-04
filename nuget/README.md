# NuGet Packaging

This directory contains assets for NuGet packaging.

## Package Icon

Place your package icon here as `icon.png` (128x128 or 256x256 pixels recommended).

Current icon location: `../SharpCoreDB.jpg` (referenced in Directory.Build.props)

## README

A package README can be included by referencing `README.md` from the root.

## Building Packages

To build NuGet packages:

```bash
# Build all packages
dotnet pack --configuration Release --output ./artifacts

# Build specific package
dotnet pack src/SharpCoreDB/SharpCoreDB.csproj --configuration Release --output ./artifacts
```

## Publishing to NuGet

```bash
# Publish (requires API key)
dotnet nuget push artifacts/*.nupkg --api-key YOUR_API_KEY --source https://api.nuget.org/v3/index.json
```

## Package Configuration

Package metadata is configured in `Directory.Build.props` at the root:
- Package ID
- Version
- Authors
- Description
- License
- Icon
- Tags
- Repository URL

## Packages

The following packages are produced:

1. **SharpCoreDB** - Core library
2. **SharpCoreDB.Extensions** - Extension methods
3. **SharpCoreDB.Data.Provider** - ADO.NET provider
4. **SharpCoreDB.EntityFrameworkCore** - EF Core provider
5. **SharpCoreDB.Serilog.Sinks** - Serilog sink
