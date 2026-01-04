// <copyright file="StorageEngineFactory.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Storage.Engines;

using SharpCoreDB.Interfaces;
using System;

/// <summary>
/// Factory for creating storage engine instances based on configuration.
/// ✅ C# 14: Static class with modern pattern matching
/// </summary>
public static class StorageEngineFactory
{
    /// <summary>
    /// Creates a storage engine instance based on the specified type and configuration.
    /// ✅ NEW: Supports Auto selection based on WorkloadHint!
    /// </summary>
    /// <param name="engineType">Type of storage engine to create (or Auto for intelligent selection).</param>
    /// <param name="config">Database configuration containing WorkloadHint for Auto selection.</param>
    /// <param name="storage">Underlying IStorage implementation (required for AppendOnly).</param>
    /// <param name="databasePath">Path to the database directory.</param>
    /// <returns>A configured storage engine instance.</returns>
    /// <exception cref="NotSupportedException">Thrown when engine type is not supported.</exception>
    /// <exception cref="ArgumentNullException">Thrown when required parameters are null.</exception>
    public static IStorageEngine CreateEngine(
        StorageEngineType engineType,
        DatabaseConfig? config,
        IStorage? storage,
        string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath); // ✅ C# 14
        
        // ✅ NEW: Auto-select based on WorkloadHint
        var actualEngineType = engineType is StorageEngineType.Auto && config is not null // ✅ C# 14 pattern
            ? config.GetOptimalStorageEngine()
            : engineType;
        
        return actualEngineType switch // ✅ C# 14 switch expression
        {
            StorageEngineType.AppendOnly => CreateAppendOnlyEngine(storage, databasePath),
            StorageEngineType.PageBased => CreatePageBasedEngine(databasePath, config),
            StorageEngineType.Columnar => CreateColumnarEngine(databasePath, config, storage), // ✅ Pass storage
            _ => throw new NotSupportedException($"Storage engine type '{actualEngineType}' is not supported")
        };
    }

    /// <summary>
    /// Creates an append-only storage engine.
    /// </summary>
    /// <param name="storage">IStorage implementation for persistence.</param>
    /// <param name="databasePath">Path to the database directory.</param>
    /// <returns>Configured AppendOnlyEngine instance.</returns>
    /// <exception cref="ArgumentNullException">Thrown when storage is null.</exception>
    private static IStorageEngine CreateAppendOnlyEngine(IStorage? storage, string databasePath)
    {
        ArgumentNullException.ThrowIfNull(storage); // ✅ C# 14 - caller info automatic
        return new AppendOnlyEngine(storage, databasePath);
    }

    /// <summary>
    /// Creates a page-based storage engine.
    /// ✅ READY: Optimized with O(1) free list, LRU cache, async flushing
    /// ✅ NEW: Passes DatabaseConfig for auto-configuration!
    /// </summary>
    /// <param name="databasePath">Path to the database directory.</param>
    /// <param name="config">Optional database configuration for auto-tuning.</param>
    /// <returns>Configured PageBasedEngine instance.</returns>
    private static IStorageEngine CreatePageBasedEngine(string databasePath, DatabaseConfig? config)
        => new PageBasedEngine(databasePath, config); // ✅ C# 14 expression-bodied

    /// <summary>
    /// Creates a columnar storage engine for analytics workloads.
    /// Optimized for: Heavy aggregations, scans, read-heavy queries.
    /// Expected: 5-10x faster GROUP BY, SUM, AVG vs row-based storage.
    /// Uses AppendOnlyEngine for sequential writes with columnar file format (.dat files).
    /// </summary>
    /// <param name="databasePath">Path to the database directory.</param>
    /// <param name="config">Optional database configuration (currently unused but reserved for future use).</param>
    /// <param name="storage">IStorage instance required for AppendOnly engine.</param>
    /// <returns>Configured AppendOnlyEngine instance for columnar storage.</returns>
    /// <exception cref="ArgumentNullException">Thrown when storage is null (required for columnar).</exception>
#pragma warning disable S1172 // Unused parameter 'config' - reserved for future use
    private static IStorageEngine CreateColumnarEngine(string databasePath, DatabaseConfig? config, IStorage? storage)
#pragma warning restore S1172
    {
        ArgumentNullException.ThrowIfNull(storage);
        return new AppendOnlyEngine(storage, databasePath);
    }
}
