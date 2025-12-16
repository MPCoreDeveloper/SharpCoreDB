// <copyright file="TemporaryBufferPool.Core.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Pooling;

using System.Buffers;
using System.Threading;

/// <summary>
/// TemporaryBufferPool - Core pool management.
/// Contains fields, constructor, configuration, and pool infrastructure.
/// Part of the TemporaryBufferPool partial class.
/// Modern C# 14 with collection expressions and primary constructors.
/// See also: TemporaryBufferPool.Operations.cs, TemporaryBufferPool.Diagnostics.cs
/// </summary>
public partial class TemporaryBufferPool : IDisposable
{
    private readonly ArrayPool<byte> bytePool;
    private readonly ArrayPool<char> charPool;
    private readonly ThreadLocal<TempBufferCache> threadLocalCache;
    private readonly PoolConfiguration config;
    private bool disposed;

    // Common buffer sizes for database operations
    private const int SmallBufferSize = 1024;      // 1KB - for small keys/values
    private const int MediumBufferSize = 8192;     // 8KB - for typical records
    private const int LargeBufferSize = 65536;     // 64KB - for large records
    private const int XLargeBufferSize = 262144;   // 256KB - for bulk operations

    // Metrics
    private long byteBuffersRented;
    private long charBuffersRented;
    private long cacheHits;
    private long cacheMisses;

    /// <summary>
    /// Initializes a new instance of the <see cref="TemporaryBufferPool"/> class.
    /// ✅ C# 14: Target-typed new for default config.
    /// </summary>
    /// <param name="config">Optional pool configuration.</param>
    public TemporaryBufferPool(PoolConfiguration? config = null)
    {
        this.bytePool = ArrayPool<byte>.Shared;
        this.charPool = ArrayPool<char>.Shared;
        
        // ✅ C# 14: Target-typed new expression
        this.config = config ?? new()
        {
            UseThreadLocal = true,
            ThreadLocalCapacity = 8, // Temp buffers used frequently
            ClearBuffersOnReturn = false, // Not sensitive data
        };

        if (this.config.UseThreadLocal)
        {
            this.threadLocalCache = new ThreadLocal<TempBufferCache>(
                () => new TempBufferCache(),
                trackAllValues: true); // Track all values to dispose them
        }
        else
        {
            this.threadLocalCache = null!;
        }
    }

    /// <summary>
    /// Disposes the pool and releases all resources.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases the unmanaged resources used by the pool and optionally releases managed resources.
    /// ✅ C# 14: is not null pattern.
    /// </summary>
    /// <param name="disposing">True to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!disposed)
        {
            if (disposing && threadLocalCache is not null)  // ✅ C# 14: is not null
            {
                // Dispose all thread-local caches to clear buffers
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
