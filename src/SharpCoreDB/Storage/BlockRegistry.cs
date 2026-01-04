// <copyright file="BlockRegistry.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Storage;

using SharpCoreDB.Storage.Scdb;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Block registry for O(1) block name lookups.
/// Maintains in-memory hash table and persisted index.
/// Thread-safe via ConcurrentDictionary.
/// Format: [Header(64B)] [Entry1(64B)] [Entry2(64B)] ... [EntryN(64B)]
/// </summary>
internal sealed class BlockRegistry : IDisposable
{
    private readonly SingleFileStorageProvider _provider;
    private readonly ulong _registryOffset;
    private readonly ulong _registryLength;
    private readonly ConcurrentDictionary<string, BlockEntry> _blocks;
    private readonly Lock _registryLock = new();
    private bool _isDirty;
    private bool _disposed;

    public BlockRegistry(SingleFileStorageProvider provider, ulong registryOffset, ulong registryLength)
    {
        _provider = provider;
        _registryOffset = registryOffset;
        _registryLength = registryLength;
        _blocks = new ConcurrentDictionary<string, BlockEntry>(StringComparer.Ordinal);
        
        // Load existing registry from disk
        LoadRegistry();
    }

    public int Count => _blocks.Count;

    public bool TryGetBlock(string blockName, out BlockEntry entry)
    {
        return _blocks.TryGetValue(blockName, out entry);
    }

    public void AddOrUpdateBlock(string blockName, BlockEntry entry)
    {
        _blocks[blockName] = entry;
        _isDirty = true;
    }

    public bool RemoveBlock(string blockName)
    {
        var removed = _blocks.TryRemove(blockName, out _);
        if (removed)
        {
            _isDirty = true;
        }
        return removed;
    }

    public IEnumerable<string> EnumerateBlockNames()
    {
        return _blocks.Keys;
    }

    /// <summary>
    /// Flushes the block registry to disk.
    /// Format: [BlockRegistryHeader(64B)] [BlockEntry1(64B)] [BlockEntry2(64B)] ...
    /// </summary>
    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        if (!_isDirty) return;

        byte[] buffer;
        int totalSize;

        // Prepare data inside lock (fast, synchronous)
        lock (_registryLock)
        {
            if (!_isDirty) return; // Double-check after lock

            // Calculate required size
            var headerSize = BlockRegistryHeader.SIZE;
            var entrySize = BlockEntry.SIZE;
            totalSize = headerSize + (_blocks.Count * entrySize);

            if ((ulong)totalSize > _registryLength)
            {
                throw new InvalidOperationException(
                    $"Block registry too large: {totalSize} bytes exceeds limit {_registryLength}");
            }

            // Rent buffer from ArrayPool for zero-allocation
            buffer = ArrayPool<byte>.Shared.Rent(totalSize);
            var span = buffer.AsSpan(0, totalSize);
            span.Clear();

            // Write header
            var header = new BlockRegistryHeader
            {
                Magic = BlockRegistryHeader.MAGIC,
                Version = BlockRegistryHeader.CURRENT_VERSION,
                BlockCount = (ulong)_blocks.Count,
                TotalSize = (ulong)totalSize,
                LastModified = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            var headerSpan = span[..BlockRegistryHeader.SIZE];
            MemoryMarshal.Write(headerSpan, in header);

            // Write block entries
            var offset = BlockRegistryHeader.SIZE;
            foreach (var (blockName, blockEntry) in _blocks)
            {
                // Create entry with name
                var namedEntry = BlockEntry.WithName(blockName, blockEntry);
                
                var entrySpan = span.Slice(offset, BlockEntry.SIZE);
                MemoryMarshal.Write(entrySpan, in namedEntry);
                
                offset += BlockEntry.SIZE;
            }

            _isDirty = false;
        }

        // Write to file OUTSIDE lock (I/O is slow)
        try
        {
            var fileStream = GetFileStream();
            fileStream.Position = (long)_registryOffset;
            await fileStream.WriteAsync(buffer.AsMemory(0, totalSize), cancellationToken);
            await fileStream.FlushAsync(cancellationToken);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        
        try
        {
            if (_isDirty)
            {
                FlushAsync().GetAwaiter().GetResult();
            }
        }
        finally
        {
            _disposed = true;
        }
    }

    /// <summary>
    /// Loads the block registry from disk.
    /// </summary>
    private void LoadRegistry()
    {
        try
        {
            var fileStream = GetFileStream();
            
            // Check if registry exists (file large enough)
            if (fileStream.Length < (long)(_registryOffset + BlockRegistryHeader.SIZE))
            {
                return; // Empty registry
            }

            // Read header
            fileStream.Position = (long)_registryOffset;
            Span<byte> headerBuffer = stackalloc byte[BlockRegistryHeader.SIZE];
            fileStream.ReadExactly(headerBuffer);
            
            var header = BlockRegistryHeader.Parse(headerBuffer);
            
            if (!header.IsValid)
            {
                return; // Invalid or empty registry
            }

            if (header.BlockCount == 0)
            {
                return; // No blocks
            }

            // Read all block entries
            var totalEntrySize = (int)(header.BlockCount * (ulong)BlockEntry.SIZE);
            var buffer = ArrayPool<byte>.Shared.Rent(totalEntrySize);
            try
            {
                var entrySpan = buffer.AsSpan(0, totalEntrySize);
                fileStream.ReadExactly(entrySpan);

                // Parse each entry
                for (var i = 0; i < (int)header.BlockCount; i++)
                {
                    var offset = i * BlockEntry.SIZE;
                    var entryData = entrySpan.Slice(offset, BlockEntry.SIZE);
                    var entry = BlockEntry.Parse(entryData);
                    
                    var blockName = entry.GetName();
                    if (!string.IsNullOrEmpty(blockName))
                    {
                        _blocks[blockName] = entry;
                    }
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
        catch (Exception)
        {
            // If loading fails, start with empty registry
            _blocks.Clear();
        }
    }

    /// <summary>
    /// Gets the underlying FileStream from the provider.
    /// </summary>
    private FileStream GetFileStream()
    {
        return _provider.GetInternalFileStream();
    }
}
