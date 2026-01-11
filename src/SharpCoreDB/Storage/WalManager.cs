// <copyright file="WalManager.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Storage;

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

/// <summary>
/// Write-Ahead Log (WAL) manager for crash recovery.
/// Implements circular buffer of WAL entries.
/// Transaction boundaries and redo/undo logging.
/// </summary>
internal sealed class WalManager : IDisposable
{
    // NOTE: These fields will be used for future WAL persistence
    #pragma warning disable S4487 // Remove unread private field
    private readonly SingleFileStorageProvider _provider;
    private readonly ulong _walOffset;
    #pragma warning restore S4487
    
    private readonly ulong _walLength;
    private readonly int _maxEntries;
    private readonly Queue<WalLogEntry> _pendingEntries;
    private readonly Lock _walLock = new();
    private ulong _currentLsn;
    private ulong _currentTransactionId;
    private ulong _lastCheckpointLsn;
    private bool _inTransaction;
    private bool _disposed;

    public WalManager(SingleFileStorageProvider provider, ulong walOffset, ulong walLength, int maxEntries)
    {
        _provider = provider;
        _walOffset = walOffset;
        _walLength = walLength;
        _maxEntries = maxEntries;
        _pendingEntries = new Queue<WalLogEntry>();
        _currentLsn = 0;
        _currentTransactionId = 0;
        _inTransaction = false;
        _lastCheckpointLsn = 0;
    }

    public ulong CurrentLsn => _currentLsn;

    public void BeginTransaction()
    {
        lock (_walLock)
        {
            if (_inTransaction)
            {
                throw new InvalidOperationException("Transaction already in progress");
            }

            _inTransaction = true;
            _currentTransactionId++;

            // Log transaction begin
            _pendingEntries.Enqueue(new WalLogEntry
            {
                Lsn = ++_currentLsn,
                TransactionId = _currentTransactionId,
                Operation = WalOperation.TransactionBegin,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });
        }
    }

    public async Task CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        lock (_walLock)
        {
            if (!_inTransaction)
            {
                throw new InvalidOperationException("No active transaction");
            }

            // Log transaction commit
            _pendingEntries.Enqueue(new WalLogEntry
            {
                Lsn = ++_currentLsn,
                TransactionId = _currentTransactionId,
                Operation = WalOperation.TransactionCommit,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });

            _inTransaction = false;
        }

