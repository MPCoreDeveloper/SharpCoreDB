// <copyright file="DatabaseFile.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Core.File;

using SharpCoreDB.Interfaces;
using System;
using System.IO;
using System.Runtime.CompilerServices;

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
    
    // Pinned buffers for reuse (eliminates per-call allocations)
    private readonly byte[] _encryptedBuffer = GC.AllocateUninitializedArray<byte>(StoredPageSize, pinned: true);
    private bool disposed;
    private ulong _currentTransactionId;

    /// <summary>
    /// Initializes a new instance of the <see cref="DatabaseFile"/> class.
    /// </summary>
    /// <param name="filePath">The path to the database file.</param>
    /// <param name="crypto">The crypto service for encryption/decryption.</param>
    /// <param name="key">The encryption key.</param>
    public DatabaseFile(string filePath, ICryptoService crypto, byte[] key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(crypto);
        ArgumentNullException.ThrowIfNull(key);

        this.filePath = filePath;
        this.crypto = crypto.GetAesGcmEncryption(key);
        this._currentTransactionId = (ulong)DateTime.UtcNow.Ticks;

        // Ensure file exists
        if (!System.IO.File.Exists(filePath))
        {
            using var fs = System.IO.File.Create(filePath);
        }
    }

    /// <summary>
    /// Reads a page from the database file using zero-allocation PageSerializer.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public byte[] ReadPage(int pageNum)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pageNum);

        long offset = (long)pageNum * StoredPageSize;
        
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan);
        fs.Seek(offset, SeekOrigin.Begin);
        int bytesRead = fs.Read(_encryptedBuffer.AsSpan(0, StoredPageSize));
        
        if (bytesRead == 0)
        {
            var header = PageHeader.Create((byte)PageType.Data, _currentTransactionId);
            Span<byte> resultBuffer = stackalloc byte[PageSize];
            PageSerializer.SerializeHeader(ref header, resultBuffer);
            return resultBuffer.ToArray();
        }

        this.crypto.DecryptPage(_encryptedBuffer.AsSpan(0, bytesRead));
        
        if (!PageSerializer.ValidatePage(_encryptedBuffer.AsSpan(0, PageSize)))
        {
            throw new InvalidOperationException($"Page {pageNum} failed integrity check");
        }

        var result = new byte[PageSize];
        _encryptedBuffer.AsSpan(0, PageSize).CopyTo(result);
        return result;
    }

    /// <summary>
    /// Writes a page using zero-allocation PageSerializer with MemoryMarshal.
    /// </summary>
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
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public void WritePageFromSpan(int pageNum, ReadOnlySpan<byte> data)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pageNum);
        if (data.Length != PageSize)
        {
            throw new ArgumentException($"Page data must be exactly {PageSize} bytes", nameof(data));
        }

        long offset = (long)pageNum * StoredPageSize;

        data.CopyTo(_encryptedBuffer.AsSpan(0, PageSize));
        this.crypto.EncryptPage(_encryptedBuffer.AsSpan(0, StoredPageSize));

        using var fs = new FileStream(this.filePath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough);
        fs.Seek(offset, SeekOrigin.Begin);
        fs.Write(_encryptedBuffer.AsSpan(0, StoredPageSize));
        fs.Flush(flushToDisk: true);
    }

    /// <summary>
    /// Zero-allocation read of a page into a provided buffer using PageSerializer.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public int ReadPageZeroAlloc(int pageNum, Span<byte> buffer)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pageNum);
        if (buffer.Length < PageSize)
        {
            throw new ArgumentException($"Buffer must be at least {PageSize} bytes", nameof(buffer));
        }

        long offset = (long)pageNum * StoredPageSize;
        
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan);
        fs.Seek(offset, SeekOrigin.Begin);
        int bytesRead = fs.Read(_encryptedBuffer.AsSpan(0, StoredPageSize));
        
        if (bytesRead == 0)
        {
            buffer.Clear();
            return 0;
        }

        this.crypto.DecryptPage(_encryptedBuffer.AsSpan(0, bytesRead));
        
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
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public void WritePageWithHeader(int pageNum, PageType pageType, ReadOnlySpan<byte> data)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pageNum);

        var header = PageHeader.Create((byte)pageType, ++_currentTransactionId);

        Span<byte> pageBuffer = stackalloc byte[PageSize];
        PageSerializer.CreatePage(ref header, data, pageBuffer);

        WritePageFromSpan(pageNum, pageBuffer);
    }

    /// <summary>
    /// Reads page data (excluding header) using PageSerializer.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public int ReadPageData(int pageNum, Span<byte> dataBuffer)
    {
        Span<byte> pageBuffer = stackalloc byte[PageSize];
        int bytesRead = ReadPageZeroAlloc(pageNum, pageBuffer);
        if (bytesRead == 0)
            return 0;

        var data = PageSerializer.GetPageData(pageBuffer, out int dataLength);
        if (dataLength > dataBuffer.Length)
            throw new ArgumentException("Data buffer too small", nameof(dataBuffer));

        data.CopyTo(dataBuffer);
        return dataLength;
    }

    /// <summary>
    /// Gets the total number of pages in the file.
    /// </summary>
    public long PageCount
    {
        get
        {
            if (!System.IO.File.Exists(filePath))
                return 0;
            return new FileInfo(filePath).Length / StoredPageSize;
        }
    }

    /// <summary>
    /// Disposes the database file handler.
    /// </summary>
    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        this.crypto.Dispose();
        this.disposed = true;
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Ref struct for page buffer to avoid allocations.
    /// </summary>
    public readonly ref struct PageBuffer(int size)
    {
        /// <summary>
        /// Gets the buffer as a span.
        /// </summary>
        public readonly Span<byte> Span { get; } = new byte[size];

        /// <summary>
        /// Reads a uint32 from the buffer.
        /// </summary>
        /// <param name="offset">The offset in bytes.</param>
        /// <returns>The uint32 value.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly uint ReadUInt32LittleEndian(int offset) => PageSerializer.ReadUInt32(Span[offset..]);

        /// <summary>
        /// Writes a uint32 to the buffer.
        /// </summary>
        /// <param name="offset">The offset in bytes.</param>
        /// <param name="value">The value to write.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly void WriteUInt32LittleEndian(int offset, uint value) => PageSerializer.WriteUInt32(Span[offset..], value);

        /// <summary>
        /// Reads an int32 from the buffer.
        /// </summary>
        /// <param name="offset">The offset in bytes.</param>
        /// <returns>The int32 value.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly int ReadInt32LittleEndian(int offset) => PageSerializer.ReadInt32(Span[offset..]);

        /// <summary>
        /// Writes an int32 to the buffer.
        /// </summary>
        /// <param name="offset">The offset in bytes.</param>
        /// <param name="value">The value to write.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly void WriteInt32LittleEndian(int offset, int value) => PageSerializer.WriteInt32(Span[offset..], value);
    }
}
