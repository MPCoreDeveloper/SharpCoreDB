// <copyright file="DatabaseMigrator.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Migration;

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SharpCoreDB.Storage;

/// <summary>
/// Provides bidirectional migration between directory-based and single-file (.scdb) storage formats.
/// Supports incremental migration with checkpoints for recovery from interruptions.
/// C# 14: Uses modern async patterns, file scoped namespaces, and primary constructors.
/// </summary>
public static class DatabaseMigrator
{
    /// <summary>
    /// Migrates a directory-based database to single-file (.scdb) format.
    /// </summary>
    /// <param name="sourceDirectoryPath">Source directory containing .dat and metadata files</param>
    /// <param name="targetScdbPath">Target .scdb file path</param>
    /// <param name="password">Master password for encryption/decryption</param>
    /// <param name="options">Optional database options for target file</param>
    /// <param name="progress">Progress callback (0.0 to 1.0)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Migration result with statistics</returns>
    public static async Task<MigrationResult> MigrateToSingleFileAsync(
        string sourceDirectoryPath,
        string targetScdbPath,
        string password,
        DatabaseOptions? options = null,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectoryPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetScdbPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        if (!Directory.Exists(sourceDirectoryPath))
        {
            throw new DirectoryNotFoundException($"Source directory not found: {sourceDirectoryPath}");
        }

        var sw = Stopwatch.StartNew();
        var result = new MigrationResult
        {
            SourcePath = sourceDirectoryPath,
            TargetPath = targetScdbPath,
            SourceFormat = StorageMode.Directory,
            TargetFormat = StorageMode.SingleFile,
            StartTime = DateTime.UtcNow
        };

        try
        {
            // Create checkpoint file for incremental migration
            var checkpointPath = targetScdbPath + ".checkpoint";
            var checkpoint = await LoadOrCreateCheckpoint(checkpointPath);

            // Setup target options
            options ??= DatabaseOptions.CreateSingleFileDefault();
            options.StorageMode = StorageMode.SingleFile;
            
            // Ensure .scdb extension
            if (!targetScdbPath.EndsWith(".scdb", StringComparison.OrdinalIgnoreCase))
            {
                targetScdbPath += ".scdb";
            }

            // Open source directory provider
            using var sourceProvider = DirectoryStorageProvider.Open(sourceDirectoryPath, options);
            
            // Create or resume target single-file provider
            using var targetProvider = SingleFileStorageProvider.Open(targetScdbPath, options);

            // Get all blocks from source
            var sourceBlocks = sourceProvider.EnumerateBlocks().ToList();
            result.TotalFiles = sourceBlocks.Count;

            // Migrate each block
            for (var i = 0; i < sourceBlocks.Count; i++)
            {
                var blockName = sourceBlocks[i];
                
                // Skip if already migrated (checkpoint recovery)
                if (checkpoint.MigratedBlocks.Contains(blockName))
                {
                    result.FilesMigrated++;
                    continue;
                }

                cancellationToken.ThrowIfCancellationRequested();

                // Read from source
                var blockData = await sourceProvider.ReadBlockAsync(blockName, cancellationToken);
                if (blockData == null)
                {
                    result.FilesSkipped++;
                    continue;
                }

                // Calculate checksum before migration
                var checksumBefore = SHA256.HashData(blockData);

                // Write to target
                await targetProvider.WriteBlockAsync(blockName, blockData, cancellationToken);

                // Verify checksum after migration
                var verifyData = await targetProvider.ReadBlockAsync(blockName, cancellationToken);
                var checksumAfter = SHA256.HashData(verifyData!);

                if (!checksumBefore.SequenceEqual(checksumAfter))
                {
                    throw new InvalidDataException($"Checksum mismatch for block '{blockName}'");
                }

                result.BytesMigrated += blockData.Length;
                result.FilesMigrated++;

                // Update checkpoint
                checkpoint.MigratedBlocks.Add(blockName);
                await SaveCheckpoint(checkpointPath, checkpoint);

                // Report progress
                progress?.Report((double)(i + 1) / sourceBlocks.Count);
            }

            // Flush target to ensure all data is written
            await targetProvider.FlushAsync(cancellationToken);

            // Run VACUUM to optimize the new file
            var vacuumResult = await targetProvider.VacuumAsync(VacuumMode.Full, cancellationToken);
            result.VacuumDuration = TimeSpan.FromMilliseconds(vacuumResult.DurationMs);

            // Delete checkpoint file on success
            if (File.Exists(checkpointPath))
            {
                File.Delete(checkpointPath);
            }

            result.Success = true;
            result.EndTime = DateTime.UtcNow;
            result.Duration = sw.Elapsed;

            return result;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
            result.EndTime = DateTime.UtcNow;
            result.Duration = sw.Elapsed;
            throw;
        }
    }

