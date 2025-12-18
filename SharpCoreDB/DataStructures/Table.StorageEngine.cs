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
    /// </summary>
    private IStorageEngine CreateColumnarEngine(string databasePath)
    {
        if (storage == null)
        {
            throw new InvalidOperationException(
                $"Cannot create columnar storage engine for table '{Name}': IStorage instance is null. " +
                "Ensure SetStorage() is called before any data operations.");
        }

        return StorageEngineFactory.CreateEngine(
            StorageEngineType.AppendOnly,
            storage,
            databasePath);
    }

    /// <summary>
    /// Creates a PageBasedEngine for page-based storage.
    /// Self-contained, does not require IStorage (manages its own .pages files).
    /// </summary>
    private IStorageEngine CreatePageBasedEngine(string databasePath)
    {
        // PageBasedEngine is self-contained and doesn't need IStorage
        return StorageEngineFactory.CreateEngine(
            StorageEngineType.PageBased,
            storage: null,
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
    public StorageEngineType? GetStorageEngineType()
    {
        return _storageEngine?.EngineType;
    }

    /// <summary>
    /// Gets storage engine performance metrics (for monitoring/diagnostics).
    /// Returns null if engine not yet initialized.
    /// </summary>
    public StorageEngineMetrics? GetStorageEngineMetrics()
    {
        return _storageEngine?.GetMetrics();
    }

    /// <summary>
    /// Disposes the storage engine (called from Table.Dispose).
    /// </summary>
    private void DisposeStorageEngine()
    {
        if (_storageEngine != null)
        {
            _storageEngine.Dispose();
            _storageEngine = null;
        }
    }
}
