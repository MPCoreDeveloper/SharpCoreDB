// <copyright file="StorageStrategy.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Storage.Overflow;

using System;

/// <summary>
/// Determines optimal storage strategy for row data based on size.
/// C# 14: Modern static class with expression-bodied members.
/// </summary>
/// <remarks>
/// ✅ SCDB Phase 6: 3-tier storage strategy.
/// 
/// Tiers:
/// - Inline (0-4KB): Store directly in data page
/// - Overflow (4KB-256KB): Page chain within database
/// - FileStream (256KB+): External file with pointer
/// 
/// Thresholds are configurable via StorageOptions.
/// </remarks>
public static class StorageStrategy
{
    /// <summary>Default inline threshold (4KB).</summary>
    public const int DefaultInlineThreshold = 4096;
    
    /// <summary>Default overflow threshold (256KB).</summary>
    public const int DefaultOverflowThreshold = 262144;
    
    /// <summary>
    /// Determines the optimal storage mode for data of given size.
    /// </summary>
    /// <param name="dataSize">Size of data in bytes.</param>
    /// <param name="inlineThreshold">Max size for inline storage.</param>
    /// <param name="overflowThreshold">Max size for overflow storage.</param>
    /// <returns>The optimal storage mode.</returns>
    public static StorageMode DetermineMode(
        int dataSize,
        int inlineThreshold = DefaultInlineThreshold,
        int overflowThreshold = DefaultOverflowThreshold)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(dataSize);
        
        if (dataSize <= inlineThreshold)
            return StorageMode.Inline;
        
        if (dataSize <= overflowThreshold)
            return StorageMode.Overflow;
        
        return StorageMode.FileStream;
    }
    
    /// <summary>
    /// Determines the optimal storage mode using options.
    /// </summary>
    /// <param name="dataSize">Size of data in bytes.</param>
    /// <param name="options">Storage options.</param>
    /// <returns>The optimal storage mode.</returns>
    public static StorageMode DetermineMode(int dataSize, StorageOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return DetermineMode(dataSize, options.InlineThreshold, options.OverflowThreshold);
    }
    
    /// <summary>
    /// Calculates the number of overflow pages needed for data.
    /// </summary>
    /// <param name="dataSize">Total data size in bytes.</param>
    /// <param name="pageSize">Page size in bytes.</param>
    /// <returns>Number of pages required.</returns>
    public static int CalculateOverflowPages(int dataSize, int pageSize = 4096)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageSize);
        
        if (dataSize <= 0)
            return 0;
        
        // Account for page header (32 bytes)
        var usableSpace = pageSize - OverflowPageHeader.HEADER_SIZE;
        
        return (int)Math.Ceiling((double)dataSize / usableSpace);
    }
    
    /// <summary>
    /// Gets a human-readable description of the storage mode.
    /// </summary>
    /// <param name="mode">Storage mode.</param>
    /// <returns>Description string.</returns>
    public static string GetDescription(StorageMode mode) => mode switch
    {
        StorageMode.Inline => "Stored inline in data page (fastest)",
        StorageMode.Overflow => "Stored in overflow page chain (medium)",
        StorageMode.FileStream => "Stored in external file (unlimited size)",
        _ => "Unknown storage mode",
    };
}

/// <summary>
/// Configuration options for row storage strategy.
/// </summary>
public sealed record StorageOptions
{
    /// <summary>Gets or sets the maximum size for inline storage (default 4KB).</summary>
    public int InlineThreshold { get; init; } = StorageStrategy.DefaultInlineThreshold;
    
    /// <summary>Gets or sets the maximum size for overflow storage (default 256KB).</summary>
    public int OverflowThreshold { get; init; } = StorageStrategy.DefaultOverflowThreshold;
    
    /// <summary>Gets or sets whether FILESTREAM is enabled (default true).</summary>
    public bool EnableFileStream { get; init; } = true;
    
    /// <summary>Gets or sets the relative path for blob storage (default "blobs").</summary>
    public string FileStreamPath { get; init; } = "blobs";
    
    /// <summary>Gets or sets the relative path for temp files (default "temp").</summary>
    public string TempPath { get; init; } = "temp";
    
    /// <summary>Gets or sets whether orphan detection is enabled (default true).</summary>
    public bool EnableOrphanDetection { get; init; } = true;
    
    /// <summary>Gets or sets the retention period for orphaned files (default 7 days).</summary>
    public TimeSpan OrphanRetentionPeriod { get; init; } = TimeSpan.FromDays(7);
    
    /// <summary>Gets or sets the interval for orphan scans in hours (default 24).</summary>
    public int OrphanScanIntervalHours { get; init; } = 24;
    
    /// <summary>Gets or sets the policy for missing files (default AlertOnly).</summary>
    public MissingFilePolicy MissingFilePolicy { get; init; } = MissingFilePolicy.AlertOnly;
    
    /// <summary>Gets or sets the backup path for recovery (default null).</summary>
    public string? BackupPath { get; init; }
    
    /// <summary>Gets or sets whether to auto-recover from backup (default false).</summary>
    public bool AutoRecoverFromBackup { get; init; }
    
    /// <summary>Default storage options.</summary>
    public static StorageOptions Default { get; } = new();
}
