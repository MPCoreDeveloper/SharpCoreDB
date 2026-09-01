// src\SharpCoreDB\Compression\OptionalCompressionLevel.cs
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace SharpCoreDB.Compression;

/// <summary>
/// Compression level presets for Brotli and GZip streaming compression.
/// Maps to System.IO.Compression.CompressionLevel with trade-offs between CPU cost and compression ratio.
/// </summary>
public enum OptionalCompressionLevel
{
    /// <summary>
    /// Balanced preset: good compression ratio with reasonable CPU cost.
    /// Recommended for data blocks where storage efficiency matters.
    /// Maps to CompressionLevel.Optimal.
    /// </summary>
    Optimal = 0,

    /// <summary>
    /// Fastest preset: minimal CPU cost, larger output size.
    /// Recommended for metadata where write speed is critical and data is small.
    /// Maps to CompressionLevel.Fastest.
    /// </summary>
    Fastest = 1,

    /// <summary>
    /// Best compression preset: maximum ratio, highest CPU cost.
    /// Recommended for offline archival or cold storage workloads.
    /// Maps to CompressionLevel.SmallestSize (.NET 10+).
    /// </summary>
    SmallestSize = 3
}
