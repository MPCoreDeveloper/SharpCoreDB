// <copyright file="QuantizationType.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.VectorSearch;

/// <summary>
/// Supported vector quantization methods for memory reduction.
/// </summary>
public enum QuantizationType
{
    /// <summary>No quantization — full float32 precision. Baseline for accuracy.</summary>
    None,

    /// <summary>
    /// Scalar quantization: float32 → uint8. 4× memory reduction.
    /// Each dimension is linearly mapped to [0, 255] based on calibrated min/max.
    /// Typical accuracy loss: &lt; 1% for most embeddings.
    /// </summary>
    Scalar8,

    /// <summary>
    /// Binary quantization: float32 → 1 bit. 32× memory reduction.
    /// Each dimension becomes 1 (positive) or 0 (negative/zero).
    /// Best for: pre-filtering with Hamming distance, then re-ranking with full precision.
    /// </summary>
    Binary,
}
