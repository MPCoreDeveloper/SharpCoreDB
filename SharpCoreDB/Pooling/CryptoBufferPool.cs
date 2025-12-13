// <copyright file="CryptoBufferPool.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Pooling;

using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Threading;

/// <summary>
/// Specialized buffer pool for cryptographic operations with secure cleanup.
/// SECURITY: All buffers are cleared on return to prevent key/plaintext leakage.
/// PERFORMANCE: Thread-local caching for zero-contention in high-throughput scenarios.
/// MEMORY: Reuses buffers for encryption/decryption to minimize GC pressure.
/// </summary>
public class CryptoBufferPool : IDisposable
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
    /// </summary>
    /// <param name="maxBufferSize">Maximum buffer size for crypto operations (default: 16MB).</param>
    /// <param name="config">Optional pool configuration.</param>
    public CryptoBufferPool(int maxBufferSize = 16 * 1024 * 1024, PoolConfiguration? config = null)
    {
        this.maxBufferSize = maxBufferSize;
        this.bufferPool = ArrayPool<byte>.Shared;
        this.config = config ?? new PoolConfiguration
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
    /// Rents a buffer for encryption operations.
    /// SECURITY: Buffer is cleared on return to prevent plaintext/key leakage.
    /// USAGE: Always use with 'using' statement or ensure Return() is called.
    /// </summary>
    /// <param name="minimumSize">The minimum required buffer size.</param>
    /// <returns>A rented crypto buffer.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public RentedCryptoBuffer RentEncryptionBuffer(int minimumSize)
    {
        return RentBuffer(minimumSize, CryptoBufferType.Encryption);
    }

    /// <summary>
    /// Rents a buffer for decryption operations.
    /// SECURITY: Buffer is cleared on return to prevent plaintext/key leakage.
    /// </summary>
    /// <param name="minimumSize">The minimum required buffer size.</param>
    /// <returns>A rented crypto buffer.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public RentedCryptoBuffer RentDecryptionBuffer(int minimumSize)
    {
        return RentBuffer(minimumSize, CryptoBufferType.Decryption);
    }

    /// <summary>
    /// Rents a buffer for key material (keys, nonces, tags).
    /// SECURITY: Highest priority for clearing - crypto keys must not leak.
    /// RECOMMENDATION: Use this for AES keys, nonces, authentication tags.
    /// </summary>
    /// <param name="minimumSize">The minimum required buffer size.</param>
    /// <returns>A rented crypto buffer.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public RentedCryptoBuffer RentKeyBuffer(int minimumSize)
    {
        return RentBuffer(minimumSize, CryptoBufferType.KeyMaterial);
    }

    /// <summary>
    /// Rents a buffer for generic crypto operations.
    /// SECURITY: Buffer is cleared on return like all crypto buffers.
    /// </summary>
    /// <param name="minimumSize">The minimum required buffer size.</param>
    /// <param name="bufferType">The type of crypto buffer.</param>
    /// <returns>A rented crypto buffer.</returns>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private RentedCryptoBuffer RentBuffer(int minimumSize, CryptoBufferType bufferType)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        if (minimumSize > maxBufferSize)
        {
            throw new ArgumentException($"Requested size {minimumSize} exceeds maximum {maxBufferSize}");
        }

        Interlocked.Increment(ref buffersRented);

        byte[] buffer;
        bool fromCache = false;

        // Try thread-local cache first
        if (config.UseThreadLocal && threadLocalCache?.Value is CryptoCache cache)
        {
            if (cache.TryRent(minimumSize, out buffer))
            {
                fromCache = true;
            }
            else
            {
                buffer = bufferPool.Rent(minimumSize);
            }
        }
        else
        {
            buffer = bufferPool.Rent(minimumSize);
        }

        // SECURITY: Zero out buffer before use
        CryptographicOperations.ZeroMemory(buffer.AsSpan(0, Math.Min(minimumSize, buffer.Length)));

        return new RentedCryptoBuffer(buffer, this, minimumSize, bufferType, fromCache);
    }

    /// <summary>
    /// Returns a crypto buffer to the pool.
    /// SECURITY: ALWAYS clears buffer using CryptographicOperations.ZeroMemory.
    /// INTERNAL: Called automatically by RentedCryptoBuffer.Dispose().
    /// </summary>
    /// <param name="buffer">The buffer to return.</param>
    /// <param name="usedSize">The number of bytes actually used.</param>
    /// <param name="bufferType">The type of crypto buffer.</param>
    /// <param name="fromCache">Whether buffer came from thread-local cache.</param>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    internal void Return(byte[] buffer, int usedSize, CryptoBufferType bufferType, bool fromCache)
    {
        if (disposed || buffer == null)
        {
            return;
        }

        Interlocked.Increment(ref buffersReturned);

        // SECURITY: CRITICAL - Always clear crypto buffers
        // Use CryptographicOperations.ZeroMemory which is guaranteed not to be optimized away
        int clearSize = Math.Min(usedSize > 0 ? usedSize : buffer.Length, buffer.Length);
        CryptographicOperations.ZeroMemory(buffer.AsSpan(0, clearSize));
        Interlocked.Add(ref bytesCleared, clearSize);

        // Extra paranoia for key material - clear entire buffer
        if (bufferType == CryptoBufferType.KeyMaterial && clearSize < buffer.Length)
        {
            CryptographicOperations.ZeroMemory(buffer.AsSpan(clearSize));
        }

        // Return to cache or pool
        if (config.UseThreadLocal && threadLocalCache?.Value is CryptoCache cache)
        {
            if (!cache.TryReturn(buffer))
            {
                // Cache full, return to ArrayPool
                bufferPool.Return(buffer, clearArray: false); // Already cleared
            }
        }
        else
        {
            bufferPool.Return(buffer, clearArray: false); // Already cleared
        }
    }

    /// <summary>
    /// Gets pool statistics for monitoring and security auditing.
    /// </summary>
    /// <returns>Crypto buffer pool statistics.</returns>
    public CryptoBufferPoolStatistics GetStatistics()
    {
        return new CryptoBufferPoolStatistics
        {
            BuffersRented = Interlocked.Read(ref buffersRented),
            BuffersReturned = Interlocked.Read(ref buffersReturned),
            BytesCleared = Interlocked.Read(ref bytesCleared),
            MaxBufferSize = maxBufferSize,
            ThreadLocalEnabled = config.UseThreadLocal,
            ThreadLocalCapacity = config.ThreadLocalCapacity,
        };
    }

    /// <summary>
    /// Resets pool statistics.
    /// NOTE: Does not reset BytesCleared for security audit trail.
    /// </summary>
    public void ResetStatistics()
    {
        Interlocked.Exchange(ref buffersRented, 0);
        Interlocked.Exchange(ref buffersReturned, 0);
        // Intentionally keep bytesCleared for audit trail
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
    /// </summary>
    /// <param name="disposing">True if disposing managed resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!disposed)
        {
            if (disposing && threadLocalCache != null)
            {
                // SECURITY: Dispose all thread-local caches to clear buffers
                // Clear all tracked cache instances
                foreach (var cache in threadLocalCache.Values)
                {
                    cache?.Dispose();
                }
                
                threadLocalCache.Dispose();
            }

            disposed = true;
        }
    }

    /// <summary>
    /// Thread-local cache for crypto buffers.
    /// SECURITY: All buffers are cleared when removed from cache.
    /// </summary>
    private sealed class CryptoCache(int capacity) : IDisposable
    {
        private readonly byte[][] buffers = new byte[capacity][];
        private int count = 0;
        private bool disposed = false;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryRent(int minimumSize, out byte[] buffer)
        {
            // Find buffer of adequate size
            for (int i = count - 1; i >= 0; i--)
            {
                if (buffers[i].Length >= minimumSize)
                {
                    buffer = buffers[i];
                    
                    // Remove from cache
                    for (int j = i; j < count - 1; j++)
                    {
                        buffers[j] = buffers[j + 1];
                    }
                    buffers[--count] = null!;
                    
                    return true;
                }
            }

            buffer = null!;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryReturn(byte[] buffer)
        {
            if (count < buffers.Length)
            {
                buffers[count++] = buffer;
                return true;
            }

            return false;
        }

        public void Clear()
        {
            // SECURITY: Clear all cached buffers
            for (int i = 0; i < count; i++)
            {
                if (buffers[i] != null)
                {
                    CryptographicOperations.ZeroMemory(buffers[i]);
                    buffers[i] = null!;
                }
            }
            count = 0;
        }

        /// <summary>
        /// Disposes the cache and securely clears all buffers.
        /// SECURITY: Ensures crypto buffers are cleared on disposal.
        /// </summary>
        public void Dispose()
        {
            if (!disposed)
            {
                Clear();
                disposed = true;
            }
        }
    }
}

