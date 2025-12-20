// <copyright file="Table.StorageEngine.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.DataStructures;

using SharpCoreDB.Interfaces;
using SharpCoreDB.Storage.Engines;
using SharpCoreDB.Storage.Hybrid;
using System;
using System.IO;

/// <summary>
/// Storage engine routing methods for Table.
/// Handles initialization and routing between AppendOnlyEngine (columnar) and PageBasedEngine.
/// </summary>
public partial class Table
{
    /// <summary>
    /// Gets or creates the storage engine for this table based on StorageMode.
    /// Thread-safe lazy initialization with double-checked locking.
    /// </summary>
    /// <returns>The active storage engine instance.</returns>
    /// <exception cref="InvalidOperationException">Thrown if storage mode is not supported or storage is null.</exception>
    private IStorageEngine GetOrCreateStorageEngine()
    {
        // Fast path: engine already initialized
        if (_storageEngine != null)
            return _storageEngine;

        // Slow path: initialize engine (thread-safe)
        lock (_engineLock)
        {
            // Double-check after acquiring lock
            if (_storageEngine != null)
                return _storageEngine;

            // Validate prerequisites
            if (string.IsNullOrEmpty(DataFile))
            {
                throw new InvalidOperationException(
                    $"Cannot initialize storage engine for table '{Name}': DataFile is not set");
            }

            var databasePath = Path.GetDirectoryName(DataFile);
            if (string.IsNullOrEmpty(databasePath))
            {
                throw new InvalidOperationException(
                    $"Cannot determine database path from DataFile: {DataFile}");
            }

            // Create engine based on StorageMode
            _storageEngine = StorageMode switch
            {
                StorageMode.Columnar => CreateColumnarEngine(databasePath),
                StorageMode.PageBased => CreatePageBasedEngine(databasePath),
                StorageMode.Hybrid => throw new NotImplementedException(
                    "Hybrid storage mode is not yet implemented. Use COLUMNAR or PAGE_BASED."),
                _ => throw new NotSupportedException(
                    $"Storage mode '{StorageMode}' is not supported")
            };

            return _storageEngine;
        }
    }

    /// <summary>
    /// Creates an AppendOnlyEngine for columnar storage.
    /// Requires IStorage instance for encryption and WAL support.
    /// Note: DatabaseConfig is not currently passed to Table - will be added in future enhancement.
    /// </summary>
    /// <param name="databasePath">Path to the database directory.</param>
    /// <returns>Configured AppendOnlyEngine instance.</returns>
    /// <exception cref="InvalidOperationException">Thrown when storage is null.</exception>
    private IStorageEngine CreateColumnarEngine(string databasePath)
    {
        if (storage is null) // ✅ C# 14 pattern
        {
            throw new InvalidOperationException(
                $"Cannot create columnar storage engine for table '{Name}': IStorage instance is null. " +
                "Ensure SetStorage() is called before any data operations.");
        }

        return StorageEngineFactory.CreateEngine(
            StorageEngineType.AppendOnly,
            config: _config, // ✅ Pass config through chain
            storage,
            databasePath);
    }

    /// <summary>
    /// Creates a PageBasedEngine for page-based storage.
    /// Self-contained, does not require IStorage (manages its own .pages files).
    /// Note: DatabaseConfig is not currently passed to Table - will be added in future enhancement.
    /// </summary>
    /// <param name="databasePath">Path to the database directory.</param>
    /// <returns>Configured PageBasedEngine instance.</returns>
    private IStorageEngine CreatePageBasedEngine(string databasePath)
    {
        return StorageEngineFactory.CreateEngine(
            StorageEngineType.PageBased,
            config: _config, // ✅ Pass config through chain
            storage: null, // PageBased doesn't need IStorage
            databasePath);
    }

    /// <summary>
    /// Initializes the storage engine explicitly.
    /// Call this after table creation to ensure engine is ready before first operation.
    /// </summary>
    /// <remarks>
    /// This is useful for:
    /// - Pre-warming the engine during table creation
    /// - Validating configuration before data operations
    /// - Explicit control over initialization timing
    /// </remarks>
    public void InitializeStorageEngine()
    {
        _ = GetOrCreateStorageEngine();
    }

    /// <summary>
    /// Gets the current storage engine type (for diagnostics/testing).
    /// Returns null if engine not yet initialized.
    /// </summary>
    /// <returns>The storage engine type or null if not initialized.</returns>
    public StorageEngineType? GetStorageEngineType()
        => _storageEngine?.EngineType; // ✅ C# 14 expression-bodied

    /// <summary>
    /// Gets storage engine performance metrics (for monitoring/diagnostics).
    /// Returns null if engine not yet initialized.
    /// </summary>
    /// <returns>Performance metrics or null if not initialized.</returns>
    public StorageEngineMetrics? GetStorageEngineMetrics()
        => _storageEngine?.GetMetrics(); // ✅ C# 14 expression-bodied

    /// <summary>
    /// Disposes the storage engine (called from Table.Dispose).
    /// </summary>
    private void DisposeStorageEngine()
    {
        _storageEngine?.Dispose(); // ✅ C# 14 null-conditional
        _storageEngine = null;
    }
}
