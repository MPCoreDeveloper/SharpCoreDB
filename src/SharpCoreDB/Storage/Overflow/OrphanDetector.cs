// <copyright file="OrphanDetector.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Storage.Overflow;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Detects orphaned files and missing file references.
/// C# 14: Modern async patterns with collection expressions.
/// </summary>
/// <remarks>
/// ✅ SCDB Phase 6: Orphan detection for FILESTREAM data.
/// 
/// Detects:
/// - Orphaned files: Files on disk without database references
/// - Missing files: Database references without files on disk
/// </remarks>
public sealed class OrphanDetector
{
    private readonly string _blobsPath;
    private readonly Func<CancellationToken, Task<IEnumerable<FilePointer>>> _getPointersFunc;

    /// <summary>
    /// Initializes a new instance of the <see cref="OrphanDetector"/> class.
    /// </summary>
    /// <param name="dbPath">Database root path.</param>
    /// <param name="getPointersFunc">Function to get all file pointers from database.</param>
    public OrphanDetector(
        string dbPath,
        Func<CancellationToken, Task<IEnumerable<FilePointer>>> getPointersFunc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dbPath);
        ArgumentNullException.ThrowIfNull(getPointersFunc);
        
        _blobsPath = Path.Combine(dbPath, "blobs");
        _getPointersFunc = getPointersFunc;
    }

    /// <summary>
    /// Performs a full orphan detection scan.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Orphan detection report.</returns>
    public async Task<OrphanReport> DetectAsync(CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;
        
        // Get all files on disk
        var filesOnDisk = ScanFilesOnDisk();
        
        // Get all pointers from database
        var pointersInDb = (await _getPointersFunc(cancellationToken)).ToList();
        
        // Create lookup sets
        var fileIdsOnDisk = filesOnDisk.Select(f => f.FileId).ToHashSet();
        var fileIdsInDb = pointersInDb.Select(p => p.FileId).ToHashSet();
        
        // Find orphaned files (on disk but not in DB)
        var orphanedFiles = filesOnDisk
            .Where(f => !fileIdsInDb.Contains(f.FileId))
            .ToList();
        
        // Find missing files (in DB but not on disk)
        var missingFiles = pointersInDb
            .Where(p => !fileIdsOnDisk.Contains(p.FileId))
            .ToList();
        
        return new OrphanReport
        {
            ScanTime = DateTime.UtcNow - startTime,
            TotalFilesOnDisk = filesOnDisk.Count,
            TotalPointersInDb = pointersInDb.Count,
            OrphanedFiles = orphanedFiles,
            OrphanedFilesSize = orphanedFiles.Sum(f => f.FileSize),
            MissingFiles = missingFiles,
            MissingFilesSize = missingFiles.Sum(p => p.FileSize),
        };
    }

    /// <summary>
    /// Performs a quick check for orphans (files only, no DB scan).
    /// </summary>
    /// <returns>Count of files on disk.</returns>
    public int QuickScanFileCount()
    {
        if (!Directory.Exists(_blobsPath))
            return 0;
        
        return Directory.EnumerateFiles(_blobsPath, "*.bin", SearchOption.AllDirectories).Count();
    }

    // ========================================
    // Private Helper Methods
    // ========================================

    private List<OrphanedFileInfo> ScanFilesOnDisk()
    {
        var files = new List<OrphanedFileInfo>();
        
        if (!Directory.Exists(_blobsPath))
            return files;
        
        foreach (var filePath in Directory.EnumerateFiles(_blobsPath, "*.bin", SearchOption.AllDirectories))
        {
            var fileName = Path.GetFileNameWithoutExtension(filePath);
            
            if (Guid.TryParse(fileName, out var fileId))
            {
                var fileInfo = new FileInfo(filePath);
                files.Add(new OrphanedFileInfo
                {
                    FileId = fileId,
                    FilePath = filePath,
                    FileSize = fileInfo.Length,
                    CreatedAt = fileInfo.CreationTimeUtc,
                    LastAccessedAt = fileInfo.LastAccessTimeUtc,
                });
            }
        }
        
        return files;
    }
}

/// <summary>
/// Report from orphan detection scan.
/// </summary>
public sealed record OrphanReport
{
    /// <summary>Gets how long the scan took.</summary>
    public TimeSpan ScanTime { get; init; }
    
    /// <summary>Gets total files found on disk.</summary>
    public int TotalFilesOnDisk { get; init; }
    
    /// <summary>Gets total pointers found in database.</summary>
    public int TotalPointersInDb { get; init; }
    
    /// <summary>Gets list of orphaned files (on disk, not in DB).</summary>
    public IReadOnlyList<OrphanedFileInfo> OrphanedFiles { get; init; } = [];
    
    /// <summary>Gets total size of orphaned files.</summary>
    public long OrphanedFilesSize { get; init; }
    
    /// <summary>Gets list of missing files (in DB, not on disk).</summary>
    public IReadOnlyList<FilePointer> MissingFiles { get; init; } = [];
    
    /// <summary>Gets total size of missing files.</summary>
    public long MissingFilesSize { get; init; }
    
    /// <summary>Gets whether there are any issues.</summary>
    public bool HasIssues => OrphanedFiles.Count > 0 || MissingFiles.Count > 0;
    
    /// <summary>Gets a summary string.</summary>
    public string Summary => $"Orphaned: {OrphanedFiles.Count} files ({OrphanedFilesSize:N0} bytes), " +
        $"Missing: {MissingFiles.Count} files ({MissingFilesSize:N0} bytes)";
}

/// <summary>
/// Information about an orphaned file.
/// </summary>
public sealed record OrphanedFileInfo
{
    /// <summary>Gets the file GUID.</summary>
    public required Guid FileId { get; init; }
    
    /// <summary>Gets the full file path.</summary>
    public required string FilePath { get; init; }
    
    /// <summary>Gets the file size in bytes.</summary>
    public required long FileSize { get; init; }
    
    /// <summary>Gets when the file was created.</summary>
    public required DateTime CreatedAt { get; init; }
    
    /// <summary>Gets when the file was last accessed.</summary>
    public required DateTime LastAccessedAt { get; init; }
}
