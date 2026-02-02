// <copyright file="OrphanCleaner.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Storage.Overflow;

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Cleans up orphaned files and recovers missing files from backup.
/// C# 14: Modern async patterns with collection expressions.
/// </summary>
/// <remarks>
/// ✅ SCDB Phase 6: Orphan cleanup and recovery.
/// 
/// Features:
/// - Safe cleanup with retention period
/// - Dry-run mode for preview
/// - Backup recovery with checksum validation
/// - Progress reporting
/// </remarks>
public sealed class OrphanCleaner
{
    private readonly string _blobsPath;
    private readonly StorageOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="OrphanCleaner"/> class.
    /// </summary>
    /// <param name="dbPath">Database root path.</param>
    /// <param name="options">Storage options.</param>
    public OrphanCleaner(string dbPath, StorageOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dbPath);
        
        _blobsPath = Path.Combine(dbPath, "blobs");
        _options = options ?? StorageOptions.Default;
    }

    /// <summary>
    /// Cleans up orphaned files based on the report.
    /// </summary>
    /// <param name="report">Orphan detection report.</param>
    /// <param name="options">Cleanup options.</param>
    /// <param name="progress">Optional progress reporter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Cleanup result.</returns>
    public async Task<CleanupResult> CleanupOrphansAsync(
        OrphanReport report,
        CleanupOptions? options = null,
        IProgress<CleanupProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new CleanupOptions();
        
        var deletedCount = 0;
        var deletedSize = 0L;
        var skippedCount = 0;
        var errors = new List<string>();
        var now = DateTime.UtcNow;
        
        var total = report.OrphanedFiles.Count;
        var current = 0;
        
        foreach (var orphan in report.OrphanedFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            current++;
            
            // Check retention period
            var age = now - orphan.CreatedAt;
            if (age < options.RetentionPeriod)
            {
                skippedCount++;
                progress?.Report(new CleanupProgress
                {
                    Current = current,
                    Total = total,
                    CurrentFile = orphan.FilePath,
                    Message = $"Skipped (age: {age.TotalDays:F1} days, retention: {options.RetentionPeriod.TotalDays} days)",
                });
                continue;
            }
            
            if (options.DryRun)
            {
                progress?.Report(new CleanupProgress
                {
                    Current = current,
                    Total = total,
                    CurrentFile = orphan.FilePath,
                    Message = $"Would delete: {orphan.FileSize:N0} bytes",
                });
                deletedCount++;
                deletedSize += orphan.FileSize;
                continue;
            }
            
            try
            {
                // Delete file
                if (File.Exists(orphan.FilePath))
                {
                    File.Delete(orphan.FilePath);
                }
                
                // Delete metadata
                var metaPath = Path.ChangeExtension(orphan.FilePath, ".meta");
                if (File.Exists(metaPath))
                {
                    File.Delete(metaPath);
                }
                
                deletedCount++;
                deletedSize += orphan.FileSize;
                
                progress?.Report(new CleanupProgress
                {
                    Current = current,
                    Total = total,
                    CurrentFile = orphan.FilePath,
                    Message = $"Deleted: {orphan.FileSize:N0} bytes",
                });
            }
            catch (Exception ex)
            {
                errors.Add($"Failed to delete {orphan.FileId}: {ex.Message}");
            }
        }
        
        return new CleanupResult
        {
            DeletedCount = deletedCount,
            DeletedSize = deletedSize,
            SkippedCount = skippedCount,
            Errors = errors,
            IsDryRun = options.DryRun,
        };
    }

    /// <summary>
    /// Recovers missing files from backup.
    /// </summary>
    /// <param name="report">Orphan detection report.</param>
    /// <param name="options">Recovery options.</param>
    /// <param name="progress">Optional progress reporter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Recovery result.</returns>
    public async Task<RecoveryResult> RecoverMissingFilesAsync(
        OrphanReport report,
        RecoveryOptions options,
        IProgress<RecoveryProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        
        if (!Directory.Exists(options.BackupPath))
        {
            return new RecoveryResult
            {
                RecoveredCount = 0,
                FailedCount = report.MissingFiles.Count,
                Errors = [$"Backup path not found: {options.BackupPath}"],
            };
        }
        
        var recoveredCount = 0;
        var failedCount = 0;
        var errors = new List<string>();
        
        var total = report.MissingFiles.Count;
        var current = 0;
        
        foreach (var missing in report.MissingFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            current++;
            
            var backupFilePath = Path.Combine(options.BackupPath, "blobs", missing.RelativePath);
            var targetFilePath = Path.Combine(_blobsPath, missing.RelativePath);
            
            try
            {
                if (!File.Exists(backupFilePath))
                {
                    errors.Add($"Backup file not found: {missing.FileId}");
                    failedCount++;
                    continue;
                }
                
                // Skip if target exists
                if (options.SkipExisting && File.Exists(targetFilePath))
                {
                    progress?.Report(new RecoveryProgress
                    {
                        Current = current,
                        Total = total,
                        CurrentFile = missing.FileId.ToString(),
                        Message = "Skipped (already exists)",
                    });
                    continue;
                }
                
                // Verify checksum if requested
                if (options.VerifyChecksums)
                {
                    var backupChecksum = await ComputeChecksumAsync(backupFilePath, cancellationToken);
                    
                    if (!backupChecksum.AsSpan().SequenceEqual(missing.Checksum))
                    {
                        errors.Add($"Checksum mismatch for {missing.FileId}");
                        failedCount++;
                        continue;
                    }
                }
                
                // Create target directory
                Directory.CreateDirectory(Path.GetDirectoryName(targetFilePath)!);
                
                // Copy file
                File.Copy(backupFilePath, targetFilePath, overwrite: true);
                
                // Copy metadata if exists
                var backupMetaPath = Path.ChangeExtension(backupFilePath, ".meta");
                var targetMetaPath = Path.ChangeExtension(targetFilePath, ".meta");
                if (File.Exists(backupMetaPath))
                {
                    File.Copy(backupMetaPath, targetMetaPath, overwrite: true);
                }
                
                recoveredCount++;
                
                progress?.Report(new RecoveryProgress
                {
                    Current = current,
                    Total = total,
                    CurrentFile = missing.FileId.ToString(),
                    Message = $"Recovered: {missing.FileSize:N0} bytes",
                });
            }
            catch (Exception ex)
            {
                errors.Add($"Failed to recover {missing.FileId}: {ex.Message}");
                failedCount++;
            }
        }
        
        return new RecoveryResult
        {
            RecoveredCount = recoveredCount,
            FailedCount = failedCount,
            Errors = errors,
        };
    }

    // ========================================
    // Private Helper Methods
    // ========================================

    private static async Task<byte[]> ComputeChecksumAsync(string filePath, CancellationToken cancellationToken)
    {
        using var stream = File.OpenRead(filePath);
        return await SHA256.HashDataAsync(stream, cancellationToken);
    }
}

