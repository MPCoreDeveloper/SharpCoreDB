// <copyright file="AesGcmEncryption.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace SharpCoreDB.Services;

using System.Buffers;
using System.Security.Cryptography;

/// <summary>
/// Optimized AES-256-GCM encryption using reusable AesGcm instance and ArrayPool for buffers.
/// </summary>
public class AesGcmEncryption : IDisposable
{
    private readonly AesGcm _aes; // PERFORMANCE FIX: Single reusable AesGcm instance to avoid repeated allocations
    private readonly bool _disableEncrypt;
    private readonly ArrayPool<byte> _pool = ArrayPool<byte>.Shared;
    private readonly byte[] _nonceBuffer; // PERFORMANCE FIX: Pre-allocated nonce buffer for reuse
    private readonly byte[] _tagBuffer; // PERFORMANCE FIX: Pre-allocated tag buffer for reuse

    /// <summary>
    /// Initializes a new instance of the AesGcmEncryption class.
    /// </summary>
    /// <param name="key">The encryption key.</param>
    /// <param name="disableEncrypt">If true, encryption is disabled and data is returned as-is.</param>
    public AesGcmEncryption(byte[] key, bool disableEncrypt = false)
    {
        _disableEncrypt = disableEncrypt;
        if (!disableEncrypt)
        {
            _aes = new AesGcm(key, AesGcm.TagByteSizes.MaxSize); // PERFORMANCE FIX: Create once and reuse
            _nonceBuffer = _pool.Rent(AesGcm.NonceByteSizes.MaxSize); // PERFORMANCE FIX: Pre-allocate for reuse
            _tagBuffer = _pool.Rent(AesGcm.TagByteSizes.MaxSize); // PERFORMANCE FIX: Pre-allocate for reuse
        }
    }

    /// <summary>
    /// Encrypts data using AES-256-GCM.
    /// </summary>
    /// <param name="data">The data to encrypt.</param>
    /// <returns>The encrypted data.</returns>
    public byte[] Encrypt(byte[] data)
    {
        if (_disableEncrypt) return data;

        var cipher = _pool.Rent(data.Length);
        try
        {
            RandomNumberGenerator.Fill(_nonceBuffer.AsSpan(0, AesGcm.NonceByteSizes.MaxSize)); // PERFORMANCE FIX: Use pre-allocated buffer
            _aes.Encrypt(_nonceBuffer.AsSpan(0, AesGcm.NonceByteSizes.MaxSize), data, cipher.AsSpan(0, data.Length), _tagBuffer.AsSpan(0, AesGcm.TagByteSizes.MaxSize)); // PERFORMANCE FIX: Reuse AesGcm instance
            var result = new byte[AesGcm.NonceByteSizes.MaxSize + data.Length + AesGcm.TagByteSizes.MaxSize];
            _nonceBuffer.AsSpan(0, AesGcm.NonceByteSizes.MaxSize).CopyTo(result.AsSpan(0, AesGcm.NonceByteSizes.MaxSize));
            cipher.AsSpan(0, data.Length).CopyTo(result.AsSpan(AesGcm.NonceByteSizes.MaxSize, data.Length));
            _tagBuffer.AsSpan(0, AesGcm.TagByteSizes.MaxSize).CopyTo(result.AsSpan(AesGcm.NonceByteSizes.MaxSize + data.Length, AesGcm.TagByteSizes.MaxSize));
            return result;
        }
        finally
        {
            _pool.Return(cipher);
        }
    }

    /// <summary>
    /// Decrypts data using AES-256-GCM.
    /// </summary>
    /// <param name="encryptedData">The encrypted data.</param>
    /// <returns>The decrypted data.</returns>
    public byte[] Decrypt(byte[] encryptedData)
    {
        if (_disableEncrypt) return encryptedData;

        var nonceSize = AesGcm.NonceByteSizes.MaxSize;
        var tagSize = AesGcm.TagByteSizes.MaxSize;
        var cipherLength = encryptedData.Length - nonceSize - tagSize;
        var plain = new byte[cipherLength];
        var nonce = encryptedData.AsSpan(0, nonceSize);
        var cipher = encryptedData.AsSpan(nonceSize, cipherLength);
        var tag = encryptedData.AsSpan(nonceSize + cipherLength, tagSize);
        _aes.Decrypt(nonce, cipher, tag, plain); // PERFORMANCE FIX: Reuse AesGcm instance
        return plain;
    }