    /// <summary>
    /// Migrates a single-file (.scdb) database back to directory-based format.
    /// </summary>
    /// <param name="sourceScdbPath">Source .scdb file path</param>
    /// <param name="targetDirectoryPath">Target directory path</param>
    /// <param name="password">Master password for encryption/decryption</param>
    /// <param name="options">Optional database options</param>
    /// <param name="progress">Progress callback (0.0 to 1.0)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Migration result with statistics</returns>
    public static async Task<MigrationResult> MigrateToDirectoryAsync(
        string sourceScdbPath,
        string targetDirectoryPath,
        string password,
        DatabaseOptions? options = null,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceScdbPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDirectoryPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        if (!File.Exists(sourceScdbPath))
        {
            throw new FileNotFoundException($"Source file not found: {sourceScdbPath}");
        }

        var sw = Stopwatch.StartNew();
        var result = new MigrationResult
        {
            SourcePath = sourceScdbPath,
            TargetPath = targetDirectoryPath,
            SourceFormat = StorageMode.SingleFile,
            TargetFormat = StorageMode.Directory,
            StartTime = DateTime.UtcNow
        };

        try
        {
            // Create checkpoint file
            var checkpointPath = Path.Combine(targetDirectoryPath, ".migration.checkpoint");
            Directory.CreateDirectory(targetDirectoryPath);
            var checkpoint = await LoadOrCreateCheckpoint(checkpointPath);

            // Setup options
            options ??= DatabaseOptions.CreateDirectoryDefault();
            
            // Open source single-file provider
            var sourceOptions = new DatabaseOptions
            {
                StorageMode = StorageMode.SingleFile,
                PageSize = options.PageSize,
                EnableEncryption = options.EnableEncryption,
                EncryptionKey = options.EncryptionKey,
                EnableMemoryMapping = options.EnableMemoryMapping
            };
            using var sourceProvider = SingleFileStorageProvider.Open(sourceScdbPath, sourceOptions);

            // Open target directory provider
            using var targetProvider = DirectoryStorageProvider.Open(targetDirectoryPath, options);

            // Get all blocks from source
            var sourceBlocks = sourceProvider.EnumerateBlocks().ToList();
            result.TotalFiles = sourceBlocks.Count;

            // Migrate each block
            for (var i = 0; i < sourceBlocks.Count; i++)
            {
                var blockName = sourceBlocks[i];
                
                // Skip if already migrated
                if (checkpoint.MigratedBlocks.Contains(blockName))
                {
                    result.FilesMigrated++;
                    continue;
                }

                cancellationToken.ThrowIfCancellationRequested();

                // Read from source
                var blockData = await sourceProvider.ReadBlockAsync(blockName, cancellationToken);
                if (blockData == null)
                {
                    result.FilesSkipped++;
                    continue;
                }

                // Calculate checksum
                var checksumBefore = SHA256.HashData(blockData);

                // Write to target
                await targetProvider.WriteBlockAsync(blockName, blockData, cancellationToken);

                // Verify
                var verifyData = await targetProvider.ReadBlockAsync(blockName, cancellationToken);
                var checksumAfter = SHA256.HashData(verifyData!);

                if (!checksumBefore.SequenceEqual(checksumAfter))
                {
                    throw new InvalidDataException($"Checksum mismatch for block '{blockName}'");
                }

                result.BytesMigrated += blockData.Length;
                result.FilesMigrated++;

                // Update checkpoint
                checkpoint.MigratedBlocks.Add(blockName);
                await SaveCheckpoint(checkpointPath, checkpoint);

                // Report progress
                progress?.Report((double)(i + 1) / sourceBlocks.Count);
            }

            // Flush target
            await targetProvider.FlushAsync(cancellationToken);

            // Delete checkpoint on success
            if (File.Exists(checkpointPath))
            {
                File.Delete(checkpointPath);
            }

            result.Success = true;
            result.EndTime = DateTime.UtcNow;
            result.Duration = sw.Elapsed;

            return result;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
            result.EndTime = DateTime.UtcNow;
            result.Duration = sw.Elapsed;
            throw;
        }
    }

