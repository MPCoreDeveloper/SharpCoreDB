# SharpCoreDB NuGet Quick Start

## Installation

### From NuGet.org

```bash
dotnet add package SharpCoreDB
```

or in Package Manager Console:

```powershell
Install-Package SharpCoreDB
```

or in your `.csproj`:

```xml
<PackageReference Include="SharpCoreDB" Version="1.0.0" />
```

### From Local Build

If you built the package locally:

```bash
# Add local package source (one-time setup)
dotnet nuget add source D:\source\repos\MPCoreDeveloper\SharpCoreDB\SharpCoreDB\artifacts --name LocalSharpCoreDB

# Install the package
dotnet add package SharpCoreDB --source LocalSharpCoreDB
```

## Platform-Specific Assemblies

SharpCoreDB automatically selects the optimized assembly for your platform:

| Platform | Runtime ID | Optimizations |
|----------|------------|---------------|
| Windows x64 | `win-x64` | AVX2 (optional) |
| Windows ARM64 | `win-arm64` | NEON (optional) |
| Linux x64 | `linux-x64` | AVX2 (optional) |
| Linux ARM64 | `linux-arm64` | NEON (optional) |
| macOS x64 | `osx-x64` | AVX2 (optional) |
| macOS ARM64 (Apple Silicon) | `osx-arm64` | NEON (optional) |

The correct assembly is automatically loaded based on your runtime environment.

## Basic Usage

```csharp
using SharpCoreDB;
using SharpCoreDB.Platform;

// Check platform optimizations
Console.WriteLine(PlatformOptimizations.GetPlatformInfo());

// Your database code here...
```

## Verifying Platform Optimizations

Create a simple console app to verify platform-specific optimizations are working:

```csharp
using SharpCoreDB.Platform;

Console.WriteLine("SharpCoreDB Platform Information");
Console.WriteLine("=================================");
Console.WriteLine();
Console.WriteLine(PlatformOptimizations.GetPlatformInfo());
Console.WriteLine();
Console.WriteLine("Optimization Features:");
Console.WriteLine($"  Architecture: {PlatformOptimizations.PlatformArchitecture}");
Console.WriteLine($"  Optimization Level: {PlatformOptimizations.OptimizationLevel}");
Console.WriteLine($"  SIMD Enabled: {PlatformOptimizations.IsSIMDEnabled}");
Console.WriteLine($"  AVX2 Available: {PlatformOptimizations.IsAVX2Enabled}");
Console.WriteLine($"  NEON Available: {PlatformOptimizations.IsNEONEnabled}");
```

Expected output on Windows x64 with AVX2:
```
SharpCoreDB Platform Information
=================================

SharpCoreDB Platform Optimizations
==================================
Architecture: x64
Optimization: AVX2 (x64)
SIMD Enabled: True
AVX2 Support: True
NEON Support: False
Vector Size: 32 bytes

Runtime Information:
OS: Microsoft Windows NT 10.0.22631.0
.NET Version: 10.0.0
Processor Count: 16
64-bit Process: True
64-bit OS: True

Optimization Features:
  Architecture: x64
  Optimization Level: AVX2 (x64)
  SIMD Enabled: True
  AVX2 Available: True
  NEON Available: False
```

Expected output on macOS ARM64 (Apple Silicon) with NEON:
```
SharpCoreDB Platform Information
=================================

SharpCoreDB Platform Optimizations
==================================
Architecture: ARM64
Optimization: NEON (ARM64)
SIMD Enabled: True
AVX2 Support: False
NEON Support: True
Vector Size: 16 bytes

Runtime Information:
OS: Unix 14.0.0
.NET Version: 10.0.0
Processor Count: 10
64-bit Process: True
64-bit OS: True

Optimization Features:
  Architecture: ARM64
  Optimization Level: NEON (ARM64)
  SIMD Enabled: True
  AVX2 Available: False
  NEON Available: True
```

## Performance Tips

1. **Use the correct runtime**: Deploy as self-contained with specific RID for best performance:
   ```bash
   dotnet publish -r win-x64 -c Release
   ```

2. **Enable tiered compilation** (enabled by default in SharpCoreDB)

3. **Profile your application**: Use the platform-specific optimizations where they matter most

4. **Hardware considerations**:
   - Older x64 CPUs may not support AVX2
   - ARM64 benefits significantly from NEON intrinsics
   - The library automatically falls back to standard implementations

## Troubleshooting

### Wrong assembly loaded

Check which assembly is being used:

```bash
# Windows
where /R "C:\Users\YourUser\.nuget\packages\sharpcoredb" SharpCoreDB.dll

# Linux/macOS
find ~/.nuget/packages/sharpcoredb -name "SharpCoreDB.dll"
```

### Performance not as expected

1. Verify optimizations are enabled:
   ```csharp
   if (!PlatformOptimizations.IsSIMDEnabled)
   {
       Console.WriteLine("Warning: SIMD optimizations not enabled!");
   }
   ```

2. Check you're running in Release mode

3. Ensure proper runtime identifier:
   ```bash
   dotnet --info
   ```

### Missing platform assembly

If a platform-specific assembly is missing, the AnyCPU fallback will be used. This is expected and works correctly, but without platform-specific optimizations.

## Building from Source

See [BUILD.md](BUILD.md) for detailed instructions on building SharpCoreDB with platform-specific optimizations.

## Support

- GitHub: https://github.com/MPCoreDeveloper/SharpCoreDB
- Issues: https://github.com/MPCoreDeveloper/SharpCoreDB/issues
- Documentation: See README.md in the package

## License

MIT License - See package for full license text.
