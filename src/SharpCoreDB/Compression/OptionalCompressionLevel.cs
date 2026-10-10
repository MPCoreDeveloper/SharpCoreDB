// src\SharpCoreDB\Compression\OptionalCompressionLevel.cs
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

namespace SharpCoreDB.Compression;

/// <summary>
/// Compression level presets for streaming compression. Maps to
/// <see cref="System.IO.Compression.CompressionLevel"/> for Brotli/GZip, and on net11.0 to the
/// native Zstandard quality scale (see <c>BlockCompressor.ZstdOptions</c>) for Zstd.
/// </summary>
public enum OptionalCompressionLevel
{
    /// <summary>
    /// Balanced preset: good compression ratio with reasonable CPU cost.
    /// Recommended for data blocks where storage efficiency matters.
    /// Maps to CompressionLevel.Optimal (Zstd quality 5).
    /// </summary>
    Optimal = 0,

    /// <summary>
    /// Fastest preset: minimal CPU cost, larger output size.
    /// Recommended for metadata where write speed is critical and data is small.
    /// Maps to CompressionLevel.Fastest (Zstd quality 1).
    /// </summary>
    Fastest = 1,

    /// <summary>
    /// Best compression preset: maximum ratio, highest CPU cost.
    /// Recommended for offline archival or cold storage workloads.
    /// Maps to CompressionLevel.SmallestSize (.NET 10+); Zstd quality 19 on net11.0.
    /// </summary>
    SmallestSize = 3
}
