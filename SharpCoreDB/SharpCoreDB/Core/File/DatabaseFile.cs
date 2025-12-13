// <copyright file="DatabaseFile.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Core.File;

using SharpCoreDB.Interfaces;
using System;
using System.Buffers.Binary;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

/// <summary>
/// High-performance paged file handler with encryption support and zero-allocation page I/O.
/// Uses PageSerializer with MemoryMarshal for struct serialization and stackalloc for temp buffers.
/// </summary>
public sealed class DatabaseFile : IDisposable
{
    private const int PageSize = 4096;
    private const int StoredPageSize = PageSize + 12 + 16; // Account for AES-GCM overhead
    private readonly string filePath;
    private readonly SharpCoreDB.Services.AesGcmEncryption crypto;
    private readonly byte[] key;
    private readonly MemoryMappedFileHandler handler;
    
    // Pinned buffers for reuse (eliminates per-call allocations)
    private readonly byte[] _pageBuffer = GC.AllocateUninitializedArray<byte>(PageSize, pinned: true);
    private readonly byte[] _encryptedBuffer = GC.AllocateUninitializedArray<byte>(StoredPageSize, pinned: true);
    private bool disposed;
    private ulong _currentTransactionId;

    /// <summary>
    /// Initializes a new instance of the <see cref="DatabaseFile"/> class.
    /// </summary>
    /// <param name="filePath">The path to the database file.</param>
    /// <param name="crypto">The crypto service for encryption/decryption.</param>
    /// <param name="key">The encryption key.</param>
    /// <param name="useMemoryMapping">Whether to use memory-mapped files for performance.</param>
    public DatabaseFile(string filePath, ICryptoService crypto, byte[] key, bool useMemoryMapping = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(crypto);
        ArgumentNullException.ThrowIfNull(key);

        this.filePath = filePath;
        this.crypto = crypto.GetAesGcmEncryption(key);
        this.key = key;
        this._currentTransactionId = (ulong)DateTime.UtcNow.Ticks;

        // Ensure file exists
        if (!System.IO.File.Exists(filePath))
        {
            using var fs = System.IO.File.Create(filePath);
        }

        this.handler = MemoryMappedFileHandler.TryCreate(filePath, useMemoryMapping) ??
                       throw new InvalidOperationException("Failed to create memory-mapped file handler");
    }

    /// <summary>
    /// Reads a page from the database file using zero-allocation PageSerializer.
    /// </summary>
    /// <param name="pageNum">The page number to read (0-based).</param>
    /// <returns>The decrypted page data.</returns>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public byte[] ReadPage(int pageNum)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pageNum);

        long offset = (long)pageNum * StoredPageSize;
        int bytesRead = this.handler.ReadBytes(offset, _encryptedBuffer.AsSpan(0, StoredPageSize));
        if (bytesRead == 0)
        {
            // Return empty page with valid header
            var header = PageHeader.Create((byte)PageType.Data, _currentTransactionId);
            Span<byte> resultBuffer = stackalloc byte[PageSize];
            PageSerializer.SerializeHeader(ref header, resultBuffer);
            return resultBuffer.ToArray();
        }

        // Decrypt page
        this.crypto.DecryptPage(_encryptedBuffer.AsSpan(0, bytesRead));
        
        // Validate page integrity using PageSerializer
        if (!PageSerializer.ValidatePage(_encryptedBuffer.AsSpan(0, PageSize)))
        {
            throw new InvalidOperationException($"Page {pageNum} failed integrity check");
        }

