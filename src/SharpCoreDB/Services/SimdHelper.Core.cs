// <copyright file="SimdHelper.Core.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Services;

using System.Runtime.Intrinsics.X86;
using System.Runtime.Intrinsics.Arm;

/// <summary>
/// SimdHelper - Core platform detection and capabilities.
/// Contains hardware detection properties and public API entry points.
/// Part of the SimdHelper partial class.
/// See also: SimdHelper.Operations.cs, SimdHelper.Fallback.cs
/// </summary>
public static partial class SimdHelper
{
    /// <summary>
    /// Gets a value indicating whether AVX2 (256-bit SIMD) is supported on this hardware.
    /// </summary>
    public static bool IsAvx2Supported => Avx2.IsSupported;

    /// <summary>
    /// Gets a value indicating whether SSE2 (128-bit SIMD) is supported on this hardware.
    /// </summary>
    public static bool IsSse2Supported => Sse2.IsSupported;

    /// <summary>
    /// Gets a value indicating whether ARM NEON (128-bit SIMD) is supported on this hardware.
    /// </summary>
    public static bool IsAdvSimdSupported => AdvSimd.IsSupported;

    /// <summary>
    /// Gets a value indicating whether AVX-512 (512-bit SIMD) is supported on this hardware.
    /// NEW in Phase 2D: Extended SIMD support for next-gen processors.
    /// </summary>
    public static bool IsVector512Supported => Avx512F.IsSupported;

    /// <summary>
    /// Gets a value indicating whether any SIMD acceleration is available.
    /// </summary>
    public static bool IsSimdSupported => IsVector512Supported || IsAvx2Supported || IsSse2Supported || IsAdvSimdSupported;

    /// <summary>
    /// Gets the optimal vector size in bytes for this hardware.
    /// Returns: 64 (Vector512), 32 (Vector256), 16 (Vector128), or 4 (Scalar)
    /// NEW in Phase 2D: Simplified SIMD selection.
    /// </summary>
    public static int GetOptimalVectorSizeBytes =>
        IsVector512Supported ? 64 :
        IsAvx2Supported ? 32 :
        IsSse2Supported ? 16 : 4;

    /// <summary>
    /// Gets a human-readable description of SIMD capabilities on this hardware.
    /// </summary>
    /// <returns>SIMD capability string.</returns>
    public static string GetSimdCapabilities()
    {
        var caps = new System.Collections.Generic.List<string>();

        if (Avx512F.IsSupported) caps.Add("AVX-512 (512-bit)");
        if (Avx2.IsSupported) caps.Add("AVX2 (256-bit)");
        if (Sse2.IsSupported) caps.Add("SSE2 (128-bit)");
        if (AdvSimd.IsSupported) caps.Add("ARM NEON (128-bit)");

        return caps.Count > 0 ? string.Join(", ", caps) : "No SIMD support (scalar only)";
    }
}
