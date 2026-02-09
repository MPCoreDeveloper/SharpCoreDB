// <copyright file="VectorSearchOptions.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.VectorSearch;

/// <summary>
/// Configuration options for vector search behavior.
/// Configurable at DI registration time via <c>AddVectorSupport(options => ...)</c>.
/// </summary>
public sealed class VectorSearchOptions
{
    /// <summary>
    /// Maximum allowed vector dimensions. Rejects vectors larger than this.
    /// Default: 4096. Set lower for embedded/mobile to prevent accidental memory abuse.
    /// </summary>
    public int MaxDimensions { get; set; } = 4096;

    /// <summary>
    /// Default HNSW M parameter (max bidirectional connections per layer).
    /// Higher values improve recall but increase memory and build time.
    /// Recommended: 12-48. Default: 16.
    /// </summary>
    public int DefaultM { get; set; } = 16;

    /// <summary>
    /// Default HNSW efConstruction parameter (build-time search width).
    /// Higher values produce better index quality at the cost of slower builds.
    /// Recommended: 100-400. Default: 200.
    /// </summary>
    public int DefaultEfConstruction { get; set; } = 200;

    /// <summary>
    /// Default HNSW efSearch parameter (query-time search width).
    /// Higher values improve recall at the cost of query latency.
    /// Recommended: 20-500. Default: 50.
    /// </summary>
    public int DefaultEfSearch { get; set; } = 50;

    /// <summary>
    /// Hard memory limit in MB for all vector indexes combined.
    /// 0 = unlimited (server mode). Default: 256 MB.
    /// </summary>
    public int MaxMemoryMB { get; set; } = 256;

    /// <summary>
    /// Load HNSW index into memory only on first vector query.
    /// Saves startup memory when vector queries are infrequent.
    /// Default: true.
    /// </summary>
    public bool LazyIndexLoading { get; set; } = true;

    /// <summary>
    /// Release index memory when GC detects memory pressure.
    /// Index is rebuilt on next query (requires LazyIndexLoading = true).
    /// Default: false.
    /// </summary>
    public bool EvictIndexOnMemoryPressure { get; set; }

    /// <summary>Preset for embedded/mobile devices with limited memory.</summary>
    public static VectorSearchOptions Embedded => new()
    {
        MaxMemoryMB = 50,
        LazyIndexLoading = true,
        EvictIndexOnMemoryPressure = true,
        MaxDimensions = 1536,
    };

    /// <summary>Preset for standard desktop/web applications.</summary>
    public static VectorSearchOptions Standard => new()
    {
        MaxMemoryMB = 512,
        LazyIndexLoading = true,
        EvictIndexOnMemoryPressure = false,
        MaxDimensions = 4096,
    };

    /// <summary>Preset for enterprise server deployments with ample memory.</summary>
    public static VectorSearchOptions Enterprise => new()
    {
        MaxMemoryMB = 0,
        LazyIndexLoading = false,
        EvictIndexOnMemoryPressure = false,
        MaxDimensions = 4096,
    };
}
