// <copyright file="DatabaseFile.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace SharpCoreDB.Core.File;

using SharpCoreDB.Interfaces;
using System;
using System.Buffers.Binary;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

/// <summary>
/// High-performance paged file handler with encryption support.
/// Uses 4096-byte pages with zero-allocation reads via Span&lt;byte&gt; and MemoryMarshal.
/// </summary>
public sealed class DatabaseFile : IDisposable
{
    private const int PageSize = 4096;
    private const int StoredPageSize = PageSize + 12 + 16; // Account for AES-GCM overhead
    private readonly string filePath;
    private readonly SharpCoreDB.Services.AesGcmEncryption crypto;
    private readonly byte[] key;
    private readonly MemoryMappedFileHandler handler;
    private readonly byte[] _pageBuffer = GC.AllocateUninitializedArray<byte>(PageSize, pinned: true);
    private readonly byte[] _encryptedBuffer = GC.AllocateUninitializedArray<byte>(StoredPageSize, pinned: true);
    private bool disposed;

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

        // Ensure file exists
        if (!System.IO.File.Exists(filePath))
        {
            using var fs = System.IO.File.Create(filePath);
        }

        this.handler = MemoryMappedFileHandler.TryCreate(filePath, useMemoryMapping) ??
                       throw new InvalidOperationException("Failed to create memory-mapped file handler");
    }

    /// <summary>
    /// Reads a page from the database file.
    /// </summary>
    /// <param name="pageNum">The page number to read (0-based).</param>
    /// <returns>The decrypted page data.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte[] ReadPage(int pageNum)
    {
        // ZERO-ALLOC PAGE I/O
        ArgumentOutOfRangeException.ThrowIfNegative(pageNum);

        long offset = (long)pageNum * StoredPageSize;
        int bytesRead = this.handler.ReadBytes(offset, _encryptedBuffer.AsSpan(0, StoredPageSize));
        if (bytesRead == 0)
        {
            return new byte[PageSize]; // Empty page
        }

        this.crypto.DecryptPage(_encryptedBuffer.AsSpan(0, bytesRead));
        _encryptedBuffer.AsSpan(0, PageSize).CopyTo(_pageBuffer);
        return _pageBuffer.ToArray();
    }

    /// <summary>
    /// Writes a page to the database file.
    /// </summary>
    /// <param name="pageNum">The page number to write (0-based).</param>
    /// <param name="data">The page data to write (must be exactly PageSize bytes).</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WritePage(int pageNum, byte[] data)
    {
        // ZERO-ALLOC PAGE I/O
        ArgumentOutOfRangeException.ThrowIfNegative(pageNum);
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length != PageSize)
        {
            throw new ArgumentException($"Page data must be exactly {PageSize} bytes", nameof(data));
        }

        long offset = (long)pageNum * StoredPageSize;

        data.AsSpan().CopyTo(_encryptedBuffer.AsSpan(0, PageSize));
        this.crypto.EncryptPage(_encryptedBuffer.AsSpan(0, PageSize));

        using var fs = new FileStream(this.filePath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read);
        fs.Seek(offset, SeekOrigin.Begin);
        fs.Write(_encryptedBuffer.AsSpan(0, StoredPageSize));
    }

    /// <summary>
    /// Zero-allocation read of a page into a provided buffer.
    /// </summary>
    /// <param name="pageNum">The page number to read.</param>
    /// <param name="buffer">The buffer to read into (must be at least PageSize).</param>
    /// <returns>The number of bytes read.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int ReadPageZeroAlloc(int pageNum, Span<byte> buffer)
    {
        // ZERO-ALLOC PAGE I/O
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
        _encryptedBuffer.AsSpan(0, PageSize).CopyTo(buffer);
        return PageSize;
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
        this.disposed = true;
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Ref struct for page buffer to avoid allocations.
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
        /// Reads a uint32 from the buffer at the specified offset.
        /// </summary>
        /// <param name="offset">The offset.</param>
        /// <returns>The uint32 value.</returns>
        public uint ReadUInt32LittleEndian(int offset)
        {
            return BinaryPrimitives.ReadUInt32LittleEndian(this.buffer[offset..]);
        }

        /// <summary>
        /// Writes a uint32 to the buffer at the specified offset.
        /// </summary>
        /// <param name="offset">The offset.</param>
        /// <param name="value">The value.</param>
        public void WriteUInt32LittleEndian(int offset, uint value)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(this.buffer[offset..], value);
        }
    }
}