/// <summary>
/// Type of crypto buffer for tracking and auditing.
/// </summary>
public enum CryptoBufferType
{
    /// <summary>
    /// Buffer for encryption operations (plaintext input).
    /// </summary>
    Encryption,

    /// <summary>
    /// Buffer for decryption operations (ciphertext input).
    /// </summary>
    Decryption,

    /// <summary>
    /// Buffer for key material (keys, nonces, tags) - highest security priority.
    /// </summary>
    KeyMaterial,

    /// <summary>
    /// Generic crypto buffer.
    /// </summary>
    Generic,
}

/// <summary>
/// RAII wrapper for rented crypto buffer.
/// SECURITY: Automatically clears buffer on disposal using secure memory clearing.
/// USAGE: ALWAYS use with 'using' statement to ensure proper cleanup.
/// </summary>
public ref struct RentedCryptoBuffer
{
    private byte[]? buffer;
    private CryptoBufferPool? pool;
    private int usedSize;
    private readonly CryptoBufferType bufferType;
    private readonly bool fromCache;

    /// <summary>
    /// Initializes a new instance of the <see cref="RentedCryptoBuffer"/> struct.
    /// INTERNAL: Created by CryptoBufferPool only.
    /// </summary>
    internal RentedCryptoBuffer(
        byte[] buffer,
        CryptoBufferPool pool,
        int requestedSize,
        CryptoBufferType bufferType,
        bool fromCache)
    {
        this.buffer = buffer;
        this.pool = pool;
        this.usedSize = 0;
        this.bufferType = bufferType;
        this.fromCache = fromCache;
    }

    /// <summary>
    /// Gets the underlying buffer array.
    /// SECURITY: Buffer will be cleared on disposal.
    /// WARNING: Do not store references to this buffer beyond disposal.
    /// </summary>
    public readonly byte[] Buffer => buffer 
        ?? throw new ObjectDisposedException(nameof(RentedCryptoBuffer));

    /// <summary>
    /// Gets or sets the number of bytes actually used.
    /// SECURITY: Only this portion is guaranteed to be cleared.
    /// RECOMMENDATION: Always set this to the actual used size.
    /// </summary>
    public int UsedSize
    {
        readonly get => usedSize;
        set
        {
            if (value < 0 || (buffer != null && value > buffer.Length))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }
            usedSize = value;
        }
    }

    /// <summary>
    /// Gets the buffer type.
    /// </summary>
    public readonly CryptoBufferType BufferType => bufferType;

    /// <summary>
    /// Gets a span view of the buffer.
    /// SECURITY: Data in span will be cleared on disposal.
    /// </summary>
    public readonly Span<byte> AsSpan() => buffer.AsSpan();

    /// <summary>
    /// Gets a span view of the used portion.
    /// </summary>
    public readonly Span<byte> UsedSpan() => buffer.AsSpan(0, usedSize);

    /// <summary>
    /// Gets a memory view of the buffer.
    /// </summary>
    public readonly Memory<byte> AsMemory() => buffer.AsMemory();

    /// <summary>
    /// Gets a memory view of the used portion.
    /// </summary>
    public readonly Memory<byte> UsedMemory() => buffer.AsMemory(0, usedSize);

    /// <summary>
    /// Returns the buffer to the pool with secure clearing.
    /// SECURITY: Buffer is cleared using CryptographicOperations.ZeroMemory.
    /// IDEMPOTENT: Safe to call multiple times.
    /// </summary>
    public void Dispose()
    {
        if (buffer != null && pool != null)
        {
            pool.Return(buffer, usedSize, bufferType, fromCache);
            buffer = null;
            pool = null;
        }
    }
}

/// <summary>
/// Statistics for crypto buffer pool monitoring and security auditing.
/// </summary>
public class CryptoBufferPoolStatistics
{
    /// <summary>
    /// Gets or sets the total number of buffers rented (lifetime).
    /// </summary>
    public long BuffersRented { get; set; }

    /// <summary>
    /// Gets or sets the total number of buffers returned (lifetime).
    /// </summary>
    public long BuffersReturned { get; set; }

    /// <summary>
    /// Gets or sets the total number of bytes securely cleared (lifetime).
    /// SECURITY: This metric demonstrates secure cleanup is working.
    /// </summary>
    public long BytesCleared { get; set; }

    /// <summary>
    /// Gets or sets the maximum buffer size.
    /// </summary>
    public int MaxBufferSize { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether thread-local caching is enabled.
    /// </summary>
    public bool ThreadLocalEnabled { get; set; }

    /// <summary>
    /// Gets or sets the thread-local cache capacity.
    /// </summary>
    public int ThreadLocalCapacity { get; set; }

    /// <summary>
    /// Gets the number of buffers currently outstanding.
    /// DIAGNOSTICS: Should be 0 when all operations complete.
    /// </summary>
    public long OutstandingBuffers => BuffersRented - BuffersReturned;
}
