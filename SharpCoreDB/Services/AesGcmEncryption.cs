// <copyright file="AesGcmEncryption.cs" company="MPCoreDeveloper">
// Copyright (c) 2024-2025 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Services;

using System;
using System.Buffers;
using System.Security.Cryptography;
using System.Runtime.CompilerServices;

/// <summary>
/// Zero-allocation AES-256-GCM encryption using stackalloc for small buffers and ArrayPool for large ones.
/// SECURITY: All sensitive buffers are cleared immediately after use.
/// PERFORMANCE: Eliminates all unnecessary allocations through Span<byte> and stackalloc.
/// </summary>
/// <param name="key">The encryption key (must be 32 bytes for AES-256).</param>
/// <param name="disableEncrypt">If true, encryption is disabled (passthrough mode).</param>
public sealed class AesGcmEncryption(byte[] key, bool disableEncrypt = false) : IDisposable
{
    private readonly byte[] _key = disableEncrypt ? [] : [.. key];
    private readonly ArrayPool<byte> _pool = ArrayPool<byte>.Shared;

    // Size constants for AES-GCM
    private const int NonceSize = 12; // AesGcm.NonceByteSizes.MaxSize = 12
    private const int TagSize = 16;   // AesGcm.TagByteSizes.MaxSize = 16
    private const int StackAllocThreshold = 256; // Use stackalloc for buffers <= 256 bytes

    /// <summary>
    /// Encrypts data using AES-256-GCM with optimized buffer handling.
    /// Uses stackalloc for nonce/tag, ArrayPool for cipher.
    /// </summary>
    /// <param name="data">The plaintext data to encrypt.</param>
    /// <returns>Encrypted data in format: [nonce(12)][ciphertext][tag(16)].</returns>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public byte[] Encrypt(byte[] data)
    {
        if (disableEncrypt) 
            return data;

        using var aes = new AesGcm(_key, TagSize);
        
        // OPTIMIZED: Use stackalloc for small buffers (nonce, tag)
        Span<byte> nonce = stackalloc byte[NonceSize];
        Span<byte> tag = stackalloc byte[TagSize];
        
        RandomNumberGenerator.Fill(nonce);
        
        byte[]? cipherArray = null;
        try
        {
            // OPTIMIZED: Rent from pool for cipher data
            cipherArray = _pool.Rent(data.Length);
            Span<byte> cipher = cipherArray.AsSpan(0, data.Length);
            
            // Encrypt: plaintext → ciphertext + tag
            aes.Encrypt(nonce, data, cipher, tag);
            
            // Build result: [nonce][cipher][tag]
            var result = new byte[NonceSize + data.Length + TagSize];
            nonce.CopyTo(result.AsSpan(0, NonceSize));
            cipher.CopyTo(result.AsSpan(NonceSize, data.Length));
            tag.CopyTo(result.AsSpan(NonceSize + data.Length, TagSize));
            
            return result;
        }
        finally
        {
            // SECURITY: Clear sensitive cipher data
            if (cipherArray != null)
                _pool.Return(cipherArray, clearArray: true);
            
            // SECURITY: Clear stack-allocated buffers
            nonce.Clear();
            tag.Clear();
        }
    }

    /// <summary>
    /// Decrypts data using AES-256-GCM with zero-allocation Span operations.
    /// </summary>
    /// <param name="encryptedData">Encrypted data in format: [nonce(12)][ciphertext][tag(16)].</param>
    /// <returns>The decrypted plaintext.</returns>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public byte[] Decrypt(byte[] encryptedData)
    {
        if (disableEncrypt) 
            return encryptedData;

        var cipherLength = encryptedData.Length - NonceSize - TagSize;
        if (cipherLength < 0)
            throw new ArgumentException("Invalid encrypted data length", nameof(encryptedData));

        using var aes = new AesGcm(_key, TagSize);
        
        // OPTIMIZED: Use Span slicing to avoid allocations
        ReadOnlySpan<byte> nonce = encryptedData.AsSpan(0, NonceSize);
        ReadOnlySpan<byte> cipher = encryptedData.AsSpan(NonceSize, cipherLength);
        ReadOnlySpan<byte> tag = encryptedData.AsSpan(NonceSize + cipherLength, TagSize);
        
        // Decrypt directly to result array
        var plaintext = new byte[cipherLength];
        aes.Decrypt(nonce, cipher, tag, plaintext);
        
        return plaintext;
    }

    /// <summary>
    /// Encrypts data using AES-256-GCM with Span input/output (zero-allocation).
    /// </summary>
    /// <param name="data">The plaintext data to encrypt.</param>
    /// <param name="output">The output buffer (must be at least data.Length + 28 bytes).</param>
    /// <returns>Number of bytes written to output.</returns>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public int Encrypt(ReadOnlySpan<byte> data, Span<byte> output)
    {
        if (disableEncrypt)
        {
            data.CopyTo(output);
            return data.Length;
        }

        var totalSize = NonceSize + data.Length + TagSize;
        if (output.Length < totalSize)
            throw new ArgumentException("Output buffer too small", nameof(output));

        using var aes = new AesGcm(_key, TagSize);
        
        // OPTIMIZED: stackalloc for nonce and tag
        Span<byte> nonce = stackalloc byte[NonceSize];
        Span<byte> tag = stackalloc byte[TagSize];
        
        RandomNumberGenerator.Fill(nonce);
        
        byte[]? cipherArray = null;
        try
        {
            // For small data, use stackalloc; for large data, use ArrayPool
            if (data.Length <= StackAllocThreshold)
            {
                // OPTIMIZED: stackalloc for small cipher data
                Span<byte> cipher = stackalloc byte[data.Length];
                
                // Encrypt
                aes.Encrypt(nonce, data, cipher, tag);
                
                // Write to output: [nonce][cipher][tag]
                nonce.CopyTo(output);
                cipher.CopyTo(output[NonceSize..]);
                tag.CopyTo(output[(NonceSize + data.Length)..]);
                
                // SECURITY: Clear stack buffers
                cipher.Clear();
            }
            else
            {
                // OPTIMIZED: ArrayPool for large cipher data
                cipherArray = _pool.Rent(data.Length);
                Span<byte> cipher = cipherArray.AsSpan(0, data.Length);
                
                // Encrypt
                aes.Encrypt(nonce, data, cipher, tag);
                
                // Write to output: [nonce][cipher][tag]
                nonce.CopyTo(output);
                cipher.CopyTo(output[NonceSize..]);
                tag.CopyTo(output[(NonceSize + data.Length)..]);
            }
            
            return totalSize;
        }
        finally
        {
            // SECURITY: Clear pooled buffer
            if (cipherArray != null)
                _pool.Return(cipherArray, clearArray: true);
            
            // SECURITY: Clear stack-allocated buffers
            nonce.Clear();
            tag.Clear();
        }
    }

