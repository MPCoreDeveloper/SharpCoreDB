// <copyright file="SimdHelper.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Services;

/// <summary>
/// SIMD acceleration utilities using System.Runtime.Intrinsics.
/// Provides Vector128/Vector256 operations with automatic hardware detection and scalar fallbacks.
/// 
/// REFACTORED TO PARTIAL CLASSES FOR MAINTAINABILITY:
/// - SimdHelper.Core.cs: Platform detection and public API
/// - SimdHelper.Operations.cs: SIMD-accelerated operations (hash, compare, zero, indexOf)
/// - SimdHelper.Fallback.cs: Scalar fallback implementations and helpers
/// - SimdHelper.cs (this file): Main documentation and class declaration
/// 
/// USAGE:
/// - ComputeHashCode(): SIMD-accelerated FNV-1a hashing
/// - SequenceEqual(): SIMD-accelerated byte comparison
/// - ZeroBuffer(): Fast buffer clearing (faster than Array.Clear)
/// - IndexOf(): SIMD-accelerated pattern search
/// 
/// PLATFORM SUPPORT:
/// - x86/x64: AVX2 (256-bit), SSE2 (128-bit)
/// - ARM: NEON (128-bit)
/// - Fallback: Scalar implementations for all platforms
/// </summary>
public static partial class SimdHelper
{
    // This file intentionally left minimal.
    // All functionality is implemented in partial class files:
    // - SimdHelper.Core.cs
    // - SimdHelper.Operations.cs
    // - SimdHelper.Fallback.cs
}
