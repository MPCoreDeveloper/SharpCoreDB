// <copyright file="StorageFormatSwitcher.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Storage.Scdb;

using System;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Enables runtime switching between storage formats.
/// Maintains data consistency during format transitions.
/// C# 14: Uses Lock type and modern async patterns.
/// </summary>
/// <remarks>
/// ✅ SCDB Phase 4: Cross-format compatibility layer.
/// 
/// Use cases:
/// - Development: Directory format for debugging, SCDB for production
/// - Migration: Hot-switch during rolling upgrades
/// - Testing: Compare performance between formats
/// </remarks>
public sealed class StorageFormatSwitcher : IDisposable
{
    private IStorageProvider _currentProvider;
    private readonly Lock _switchLock = new();
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="StorageFormatSwitcher"/> class.
    /// </summary>
    /// <param name="initialProvider">The initial storage provider.</param>
    public StorageFormatSwitcher(IStorageProvider initialProvider)
    {
        ArgumentNullException.ThrowIfNull(initialProvider);
        _currentProvider = initialProvider;
    }

    /// <summary>
    /// Gets the current storage provider.
    /// </summary>
    public IStorageProvider CurrentProvider
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _currentProvider;
        }
    }

    /// <summary>
    /// Gets the current storage mode.
    /// </summary>
    public StorageMode CurrentMode => _currentProvider.Mode;

    /// <summary>
    /// Switches to SCDB single-file format, migrating data if needed.
    /// </summary>
    /// <param name="scdbPath">Path for the SCDB file.</param>
    /// <param name="options">Database options.</param>
    /// <param name="progress">Optional progress reporter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Migration result.</returns>
    public async Task<MigrationResult> SwitchToScdbAsync(
        string scdbPath,
        DatabaseOptions? options = null,
        IProgress<MigrationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        
        lock (_switchLock)
        {
            if (_currentProvider.Mode == StorageMode.SingleFile)
            {
                return new MigrationResult
                {
                    Success = true,
                    ErrorMessage = "Already using SingleFile format",
                };
            }
        }
        
        // Migrate from Directory to SCDB
        using var migrator = new ScdbMigrator(
            _currentProvider.RootPath,
            scdbPath,
            options);
        
        var result = await migrator.MigrateAsync(
            new MigrationOptions
            {
                CreateBackup = true,
                ValidateAfterMigration = true,
                Progress = progress,
            },
            cancellationToken);
        
        if (result.Success)
        {
            lock (_switchLock)
            {
                var oldProvider = _currentProvider;
                
                // Open new SCDB provider
                _currentProvider = SingleFileStorageProvider.Open(scdbPath, options ?? new DatabaseOptions
                {
                    StorageMode = StorageMode.SingleFile
                });
                
                // Dispose old provider
                oldProvider.Dispose();
            }
        }
        
        return result;
    }

    /// <summary>
    /// Switches to Directory format, exporting data if needed.
    /// </summary>
    /// <param name="directoryPath">Path for the directory.</param>
    /// <param name="options">Database options.</param>
    /// <param name="progress">Optional progress reporter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Export result.</returns>
    public async Task<MigrationResult> SwitchToDirectoryAsync(
        string directoryPath,
        DatabaseOptions? options = null,
        IProgress<MigrationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        
        lock (_switchLock)
        {
            if (_currentProvider.Mode == StorageMode.Directory)
            {
                return new MigrationResult
                {
                    Success = true,
                    ErrorMessage = "Already using Directory format",
                };
            }
        }
        
        // Export from SCDB to Directory
        var dirOptions = options ?? new DatabaseOptions { StorageMode = StorageMode.Directory };
        var targetProvider = DirectoryStorageProvider.Open(directoryPath, dirOptions);
        
        try
        {
            var blockCount = 0;
            long byteCount = 0;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            
            foreach (var blockName in _currentProvider.EnumerateBlocks())
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                var data = await _currentProvider.ReadBlockAsync(blockName, cancellationToken);
                if (data != null)
                {
                    await targetProvider.WriteBlockAsync(blockName, data, cancellationToken);
                    blockCount++;
                    byteCount += data.Length;
                    
                    progress?.Report(new MigrationProgress($"Exported {blockCount} blocks", blockCount, 0));
                }
            }
            
            await targetProvider.FlushAsync(cancellationToken);
            sw.Stop();
            
            lock (_switchLock)
            {
                var oldProvider = _currentProvider;
                _currentProvider = targetProvider;
                oldProvider.Dispose();
            }
            
            return new MigrationResult
            {
                Success = true,
                BlocksMigrated = blockCount,
                BytesMigrated = byteCount,
                Duration = sw.Elapsed,
            };
        }
        catch (Exception ex)
        {
            targetProvider.Dispose();
            return new MigrationResult
            {
                Success = false,
                ErrorMessage = ex.Message,
            };
        }
    }

    /// <summary>
    /// Creates a new instance with the optimal format for the given use case.
    /// </summary>
    /// <param name="path">Database path.</param>
    /// <param name="useCase">The intended use case.</param>
    /// <param name="options">Database options.</param>
    /// <returns>Configured storage switcher.</returns>
    public static StorageFormatSwitcher CreateForUseCase(
        string path,
        StorageUseCase useCase,
        DatabaseOptions? options = null)
    {
        var opts = options ?? new DatabaseOptions();
        
        IStorageProvider provider = useCase switch
        {
            StorageUseCase.Development => DirectoryStorageProvider.Open(path, opts),
            StorageUseCase.Production => SingleFileStorageProvider.Open(path + ".scdb", new DatabaseOptions 
            { 
                StorageMode = StorageMode.SingleFile, 
                CreateImmediately = true,
                PageSize = opts.PageSize > 0 ? opts.PageSize : 4096,
            }),
            StorageUseCase.Testing => DirectoryStorageProvider.Open(path, opts),
            StorageUseCase.HighPerformance => SingleFileStorageProvider.Open(path + ".scdb", new DatabaseOptions 
            { 
                StorageMode = StorageMode.SingleFile, 
                CreateImmediately = true,
                PageSize = opts.PageSize > 0 ? opts.PageSize : 4096,
            }),
            _ => DirectoryStorageProvider.Open(path, opts)
        };
        
        return new StorageFormatSwitcher(provider);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        
        lock (_switchLock)
        {
            _currentProvider.Dispose();
            _disposed = true;
        }
    }
}

/// <summary>
/// Storage use cases for automatic format selection.
/// </summary>
public enum StorageUseCase
{
    /// <summary>Development/debugging - uses Directory format for easy inspection.</summary>
    Development,
    
    /// <summary>Production - uses SCDB format for optimal performance.</summary>
    Production,
    
    /// <summary>Testing - uses Directory format for isolation.</summary>
    Testing,
    
    /// <summary>High performance - uses SCDB with optimizations.</summary>
    HighPerformance,
}
