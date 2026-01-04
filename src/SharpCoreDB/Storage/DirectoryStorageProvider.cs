// <copyright file="DirectoryStorageProvider.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Storage;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Directory-based storage provider (legacy multi-file format).
/// Maintains backward compatibility with existing databases.
/// Each block = separate file in directory.
/// </summary>
public sealed class DirectoryStorageProvider : IStorageProvider
{
    private readonly string _rootDirectory;
    // NOTE: _options field reserved for future use in encryption/validation
    #pragma warning disable S4487 // Remove unread private field
    private readonly DatabaseOptions _options;
    #pragma warning restore S4487
    private readonly Lock _transactionLock = new();
    private readonly List<(string BlockName, byte[] Data)> _transactionLog;
    private bool _isInTransaction;
    private bool _disposed;

    private DirectoryStorageProvider(string rootDirectory, DatabaseOptions options)
    {
        _rootDirectory = rootDirectory;
        _options = options;
        _transactionLog = new List<(string, byte[])>();
        _isInTransaction = false;
    }

    /// <summary>
    /// Opens or creates a directory-based storage provider.
    /// </summary>
    /// <param name="rootDirectory">Root directory path</param>
    /// <param name="options">Database options</param>
    /// <returns>Initialized provider</returns>
    public static DirectoryStorageProvider Open(string rootDirectory, DatabaseOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentNullException.ThrowIfNull(options);

        // Create directory if it doesn't exist
        if (!Directory.Exists(rootDirectory))
        {
            Directory.CreateDirectory(rootDirectory);
        }

        return new DirectoryStorageProvider(rootDirectory, options);
    }

    /// <inheritdoc/>
    public StorageMode Mode => StorageMode.Directory;

    /// <inheritdoc/>
    public string RootPath => _rootDirectory;

    /// <inheritdoc/>
    public bool IsEncrypted => false; // Directory mode doesn't support encryption

    /// <inheritdoc/>
    public int PageSize => 0; // Not applicable for directory mode

    /// <inheritdoc/>
    public bool BlockExists(string blockName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var filePath = GetBlockPath(blockName);
        return File.Exists(filePath);
    }

