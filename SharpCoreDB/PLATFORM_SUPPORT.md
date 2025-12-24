# SharpCoreDB Platform Support

## Supported Platforms

SharpCoreDB is designed to run on a wide range of platforms, from high-performance servers to resource-constrained IoT devices.

### ✅ Desktop Platforms

| Platform | Architecture | Runtime ID | Optimizations |
|----------|--------------|------------|---------------|
| Windows | x64 | `win-x64` | AVX2 (optional) |
| Windows | ARM64 | `win-arm64` | NEON (optional) |
| Linux | x64 | `linux-x64` | AVX2 (optional) |
| Linux | ARM64 | `linux-arm64` | NEON (optional) |
| macOS | x64 (Intel) | `osx-x64` | AVX2 (optional) |
| macOS | ARM64 (Apple Silicon) | `osx-arm64` | NEON (enabled) |

### 📱 Mobile Platforms

| Platform | Architecture | Runtime ID | Optimizations | Notes |
|----------|--------------|------------|---------------|-------|
| Android | ARM64 | `android-arm64` | NEON (enabled) | Most modern Android devices |
| Android | x64 | `android-x64` | Standard | Emulators, Chrome OS |
| iOS | ARM64 | `ios-arm64` | NEON (enabled) | iPhone 5s and newer |
| iOS Simulator | ARM64 | `iossimulator-arm64` | NEON (enabled) | M1/M2/M3 Macs |
| iOS Simulator | x64 | `iossimulator-x64` | Standard | Intel Macs |

### 🔌 IoT & Embedded Platforms

| Platform | Architecture | Runtime ID | Optimizations | Example Devices |
|----------|--------------|------------|---------------|-----------------|
| Linux | ARM32 | `linux-arm` | Limited SIMD | Raspberry Pi 2/3/4 (32-bit OS) |
| Linux | ARM64 | `linux-arm64` | NEON (optional) | Raspberry Pi 3/4/5 (64-bit OS), NVIDIA Jetson |

## Platform-Specific Features

### Compiler Constants

SharpCoreDB defines the following compiler constants based on the target platform:

```csharp
// Architecture
#if X64           // x64 platforms
#if ARM64         // ARM64 platforms
#if ARM32         // ARM32 platforms (IoT)

// Platform Type
#if ANDROID       // Android devices
#if IOS           // iOS devices
#if MOBILE        // Any mobile platform (Android or iOS)
#if IOT           // IoT devices
#if EMBEDDED      // Embedded devices

// Optimizations
#if SIMD_ENABLED  // SIMD operations available
#if AVX2          // AVX2 intrinsics (x64)
#if NEON          // NEON intrinsics (ARM)
```

### Usage Example

```csharp
#if MOBILE
    // Mobile-specific code (reduce memory usage, optimize for battery)
    const int MaxCacheSize = 10 * 1024 * 1024; // 10 MB
#elif IOT || EMBEDDED
    // IoT-specific code (minimal memory footprint)
    const int MaxCacheSize = 1 * 1024 * 1024; // 1 MB
#else
    // Desktop/server code (maximize performance)
    const int MaxCacheSize = 100 * 1024 * 1024; // 100 MB
#endif

#if ARM64 && NEON
    // Use NEON intrinsics for fast operations
    ProcessDataWithNEON(buffer);
#elif X64 && AVX2
    // Use AVX2 intrinsics
    ProcessDataWithAVX2(buffer);
#else
    // Standard implementation
    ProcessDataStandard(buffer);
#endif
```

## Mobile Platform Considerations

### Android

**Requirements:**
- .NET 10 for Android (MAUI, Xamarin.Android)
- Minimum API Level: 21 (Android 5.0)
- Recommended API Level: 29+ (Android 10+)

**Optimizations:**
- ARM64 builds use NEON intrinsics for encryption/decryption
- Automatic memory management for limited devices
- Power-efficient background operations

**File Storage:**
```csharp
// Android-specific paths
string dbPath = Path.Combine(
    Android.App.Application.Context.GetExternalFilesDir(null).AbsolutePath,
    "sharpcoredb.db"
);
```

### iOS

**Requirements:**
- .NET 10 for iOS (MAUI, Xamarin.iOS)
- Minimum iOS Version: 12.0
- Recommended iOS Version: 15.0+

**Optimizations:**
- Native ARM64 performance on all modern iPhones/iPads
- NEON intrinsics enabled by default
- App Sandbox compliant file storage

**File Storage:**
```csharp
// iOS-specific paths
string dbPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
    "..",
    "Library",
    "sharpcoredb.db"
);
```

### Platform Detection

```csharp
using SharpCoreDB.Platform;

// Runtime platform detection
if (PlatformOptimizations.PlatformArchitecture == "ARM64")
{
    Console.WriteLine("Running on ARM64");
    if (PlatformOptimizations.IsNEONEnabled)
    {
        Console.WriteLine("NEON optimizations active");
    }
}

// Check if mobile
#if MOBILE
    Console.WriteLine("Running on mobile device");
#endif
```

## IoT & Embedded Considerations

### Raspberry Pi

**Supported Models:**
- Raspberry Pi 2 Model B (ARMv7, 32-bit) - `linux-arm`
- Raspberry Pi 3 Model B/B+ (ARMv8, 32/64-bit) - `linux-arm` or `linux-arm64`
- Raspberry Pi 4 Model B (ARMv8, 64-bit) - `linux-arm64`
- Raspberry Pi 5 (ARMv8, 64-bit) - `linux-arm64`

