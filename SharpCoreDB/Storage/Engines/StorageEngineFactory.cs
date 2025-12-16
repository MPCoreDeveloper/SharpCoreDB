// <copyright file="StorageEngineFactory.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Storage.Engines;

using SharpCoreDB.Interfaces;
using System;

/// <summary>
/// Factory for creating storage engine instances based on configuration.
/// </summary>
public static class StorageEngineFactory
{
    /// <summary>
    /// Creates a storage engine instance based on the specified type.
    /// </summary>
    /// <param name="engineType">Type of storage engine to create.</param>
    /// <param name="storage">Underlying IStorage implementation (required for AppendOnly and Hybrid).</param>
    /// <param name="databasePath">Path to the database directory.</param>
    /// <returns>A configured storage engine instance.</returns>
    public static IStorageEngine CreateEngine(
        StorageEngineType engineType,
        IStorage? storage,
        string databasePath)
    {
        return engineType switch
        {
            StorageEngineType.AppendOnly => CreateAppendOnlyEngine(storage, databasePath),
            StorageEngineType.PageBased => CreatePageBasedEngine(databasePath),
            StorageEngineType.Hybrid => CreateHybridEngine(storage, databasePath),
            _ => throw new NotSupportedException($"Storage engine type '{engineType}' is not supported")
        };
    }

    /// <summary>
    /// Creates an append-only storage engine.
    /// </summary>
    private static IStorageEngine CreateAppendOnlyEngine(IStorage? storage, string databasePath)
    {
        if (storage == null)
        {
            throw new ArgumentNullException(nameof(storage), 
                "IStorage instance is required for AppendOnly engine");
        }

        return new AppendOnlyEngine(storage, databasePath);
    }

    /// <summary>
    /// Creates a page-based storage engine.
    /// </summary>
    private static IStorageEngine CreatePageBasedEngine(string databasePath)
    {
        return new PageBasedEngine(databasePath);
    }

    /// <summary>
    /// Creates a hybrid storage engine combining WAL and page-based storage.
    /// </summary>
    private static IStorageEngine CreateHybridEngine(IStorage? storage, string databasePath)
    {
        if (storage == null)
        {
            throw new ArgumentNullException(nameof(storage), 
                "IStorage instance is required for Hybrid engine");
        }

        return new HybridEngine(storage, databasePath);
    }
}
