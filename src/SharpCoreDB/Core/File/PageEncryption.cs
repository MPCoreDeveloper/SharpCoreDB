// <copyright file="PageEncryption.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Core.File;

using SharpCoreDB.Interfaces;
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using SharpCoreDB.Services;

/// <summary>
/// Page-level encryption wrapper that encrypts/decrypts data at page granularity.
/// Replaces full-file encryption with more efficient per-page encryption.
/// 
/// Key benefits:
/// - Only modified pages are encrypted (not entire file)
/// - Supports streaming/incremental encryption
/// - Allows partial page reads without decrypting full file
/// - Better CPU cache locality
/// </summary>
public class PageEncryption : IDisposable
{
    private readonly ICryptoService crypto;
    private readonly AesGcmEncryption aes;
    private readonly byte[] key;
    private readonly int pageSize;
    
    private bool disposed = false;

    /// <summary>
    /// Initializes a new instance of PageEncryption.
    /// </summary>
    /// <param name="crypto">The crypto service for encryption/decryption.</param>
    /// <param name="key">The encryption key.</param>
    /// <param name="pageSize">Page size in bytes (default 4096).</param>
    public PageEncryption(ICryptoService crypto, byte[] key, int pageSize = 4096)
    {
        this.crypto = crypto ?? throw new ArgumentNullException(nameof(crypto));
        this.key = key ?? throw new ArgumentNullException(nameof(key));
        this.pageSize = pageSize;
        this.aes = crypto.GetAesGcmEncryption(key);
    }

    /// <summary>
    /// Encrypts a single page of data.
    /// </summary>
    /// <param name="plaintext">The plaintext data to encrypt.</param>
    /// <returns>Encrypted page data.</returns>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public byte[] EncryptPage(ReadOnlySpan<byte> plaintext)
    {
        return EncryptPage(plaintext, pageId: 0);
    }

    /// <summary>
    /// Encrypts a single page of data with page-context AAD binding.
    /// </summary>
    /// <param name="plaintext">The plaintext data to encrypt.</param>
    /// <param name="pageId">Logical page identifier used for AAD binding.</param>
    /// <returns>Encrypted page data.</returns>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public byte[] EncryptPage(ReadOnlySpan<byte> plaintext, ulong pageId)
    {
        if (disposed)
            throw new ObjectDisposedException(nameof(PageEncryption));

        var data = plaintext.ToArray();
        var aad = BuildPageAad(pageId);
        return aes.Encrypt(data, aad);
    }

    /// <summary>
    /// Decrypts a single page of data.
    /// </summary>
    /// <param name="ciphertext">The encrypted page data.</param>
    /// <returns>Decrypted plaintext.</returns>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public byte[]? DecryptPage(ReadOnlySpan<byte> ciphertext)
    {
        return DecryptPage(ciphertext, pageId: 0);
    }

    /// <summary>
    /// Decrypts a single page of data with page-context AAD verification.
    /// </summary>
    /// <param name="ciphertext">The encrypted page data.</param>
    /// <param name="pageId">Logical page identifier used for AAD binding.</param>
    /// <returns>Decrypted plaintext.</returns>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public byte[]? DecryptPage(ReadOnlySpan<byte> ciphertext, ulong pageId)
    {
        if (disposed)
            throw new ObjectDisposedException(nameof(PageEncryption));

        try
        {
            var data = ciphertext.ToArray();
            var aad = BuildPageAad(pageId);
            return aes.Decrypt(data, aad);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
    }

    /// <summary>
    /// Encrypts data as a series of pages.
    /// Useful for large data that spans multiple pages.
    /// </summary>
    /// <param name="plaintext">The plaintext data.</param>
    /// <returns>Array of encrypted pages.</returns>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public byte[][] EncryptPages(ReadOnlySpan<byte> plaintext)
    {
        if (disposed)
            throw new ObjectDisposedException(nameof(PageEncryption));
        
        int pageCount = (plaintext.Length + pageSize - 1) / pageSize;
        var pages = new byte[pageCount][];
        
        for (int i = 0; i < pageCount; i++)
        {
            int offset = i * pageSize;
            int length = Math.Min(pageSize, plaintext.Length - offset);
            
            var page = plaintext.Slice(offset, length);
            pages[i] = EncryptPage(page, (ulong)i);
        }
        
        return pages;
    }

    /// <summary>
    /// Decrypts a series of pages back into a single byte array.
    /// </summary>
    /// <param name="encryptedPages">Array of encrypted pages.</param>
    /// <returns>Decrypted data, or null if any page fails to decrypt.</returns>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public byte[]? DecryptPages(ReadOnlySpan<byte[]> encryptedPages)
    {
        if (disposed)
            throw new ObjectDisposedException(nameof(PageEncryption));
        
        int totalSize = 0;
        var decryptedPages = new byte[encryptedPages.Length][];
        
        // Decrypt all pages first
        for (int i = 0; i < encryptedPages.Length; i++)
        {
            var decrypted = DecryptPage(encryptedPages[i], (ulong)i);
            if (decrypted == null)
                return null;  // Decryption failed
            
            decryptedPages[i] = decrypted;
            totalSize += decrypted.Length;
        }
        
        // Combine into single buffer
        var result = new byte[totalSize];
        int offset = 0;
        
        foreach (var page in decryptedPages)
        {
            Array.Copy(page, 0, result, offset, page.Length);
            offset += page.Length;
        }
        
        return result;
    }

    /// <summary>
    /// Gets the page size.
    /// </summary>
    public int PageSize => pageSize;

    /// <summary>
    /// Disposes the encryption helper.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Protected dispose implementation.
    /// </summary>
    /// <param name="disposing">True if disposing managed resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (disposed)
            return;

        if (disposing)
        {
            aes.Dispose();
        }
        
        disposed = true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private byte[] BuildPageAad(ulong pageId)
    {
        var aad = new byte[sizeof(int) + sizeof(ulong) + sizeof(ulong)];
        BinaryPrimitives.WriteInt32LittleEndian(aad.AsSpan(0, sizeof(int)), pageSize);
        BinaryPrimitives.WriteUInt64LittleEndian(aad.AsSpan(sizeof(int), sizeof(ulong)), pageId);
        BinaryPrimitives.WriteUInt64LittleEndian(aad.AsSpan(sizeof(int) + sizeof(ulong), sizeof(ulong)), (ulong)key.Length);
        return aad;
    }
}
