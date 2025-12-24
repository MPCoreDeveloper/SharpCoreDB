using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Numerics;

namespace SharpCoreDB.Platform;

/// <summary>
/// Platform-specific optimization utilities for SharpCoreDB
/// </summary>
public static class PlatformOptimizations
{
    /// <summary>
    /// Indicates if SIMD operations are available
    /// </summary>
    public static bool IsSIMDEnabled =>
#if SIMD_ENABLED
        true;
#else
        false;
#endif

    /// <summary>
    /// Indicates if AVX2 optimizations are available (x64)
    /// </summary>
    public static bool IsAVX2Enabled =>
#if AVX2
        System.Runtime.Intrinsics.X86.Avx2.IsSupported;
#else
        false;
#endif

    /// <summary>
    /// Indicates if NEON optimizations are available (ARM64)
    /// </summary>
    public static bool IsNEONEnabled =>
#if NEON
        System.Runtime.Intrinsics.Arm.AdvSimd.IsSupported;
#else
        false;
#endif

    /// <summary>
    /// Gets the current platform architecture
    /// </summary>
    public static string PlatformArchitecture =>
#if X64
        "x64";
#elif ARM64
        "ARM64";
#else
        "AnyCPU";
#endif

    /// <summary>
    /// Gets optimization level description
    /// </summary>
    public static string OptimizationLevel
    {
        get
        {
#if AVX2
            return "AVX2 (x64)";
#elif NEON
            return "NEON (ARM64)";
#elif SIMD_ENABLED
            return "SIMD Generic";
#else
            return "Standard";
#endif
        }
    }

    /// <summary>
    /// Fast memory comparison using platform-specific optimizations
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe bool FastMemoryEquals(byte* ptr1, byte* ptr2, int length)
    {
#if AVX2
        // Use AVX2 for x64
        if (System.Runtime.Intrinsics.X86.Avx2.IsSupported && length >= 32)
        {
            return FastMemoryEqualsAVX2(ptr1, ptr2, length);
        }
#elif NEON
        // Use NEON for ARM64
        if (System.Runtime.Intrinsics.Arm.AdvSimd.IsSupported && length >= 16)
        {
            return FastMemoryEqualsNEON(ptr1, ptr2, length);
        }
#endif
        // Fallback to standard comparison
        return FastMemoryEqualsStandard(ptr1, ptr2, length);
    }

#if AVX2
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe bool FastMemoryEqualsAVX2(byte* ptr1, byte* ptr2, int length)
    {
        int i = 0;
        
        // Process 32-byte chunks with AVX2
        while (i + 32 <= length)
        {
            var v1 = System.Runtime.Intrinsics.X86.Avx.LoadVector256(ptr1 + i);
            var v2 = System.Runtime.Intrinsics.X86.Avx.LoadVector256(ptr2 + i);
            
            if (!v1.Equals(v2))
                return false;
                
            i += 32;
        }
        
        // Handle remaining bytes
        while (i < length)
        {
            if (ptr1[i] != ptr2[i])
                return false;
            i++;
        }
        
        return true;
    }
#endif

#if NEON
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe bool FastMemoryEqualsNEON(byte* ptr1, byte* ptr2, int length)
    {
        int i = 0;
        
        // Process 16-byte chunks with NEON
        while (i + 16 <= length)
        {
            var v1 = System.Runtime.Intrinsics.Arm.AdvSimd.LoadVector128(ptr1 + i);
            var v2 = System.Runtime.Intrinsics.Arm.AdvSimd.LoadVector128(ptr2 + i);
            
            var cmp = System.Runtime.Intrinsics.Arm.AdvSimd.CompareEqual(v1, v2);
            if (System.Runtime.Intrinsics.Arm.AdvSimd.Arm64.MinAcross(cmp).ToScalar() == 0)
                return false;
                
            i += 16;
        }
        
        // Handle remaining bytes
        while (i < length)
        {
            if (ptr1[i] != ptr2[i])
                return false;
            i++;
        }
        
        return true;
    }
#endif

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe bool FastMemoryEqualsStandard(byte* ptr1, byte* ptr2, int length)
    {
        // Use ulong comparison for better performance
        int i = 0;
        
        // Compare 8 bytes at a time
        while (i + 8 <= length)
        {
            if (*(ulong*)(ptr1 + i) != *(ulong*)(ptr2 + i))
                return false;
            i += 8;
        }
        
        // Compare remaining bytes
        while (i < length)
        {
            if (ptr1[i] != ptr2[i])
                return false;
            i++;
        }
        
        return true;
    }

    /// <summary>
    /// Gets platform optimization information for diagnostics
    /// </summary>
    public static string GetPlatformInfo()
    {
        return $"""
            SharpCoreDB Platform Optimizations
            ==================================
            Architecture: {PlatformArchitecture}
            Optimization: {OptimizationLevel}
            SIMD Enabled: {IsSIMDEnabled}
            AVX2 Support: {IsAVX2Enabled}
            NEON Support: {IsNEONEnabled}
            Vector Size: {Vector<byte>.Count} bytes
            
            Runtime Information:
            OS: {Environment.OSVersion}
            .NET Version: {Environment.Version}
            Processor Count: {Environment.ProcessorCount}
            64-bit Process: {Environment.Is64BitProcess}
            64-bit OS: {Environment.Is64BitOperatingSystem}
            """;
    }
}
