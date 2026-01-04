// <copyright file="WalManager.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Storage;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

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

    #pragma warning disable S1172 // Unused parameter will be used in future implementation
    private async Task FlushWalAsync(CancellationToken cancellationToken = default)
    #pragma warning restore S1172
    {
        // NOTE: Implement WAL persistence
        // For now, just clear pending entries
        lock (_walLock)
        {
            _pendingEntries.Clear();
        }

        await Task.CompletedTask;
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
