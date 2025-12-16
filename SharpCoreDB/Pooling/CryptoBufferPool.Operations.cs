// <copyright file="CryptoBufferPool.Operations.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Pooling;

using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Threading;

/// <summary>
/// CryptoBufferPool - Rent and return operations.
/// Contains buffer rental, return logic with secure clearing.
/// Part of the CryptoBufferPool partial class.
/// Modern C# 14 with ObjectDisposedException.ThrowIf and pattern matching.
/// See also: CryptoBufferPool.Core.cs, CryptoBufferPool.Diagnostics.cs
/// </summary>
public partial class CryptoBufferPool
{
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
    /// ✅ C# 14: ObjectDisposedException.ThrowIf and is pattern.
    /// </summary>
    /// <param name="minimumSize">The minimum required buffer size.</param>
    /// <param name="bufferType">The type of crypto buffer.</param>
    /// <returns>A rented crypto buffer.</returns>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private RentedCryptoBuffer RentBuffer(int minimumSize, CryptoBufferType bufferType)
    {
        ObjectDisposedException.ThrowIf(disposed, this);  // ✅ C# 14: ThrowIf

        if (minimumSize > maxBufferSize)
        {
            throw new ArgumentException($"Requested size {minimumSize} exceeds maximum {maxBufferSize}");
        }

        Interlocked.Increment(ref buffersRented);

        byte[] buffer;
        bool fromCache = false;

        // Try thread-local cache first
        if (config.UseThreadLocal && threadLocalCache?.Value is CryptoCache cache)  // ✅ C# 14: is pattern
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
    /// ✅ C# 14: is null and is pattern matching.
    /// </summary>
    /// <param name="buffer">The buffer to return.</param>
    /// <param name="usedSize">The number of bytes actually used.</param>
    /// <param name="bufferType">The type of crypto buffer.</param>
    /// <param name="fromCache">Whether buffer came from thread-local cache.</param>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    internal void Return(byte[] buffer, int usedSize, CryptoBufferType bufferType, bool fromCache)
    {
        if (disposed || buffer is null)  // ✅ C# 14: is null
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
        if (bufferType is CryptoBufferType.KeyMaterial && clearSize < buffer.Length)  // ✅ C# 14: is pattern
        {
            CryptographicOperations.ZeroMemory(buffer.AsSpan(clearSize));
        }

        // Return to cache or pool
        if (config.UseThreadLocal && threadLocalCache?.Value is CryptoCache cache)  // ✅ C# 14: is pattern
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
    /// ✅ C# 14: Target-typed new for statistics.
    /// </summary>
    /// <returns>Crypto buffer pool statistics.</returns>
    public CryptoBufferPoolStatistics GetStatistics()
    {
        return new()  // ✅ C# 14: Target-typed new
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
}