        // Return copy
        var result = new byte[PageSize];
        _encryptedBuffer.AsSpan(0, PageSize).CopyTo(result);
        return result;
    }

    /// <summary>
    /// Writes a page using zero-allocation PageSerializer with MemoryMarshal.
    /// </summary>
    /// <param name="pageNum">The page number to write (0-based).</param>
    /// <param name="data">The page data to write (must be exactly PageSize bytes).</param>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public void WritePage(int pageNum, byte[] data)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pageNum);
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length != PageSize)
        {
            throw new ArgumentException($"Page data must be exactly {PageSize} bytes", nameof(data));
        }

        WritePageFromSpan(pageNum, data.AsSpan());
    }

    /// <summary>
    /// Writes a page from a Span using zero-allocation PageSerializer.
    /// </summary>
    /// <param name="pageNum">The page number to write (0-based).</param>
    /// <param name="data">The page data to write (must be exactly PageSize bytes).</param>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public void WritePageFromSpan(int pageNum, ReadOnlySpan<byte> data)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pageNum);
        if (data.Length != PageSize)
        {
            throw new ArgumentException($"Page data must be exactly {PageSize} bytes", nameof(data));
        }

        long offset = (long)pageNum * StoredPageSize;

        // Copy to encryption buffer and encrypt in-place
        data.CopyTo(_encryptedBuffer.AsSpan(0, PageSize));
        this.crypto.EncryptPage(_encryptedBuffer.AsSpan(0, StoredPageSize));

        // Write to disk
        using var fs = new FileStream(this.filePath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough);
        fs.Seek(offset, SeekOrigin.Begin);
        fs.Write(_encryptedBuffer.AsSpan(0, StoredPageSize));
        fs.Flush(flushToDisk: true);
    }

    /// <summary>
    /// Zero-allocation read of a page into a provided buffer using PageSerializer.
    /// </summary>
    /// <param name="pageNum">The page number to read.</param>
    /// <param name="buffer">The buffer to read into (must be at least PageSize).</param>
    /// <returns>The number of bytes read.</returns>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public int ReadPageZeroAlloc(int pageNum, Span<byte> buffer)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pageNum);
        if (buffer.Length < PageSize)
        {
            throw new ArgumentException($"Buffer must be at least {PageSize} bytes", nameof(buffer));
        }

        long offset = (long)pageNum * StoredPageSize;
        int bytesRead = this.handler.ReadBytes(offset, _encryptedBuffer.AsSpan(0, StoredPageSize));
        if (bytesRead == 0)
        {
            buffer.Clear();
            return 0;
        }

        this.crypto.DecryptPage(_encryptedBuffer.AsSpan(0, bytesRead));
        
        // Validate with PageSerializer
        if (!PageSerializer.ValidatePage(_encryptedBuffer.AsSpan(0, PageSize)))
        {
            throw new InvalidOperationException($"Page {pageNum} failed integrity check");
        }

        _encryptedBuffer.AsSpan(0, PageSize).CopyTo(buffer);
        return PageSize;
    }

    /// <summary>
    /// Creates and writes a new page with header using PageSerializer.
    /// </summary>
    /// <param name="pageNum">The page number.</param>
    /// <param name="pageType">The page type.</param>
    /// <param name="data">The page data (excluding header).</param>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public void WritePageWithHeader(int pageNum, PageType pageType, ReadOnlySpan<byte> data)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pageNum);

        // Create header
        var header = PageHeader.Create((byte)pageType, ++_currentTransactionId);

        // Build complete page using stackalloc
        Span<byte> pageBuffer = stackalloc byte[PageSize];
        PageSerializer.CreatePage(ref header, data, pageBuffer);

        // Write page
        WritePageFromSpan(pageNum, pageBuffer);
    }

    /// <summary>
    /// Reads page data (excluding header) using PageSerializer.
    /// </summary>
    /// <param name="pageNum">The page number.</param>
    /// <param name="dataBuffer">Buffer to receive data.</param>
    /// <returns>Number of data bytes read.</returns>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public int ReadPageData(int pageNum, Span<byte> dataBuffer)
    {
        // Read full page using zero-alloc method
        Span<byte> pageBuffer = stackalloc byte[PageSize];
        int bytesRead = ReadPageZeroAlloc(pageNum, pageBuffer);
        if (bytesRead == 0)
            return 0;

        // Extract data using PageSerializer
        var data = PageSerializer.GetPageData(pageBuffer, out int dataLength);
        if (dataLength > dataBuffer.Length)
            throw new ArgumentException("Data buffer too small", nameof(dataBuffer));

        data.CopyTo(dataBuffer);
        return dataLength;
    }

    /// <summary>
    /// Gets the total number of pages in the file.
    /// </summary>
    public long PageCount => this.handler.FileSize / StoredPageSize;

    /// <summary>
    /// Disposes the database file handler.
    /// </summary>
    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        this.handler.Dispose();
        this.crypto.Dispose();
        this.disposed = true;
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Ref struct for page buffer to avoid allocations.
    /// OPTIMIZED: Uses PageSerializer for all operations.
    /// </summary>
    public ref struct PageBuffer
    {
        private Span<byte> buffer;

        /// <summary>
        /// Initializes a new instance of the <see cref="PageBuffer"/> struct.
        /// </summary>
        /// <param name="size">The buffer size.</param>
        public PageBuffer(int size)
        {
            this.buffer = new byte[size];
        }

        /// <summary>
        /// Gets the buffer as a span.
        /// </summary>
        public Span<byte> Span => this.buffer;

        /// <summary>
        /// Reads a uint32 from the buffer using PageSerializer.
        /// </summary>
        /// <param name="offset">The offset.</param>
        /// <returns>The uint32 value.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint ReadUInt32LittleEndian(int offset)
        {
            return PageSerializer.ReadUInt32(this.buffer[offset..]);
        }

        /// <summary>
        /// Writes a uint32 to the buffer using PageSerializer.
        /// </summary>
        /// <param name="offset">The offset.</param>
        /// <param name="value">The value.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteUInt32LittleEndian(int offset, uint value)
        {
            PageSerializer.WriteUInt32(this.buffer[offset..], value);
        }

        /// <summary>
        /// Reads an int32 from the buffer using PageSerializer.
        /// </summary>
        /// <param name="offset">The offset.</param>
        /// <returns>The int32 value.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ReadInt32LittleEndian(int offset)
        {
            return PageSerializer.ReadInt32(this.buffer[offset..]);
        }

        /// <summary>
        /// Writes an int32 to the buffer using PageSerializer.
        /// </summary>
        /// <param name="offset">The offset.</param>
        /// <param name="value">The value.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteInt32LittleEndian(int offset, int value)
        {
            PageSerializer.WriteInt32(this.buffer[offset..], value);
        }
    }
}
