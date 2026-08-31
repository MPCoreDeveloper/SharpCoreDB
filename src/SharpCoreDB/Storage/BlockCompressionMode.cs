// <copyright file="BlockCompressionMode.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Storage;

/// <summary>
/// Compression mode for block data in SingleFile storage.
/// </summary>
public enum BlockCompressionMode
{
    /// <summary>
    /// No compression (default, backward compatible).
    /// </summary>
    None = 0,

    /// <summary>
    /// Brotli compression (best ratio at high levels, expensive CPU at SmallestSize).
    /// Best for: archival, cold storage, read-heavy workloads.
    /// </summary>
    Brotli = 1,

    /// <summary>
    /// GZip compression (fast, decent ratio).
    /// Best for: high-frequency writes, individual inserts.
    /// Note: GZip is often faster than no compression due to I/O savings.
    /// </summary>
    GZip = 2,

    /// <summary>
    /// Zstandard compression (excellent speed/ratio balance).
    /// Best for: general-purpose database blocks, telemetry, mixed workloads.
    /// Requires .NET 11+ (System.IO.Compression.ZstandardStream).
    /// On .NET 10, using this mode will throw NotSupportedException.
    /// </summary>
    Zstd = 3
}
