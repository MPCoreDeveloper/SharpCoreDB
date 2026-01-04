// <copyright file="CryptoBufferPool.Diagnostics.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Pooling;

using System.Runtime.CompilerServices;
using System.Security.Cryptography;

/// <summary>
/// CryptoBufferPool - Diagnostics, statistics, and nested types.
/// Contains CryptoCache, RentedCryptoBuffer, enums, and statistics types.
/// Part of the CryptoBufferPool partial class.
/// Modern C# 14 with primary constructors and collection expressions.
/// See also: CryptoBufferPool.Core.cs, CryptoBufferPool.Operations.cs
/// </summary>
public partial class CryptoBufferPool
{
    /// <summary>
    /// Thread-local cache for crypto buffers.
    /// SECURITY: All buffers are cleared when removed from cache.
    /// ✅ C# 14: Primary constructor.
    /// </summary>
    /// <param name="capacity">Cache capacity.</param>
    private sealed class CryptoCache(int capacity) : IDisposable
    {
        private readonly byte[][] buffers = new byte[capacity][];
        private int count;
        private bool disposed;

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
                if (buffers[i] is not null)  // ✅ C# 14: is not null
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
/// ✅ C# 14: Throw expressions and is not null patterns.
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
    /// ✅ C# 14: Throw expression.
    /// </summary>
    public readonly byte[] Buffer => buffer 
        ?? throw new ObjectDisposedException(nameof(RentedCryptoBuffer));  // ✅ C# 14: ?? throw

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
            if (value < 0 || (buffer is not null && value > buffer.Length))  // ✅ C# 14: is not null
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
    public readonly Span<byte> AsSpan() => buffer!.AsSpan();

    /// <summary>
    /// Gets a span view of the used portion.
    /// </summary>
    public readonly Span<byte> UsedSpan() => buffer!.AsSpan(0, usedSize);

    /// <summary>
    /// Gets a memory view of the buffer.
    /// </summary>
    public readonly Memory<byte> AsMemory() => buffer!.AsMemory();

    /// <summary>
    /// Gets a memory view of the used portion.
    /// </summary>
    public readonly Memory<byte> UsedMemory() => buffer!.AsMemory(0, usedSize);

    /// <summary>
    /// Returns the buffer to the pool with secure clearing.
    /// SECURITY: Buffer is cleared using CryptographicOperations.ZeroMemory.
    /// IDEMPOTENT: Safe to call multiple times.
    /// ✅ C# 14: is not null pattern.
    /// </summary>
    public void Dispose()
    {
        if (buffer is not null && pool is not null)  // ✅ C# 14: is not null
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
