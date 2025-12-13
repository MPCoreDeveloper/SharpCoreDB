// <copyright file="Storage.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Services;

using SharpCoreDB.Core.Cache;
using SharpCoreDB.Core.File;
using SharpCoreDB.Interfaces;
using System.IO;
using System.IO.Compression;
using System.IO.MemoryMappedFiles;
using System.Text;
using System.Runtime.CompilerServices;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Buffers;

/// <summary>
/// Implementation of IStorage using encrypted files and memory-mapped files for performance.
/// Includes SIMD-accelerated page scanning and pattern matching.
/// Now with integrated PageCache for high-performance page-level caching.
/// OPTIMIZED: Uses ArrayPool of byte for buffer pooling to reduce memory allocations.
/// </summary>
public class Storage : IStorage
{
    private readonly ICryptoService crypto;
    private readonly byte[] key;
    private readonly bool noEncryption;
    private readonly bool useMemoryMapping;
    private readonly PageCache? pageCache;
    private readonly int pageSize;
    private readonly ArrayPool<byte> bufferPool;

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
        this.useMemoryMapping = config?.UseMemoryMapping ?? true;
        this.pageCache = pageCache;
        this.pageSize = config?.PageSize ?? 4096;
        this.bufferPool = ArrayPool<byte>.Shared;
    }

    /// <inheritdoc />
    public void Write(string path, string data)
    {
        var plain = Encoding.UTF8.GetBytes(data);
        if (this.noEncryption)
        {
            File.WriteAllBytes(path, plain);
        }
        else
        {
            var encrypted = this.crypto.Encrypt(this.key, plain);
            File.WriteAllBytes(path, encrypted);
        }
    }

    /// <inheritdoc />
    public string? Read(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var data = File.ReadAllBytes(path);
        try
        {
            if (this.noEncryption)
            {
                return Encoding.UTF8.GetString(data);
            }
            else
            {
                var plain = this.crypto.Decrypt(this.key, data);
                return Encoding.UTF8.GetString(plain);
            }
        }
        catch
        {
            return null;
        }
    }

    /// <inheritdoc />
    public byte[]? ReadBytes(string path)
    {
        return ReadBytes(path, false);
    }

    /// <inheritdoc />
    public byte[]? ReadBytes(string path, bool noEncrypt)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        byte[] fileData = File.ReadAllBytes(path);

        var effectiveNoEncrypt = noEncrypt || this.noEncryption;
        if (effectiveNoEncrypt)
        {
            return fileData;
        }
        else
        {
            try
            {
                return this.crypto.Decrypt(this.key, fileData);
            }
            catch
            {
                // Assume plain data (for appended inserts)
                return fileData;
            }
        }
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public byte[]? ReadBytesAt(string path, long position, int maxLength, bool noEncrypt)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        // Calculate page ID for caching (position / pageSize)
        int pageId = ComputePageId(path, position);

        // Try page cache first if enabled
        if (this.pageCache != null && maxLength <= this.pageSize)
        {
            var cachedPage = this.pageCache.GetPage(pageId, (id) =>
            {
                // Cache miss - load from disk
                return LoadPageFromDisk(path, position, maxLength, noEncrypt);
            });

            try
            {
                // Extract data from cached page
                int offsetInPage = (int)(position % this.pageSize);
                int bytesToCopy = Math.Min(maxLength, this.pageSize - offsetInPage);
                
                var result = new byte[bytesToCopy];
                cachedPage.Buffer.Slice(offsetInPage, bytesToCopy).CopyTo(result);
                
                return result;
            }
            finally
            {
                this.pageCache.UnpinPage(pageId);
            }
        }

        // Fallback to direct read if cache disabled or page too large
        return ReadBytesAtDirect(path, position, maxLength, noEncrypt);
    }

    /// <summary>
    /// Computes a unique page ID based on file path and position.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int ComputePageId(string path, long position)
    {
        // Use hash of path + page number for unique ID
        int pathHash = path.GetHashCode();
        int pageNumber = (int)(position / this.pageSize);
        return HashCode.Combine(pathHash, pageNumber);
    }

    /// <summary>
    /// Loads a page from disk into a byte array for caching.
    /// OPTIMIZED: Uses pooled buffer from ReadBytesAtDirect which already implements ArrayPool.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private ReadOnlySpan<byte> LoadPageFromDisk(string path, long position, int maxLength, bool noEncrypt)
    {
        var data = ReadBytesAtDirect(path, position, maxLength, noEncrypt);
        if (data == null)
        {
            return ReadOnlySpan<byte>.Empty;
        }

        // Ensure data fits in page size
        if (data.Length > this.pageSize)
        {
            Array.Resize(ref data, this.pageSize);
        }

        return new ReadOnlySpan<byte>(data);
    }

    /// <summary>
    /// Direct read from disk without caching.
    /// OPTIMIZED: Uses ArrayPool of byte to reduce allocations.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private byte[]? ReadBytesAtDirect(string path, long position, int maxLength, bool noEncrypt)
    {
        byte[]? pooledBuffer = null;
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.RandomAccess);
            fs.Seek(position, SeekOrigin.Begin);
            
            // OPTIMIZED: Rent buffer from pool
            pooledBuffer = this.bufferPool.Rent(maxLength);
            Span<byte> bufferSpan = pooledBuffer.AsSpan(0, maxLength);
            
            int bytesRead = fs.Read(bufferSpan);
            if (bytesRead == 0)
            {
                return null;
            }

            var effectiveNoEncrypt = noEncrypt || this.noEncryption;
            
            byte[] result;
            if (effectiveNoEncrypt)
            {
                // Copy only the bytes read
                result = new byte[bytesRead];
                bufferSpan[..bytesRead].CopyTo(result);
            }
            else
            {
                try
                {
                    // Decrypt only the bytes read
                    var dataToDecrypt = new byte[bytesRead];
                    bufferSpan[..bytesRead].CopyTo(dataToDecrypt);
                    result = this.crypto.Decrypt(this.key, dataToDecrypt);
                }
                catch
                {
                    result = new byte[bytesRead];
                    bufferSpan[..bytesRead].CopyTo(result);
                }
            }
            
            return result;
        }
        finally
        {
            // SECURITY: Return pooled buffer with clearing
            if (pooledBuffer != null)
            {
                this.bufferPool.Return(pooledBuffer, clearArray: true);
            }
        }
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public long AppendBytes(string path, byte[] data)
    {
        // OPTIMIZED: Use Span-based operations and stackalloc for length prefix
        using var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough);
        long position = fs.Position;
        
        // Write length prefix using BinaryPrimitives
        Span<byte> lengthBuffer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(lengthBuffer, data.Length);
        fs.Write(lengthBuffer);
        
        // Write data directly
        fs.Write(data.AsSpan());
        
        // Invalidate cache for this page if it exists
        if (this.pageCache != null)
        {
            int pageId = ComputePageId(path, position);
            this.pageCache.EvictPage(pageId);
        }
        
        return position;
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public long[] AppendBytesMultiple(string path, List<byte[]> dataBlocks)
    {
        if (dataBlocks == null || dataBlocks.Count == 0)
            return Array.Empty<long>();

        var positions = new long[dataBlocks.Count];
        
        // OPTIMIZED: Single file open with batched writes
        using var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read, 65536, FileOptions.WriteThrough);
        
        Span<byte> lengthBuffer = stackalloc byte[4];
        
        for (int i = 0; i < dataBlocks.Count; i++)
        {
            var data = dataBlocks[i];
            
            // Record position before writing
            positions[i] = fs.Position;
            
            // Write length prefix using BinaryPrimitives
            BinaryPrimitives.WriteInt32LittleEndian(lengthBuffer, data.Length);
            fs.Write(lengthBuffer);
            
            // Write data directly
            fs.Write(data.AsSpan());
            
            // Invalidate cache for this page if it exists
            if (this.pageCache != null)
            {
                int pageId = ComputePageId(path, positions[i]);
                this.pageCache.EvictPage(pageId);
            }
        }
        
        return positions;
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public byte[]? ReadBytesFrom(string path, long offset)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        // OPTIMIZED: Use Span-based operations for reading
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan);
        fs.Seek(offset, SeekOrigin.Begin);
        
        // Read length prefix using BinaryPrimitives
        Span<byte> lengthBuffer = stackalloc byte[4];
        int bytesRead = fs.Read(lengthBuffer);
        if (bytesRead < 4) return null;
        
        int length = BinaryPrimitives.ReadInt32LittleEndian(lengthBuffer);
        
        // FIXED: Increased sanity check from 10MB to 1GB to allow realistic large data
        // Also added validation: length must be positive and reasonable
        const int MaxRowSize = 1_000_000_000; // 1 GB max per row (realistic for very large BLOBs)
        
        if (length < 0)
        {
            // Negative length indicates data corruption or misalignment
            Console.WriteLine($"❌ ReadBytesFrom: Invalid negative length {length} at offset {offset}");
            return null;
        }
        
        if (length == 0)
        {
            // Empty row is valid (e.g., all NULL fields)
            return Array.Empty<byte>();
        }
        
        if (length > MaxRowSize)
        {
            // Length exceeds reasonable limit - likely corruption
            Console.WriteLine($"⚠️  ReadBytesFrom: Suspiciously large length {length} at offset {offset} (max: {MaxRowSize})");
            return null;
        }
        
        // Read data
        byte[] data = new byte[length];
        bytesRead = fs.Read(data.AsSpan());
        
        if (bytesRead != length)
        {
            Console.WriteLine($"⚠️  ReadBytesFrom: Incomplete read at offset {offset}: got {bytesRead} bytes, expected {length}");
            return null;
        }
        
        return data;
    }

    /// <inheritdoc />
    public byte[]? ReadBytesAt(string path, long position, int maxLength)
    {
        return ReadBytesAt(path, position, maxLength, false);
    }

    /// <inheritdoc />
    public void WriteBytes(string path, byte[] data)
    {
        if (this.noEncryption)
        {
            File.WriteAllBytes(path, data);
        }
        else
        {
            var encrypted = this.crypto.Encrypt(this.key, data);
            File.WriteAllBytes(path, encrypted);
        }
        
        // Invalidate all cached pages for this file
        if (this.pageCache != null)
        {
            // Clear cache entries for this file path
            // Note: In a production system, you'd track which page IDs belong to which files
            this.pageCache.Clear(flushDirty: false);
        }
    }

    /// <inheritdoc />
    public string? ReadMemoryMapped(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        byte[]? pooledBuffer = null;
        try
        {
            using var mmf = MemoryMappedFile.CreateFromFile(path, FileMode.Open);
            using var accessor = mmf.CreateViewAccessor();
            var length = accessor.Capacity;
            
            // OPTIMIZED: Rent buffer from pool
            pooledBuffer = this.bufferPool.Rent((int)length);
            accessor.ReadArray(0, pooledBuffer, 0, (int)length);
            
            ReadOnlySpan<byte> dataSpan = pooledBuffer.AsSpan(0, (int)length);
            
            if (this.noEncryption)
            {
                return Encoding.UTF8.GetString(dataSpan);
            }
            else
            {
                var dataArray = dataSpan.ToArray();
                var plain = this.crypto.Decrypt(this.key, dataArray);
                return Encoding.UTF8.GetString(plain);
            }
        }
        finally
        {
            // SECURITY: Return pooled buffer with clearing
            if (pooledBuffer != null)
            {
                this.bufferPool.Return(pooledBuffer, clearArray: true);
            }
        }
    }

    /// <summary>
    /// Gets page cache diagnostics.
    /// </summary>
    /// <returns>Diagnostic string with cache stats.</returns>
    public string GetPageCacheDiagnostics()
    {
        return this.pageCache?.GetDiagnostics() ?? "PageCache not enabled";
    }

    /// <summary>
    /// Scans a page for a specific byte pattern using SIMD acceleration.
    /// OPTIMIZED: Uses ArrayPool of byte to reuse buffer across reads.
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <param name="pattern">The byte pattern to search for.</param>
    /// <returns>List of positions where pattern was found.</returns>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public List<long> ScanForPattern(string path, byte pattern)
    {
        var positions = new List<long>();
        
        if (!File.Exists(path))
            return positions;

        const int chunkSize = 64 * 1024; // 64KB chunks
        byte[]? pooledBuffer = null;
        
        try
        {
            // OPTIMIZED: Rent buffer from pool
            pooledBuffer = this.bufferPool.Rent(chunkSize);
            
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, chunkSize, FileOptions.SequentialScan);
            long filePosition = 0;
            
            while (true)
            {
                int bytesRead = fs.Read(pooledBuffer, 0, chunkSize);
                if (bytesRead == 0)
                    break;

                // SIMD: Use vectorized search on the pooled buffer
                int index = 0;
                while ((index = SimdHelper.IndexOf(pooledBuffer.AsSpan(index, bytesRead - index), pattern)) != -1)
                {
                    positions.Add(filePosition + index);
                    index++; // Move past this match
                }

                filePosition += bytesRead;
            }
            
            return positions;
        }
        finally
        {
            // SECURITY: Return pooled buffer with clearing
            if (pooledBuffer != null)
            {
                this.bufferPool.Return(pooledBuffer, clearArray: true);
            }
        }
    }

    /// <summary>
    /// Validates page integrity by checking for corruption using SIMD-accelerated checksums.
    /// </summary>
    /// <param name="pageData">The page data to validate.</param>
    /// <param name="expectedChecksum">The expected checksum.</param>
    /// <returns>True if page is valid.</returns>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public bool ValidatePageIntegrity(ReadOnlySpan<byte> pageData, int expectedChecksum)
    {
        if (pageData.IsEmpty)
            return false;

        // SIMD: Use vectorized hash computation for checksum
        int actualChecksum = SimdHelper.ComputeHashCode(pageData);
        return actualChecksum == expectedChecksum;
    }

    /// <summary>
    /// Compares two pages for equality using SIMD acceleration.
    /// Useful for detecting duplicate pages or verifying writes.
    /// </summary>
    /// <param name="page1">First page data.</param>
    /// <param name="page2">Second page data.</param>
    /// <returns>True if pages are identical.</returns>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public bool ComparePagesSimd(ReadOnlySpan<byte> page1, ReadOnlySpan<byte> page2)
    {
        return SimdHelper.SequenceEqual(page1, page2);
    }

    /// <summary>
    /// Zeros a page buffer using SIMD acceleration for security.
    /// Faster than Array.Clear() for large buffers.
    /// </summary>
    /// <param name="pageBuffer">The buffer to zero.</param>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public void SecureZeroPage(Span<byte> pageBuffer)
    {
        SimdHelper.ZeroBuffer(pageBuffer);
    }
}