    /// <inheritdoc/>
    public Stream? GetReadStream(string blockName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var filePath = GetBlockPath(blockName);
        if (!File.Exists(filePath))
        {
            return null;
        }

        return new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 8192,
            useAsync: true);
    }

    /// <inheritdoc/>
    public ReadOnlySpan<byte> GetReadSpan(string blockName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Directory mode: must allocate (no memory mapping)
        var data = ReadBlockAsync(blockName).GetAwaiter().GetResult();
        return data != null ? new ReadOnlySpan<byte>(data) : ReadOnlySpan<byte>.Empty;
    }

    /// <inheritdoc/>
    public Stream GetWriteStream(string blockName, bool append = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var filePath = GetBlockPath(blockName);
        var fileMode = append ? FileMode.Append : FileMode.Create;

        return new FileStream(
            filePath,
            fileMode,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 8192,
            useAsync: true);
    }

    /// <inheritdoc/>
    public async Task WriteBlockAsync(string blockName, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var filePath = GetBlockPath(blockName);

        if (_isInTransaction)
        {
            // Buffer writes in transaction log
            _transactionLog.Add((blockName, data.ToArray()));
        }
        else
        {
            // Write directly
            await File.WriteAllBytesAsync(filePath, data.ToArray(), cancellationToken);
        }
    }

    /// <inheritdoc/>
    public async Task<byte[]?> ReadBlockAsync(string blockName, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var filePath = GetBlockPath(blockName);
        if (!File.Exists(filePath))
        {
            return null;
        }

        return await File.ReadAllBytesAsync(filePath, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task DeleteBlockAsync(string blockName, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var filePath = GetBlockPath(blockName);
        
        if (_isInTransaction)
        {
            // Log deletion
            _transactionLog.Add((blockName, Array.Empty<byte>()));
        }
        else
        {
            if (File.Exists(filePath))
            {
                await Task.Run(() => File.Delete(filePath), cancellationToken);
            }
        }
    }

    /// <inheritdoc/>
    public IEnumerable<string> EnumerateBlocks()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        
        // Enumerate all .dat and metadata files in directory
        var blocks = new List<string>();
        
        // Table data blocks
        var datFiles = Directory.GetFiles(_rootDirectory, "*.dat", SearchOption.AllDirectories);
        foreach (var file in datFiles)
        {
            var relativePath = Path.GetRelativePath(_rootDirectory, file);
            var blockName = Path.ChangeExtension(relativePath, null).Replace(Path.DirectorySeparatorChar, ':');
            blocks.Add(blockName);
        }
        
        // Metadata blocks
        var metaFiles = Directory.GetFiles(_rootDirectory, "*.meta", SearchOption.AllDirectories);
        foreach (var file in metaFiles)
        {
            var relativePath = Path.GetRelativePath(_rootDirectory, file);
            var blockName = Path.ChangeExtension(relativePath, null).Replace(Path.DirectorySeparatorChar, ':') + ":meta";
            blocks.Add(blockName);
        }
        
        return blocks.Distinct();
    }

    /// <inheritdoc/>
    public BlockMetadata? GetBlockMetadata(string blockName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var filePath = GetBlockPath(blockName);
        if (!File.Exists(filePath))
        {
            return null;
        }

        var fileInfo = new FileInfo(filePath);

        return new BlockMetadata
        {
            Name = blockName,
            BlockType = 0, // Not tracked in directory mode
            Size = fileInfo.Length,
            Offset = 0, // Not applicable
            Checksum = null,
            IsEncrypted = false,
            IsDirty = false,
            LastModified = fileInfo.LastWriteTimeUtc
        };
    }

    /// <inheritdoc/>
    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        
        // Directory mode: OS handles flushing
        await Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task<VacuumResult> VacuumAsync(VacuumMode mode, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Directory mode: VACUUM not applicable
        var stats = GetStatistics();

        return await Task.FromResult(new VacuumResult
        {
            Mode = mode,
            DurationMs = 0,
            FileSizeBefore = stats.TotalSize,
            FileSizeAfter = stats.TotalSize,
            BytesReclaimed = 0,
            FragmentationBefore = 0,
            FragmentationAfter = 0,
            BlocksMoved = 0,
            BlocksDeleted = 0,
            Success = true
        });
    }

    /// <inheritdoc/>
    public void BeginTransaction()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_transactionLock)
        {
            if (_isInTransaction)
            {
                throw new InvalidOperationException("Transaction already in progress");
            }

            _isInTransaction = true;
            _transactionLog.Clear();
        }
    }

    /// <inheritdoc/>
    public async Task CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_transactionLock)
        {
            if (!_isInTransaction)
            {
                throw new InvalidOperationException("No active transaction");
            }
        }

        // Apply all buffered writes
        foreach (var (blockName, data) in _transactionLog)
        {
            var filePath = GetBlockPath(blockName);
            
            if (data.Length == 0)
            {
                // Deletion
                if (File.Exists(filePath))
                {
                    await Task.Run(() => File.Delete(filePath), cancellationToken);
                }
            }
            else
            {
                // Write
                await File.WriteAllBytesAsync(filePath, data, cancellationToken);
            }
        }

        lock (_transactionLock)
        {
            _transactionLog.Clear();
            _isInTransaction = false;
        }
    }

    /// <inheritdoc/>
    public void RollbackTransaction()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_transactionLock)
        {
            if (!_isInTransaction)
            {
                throw new InvalidOperationException("No active transaction");
            }

            _transactionLog.Clear();
            _isInTransaction = false;
        }
    }

    /// <inheritdoc/>
    public bool IsInTransaction
    {
        get
        {
            lock (_transactionLock)
            {
                return _isInTransaction;
            }
        }
    }

    /// <inheritdoc/>
    public StorageStatistics GetStatistics()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var files = Directory.GetFiles(_rootDirectory);
        var totalSize = files.Sum(f => new FileInfo(f).Length);

        return new StorageStatistics
        {
            TotalSize = totalSize,
            UsedSpace = totalSize,
            FreeSpace = 0, // Not tracked
            FragmentationPercent = 0, // Not tracked
            BlockCount = files.Length,
            DirtyBlocks = 0, // Not tracked
            PageCount = 0, // Not applicable
            FreePages = 0, // Not applicable
            WalSize = 0, // Not applicable
            LastVacuum = null
        };
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;

        try
        {
            if (_isInTransaction)
            {
                RollbackTransaction();
            }
        }
        finally
        {
            _disposed = true;
        }
    }

    private string GetBlockPath(string blockName)
    {
        // Sanitize block name for file system
        var sanitized = blockName.Replace(':', '_').Replace('/', '_').Replace('\\', '_');
        return Path.Combine(_rootDirectory, sanitized);
    }
}
