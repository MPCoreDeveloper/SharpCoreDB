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
        this.pageCache = pageCache;
        this.pageSize = config?.PageSize ?? 4096;
        this.bufferPool = ArrayPool<byte>.Shared;
        
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
            
            // Flush all buffered writes to disk
            this.transactionBuffer.Flush();
        }
        
        await Task.Yield();
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
            lock (this.transactionLock)
            {
                return this.transactionBuffer.IsInTransaction;
            }
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
