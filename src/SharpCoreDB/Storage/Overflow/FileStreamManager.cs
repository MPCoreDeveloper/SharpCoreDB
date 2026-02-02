// <copyright file="FileStreamManager.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Storage.Overflow;

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Manages external file storage for FILESTREAM data.
/// C# 14: Modern async patterns with file-scoped namespace.
/// </summary>
/// <remarks>
/// ✅ SCDB Phase 6: FILESTREAM manager for unlimited row sizes.
/// 
/// Features:
/// - Atomic writes (temp file + move)
/// - Checksum validation (SHA-256)
/// - Metadata tracking (.meta files)
/// - Subdirectory organization (256×256 buckets)
/// - Transactional safety
/// </remarks>
public sealed class FileStreamManager : IDisposable
{
    private readonly string _blobsPath;
    private readonly string _tempPath;
    private readonly Lock _lock = new();
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileStreamManager"/> class.
    /// </summary>
    /// <param name="dbPath">Database root path.</param>
    public FileStreamManager(string dbPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dbPath);
        
        _blobsPath = Path.Combine(dbPath, "blobs");
        _tempPath = Path.Combine(dbPath, "temp");
        
        // Create directories
        Directory.CreateDirectory(_blobsPath);
        Directory.CreateDirectory(_tempPath);
    }

    /// <summary>
    /// Writes data to external file with transactional safety.
    /// </summary>
    /// <param name="rowId">Owning row ID.</param>
    /// <param name="tableName">Owning table name.</param>
    /// <param name="columnName">Column name.</param>
    /// <param name="data">Data to write.</param>
    /// <param name="contentType">MIME content type.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>File pointer to the written file.</returns>
    public async Task<FilePointer> WriteAsync(
        long rowId,
        string tableName,
        string columnName,
        byte[] data,
        string contentType = "application/octet-stream",
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(data);
        
        var fileId = Guid.NewGuid();
        string? tempFilePath = null;
        string? tempMetaPath = null;
        
        try
        {
            // 1. Write to temp file first
            tempFilePath = GetTempFilePath(fileId);
            await File.WriteAllBytesAsync(tempFilePath, data, cancellationToken);
            
            // 2. Compute checksum
            var checksum = await ComputeChecksumAsync(tempFilePath, cancellationToken);
            
            // 3. Create pointer
            var pointer = new FilePointer
            {
                FileId = fileId,
                RelativePath = FilePointer.GenerateRelativePath(fileId),
                FileSize = data.Length,
                CreatedAt = DateTime.UtcNow,
                LastAccessedAt = DateTime.UtcNow,
                ContentType = contentType,
                Checksum = checksum,
                RowId = rowId,
                TableName = tableName,
                ColumnName = columnName,
            };
            
            // 4. Write metadata to temp
            tempMetaPath = GetTempMetaPath(fileId);
            await WriteMetadataAsync(tempMetaPath, pointer, cancellationToken);
            
            // 5. Atomic move to final location
            var finalFilePath = GetFilePath(fileId);
            var finalMetaPath = GetMetaPath(fileId);
            
            // Ensure target directory exists
            Directory.CreateDirectory(Path.GetDirectoryName(finalFilePath)!);
            
            // Move files (atomic operations)
            File.Move(tempFilePath, finalFilePath, overwrite: false);
            File.Move(tempMetaPath, finalMetaPath, overwrite: false);
            
            tempFilePath = null;  // Mark as committed
            tempMetaPath = null;
            
            return pointer;
        }
        catch
        {
            // Rollback: delete temp files if they exist
            if (tempFilePath != null && File.Exists(tempFilePath))
            {
                try { File.Delete(tempFilePath); } catch { }
            }
            if (tempMetaPath != null && File.Exists(tempMetaPath))
            {
                try { File.Delete(tempMetaPath); } catch { }
            }
            
            throw;
        }
    }

    /// <summary>
    /// Reads file data.
    /// </summary>
    /// <param name="pointer">File pointer.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>File data.</returns>
    public async Task<byte[]> ReadAsync(FilePointer pointer, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(pointer);
        
        var filePath = Path.Combine(_blobsPath, pointer.RelativePath);
        
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"FILESTREAM file not found: {pointer.FileId}");
        }
        
        var data = await File.ReadAllBytesAsync(filePath, cancellationToken);
        
        // Verify checksum
        var actualChecksum = SHA256.HashData(data);
        if (!actualChecksum.AsSpan().SequenceEqual(pointer.Checksum))
        {
            throw new InvalidDataException($"Checksum mismatch for file: {pointer.FileId}");
        }
        
        return data;
    }

    /// <summary>
    /// Deletes file and metadata.
    /// </summary>
    /// <param name="pointer">File pointer.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task DeleteAsync(FilePointer pointer, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(pointer);
        
        var filePath = Path.Combine(_blobsPath, pointer.RelativePath);
        var metaPath = Path.ChangeExtension(filePath, ".meta");
        
        // Delete file
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }
        
        // Delete metadata
        if (File.Exists(metaPath))
        {
            File.Delete(metaPath);
        }
        
        await Task.CompletedTask;
    }

    /// <summary>
    /// Checks if file exists.
    /// </summary>
    /// <param name="fileId">File ID.</param>
    /// <returns>True if file exists.</returns>
    public bool FileExists(Guid fileId)
    {
        var filePath = GetFilePath(fileId);
        return File.Exists(filePath);
    }

    /// <summary>
    /// Gets file metadata.
    /// </summary>
    /// <param name="fileId">File ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>File pointer or null if not found.</returns>
    public async Task<FilePointer?> GetMetadataAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        var metaPath = GetMetaPath(fileId);
        
        if (!File.Exists(metaPath))
        {
            return null;
        }
        
        try
        {
            var json = await File.ReadAllTextAsync(metaPath, cancellationToken);
            return JsonSerializer.Deserialize<FilePointer>(json);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Enumerates all files in blobs directory.
    /// </summary>
    /// <returns>Enumerable of file IDs.</returns>
    public IEnumerable<Guid> EnumerateFiles()
    {
        if (!Directory.Exists(_blobsPath))
        {
            yield break;
        }
        
        foreach (var file in Directory.EnumerateFiles(_blobsPath, "*.bin", SearchOption.AllDirectories))
        {
            var fileName = Path.GetFileNameWithoutExtension(file);
            if (Guid.TryParse(fileName, out var fileId))
            {
                yield return fileId;
            }
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        
        // Clean up temp directory
        try
        {
            if (Directory.Exists(_tempPath))
            {
                Directory.Delete(_tempPath, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
        
        _disposed = true;
    }

    // ========================================
    // Private Helper Methods
    // ========================================

    private string GetFilePath(Guid fileId)
    {
        var relativePath = FilePointer.GenerateRelativePath(fileId);
        return Path.Combine(_blobsPath, relativePath);
    }

    private string GetMetaPath(Guid fileId)
    {
        var filePath = GetFilePath(fileId);
        return Path.ChangeExtension(filePath, ".meta");
    }

    private string GetTempFilePath(Guid fileId)
    {
        return Path.Combine(_tempPath, $"{fileId:N}.bin.tmp");
    }

    private string GetTempMetaPath(Guid fileId)
    {
        return Path.Combine(_tempPath, $"{fileId:N}.meta.tmp");
    }

    private static async Task<byte[]> ComputeChecksumAsync(string filePath, CancellationToken cancellationToken)
    {
        using var stream = File.OpenRead(filePath);
        return await SHA256.HashDataAsync(stream, cancellationToken);
    }

    private static async Task WriteMetadataAsync(string metaPath, FilePointer pointer, CancellationToken cancellationToken)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
        };
        
        var json = JsonSerializer.Serialize(pointer, options);
        await File.WriteAllTextAsync(metaPath, json, cancellationToken);
    }
}
