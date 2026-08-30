// <copyright file="BlockCompressionMode.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Storage;

/// <summary>
/// Compression algorithm for SingleFile block data.
/// </summary>
public enum BlockCompressionMode
{
    /// <summary>No compression. Default for backward compatibility.</summary>
    None = 0,
    /// <summary>Brotli compression. Best ratio for text/JSON payloads.</summary>
    Brotli = 1,
    /// <summary>GZip compression. Faster decompression, slightly larger.</summary>
    GZip = 2
}