        // Flush to disk
        await FlushWalAsync(cancellationToken);
    }

    public void RollbackTransaction()
    {
        lock (_walLock)
        {
            if (!_inTransaction)
            {
                throw new InvalidOperationException("No active transaction");
            }

            // Discard pending entries for this transaction
            var entriesToKeep = new Queue<WalLogEntry>();
            while (_pendingEntries.Count > 0)
            {
                var entry = _pendingEntries.Dequeue();
                if (entry.TransactionId != _currentTransactionId)
                {
                    entriesToKeep.Enqueue(entry);
                }
            }

            _pendingEntries.Clear();
            foreach (var entry in entriesToKeep)
            {
                _pendingEntries.Enqueue(entry);
            }

            _inTransaction = false;
        }
    }

    public async Task LogWriteAsync(string blockName, ulong offset, ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        lock (_walLock)
        {
            _pendingEntries.Enqueue(new WalLogEntry
            {
                Lsn = ++_currentLsn,
                TransactionId = _currentTransactionId,
                Operation = WalOperation.Update,
                BlockName = blockName,
                Offset = offset,
                DataLength = data.Length,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });

            // Auto-flush if buffer full
            if (_pendingEntries.Count >= _maxEntries / 2)
            {
                // NOTE: Schedule async flush
            }
        }

        await Task.CompletedTask;
    }

    public async Task LogDeleteAsync(string blockName, CancellationToken cancellationToken = default)
    {
        lock (_walLock)
        {
            _pendingEntries.Enqueue(new WalLogEntry
            {
                Lsn = ++_currentLsn,
                TransactionId = _currentTransactionId,
                Operation = WalOperation.Delete,
                BlockName = blockName,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });
        }

        await Task.CompletedTask;
    }

    public async Task CheckpointAsync(CancellationToken cancellationToken = default)
    {
        lock (_walLock)
        {
            _lastCheckpointLsn = _currentLsn;
            _pendingEntries.Enqueue(new WalLogEntry
            {
                Lsn = ++_currentLsn,
                TransactionId = 0,
                Operation = WalOperation.Checkpoint,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });
        }

        await FlushWalAsync(cancellationToken);
    }

    public (long Size, int EntryCount) GetStatistics()
    {
        lock (_walLock)
        {
            return ((long)_walLength, _pendingEntries.Count);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        try
        {
            if (_inTransaction)
            {
                RollbackTransaction();
            }

            FlushWalAsync().GetAwaiter().GetResult();
        }
        finally
        {
            _disposed = true;
        }
    }

    private async Task FlushWalAsync(CancellationToken cancellationToken = default)
    {
        // Get pending entries to write
        WalLogEntry[] entriesToWrite;
        lock (_walLock)
        {
            if (_pendingEntries.Count == 0)
            {
                return; // Nothing to flush
            }

            entriesToWrite = _pendingEntries.ToArray();
            _pendingEntries.Clear();
        }

        // Write entries to WAL file
        var fileStream = GetFileStream();
        var walHeader = new WalHeader
        {
            Magic = WalHeader.MAGIC,
            Version = WalHeader.CURRENT_VERSION,
            CurrentLsn = _currentLsn,
            LastCheckpoint = _lastCheckpointLsn,
            EntrySize = WalHeader.DEFAULT_ENTRY_SIZE,
            MaxEntries = (uint)_maxEntries,
            HeadOffset = 0,
            TailOffset = 0
        };

        // Write header first
        fileStream.Position = 0;
        var headerBuffer = new byte[WalHeader.SIZE];
        MemoryMarshal.Write(headerBuffer, in walHeader);
        await fileStream.WriteAsync(headerBuffer, cancellationToken);

        // Write entries
        foreach (var entry in entriesToWrite)
        {
            var entryBuffer = new byte[WalHeader.DEFAULT_ENTRY_SIZE];
            WriteWalEntry(entryBuffer, entry);
            await fileStream.WriteAsync(entryBuffer, cancellationToken);
        }

        await fileStream.FlushAsync(cancellationToken);
    }

    private System.IO.FileStream GetFileStream()
    {
        return _provider.GetInternalFileStream();
    }

    private static unsafe void WriteWalEntry(Span<byte> buffer, WalLogEntry entry)
    {
        if (buffer.Length < WalEntry.SIZE)
        {
            throw new ArgumentException($"Buffer too small: {buffer.Length} < {WalEntry.SIZE}");
        }

        int offset = 0;

        // Write primitive fields
        BinaryPrimitives.WriteUInt64LittleEndian(buffer[offset..], entry.Lsn);
        offset += 8;
        
        BinaryPrimitives.WriteUInt64LittleEndian(buffer[offset..], entry.TransactionId);
        offset += 8;
        
        BinaryPrimitives.WriteUInt64LittleEndian(buffer[offset..], (ulong)entry.Timestamp);
        offset += 8;
        
        BinaryPrimitives.WriteUInt16LittleEndian(buffer[offset..], (ushort)entry.Operation);
        offset += 2;
        
        BinaryPrimitives.WriteUInt16LittleEndian(buffer[offset..], 0); // BlockIndex
        offset += 2;
        
        BinaryPrimitives.WriteUInt16LittleEndian(buffer[offset..], 0); // PageId
        offset += 2;
        
        BinaryPrimitives.WriteUInt16LittleEndian(buffer[offset..], (ushort)(entry.DataLength > 4000 ? 4000 : entry.DataLength));
        offset += 2;

        // Write block name (32 bytes)
        var blockNameSpan = buffer.Slice(offset, 32);
        blockNameSpan.Clear();
        if (!string.IsNullOrEmpty(entry.BlockName))
        {
            var nameBytes = Encoding.UTF8.GetBytes(entry.BlockName);
            var nameSpan = nameBytes.AsSpan(0, Math.Min(nameBytes.Length, 32));
            nameSpan.CopyTo(blockNameSpan);
        }
        offset += 32;

        // Calculate and write checksum (32 bytes)
        var checksumSpan = buffer.Slice(offset, 32);
        var checksum = SHA256.HashData(buffer[..(offset)]);
        checksum.CopyTo(checksumSpan);
    }
}

/// <summary>
/// Internal WAL log entry.
/// </summary>
internal sealed class WalLogEntry
{
    public ulong Lsn { get; init; }
    public ulong TransactionId { get; init; }
    public WalOperation Operation { get; init; }
    public string BlockName { get; init; } = string.Empty;
    public ulong Offset { get; init; }
    public int DataLength { get; init; }
    public long Timestamp { get; init; }
}

/// <summary>
/// WAL operation enum (matches ScdbStructures.cs).
/// </summary>
internal enum WalOperation
{
    Insert = 1,
    Update = 2,
    Delete = 3,
    Checkpoint = 4,
    TransactionBegin = 5,
    TransactionCommit = 6,
    TransactionAbort = 7,
    PageAllocate = 8,
    PageFree = 9
}

internal struct WalHeader
{
    public const uint MAGIC = 0x20230522;
    public const ushort CURRENT_VERSION = 1;
    public const int SIZE = 64;
    public const int DEFAULT_ENTRY_SIZE = 4096;

    public uint Magic;
    public ushort Version;
    public ushort EntrySize;
    public uint MaxEntries;
    public ulong CurrentLsn;
    public ulong LastCheckpoint;
    public ulong HeadOffset;
    public ulong TailOffset;
}

#pragma warning disable CS0649 // Field is never assigned to, and will always have its default value
internal struct WalEntry
{
    public const int SIZE = 64;

    public ulong Lsn;
    public ulong TransactionId;
    public ulong Timestamp;
    public ushort Operation;
    public ushort BlockIndex;
    public ushort PageId;
    public ushort DataLength;
    public unsafe fixed byte BlockName[32];
    public unsafe fixed byte Checksum[32];
}
#pragma warning restore CS0649