    /// <summary>
    /// Validates that two databases have identical content.
    /// </summary>
    /// <param name="path1">First database path</param>
    /// <param name="path2">Second database path</param>
    /// <param name="password">Master password</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Validation result</returns>
    public static async Task<ValidationResult> ValidateMigrationAsync(
        string path1,
        string path2,
        string password,
        CancellationToken cancellationToken = default)
    {
        var result = new ValidationResult { IsValid = true };

        try
        {
            // Detect storage modes
            var mode1 = DetectStorageMode(path1);
            var mode2 = DetectStorageMode(path2);

            // Open both providers
            using var provider1 = OpenProvider(path1, mode1, password);
            using var provider2 = OpenProvider(path2, mode2, password);

            // Get block lists
            var blocks1 = provider1.EnumerateBlocks().OrderBy(b => b).ToList();
            var blocks2 = provider2.EnumerateBlocks().OrderBy(b => b).ToList();

            // Compare block counts
            if (blocks1.Count != blocks2.Count)
            {
                result.IsValid = false;
                result.Differences.Add($"Block count mismatch: {blocks1.Count} vs {blocks2.Count}");
                return result;
            }

            // Compare each block
            for (var i = 0; i < blocks1.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var blockName = blocks1[i];
                if (blockName != blocks2[i])
                {
                    result.IsValid = false;
                    result.Differences.Add($"Block name mismatch at index {i}: '{blockName}' vs '{blocks2[i]}'");
                    continue;
                }

                // Read both blocks
                var data1 = await provider1.ReadBlockAsync(blockName, cancellationToken);
                var data2 = await provider2.ReadBlockAsync(blockName, cancellationToken);

                if (data1 == null || data2 == null)
                {
                    result.IsValid = false;
                    result.Differences.Add($"Block '{blockName}' missing in one database");
                    continue;
                }

                // Compare checksums
                var checksum1 = SHA256.HashData(data1);
                var checksum2 = SHA256.HashData(data2);

                if (!checksum1.SequenceEqual(checksum2))
                {
                    result.IsValid = false;
                    result.Differences.Add($"Block '{blockName}' content mismatch");
                    result.Differences.Add($"  Checksum1: {Convert.ToHexString(checksum1)}");
                    result.Differences.Add($"  Checksum2: {Convert.ToHexString(checksum2)}");
                }
                else
                {
                    result.BlocksValidated++;
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            result.IsValid = false;
            result.Differences.Add($"Validation error: {ex.Message}");
            return result;
        }
    }

    /// <summary>
    /// Estimates the size of the migrated database.
    /// </summary>
    /// <param name="sourcePath">Source database path</param>
    /// <param name="password">Master password</param>
    /// <returns>Estimated size in bytes</returns>
    public static long EstimateMigratedSize(string sourcePath, string password)
    {
        var mode = DetectStorageMode(sourcePath);
        
        if (mode == StorageMode.Directory)
        {
            // For directory: sum all file sizes + overhead
            var files = Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories);
            var totalSize = files.Sum(f => new FileInfo(f).Length);
            
            // Add 10% overhead for .scdb metadata
            return (long)(totalSize * 1.1);
        }
        else
        {
            // For single-file: already know the size
            return new FileInfo(sourcePath).Length;
        }
    }

    // ==================== PRIVATE HELPER METHODS ====================

    private static StorageMode DetectStorageMode(string path)
    {
        if (File.Exists(path) && path.EndsWith(".scdb", StringComparison.OrdinalIgnoreCase))
        {
            return StorageMode.SingleFile;
        }
        else if (Directory.Exists(path))
        {
            return StorageMode.Directory;
        }
        else
        {
            throw new ArgumentException($"Invalid database path: {path}");
        }
    }

    private static IStorageProvider OpenProvider(string path, StorageMode mode, string _)
    {
        var options = mode == StorageMode.SingleFile
            ? DatabaseOptions.CreateSingleFileDefault()
            : DatabaseOptions.CreateDirectoryDefault();

        return mode switch
        {
            StorageMode.SingleFile => SingleFileStorageProvider.Open(path, options),
            StorageMode.Directory => DirectoryStorageProvider.Open(path, options),
            _ => throw new ArgumentException($"Invalid storage mode: {mode}")
        };
    }

    private static async Task<MigrationCheckpoint> LoadOrCreateCheckpoint(string path)
    {
        if (!File.Exists(path))
        {
            return new MigrationCheckpoint();
        }

        try
        {
            var json = await File.ReadAllTextAsync(path);
            return JsonSerializer.Deserialize<MigrationCheckpoint>(json) ?? new MigrationCheckpoint();
        }
        catch
        {
            return new MigrationCheckpoint();
        }
    }

    private static async Task SaveCheckpoint(string path, MigrationCheckpoint checkpoint)
    {
        var json = JsonSerializer.Serialize(checkpoint, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(path, json);
    }
}

/// <summary>
/// Result of a migration operation.
/// </summary>
public sealed class MigrationResult
{
    /// <summary>
    /// Gets or sets the source database path.
    /// </summary>
    public required string SourcePath { get; init; }

    /// <summary>
    /// Gets or sets the target database path.
    /// </summary>
    public required string TargetPath { get; init; }

    /// <summary>
    /// Gets or sets the source storage format.
    /// </summary>
    public StorageMode SourceFormat { get; init; }

    /// <summary>
    /// Gets or sets the target storage format.
    /// </summary>
    public StorageMode TargetFormat { get; init; }

    /// <summary>
    /// Gets or sets whether the migration was successful.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Gets or sets the total number of files/blocks to migrate.
    /// </summary>
    public int TotalFiles { get; set; }

    /// <summary>
    /// Gets or sets the number of files successfully migrated.
    /// </summary>
    public int FilesMigrated { get; set; }

    /// <summary>
    /// Gets or sets the number of files skipped.
    /// </summary>
    public int FilesSkipped { get; set; }

    /// <summary>
    /// Gets or sets the total bytes migrated.
    /// </summary>
    public long BytesMigrated { get; set; }

    /// <summary>
    /// Gets or sets the migration start time.
    /// </summary>
    public DateTime StartTime { get; set; }

    /// <summary>
    /// Gets or sets the migration end time.
    /// </summary>
    public DateTime EndTime { get; set; }

    /// <summary>
    /// Gets or sets the total migration duration.
    /// </summary>
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// Gets or sets the VACUUM duration (for single-file target).
    /// </summary>
    public TimeSpan? VacuumDuration { get; set; }

    /// <summary>
    /// Gets or sets the error message (if failed).
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Gets the migration throughput in MB/s.
    /// </summary>
    public double ThroughputMBps => Duration.TotalSeconds > 0
        ? (BytesMigrated / 1024.0 / 1024.0) / Duration.TotalSeconds
        : 0;
}

/// <summary>
/// Result of a validation operation.
/// </summary>
public sealed class ValidationResult
{
    /// <summary>
    /// Gets or sets whether the databases are identical.
    /// </summary>
    public bool IsValid { get; set; }

    /// <summary>
    /// Gets or sets the number of blocks validated.
    /// </summary>
    public int BlocksValidated { get; set; }

    /// <summary>
    /// Gets the list of differences found.
    /// </summary>
    public List<string> Differences { get; } = new();
}

/// <summary>
/// Checkpoint for incremental migration.
/// </summary>
internal sealed class MigrationCheckpoint
{
    /// <summary>
    /// Gets the set of migrated block names.
    /// </summary>
    public HashSet<string> MigratedBlocks { get; init; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Gets or sets the last update timestamp.
    /// </summary>
    public DateTime LastUpdate { get; set; } = DateTime.UtcNow;
}