/// <summary>
/// Result of cleanup operation.
/// </summary>
public sealed record CleanupResult
{
    /// <summary>Gets the number of files deleted.</summary>
    public int DeletedCount { get; init; }
    
    /// <summary>Gets the total size of deleted files.</summary>
    public long DeletedSize { get; init; }
    
    /// <summary>Gets the number of files skipped.</summary>
    public int SkippedCount { get; init; }
    
    /// <summary>Gets list of errors encountered.</summary>
    public IReadOnlyList<string> Errors { get; init; } = [];
    
    /// <summary>Gets whether this was a dry run.</summary>
    public bool IsDryRun { get; init; }
    
    /// <summary>Gets whether cleanup was successful (no errors).</summary>
    public bool Success => Errors.Count == 0;
    
    /// <summary>Gets a summary string.</summary>
    public string Summary => IsDryRun
        ? $"Dry run: Would delete {DeletedCount} files ({DeletedSize:N0} bytes), skip {SkippedCount}"
        : $"Deleted {DeletedCount} files ({DeletedSize:N0} bytes), skipped {SkippedCount}, errors: {Errors.Count}";
}

/// <summary>
/// Progress for cleanup operation.
/// </summary>
public sealed record CleanupProgress
{
    /// <summary>Gets the current file number.</summary>
    public int Current { get; init; }
    
    /// <summary>Gets the total number of files.</summary>
    public int Total { get; init; }
    
    /// <summary>Gets the current file being processed.</summary>
    public string CurrentFile { get; init; } = "";
    
    /// <summary>Gets the status message.</summary>
    public string Message { get; init; } = "";
    
    /// <summary>Gets the percentage complete.</summary>
    public double PercentComplete => Total > 0 ? (double)Current / Total * 100 : 0;
}

/// <summary>
/// Result of recovery operation.
/// </summary>
public sealed record RecoveryResult
{
    /// <summary>Gets the number of files recovered.</summary>
    public int RecoveredCount { get; init; }
    
    /// <summary>Gets the number of files that failed to recover.</summary>
    public int FailedCount { get; init; }
    
    /// <summary>Gets list of errors encountered.</summary>
    public IReadOnlyList<string> Errors { get; init; } = [];
    
    /// <summary>Gets whether recovery was fully successful.</summary>
    public bool Success => FailedCount == 0 && Errors.Count == 0;
    
    /// <summary>Gets a summary string.</summary>
    public string Summary => $"Recovered {RecoveredCount} files, failed {FailedCount}, errors: {Errors.Count}";
}

/// <summary>
/// Progress for recovery operation.
/// </summary>
public sealed record RecoveryProgress
{
    /// <summary>Gets the current file number.</summary>
    public int Current { get; init; }
    
    /// <summary>Gets the total number of files.</summary>
    public int Total { get; init; }
    
    /// <summary>Gets the current file being processed.</summary>
    public string CurrentFile { get; init; } = "";
    
    /// <summary>Gets the status message.</summary>
    public string Message { get; init; } = "";
    
    /// <summary>Gets the percentage complete.</summary>
    public double PercentComplete => Total > 0 ? (double)Current / Total * 100 : 0;
}
