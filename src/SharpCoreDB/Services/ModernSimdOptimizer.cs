// <copyright file="ModernSimdOptimizer.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace SharpCoreDB.Services;

/// <summary>
/// Phase 2D Modern SIMD Optimizer - Facade/Wrapper around SimdHelper.
/// 
/// NOTE: This class primarily delegates to SimdHelper for SIMD operations.
/// All new SIMD functionality is implemented in SimdHelper.
/// This class remains as a convenience wrapper and for backward compatibility.
/// 
/// For new SIMD operations, extend SimdHelper instead.
/// See: SimdHelper.Core.cs, SimdHelper.Operations.cs, SimdHelper.Fallback.cs
/// 
/// Expected Improvement: 2-5x depending on CPU capabilities (Vector512 > Vector256 > Vector128 > Scalar)
/// </summary>
public static class ModernSimdOptimizer
{
    /// <summary>
    /// Universal horizontal sum - delegates to SimdHelper.HorizontalSum.
    /// Automatically selects Vector512 (AVX-512) > Vector256 (AVX2) > Vector128 (SSE2) > Scalar.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long UniversalHorizontalSum(ReadOnlySpan<int> data)
    {
        return SimdHelper.HorizontalSum(data);
    }

    /// <summary>
    /// Universal comparison - delegates to SimdHelper.CompareGreaterThan.
    /// Automatically selects best available SIMD level.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int UniversalCompareGreaterThan(
        ReadOnlySpan<int> values, 
        int threshold, 
        Span<byte> results)
    {
        return SimdHelper.CompareGreaterThan(values, threshold, results);
    }

    /// <summary>
    /// Detect SIMD capability on this hardware.
    /// Delegates to SimdHelper for detection logic.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static SimdCapability DetectSimdCapability()
    {
        return SimdHelper.GetOptimalVectorSizeBytes switch
        {
            64 => SimdCapability.Vector512,
            32 => SimdCapability.Vector256,
            16 => SimdCapability.Vector128,
            _ => SimdCapability.Scalar
        };
    }

    /// <summary>
    /// Get SIMD capabilities string - delegates to SimdHelper.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string GetSimdCapabilities()
    {
        return SimdHelper.GetSimdCapabilities();
    }

    /// <summary>
    /// Check if any SIMD is supported - delegates to SimdHelper.
    /// </summary>
    public static bool SupportsModernSimd => SimdHelper.IsSimdSupported;
}

/// <summary>
/// SIMD Capability levels in .NET 10.
/// NOTE: Consider moving this to SimdHelper namespace in future refactoring.
/// </summary>
public enum SimdCapability
{
    Scalar = 0,
    Vector128 = 1,
    Vector256 = 2,
    Vector512 = 3
}
