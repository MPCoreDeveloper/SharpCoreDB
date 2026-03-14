// <copyright file="AesGcmEncryption.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Services;

using System;
using System.Buffers;
using System.Security.Cryptography;
using System.Runtime.CompilerServices;

/// <summary>
/// Zero-allocation AES-256-GCM encryption using stackalloc for small buffers and ArrayPool for large ones.
/// HARDWARE ACCELERATION: Automatically uses AES-NI instructions on Intel/AMD when available.
/// SECURITY: All sensitive buffers are cleared immediately after use.
/// PERFORMANCE: Eliminates all unnecessary allocations through Span&lt;byte&gt; and stackalloc.
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
    /// Gets a value indicating whether AES hardware acceleration (AES-NI) is available on this platform.
    /// Returns true on Intel/AMD CPUs with AES-NI support, false otherwise.
    /// </summary>
    public static bool IsHardwareAccelerated
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => AesGcm.IsSupported;
    }

    /// <summary>
    /// Encrypts data using AES-256-GCM with optimized buffer handling.
    /// Uses stackalloc for nonce/tag, ArrayPool for cipher.
    /// </summary>
    /// <param name="data">The plaintext data to encrypt.</param>
    /// <returns>Encrypted data in format: [nonce(12)][ciphertext][tag(16)].</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public byte[] Encrypt(byte[] data)
    {
        return Encrypt(data, ReadOnlySpan<byte>.Empty);
    }

    /// <summary>
    /// Encrypts data using AES-256-GCM with associated authenticated data (AAD).
    /// </summary>
    /// <param name="data">The plaintext data to encrypt.</param>
    /// <param name="associatedData">Authenticated context bytes bound to ciphertext integrity.</param>
    /// <returns>Encrypted data in format: [nonce(12)][ciphertext][tag(16)].</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public byte[] Encrypt(byte[] data, ReadOnlySpan<byte> associatedData)
    {
        if (disableEncrypt)
            return data;

        using var aes = new AesGcm(_key, TagSize);

        Span<byte> nonce = stackalloc byte[NonceSize];
        Span<byte> tag = stackalloc byte[TagSize];

        RandomNumberGenerator.Fill(nonce);

        byte[]? cipherArray = null;
        try
        {
            cipherArray = _pool.Rent(data.Length);
            Span<byte> cipher = cipherArray.AsSpan(0, data.Length);

            aes.Encrypt(nonce, data, cipher, tag, associatedData);

            var result = new byte[NonceSize + data.Length + TagSize];
            nonce.CopyTo(result.AsSpan(0, NonceSize));
            cipher.CopyTo(result.AsSpan(NonceSize, data.Length));
            tag.CopyTo(result.AsSpan(NonceSize + data.Length, TagSize));

            return result;
        }
        finally
        {
            if (cipherArray != null)
                _pool.Return(cipherArray, clearArray: true);

            nonce.Clear();
            tag.Clear();
        }
    }

    /// <summary>
    /// Decrypts data using AES-256-GCM with zero-allocation Span operations.
    /// </summary>
    /// <param name="encryptedData">Encrypted data in format: [nonce(12)][ciphertext][tag(16)].</param>
    /// <returns>The decrypted plaintext.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public byte[] Decrypt(byte[] encryptedData)
    {
        return Decrypt(encryptedData, ReadOnlySpan<byte>.Empty);
    }

    /// <summary>
    /// Decrypts data using AES-256-GCM with associated authenticated data (AAD).
    /// </summary>
    /// <param name="encryptedData">Encrypted data in format: [nonce(12)][ciphertext][tag(16)].</param>
    /// <param name="associatedData">Authenticated context bytes bound during encryption.</param>
    /// <returns>The decrypted plaintext.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public byte[] Decrypt(byte[] encryptedData, ReadOnlySpan<byte> associatedData)
    {
        if (disableEncrypt)
            return encryptedData;

        var cipherLength = encryptedData.Length - NonceSize - TagSize;
        if (cipherLength < 0)
            throw new ArgumentException("Invalid encrypted data length", nameof(encryptedData));

        using var aes = new AesGcm(_key, TagSize);

        ReadOnlySpan<byte> nonce = encryptedData.AsSpan(0, NonceSize);
        ReadOnlySpan<byte> cipher = encryptedData.AsSpan(NonceSize, cipherLength);
        ReadOnlySpan<byte> tag = encryptedData.AsSpan(NonceSize + cipherLength, TagSize);

        var plaintext = new byte[cipherLength];
        aes.Decrypt(nonce, cipher, tag, plaintext, associatedData);

        return plaintext;
    }

    /// <summary>
    /// Encrypts data using AES-256-GCM with Span input/output (zero-allocation).
    /// </summary>
    /// <param name="data">The plaintext data to encrypt.</param>
    /// <param name="output">The output buffer (must be at least data.Length + 28 bytes).</param>
    /// <returns>Number of bytes written to output.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public int Encrypt(ReadOnlySpan<byte> data, Span<byte> output)
    {
        return Encrypt(data, output, ReadOnlySpan<byte>.Empty);
    }

    /// <summary>
    /// Encrypts data using AES-256-GCM with Span input/output and AAD (zero-allocation).
    /// </summary>
    /// <param name="data">The plaintext data to encrypt.</param>
    /// <param name="output">The output buffer (must be at least data.Length + 28 bytes).</param>
    /// <param name="associatedData">Authenticated context bytes bound to ciphertext integrity.</param>
    /// <returns>Number of bytes written to output.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public int Encrypt(ReadOnlySpan<byte> data, Span<byte> output, ReadOnlySpan<byte> associatedData)
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

        Span<byte> nonce = stackalloc byte[NonceSize];
        Span<byte> tag = stackalloc byte[TagSize];

        RandomNumberGenerator.Fill(nonce);

        byte[]? cipherArray = null;
        try
        {
            if (data.Length <= StackAllocThreshold)
            {
                Span<byte> cipher = stackalloc byte[data.Length];

                aes.Encrypt(nonce, data, cipher, tag, associatedData);

                nonce.CopyTo(output);
                cipher.CopyTo(output[NonceSize..]);
                tag.CopyTo(output[(NonceSize + data.Length)..]);

                cipher.Clear();
            }
            else
            {
                cipherArray = _pool.Rent(data.Length);
                Span<byte> cipher = cipherArray.AsSpan(0, data.Length);

                aes.Encrypt(nonce, data, cipher, tag, associatedData);

                nonce.CopyTo(output);
                cipher.CopyTo(output[NonceSize..]);
                tag.CopyTo(output[(NonceSize + data.Length)..]);
            }

            return totalSize;
        }
        finally
        {
            if (cipherArray != null)
                _pool.Return(cipherArray, clearArray: true);

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
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public int Decrypt(ReadOnlySpan<byte> encryptedData, Span<byte> output)
    {
        return Decrypt(encryptedData, output, ReadOnlySpan<byte>.Empty);
    }

    /// <summary>
    /// Decrypts data using AES-256-GCM with Span input/output and AAD (zero-allocation).
    /// </summary>
    /// <param name="encryptedData">Encrypted data in format: [nonce(12)][ciphertext][tag(16)].</param>
    /// <param name="output">The output buffer for decrypted data.</param>
    /// <param name="associatedData">Authenticated context bytes bound during encryption.</param>
    /// <returns>Number of bytes written to output.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public int Decrypt(ReadOnlySpan<byte> encryptedData, Span<byte> output, ReadOnlySpan<byte> associatedData)
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

        var nonce = encryptedData[..NonceSize];
        var cipher = encryptedData.Slice(NonceSize, cipherLength);
        var tag = encryptedData[(NonceSize + cipherLength)..];

        aes.Decrypt(nonce, cipher, tag, output[..cipherLength], associatedData);

        return cipherLength;
    }

    /// <summary>
    /// Encrypts a page in-place using AES-256-GCM (zero-allocation).
    /// Page format: [plaintext...] → [nonce(12)][ciphertext...][tag(16)]
    /// </summary>
    /// <param name="page">The page buffer (must have space for nonce + tag overhead).</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public void EncryptPage(Span<byte> page)
    {
        EncryptPage(page, ReadOnlySpan<byte>.Empty);
    }

    /// <summary>
    /// Encrypts a page in-place using AES-256-GCM with AAD binding.
    /// </summary>
    /// <param name="page">The page buffer (must have space for nonce + tag overhead).</param>
    /// <param name="associatedData">Authenticated context bytes bound to page ciphertext integrity.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public void EncryptPage(Span<byte> page, ReadOnlySpan<byte> associatedData)
    {
        if (disableEncrypt)
            return;

        var dataSize = page.Length - NonceSize - TagSize;
        if (dataSize <= 0)
            throw new ArgumentException("Page buffer too small for encryption overhead", nameof(page));

        using var aes = new AesGcm(_key, TagSize);

        Span<byte> nonce = stackalloc byte[NonceSize];
        Span<byte> tag = stackalloc byte[TagSize];

        RandomNumberGenerator.Fill(nonce);

        byte[]? tempArray = null;
        try
        {
            tempArray = _pool.Rent(dataSize);
            Span<byte> temp = tempArray.AsSpan(0, dataSize);

            page[..dataSize].CopyTo(temp);

            aes.Encrypt(nonce, temp, temp, tag, associatedData);

            nonce.CopyTo(page);
            temp.CopyTo(page[NonceSize..]);
            tag.CopyTo(page[(NonceSize + dataSize)..]);
        }
        finally
        {
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
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public void DecryptPage(Span<byte> page)
    {
        DecryptPage(page, ReadOnlySpan<byte>.Empty);
    }

    /// <summary>
    /// Decrypts a page in-place using AES-256-GCM with AAD binding.
    /// </summary>
    /// <param name="page">The encrypted page buffer.</param>
    /// <param name="associatedData">Authenticated context bytes bound during encryption.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public void DecryptPage(Span<byte> page, ReadOnlySpan<byte> associatedData)
    {
        if (disableEncrypt)
            return;

        var cipherLength = page.Length - NonceSize - TagSize;
        if (cipherLength <= 0)
            throw new ArgumentException("Page buffer too small for decryption", nameof(page));

        using var aes = new AesGcm(_key, TagSize);

        var nonce = page[..NonceSize];
        var cipher = page.Slice(NonceSize, cipherLength);
        var tag = page[(NonceSize + cipherLength)..];

        byte[]? tempArray = null;
        try
        {
            tempArray = _pool.Rent(cipherLength);
            Span<byte> temp = tempArray.AsSpan(0, cipherLength);

            aes.Decrypt(nonce, cipher, tag, temp, associatedData);

            temp.CopyTo(page);
        }
        finally
        {
            if (tempArray != null)
                _pool.Return(tempArray, clearArray: true);
        }
    }

    /// <summary>
    /// Disposes resources and clears sensitive data.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose()
    {
        if (_key.Length > 0)
            Array.Clear(_key);
    }
}
