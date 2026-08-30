// <copyright file="Database.Vacuum.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB;

using SharpCoreDB.Storage;

/// <summary>
/// Database implementation - VACUUM operations partial class.
/// Handles single-file storage defragmentation and cleanup.
/// </summary>
public partial class Database
{
    /// <inheritdoc/>
    public StorageMode StorageMode => StorageMode.Directory; // Legacy: always directory mode for now

    /// <inheritdoc/>
    public Task<EncryptionRotationResult> ChangeEncryptionPasswordAsync(string newPassword, CancellationToken cancellationToken = default)
    {
        // Directory-mode encryption is password-derived per-record and does not yet expose
        // engine-level rotation; users should migrate to single-file (.scdb) mode for this.
        throw new NotSupportedException(
            "ChangeEncryptionPasswordAsync is only supported for encrypted single-file (.scdb) databases.");
    }

    /// <inheritdoc/>
    public Task<EncryptionRotationResult> RotateEncryptionKeyAsync(byte[]? newKey = null, string? newPassword = null, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException(
            "RotateEncryptionKeyAsync is only supported for encrypted single-file (.scdb) databases.");
    }

    /// <inheritdoc/>
    public async Task<VacuumResult> VacuumAsync(VacuumMode mode = VacuumMode.Quick, CancellationToken cancellationToken = default)
    {
        // For directory-based storage, VACUUM is a no-op
        // In future: this will delegate to IStorageProvider when refactored
        await Task.CompletedTask;

        return new VacuumResult
        {
            Mode = mode,
            DurationMs = 0,
            FileSizeBefore = 0,
            FileSizeAfter = 0,
            BytesReclaimed = 0,
            FragmentationBefore = 0,
            FragmentationAfter = 0,
            BlocksMoved = 0,
            BlocksDeleted = 0,
            Success = true
        };
    }

    /// <inheritdoc/>
    public StorageStatistics GetStorageStatistics()
    {
        // For directory-based storage, calculate basic stats
        var totalSize = 0L;
        var blockCount = 0;

        if (Directory.Exists(_dbPath))
        {
            var files = Directory.GetFiles(_dbPath, "*", SearchOption.AllDirectories);
            totalSize = files.Sum(f => new FileInfo(f).Length);
            blockCount = files.Length;
        }

        return new StorageStatistics
        {
            TotalSize = totalSize,
            UsedSpace = totalSize,
            FreeSpace = 0,
            FragmentationPercent = 0,
            BlockCount = blockCount,
            DirtyBlocks = 0,
            PageCount = 0,
            FreePages = 0,
            WalSize = 0,
            LastVacuum = null
        };
    }
}
