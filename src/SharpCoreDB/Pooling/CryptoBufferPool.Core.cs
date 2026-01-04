// <copyright file="CryptoBufferPool.Core.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Pooling;

using System.Buffers;
using System.Threading;

/// <summary>
/// CryptoBufferPool - Core pool infrastructure.
/// Contains fields, constructor, configuration, and disposal logic.
/// Part of the CryptoBufferPool partial class.
/// Modern C# 14 with target-typed new and collection expressions.
/// See also: CryptoBufferPool.Operations.cs, CryptoBufferPool.Diagnostics.cs
/// </summary>
public partial class CryptoBufferPool : IDisposable
{
    private readonly ArrayPool<byte> bufferPool;
    private readonly ThreadLocal<CryptoCache> threadLocalCache;
    private readonly int maxBufferSize;
    private readonly PoolConfiguration config;
    private bool disposed;

    // Metrics
    private long buffersRented;
    private long buffersReturned;
    private long bytesCleared; // For security auditing

    /// <summary>
    /// Initializes a new instance of the <see cref="CryptoBufferPool"/> class.
    /// ✅ C# 14: Target-typed new for configuration.
    /// </summary>
    /// <param name="maxBufferSize">Maximum buffer size for crypto operations (default: 16MB).</param>
    /// <param name="config">Optional pool configuration.</param>
    public CryptoBufferPool(int maxBufferSize = 16 * 1024 * 1024, PoolConfiguration? config = null)
    {
        this.maxBufferSize = maxBufferSize;
        this.bufferPool = ArrayPool<byte>.Shared;
        
        // ✅ C# 14: Target-typed new
        this.config = config ?? new()
        {
            UseThreadLocal = true,
            ThreadLocalCapacity = 4, // Crypto typically needs: plaintext, ciphertext, tag, nonce
            ClearBuffersOnReturn = true, // CRITICAL for security
            ValidateOnReturn = true,
        };

        if (this.config.UseThreadLocal)
        {
            this.threadLocalCache = new ThreadLocal<CryptoCache>(
                () => new CryptoCache(this.config.ThreadLocalCapacity),
                trackAllValues: true); // SECURITY: Track all values to dispose them
        }
        else
        {
            this.threadLocalCache = null!;
        }
    }

    /// <summary>
    /// Disposes the pool and securely clears all cached buffers.
    /// SECURITY: Ensures all buffers are cleared before disposal.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Protected dispose method for proper IDisposable pattern.
    /// ✅ C# 14: is not null pattern.
    /// </summary>
    /// <param name="disposing">True if disposing managed resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!disposed)
        {
            if (disposing && threadLocalCache is not null)  // ✅ C# 14: is not null
            {
                // SECURITY: Dispose all thread-local caches to clear buffers
                foreach (var cache in threadLocalCache.Values)
                {
                    cache?.Dispose();
                }
                
                threadLocalCache.Dispose();
            }

            disposed = true;
        }
    }
}