    /// <summary>
    /// Encrypts data using AES-256-GCM with Span input/output.
    /// </summary>
    /// <param name="data">The data to encrypt.</param>
    /// <param name="output">The output buffer for encrypted data.</param>
    /// <returns>The length of the encrypted data written to output.</returns>
    public int Encrypt(ReadOnlySpan<byte> data, Span<byte> output)
    {
        if (_disableEncrypt)
        {
            data.CopyTo(output);
            return data.Length;
        }

        var nonceSize = AesGcm.NonceByteSizes.MaxSize;
        var tagSize = AesGcm.TagByteSizes.MaxSize;
        var totalSize = nonceSize + data.Length + tagSize;
        if (output.Length < totalSize) throw new ArgumentException("Output buffer too small");

        var cipher = _pool.Rent(data.Length);
        try
        {
            RandomNumberGenerator.Fill(_nonceBuffer.AsSpan(0, nonceSize)); // PERFORMANCE FIX: Use pre-allocated buffer
            _aes.Encrypt(_nonceBuffer.AsSpan(0, nonceSize), data, cipher.AsSpan(0, data.Length), _tagBuffer.AsSpan(0, tagSize)); // PERFORMANCE FIX: Reuse AesGcm instance
            _nonceBuffer.AsSpan(0, nonceSize).CopyTo(output.Slice(0, nonceSize));
            cipher.AsSpan(0, data.Length).CopyTo(output.Slice(nonceSize, data.Length));
            _tagBuffer.AsSpan(0, tagSize).CopyTo(output.Slice(nonceSize + data.Length, tagSize));
            return totalSize;
        }
        finally
        {
            _pool.Return(cipher);
        }
    }

    /// <summary>
    /// Decrypts data using AES-256-GCM with Span input/output.
    /// </summary>
    /// <param name="encryptedData">The encrypted data.</param>
    /// <param name="output">The output buffer for decrypted data.</param>
    /// <returns>The length of the decrypted data written to output.</returns>
    public int Decrypt(ReadOnlySpan<byte> encryptedData, Span<byte> output)
    {
        if (_disableEncrypt)
        {
            encryptedData.CopyTo(output);
            return encryptedData.Length;
        }

        var nonceSize = AesGcm.NonceByteSizes.MaxSize;
        var tagSize = AesGcm.TagByteSizes.MaxSize;
        var cipherLength = encryptedData.Length - nonceSize - tagSize;
        if (output.Length < cipherLength) throw new ArgumentException("Output buffer too small");

        var nonce = encryptedData.Slice(0, nonceSize);
        var cipher = encryptedData.Slice(nonceSize, cipherLength);
        var tag = encryptedData.Slice(nonceSize + cipherLength, tagSize);
        _aes.Decrypt(nonce, cipher, tag, output.Slice(0, cipherLength)); // PERFORMANCE FIX: Reuse AesGcm instance
        return cipherLength;
    }

    /// <summary>
    /// Encrypts a page using AES-256-GCM.
    /// </summary>
    /// <param name="page">The page data to encrypt (modified in place if buffer is large enough).</param>
    public void EncryptPage(Span<byte> page)
    {
        var encrypted = Encrypt(page.ToArray());
        if (page.Length >= encrypted.Length)
        {
            encrypted.AsSpan().CopyTo(page);
        }
        else
        {
            throw new ArgumentException("Page buffer too small for encrypted data");
        }
    }

    /// <summary>
    /// Decrypts a page using AES-256-GCM.
    /// </summary>
    /// <param name="page">The encrypted page data (modified in place to decrypted data).</param>
    public void DecryptPage(Span<byte> page)
    {
        var decrypted = Decrypt(page.ToArray());
        if (page.Length >= decrypted.Length)
        {
            decrypted.AsSpan().CopyTo(page);
        }
        else
        {
            throw new ArgumentException("Page buffer too small for decrypted data");
        }
    }

    /// <summary>
    /// Disposes the AesGcm instance.
    /// </summary>
    public void Dispose()
    {
        _aes?.Dispose();
        if (_nonceBuffer != null) _pool.Return(_nonceBuffer); // PERFORMANCE FIX: Return pre-allocated buffers
        if (_tagBuffer != null) _pool.Return(_tagBuffer);
    }
}
