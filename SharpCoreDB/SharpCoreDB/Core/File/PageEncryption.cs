// <copyright file="PageEncryption.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Core.File;

using SharpCoreDB.Interfaces;
using System;
using System.Buffers;
using System.Runtime.CompilerServices;

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
    }

    /// <summary>
    /// Encrypts a single page of data.
    /// </summary>
    /// <param name="plaintext">The plaintext data to encrypt.</param>
    /// <returns>Encrypted page data.</returns>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public byte[] EncryptPage(ReadOnlySpan<byte> plaintext)
    {
        if (disposed)
            throw new ObjectDisposedException(nameof(PageEncryption));
        
        // Convert span to array for crypto service
        var data = plaintext.ToArray();
        return this.crypto.Encrypt(this.key, data);
    }

    /// <summary>
    /// Decrypts a single page of data.
    /// </summary>
    /// <param name="ciphertext">The encrypted page data.</param>
    /// <returns>Decrypted plaintext.</returns>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public byte[]? DecryptPage(ReadOnlySpan<byte> ciphertext)
    {
        if (disposed)
            throw new ObjectDisposedException(nameof(PageEncryption));
        
        try
        {
            var data = ciphertext.ToArray();
            return this.crypto.Decrypt(this.key, data);
        }
        catch
        {
            // Return null on decryption failure (corrupted or wrong key)
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
            pages[i] = EncryptPage(page);
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
            var decrypted = DecryptPage(encryptedPages[i]);
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
        
        disposed = true;
    }
}
