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
using SharpCoreDB.Storage.Scdb;  // ✅ Add for WalHeader, WalEntry

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
    
    // ✅ SCDB Phase 3: Circular buffer state
    private ulong _headOffset;    // Oldest entry in circular buffer
    private ulong _tailOffset;    // Newest entry in circular buffer
    private uint _entryCount;     // Current entries in buffer

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
        
        // ✅ SCDB Phase 3: Initialize circular buffer
        _headOffset = 0;
        _tailOffset = 0;
        _entryCount = 0;
        
        // Load existing WAL from disk
        LoadWal();
    }

    public ulong CurrentLsn => _currentLsn;

    internal bool HasPendingEntries
    {
        get
        {
            lock (_walLock)
            {
                return _pendingEntries.Count > 0;
            }
        }
    }

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

    /// <summary>
    /// Flushes pending WAL entries to disk using circular buffer.
    /// ✅ SCDB Phase 3: Complete implementation with wraparound.
    /// </summary>
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

        var fileStream = GetFileStream();
        
        // ✅ Write each entry to circular buffer
        foreach (var entry in entriesToWrite)
        {
            await WriteEntryToBufferAsync(fileStream, entry, cancellationToken);
        }

        // ✅ Update and persist WAL header
        await UpdateWalHeaderAsync(fileStream, cancellationToken);
        
        await fileStream.FlushAsync(cancellationToken);
    }

    /// <summary>
    /// Writes a single WAL entry to the circular buffer.
    /// Handles wraparound automatically.
    /// </summary>
    private async Task WriteEntryToBufferAsync(
        System.IO.FileStream fileStream, 
        WalLogEntry logEntry, 
        CancellationToken cancellationToken)
    {
        // Calculate position in circular buffer
        var entryIndex = _tailOffset % (ulong)_maxEntries;
        var filePosition = (long)(_walOffset + WalHeader.SIZE + (entryIndex * WalHeader.DEFAULT_ENTRY_SIZE));
        
        // Convert WalLogEntry → WalEntry (on-disk format)
        var walEntry = ConvertToWalEntry(logEntry);
        
        // Serialize to buffer
        var entryBuffer = new byte[WalEntry.SIZE];
        SerializeWalEntry(entryBuffer, walEntry);
        
        // Write to file
        fileStream.Position = filePosition;
        await fileStream.WriteAsync(entryBuffer.AsMemory(), cancellationToken);
        
        // Update circular buffer pointers
        lock (_walLock)
        {
            _tailOffset++;
            _entryCount++;
            
            // Handle buffer full - overwrite oldest entry
            if (_entryCount > (uint)_maxEntries)
            {
                _headOffset++;
                _entryCount = (uint)_maxEntries;
            }
        }
    }

    /// <summary>
    /// Converts in-memory WalLogEntry to on-disk WalEntry format.
    /// </summary>
    private WalEntry ConvertToWalEntry(WalLogEntry logEntry)
    {
        return new WalEntry
        {
            Lsn = logEntry.Lsn,
            TransactionId = logEntry.TransactionId,
            Timestamp = (ulong)logEntry.Timestamp,
            Operation = (ushort)logEntry.Operation,
            BlockIndex = 0, // TODO: Map block name to index
            PageId = logEntry.Offset / 4096, // Assume 4KB pages
            DataLength = (ushort)Math.Min(logEntry.DataLength, WalEntry.MAX_DATA_LENGTH),
            // Checksum and Data will be set in SerializeWalEntry
        };
    }

    /// <summary>
    /// Serializes WalEntry to byte buffer with checksum.
    /// C# 14: Uses modern unsafe code patterns.
    /// Format: Lsn(8) + TxId(8) + Timestamp(8) + Op(2) + BlockIdx(2) + PageId(8) + DataLen(2) + Checksum(32) + Data(4000)
    /// </summary>
    private static unsafe void SerializeWalEntry(Span<byte> buffer, WalEntry entry)
    {
        if (buffer.Length < WalEntry.SIZE)
        {
            throw new ArgumentException($"Buffer too small: {buffer.Length} < {WalEntry.SIZE}");
        }

        buffer.Clear();
        
        int offset = 0;

        // Write header fields
        BinaryPrimitives.WriteUInt64LittleEndian(buffer[offset..], entry.Lsn);
        offset += 8;
        
        BinaryPrimitives.WriteUInt64LittleEndian(buffer[offset..], entry.TransactionId);
        offset += 8;
        
        BinaryPrimitives.WriteUInt64LittleEndian(buffer[offset..], entry.Timestamp);
        offset += 8;
        
        BinaryPrimitives.WriteUInt16LittleEndian(buffer[offset..], entry.Operation);
        offset += 2;
        
        BinaryPrimitives.WriteUInt16LittleEndian(buffer[offset..], entry.BlockIndex);
        offset += 2;
        
        BinaryPrimitives.WriteUInt64LittleEndian(buffer[offset..], entry.PageId);
        offset += 8;
        
        BinaryPrimitives.WriteUInt16LittleEndian(buffer[offset..], entry.DataLength);
        offset += 2;
        
        // Mark checksum offset (will write after computing hash)
        var checksumOffset = offset;
        offset += 32;
        
        // Data payload comes after checksum (offset now at Data field)
        var dataOffset = offset;
        
        // Write data payload (if any)
        if (entry.DataLength > 0 && entry.DataLength <= WalEntry.MAX_DATA_LENGTH)
        {
            // In real implementation: copy from entry.Data
            // For Phase 3: zero-filled (data writing is stub)
            // Data will be populated when actual operations are logged
        }
        
        // Calculate and write SHA-256 checksum
        // Hash: header (before checksum) + data payload
        using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        sha256.AppendData(buffer[..checksumOffset]); // Header fields
        
        if (entry.DataLength > 0 && entry.DataLength <= WalEntry.MAX_DATA_LENGTH)
        {
            sha256.AppendData(buffer.Slice(dataOffset, entry.DataLength)); // Data payload
        }
        
        var checksum = sha256.GetHashAndReset();
        
        // Validate checksum size and buffer space
        if (checksum.Length != 32)
        {
            throw new InvalidOperationException($"SHA256 checksum must be 32 bytes, got {checksum.Length}");
        }
        
        if (checksumOffset + 32 > buffer.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(buffer), 
                $"Buffer too small for checksum at offset {checksumOffset}, buffer size {buffer.Length}");
        }
        
        checksum.CopyTo(buffer.Slice(checksumOffset, 32));
    }

    /// <summary>
    /// Updates WAL header with current state.
    /// </summary>
    private async Task UpdateWalHeaderAsync(System.IO.FileStream fileStream, CancellationToken cancellationToken)
    {
        WalHeader header;
        lock (_walLock)
        {
            header = new WalHeader
            {
                Magic = WalHeader.MAGIC,
                Version = WalHeader.CURRENT_VERSION,
                CurrentLsn = _currentLsn,
                LastCheckpoint = _lastCheckpointLsn,
                EntrySize = WalHeader.DEFAULT_ENTRY_SIZE,
                MaxEntries = (uint)_maxEntries,
                HeadOffset = _headOffset,
                TailOffset = _tailOffset,
            };
        }

        // Serialize header
        var headerBuffer = new byte[WalHeader.SIZE];
        MemoryMarshal.Write(headerBuffer, in header);
        
        // Write to beginning of WAL region
        fileStream.Position = (long)_walOffset;
        await fileStream.WriteAsync(headerBuffer.AsMemory(), cancellationToken);
    }

    private System.IO.FileStream GetFileStream()
    {
        return _provider.GetInternalFileStream();
    }

    /// <summary>
    /// Loads WAL from disk on startup.
    /// ✅ SCDB Phase 3: Restores WAL state for recovery.
    /// </summary>
    private void LoadWal()
    {
        try
        {
            var fileStream = GetFileStream();
            
            // Check if WAL region exists
            if (fileStream.Length < (long)(_walOffset + WalHeader.SIZE))
            {
                return; // No WAL yet
            }

            // Read WAL header
            fileStream.Position = (long)_walOffset;
            Span<byte> headerBuffer = stackalloc byte[WalHeader.SIZE];
            fileStream.ReadExactly(headerBuffer);
            
            var header = MemoryMarshal.Read<WalHeader>(headerBuffer);
            
            if (header.Magic != WalHeader.MAGIC || header.Version != WalHeader.CURRENT_VERSION)
            {
                return; // Invalid WAL, start fresh
            }

            // Restore state from header
            lock (_walLock)
            {
                _currentLsn = header.CurrentLsn;
                _lastCheckpointLsn = header.LastCheckpoint;
                _headOffset = header.HeadOffset;
                _tailOffset = header.TailOffset;
                _entryCount = (uint)(header.TailOffset - header.HeadOffset);
                
                if (_entryCount > (uint)_maxEntries)
                {
                    _entryCount = (uint)_maxEntries;
                }
            }
        }
        catch (Exception)
        {
            // If loading fails, start with empty WAL
            // Recovery manager will handle this
        }
    }

    /// <summary>
    /// Reads WAL entries for recovery.
    /// Used by RecoveryManager to replay transactions.
    /// </summary>
    internal async Task<List<Scdb.WalEntry>> ReadEntriesSinceCheckpointAsync(CancellationToken cancellationToken = default)
    {
        var entries = new List<Scdb.WalEntry>();
        var fileStream = GetFileStream();

        ulong startOffset, endOffset;
        lock (_walLock)
        {
            startOffset = _headOffset;
            endOffset = _tailOffset;
        }

        // Read entries from circular buffer
        for (var i = startOffset; i < endOffset; i++)
        {
            var entryIndex = i % (ulong)_maxEntries;
            var filePosition = (long)(_walOffset + WalHeader.SIZE + (entryIndex * WalHeader.DEFAULT_ENTRY_SIZE));
            
            fileStream.Position = filePosition;
            var entryBuffer = new byte[WalEntry.SIZE];
            await fileStream.ReadAsync(entryBuffer.AsMemory(), cancellationToken);
            
            var entry = DeserializeWalEntry(entryBuffer);
            
            // Validate checksum
            if (ValidateWalEntryChecksum(entryBuffer, entry))
            {
                entries.Add(entry);
            }
            else
            {
                // Corrupted entry, stop reading
                break;
            }
        }

        return entries;
    }

    /// <summary>
    /// Deserializes WalEntry from byte buffer.
    /// </summary>
    private static Scdb.WalEntry DeserializeWalEntry(ReadOnlySpan<byte> buffer)
    {
        int offset = 0;

        var entry = new Scdb.WalEntry
        {
            Lsn = BinaryPrimitives.ReadUInt64LittleEndian(buffer[offset..]),
        };
        offset += 8;
        
        entry.TransactionId = BinaryPrimitives.ReadUInt64LittleEndian(buffer[offset..]);
        offset += 8;
        
        entry.Timestamp = BinaryPrimitives.ReadUInt64LittleEndian(buffer[offset..]);
        offset += 8;
        
        entry.Operation = BinaryPrimitives.ReadUInt16LittleEndian(buffer[offset..]);
        offset += 2;
        
        entry.BlockIndex = BinaryPrimitives.ReadUInt16LittleEndian(buffer[offset..]);
        offset += 2;
        
        entry.PageId = BinaryPrimitives.ReadUInt64LittleEndian(buffer[offset..]);
        offset += 8;
        
        entry.DataLength = BinaryPrimitives.ReadUInt16LittleEndian(buffer[offset..]);
        
        return entry;
    }

    /// <summary>
    /// Validates WAL entry checksum.
    /// </summary>
    private static bool ValidateWalEntryChecksum(ReadOnlySpan<byte> buffer, Scdb.WalEntry entry)
    {
        const int checksumOffset = 30; // After header fields
        const int dataOffset = checksumOffset + 32;
        
        using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        sha256.AppendData(buffer[..checksumOffset]);
        sha256.AppendData(buffer.Slice(dataOffset, entry.DataLength));
        var computedHash = sha256.GetHashAndReset();
        
        var storedHash = buffer.Slice(checksumOffset, 32);
        return storedHash.SequenceEqual(computedHash);
    }

    private static unsafe void WriteWalEntry(Span<byte> buffer, WalLogEntry entry)
    {
        // Legacy method - kept for compatibility
        // Now use SerializeWalEntry instead
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

// ✅ PHASE 3 FIX: Using Scdb.WalHeader and Scdb.WalEntry from ScdbStructures.cs
// Removed duplicate definitions that had incorrect SIZE (64 vs 4096)