**Recommendations:**
- Use 64-bit OS on Pi 3/4/5 for better performance
- For Pi Zero/Zero W, consider the AnyCPU build
- Enable NEON on 64-bit builds for 2-3x encryption performance

**Example deployment:**
```bash
# Raspberry Pi 4 with 64-bit OS
dotnet publish -r linux-arm64 -c Release --self-contained

# Raspberry Pi 3 with 32-bit OS
dotnet publish -r linux-arm -c Release --self-contained
```

### Other Embedded Devices

**NVIDIA Jetson Series:**
- Jetson Nano, TX2, Xavier: Use `linux-arm64`
- Full NEON support for optimal performance

**Industrial IoT Devices:**
- Use `linux-arm64` for 64-bit ARM Cortex-A devices
- Use `linux-arm` for 32-bit ARM Cortex-A devices

### Memory Constraints

For devices with limited memory (< 512 MB RAM):

```csharp
var options = new SharpCoreDBOptions
{
    // Reduce cache sizes
    PageCacheSize = 1 * 1024 * 1024,  // 1 MB
    IndexCacheSize = 512 * 1024,       // 512 KB
    
    // Disable some features
    EnableQueryCache = false,
    
    // Use memory-mapped files carefully
    UseMemoryMappedFiles = false
};
```

## Building for Specific Platforms

### Build All Platforms (Desktop Only)
```bash
.\build-nuget.ps1 -Version "1.0.0"
```

### Build with Mobile Support
```bash
.\build-nuget.ps1 -Version "1.0.0" -IncludeMobile
```

### Build with IoT Support
```bash
.\build-nuget.ps1 -Version "1.0.0" -IncludeIoT
```

### Build Everything
```bash
.\build-nuget.ps1 -Version "1.0.0" -IncludeMobile -IncludeIoT
```

### Build Single Platform
```bash
# Android ARM64
dotnet build -r android-arm64 -c Release

# iOS ARM64
dotnet build -r ios-arm64 -c Release

# Raspberry Pi (ARM32)
dotnet build -r linux-arm -c Release
```

## Testing on Target Platforms

### Android Testing
```bash
# Build and deploy to Android device/emulator
dotnet build -r android-arm64 -c Release
adb install -r bin/Release/net10.0/android-arm64/YourApp.apk
```

### iOS Testing
```bash
# Build for device
dotnet build -r ios-arm64 -c Release

# Build for simulator (M1 Mac)
dotnet build -r iossimulator-arm64 -c Release
```

### IoT Testing (Raspberry Pi)
```bash
# Build
dotnet publish -r linux-arm64 -c Release --self-contained

# Deploy via SSH
scp -r bin/Release/net10.0/linux-arm64/publish/* pi@raspberrypi.local:~/sharpcoredb/

# Run on device
ssh pi@raspberrypi.local
cd ~/sharpcoredb
chmod +x YourApp
./YourApp
```

## Performance Benchmarks

### Mobile vs Desktop

| Operation | Android ARM64 | iOS ARM64 | Windows x64 |
|-----------|---------------|-----------|-------------|
| Insert (1000 records) | ~45 ms | ~40 ms | ~25 ms |
| Query (indexed) | ~8 ms | ~7 ms | ~4 ms |
| Encryption/Decryption | ~120 MB/s | ~140 MB/s | ~250 MB/s |

### IoT Performance

| Device | Architecture | Insert (1000) | Query (indexed) |
|--------|--------------|---------------|-----------------|
| Raspberry Pi 5 (ARM64) | ARM64 + NEON | ~55 ms | ~10 ms |
| Raspberry Pi 4 (ARM64) | ARM64 + NEON | ~85 ms | ~15 ms |
| Raspberry Pi 3 (ARM32) | ARM32 | ~180 ms | ~35 ms |

## Platform-Specific Limitations

### Android
- File system permissions required
- Background task limitations (Android 12+)
- Scoped storage considerations

### iOS
- App Sandbox restrictions
- iCloud backup considerations
- Background execution limits

### IoT/Embedded
- Limited memory on some devices
- No AVX2 support (ARM-only)
- Potential storage limitations (SD cards)

## FAQ

**Q: Can I use SharpCoreDB in Xamarin.Forms/MAUI?**  
A: Yes! SharpCoreDB fully supports .NET MAUI and Xamarin on both Android and iOS.

**Q: Does it work on Raspberry Pi Zero?**  
A: Yes, but use the `linux-arm` build. Performance will be limited on single-core devices.

**Q: What about Windows IoT Core?**  
A: Use `win-arm64` for Raspberry Pi running Windows IoT Core.

**Q: Can I use it in Unity for mobile games?**  
A: Yes, if Unity supports .NET 10. Otherwise, use the .NET Standard builds.

**Q: Does it work on Apple Watch or Android Wear?**  
A: Theoretically yes (using `ios-arm64` for watchOS), but not officially tested. File storage may be very limited.

## Support

For platform-specific issues:
- GitHub Issues: https://github.com/MPCoreDeveloper/SharpCoreDB/issues
- Tag issues with platform labels: `android`, `ios`, `iot`, `raspberry-pi`

## License

MIT License - See LICENSE file for details.
