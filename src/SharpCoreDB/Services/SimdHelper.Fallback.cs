// <copyright file="SimdHelper.Fallback.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Services;

using System;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

/// <summary>
/// SimdHelper - Scalar fallback implementations and helper functions.
/// Contains non-SIMD implementations for platforms without hardware acceleration.
/// Part of the SimdHelper partial class.
/// See also: SimdHelper.Core.cs, SimdHelper.Operations.cs
/// </summary>
public static partial class SimdHelper
{
    /// <summary>
    /// Scalar fallback hash code computation.
    /// Used when no SIMD acceleration is available.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ComputeHashCodeScalar(ReadOnlySpan<byte> data)
    {
        const uint FnvPrime = 16777619;
        const uint FnvOffsetBasis = 2166136261;

        uint hash = FnvOffsetBasis;
        foreach (byte b in data)
        {
            hash ^= b;
            hash *= FnvPrime;
        }
        return (int)hash;
    }

    /// <summary>
    /// Reduces a Vector256 hash to a single uint32 value.
    /// Helper method for hash operations.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint ReduceVector256ToHash(Vector256<uint> vec)
    {
        // Extract 8 uint32 values and XOR them together
        uint hash = vec.GetElement(0);
        for (int i = 1; i < 8; i++)
        {
            hash ^= vec.GetElement(i);
        }
        return hash;
    }

    /// <summary>
    /// Reduces a Vector128 hash to a single uint32 value.
    /// Helper method for hash operations.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint ReduceVector128ToHash(Vector128<uint> vec)
    {
        // Extract 4 uint32 values and XOR them together
        uint hash = vec.GetElement(0);
        for (int i = 1; i < 4; i++)
        {
            hash ^= vec.GetElement(i);
        }
        return hash;
    }
}
