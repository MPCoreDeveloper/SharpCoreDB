// <copyright file="Storage.Core.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Services;

using SharpCoreDB.Core.Cache;
using SharpCoreDB.Core.File;
using SharpCoreDB.Interfaces;
using System;
using System.Buffers;
using System.Runtime.CompilerServices;

/// <summary>
/// Storage implementation - Core partial class with fields and initialization.
/// Provides encrypted file storage with transaction support and page caching.
/// </summary>
public partial class Storage : IStorage
{
    private readonly ICryptoService crypto;
    private readonly byte[] key;
    private readonly bool noEncryption;
    private readonly PageCache? pageCache;
    private readonly int pageSize;
    private readonly ArrayPool<byte> bufferPool;

    // ✅ Buffered append mode (opt-in): DatabaseConfig.EnableBufferedAppends routes single-row appends
    // outside a transaction through the same in-memory buffer the transaction path uses, so N rows cost
    // ONE open/write/close per flush boundary instead of one write-through open/close PER ROW (measured
    // ~512 µs -> ~4.5 µs per 64-byte row for the write itself). Off by default: the trade is the
    // durability window, see DatabaseConfig.EnableBufferedAppends for the full contract.
    private readonly bool enableBufferedAppends;
    private readonly long appendBufferFlushThresholdBytes;
    private readonly int appendBufferFlushIntervalMs;
    
    // Transaction support
    private readonly TransactionBuffer transactionBuffer;
    private readonly Lock transactionLock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="Storage"/> class.
    /// </summary>
    /// <param name="crypto">The crypto service.</param>
    /// <param name="key">The encryption key.</param>
    /// <param name="config">Optional database configuration.</param>
    /// <param name="pageCache">Optional page cache for high-performance caching.</param>
    public Storage(ICryptoService crypto, byte[] key, DatabaseConfig? config = null, PageCache? pageCache = null)
    {
        this.crypto = crypto;
        this.key = key;
        this.noEncryption = config?.NoEncryptMode ?? false;
        // ✅ Known Issue 1 FIX (opt-in): per-record at-rest encryption is gated behind
        // DatabaseConfig.EnableAtRestRecordEncryption (default false) for full backward
        // compatibility with existing databases and storage-engine behavior.
        this.enableAtRestRecordEncryption = (config?.EnableAtRestRecordEncryption ?? false) && !this.noEncryption;
        this.pageCache = pageCache;
        this.pageSize = config?.PageSize ?? 4096;
        this.bufferPool = ArrayPool<byte>.Shared;

        // ✅ Opt-in buffered appends (default false). Registered last so the encryption flags above are
        // already final when the append path starts consulting them.
        this.enableBufferedAppends = config?.EnableBufferedAppends ?? false;
        this.appendBufferFlushThresholdBytes = Math.Max(0, config?.AppendBufferFlushThresholdBytes ?? 1024 * 1024);
        this.appendBufferFlushIntervalMs = Math.Max(0, config?.AppendBufferFlushIntervalMs ?? 10);
        
        // Initialize batch encryption configuration
        this.enableBatchEncryption = (config?.EnableBatchEncryption ?? false) && !this.noEncryption;
        this.batchEncryptionSizeKB = config?.BatchEncryptionSizeKB ?? 64;
        
        // Initialize transaction buffer in FULL_WRITE mode (legacy compatible)
        this.transactionBuffer = new TransactionBuffer(
            this, 
            mode: TransactionBuffer.BufferMode.FULL_WRITE,
            pageSize: this.pageSize, 
            maxBufferSize: 8 * 1024 * 1024, 
            autoFlush: true);
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void BeginTransaction()
    {
        lock (this.transactionLock)
        {
            // ✅ Buffered-append safety: appends made OUTSIDE this transaction must not share the
            // transaction's buffer, because Rollback() clears that buffer — it would silently discard
            // rows the caller already got a position for. Flushing first gives the transaction a
            // durable prefix. (No-op whenever nothing is pending, i.e. always in the default mode.)
            FlushBufferedAppends();

            this.transactionBuffer.BeginTransaction();
        }
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public async Task CommitAsync()
    {
        lock (this.transactionLock)
        {
            if (!this.transactionBuffer.IsInTransaction)
            {
                throw new InvalidOperationException("No active transaction to commit");
            }
            
            // ✅ CRITICAL FIX: Flush buffered appends BEFORE closing transaction!
            FlushBufferedAppendsAndOverwrites();
            
            // Flush all buffered writes to disk
            this.transactionBuffer.Flush();
        }
        
        await Task.Yield();
    }

    /// <inheritdoc />
    /// <summary>
    /// Synchronous commit — identical work to <see cref="CommitAsync"/> but without the
    /// <c>Task.Yield()</c> thread-pool hop, eliminating scheduler overhead in hot sync paths.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public void CommitSync()
    {
        lock (this.transactionLock)
        {
            if (!this.transactionBuffer.IsInTransaction)
            {
                throw new InvalidOperationException("No active transaction to commit");
            }
            
            FlushBufferedAppendsAndOverwrites();
            this.transactionBuffer.Flush();
        }
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Rollback()
    {
        lock (this.transactionLock)
        {
            this.transactionBuffer.Clear();
        }
    }

    /// <inheritdoc />
    public bool IsInTransaction
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            // B7: lock-free bool read (atomic in .NET). The transaction lock guards writes; a
            // stale-by-one-frame read is harmless on this hot path (per-row update check).
            return this.transactionBuffer.IsInTransaction;
        }
    }

    /// <summary>
    /// Computes a unique page ID based on file path and position.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int ComputePageId(string path, long position)
    {
        int pathHash = path.GetHashCode();
        int pageNumber = (int)(position / this.pageSize);
        return HashCode.Combine(pathHash, pageNumber);
    }
}