    /// <summary>
    /// Decrypts data using AES-256-GCM with Span input/output (zero-allocation).
    /// </summary>
    /// <param name="encryptedData">Encrypted data in format: [nonce(12)][ciphertext][tag(16)].</param>
    /// <param name="output">The output buffer for decrypted data.</param>
    /// <returns>Number of bytes written to output.</returns>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public int Decrypt(ReadOnlySpan<byte> encryptedData, Span<byte> output)
    {
        if (disableEncrypt)
        {
            encryptedData.CopyTo(output);
            return encryptedData.Length;
        }

        var cipherLength = encryptedData.Length - NonceSize - TagSize;
        if (cipherLength < 0)
            throw new ArgumentException("Invalid encrypted data length", nameof(encryptedData));
        
        if (output.Length < cipherLength)
            throw new ArgumentException("Output buffer too small", nameof(output));

        using var aes = new AesGcm(_key, TagSize);
        
        // OPTIMIZED: Use Span slicing (zero allocation)
        var nonce = encryptedData[..NonceSize];
        var cipher = encryptedData.Slice(NonceSize, cipherLength);
        var tag = encryptedData[(NonceSize + cipherLength)..];
        
        // Decrypt directly to output
        aes.Decrypt(nonce, cipher, tag, output[..cipherLength]);
        
        return cipherLength;
    }

    /// <summary>
    /// Encrypts a page in-place using AES-256-GCM (zero-allocation).
    /// Page format: [plaintext...] → [nonce(12)][ciphertext...][tag(16)]
    /// </summary>
    /// <param name="page">The page buffer (must have space for nonce + tag overhead).</param>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public void EncryptPage(Span<byte> page)
    {
        if (disableEncrypt) 
            return;

        var dataSize = page.Length - NonceSize - TagSize;
        if (dataSize <= 0)
            throw new ArgumentException("Page buffer too small for encryption overhead", nameof(page));

        using var aes = new AesGcm(_key, TagSize);
        
        // OPTIMIZED: stackalloc for nonce and tag
        Span<byte> nonce = stackalloc byte[NonceSize];
        Span<byte> tag = stackalloc byte[TagSize];
        
        RandomNumberGenerator.Fill(nonce);
        
        byte[]? tempArray = null;
        try
        {
            // OPTIMIZED: Rent temp buffer for in-place encryption
            tempArray = _pool.Rent(dataSize);
            Span<byte> temp = tempArray.AsSpan(0, dataSize);
            
            // Copy plaintext to temp
            page[..dataSize].CopyTo(temp);
            
            // Encrypt: temp → temp (in-place in temp buffer)
            aes.Encrypt(nonce, temp, temp, tag);
            
            // Write back: [nonce][ciphertext][tag]
            nonce.CopyTo(page);
            temp.CopyTo(page[NonceSize..]);
            tag.CopyTo(page[(NonceSize + dataSize)..]);
        }
        finally
        {
            // SECURITY: Clear sensitive data
            if (tempArray != null)
                _pool.Return(tempArray, clearArray: true);
            
            nonce.Clear();
            tag.Clear();
        }
    }

    /// <summary>
    /// Decrypts a page in-place using AES-256-GCM (zero-allocation).
    /// Page format: [nonce(12)][ciphertext...][tag(16)] → [plaintext...]
    /// </summary>
    /// <param name="page">The encrypted page buffer.</param>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public void DecryptPage(Span<byte> page)
    {
        if (disableEncrypt) 
            return;

        var cipherLength = page.Length - NonceSize - TagSize;
        if (cipherLength <= 0)
            throw new ArgumentException("Page buffer too small for decryption", nameof(page));

        using var aes = new AesGcm(_key, TagSize);
        
        // OPTIMIZED: Extract components via Span slicing
        var nonce = page[..NonceSize];
        var cipher = page.Slice(NonceSize, cipherLength);
        var tag = page[(NonceSize + cipherLength)..];
        
        byte[]? tempArray = null;
        try
        {
            // OPTIMIZED: Rent temp buffer for decryption
            tempArray = _pool.Rent(cipherLength);
            Span<byte> temp = tempArray.AsSpan(0, cipherLength);
            
            // Decrypt to temp
            aes.Decrypt(nonce, cipher, tag, temp);
            
            // Copy decrypted data back to start of page
            temp.CopyTo(page);
        }
        finally
        {
            // SECURITY: Clear sensitive data
            if (tempArray != null)
                _pool.Return(tempArray, clearArray: true);
        }
    }

    /// <summary>
    /// Disposes resources and clears sensitive data.
    /// </summary>
    public void Dispose()
    {
        if (_key.Length > 0)
            Array.Clear(_key);
    }
}
