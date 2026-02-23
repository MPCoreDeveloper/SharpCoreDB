// <copyright file="PoolConfiguration.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Distributed.Replication;

/// <summary>
/// Configuration for buffer pool settings.
/// </summary>
public class PoolConfiguration
{
    /// <summary>Gets or sets the default buffer size in bytes.</summary>
    public int DefaultBufferSize { get; set; } = 64 * 1024; // 64KB

    /// <summary>Gets or sets the maximum buffer size in bytes.</summary>
    public int MaxBufferSize { get; set; } = 1024 * 1024; // 1MB

    /// <summary>Gets or sets the maximum number of buffers in the pool.</summary>
    public int MaxBuffers { get; set; } = 1000;

    /// <summary>Gets or sets the initial number of buffers to pre-allocate.</summary>
    public int InitialBuffers { get; set; } = 10;

    /// <summary>Gets or sets the buffer growth factor when expanding.</summary>
    public double GrowthFactor { get; set; } = 1.5;

    /// <summary>Gets or sets whether to enable buffer validation.</summary>
    public bool EnableValidation { get; set; } = true;

    /// <summary>Gets or sets the maximum time a buffer can be rented before warning.</summary>
    public TimeSpan MaxRentalTime { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Creates a high-performance configuration optimized for WAL streaming.
    /// </summary>
    /// <returns>A high-performance configuration.</returns>
    public static PoolConfiguration CreateHighPerformance()
    {
        return new PoolConfiguration
        {
            DefaultBufferSize = 128 * 1024, // 128KB
            MaxBufferSize = 2 * 1024 * 1024, // 2MB
            MaxBuffers = 2000,
            InitialBuffers = 50,
            GrowthFactor = 2.0,
            EnableValidation = false, // Skip validation for max performance
            MaxRentalTime = TimeSpan.FromMinutes(1)
        };
    }

    /// <summary>
    /// Creates a memory-conservative configuration.
    /// </summary>
    /// <returns>A memory-conservative configuration.</returns>
    public static PoolConfiguration CreateConservative()
    {
        return new PoolConfiguration
        {
            DefaultBufferSize = 32 * 1024, // 32KB
            MaxBufferSize = 512 * 1024, // 512KB
            MaxBuffers = 500,
            InitialBuffers = 5,
            GrowthFactor = 1.2,
            EnableValidation = true,
            MaxRentalTime = TimeSpan.FromMinutes(10)
        };
    }
}
