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
    private readonly AesGcm? _aesInstance;
    private readonly bool _disableEncrypt;
    private readonly ArrayPool<byte> _pool = ArrayPool<byte>.Shared;

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
            _aesInstance = new AesGcm(key, AesGcm.TagByteSizes.MaxSize);
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

        var nonce = _pool.Rent(AesGcm.NonceByteSizes.MaxSize);
        var tag = _pool.Rent(AesGcm.TagByteSizes.MaxSize);
        var cipher = _pool.Rent(data.Length);
        try
        {
            RandomNumberGenerator.Fill(nonce.AsSpan(0, AesGcm.NonceByteSizes.MaxSize));
            _aesInstance!.Encrypt(nonce.AsSpan(0, AesGcm.NonceByteSizes.MaxSize), data, cipher.AsSpan(0, data.Length), tag.AsSpan(0, AesGcm.TagByteSizes.MaxSize));
            var result = new byte[AesGcm.NonceByteSizes.MaxSize + data.Length + AesGcm.TagByteSizes.MaxSize];
            nonce.AsSpan(0, AesGcm.NonceByteSizes.MaxSize).CopyTo(result.AsSpan(0, AesGcm.NonceByteSizes.MaxSize));
            cipher.AsSpan(0, data.Length).CopyTo(result.AsSpan(AesGcm.NonceByteSizes.MaxSize, data.Length));
            tag.AsSpan(0, AesGcm.TagByteSizes.MaxSize).CopyTo(result.AsSpan(AesGcm.NonceByteSizes.MaxSize + data.Length, AesGcm.TagByteSizes.MaxSize));
            return result;
        }
        finally
        {
            _pool.Return(nonce);
            _pool.Return(tag);
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
        _aesInstance!.Decrypt(nonce, cipher, tag, plain);
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

        var nonce = _pool.Rent(nonceSize);
        var tag = _pool.Rent(tagSize);
        var cipher = _pool.Rent(data.Length);
        try
        {
            RandomNumberGenerator.Fill(nonce.AsSpan(0, nonceSize));
            _aesInstance!.Encrypt(nonce.AsSpan(0, nonceSize), data, cipher.AsSpan(0, data.Length), tag.AsSpan(0, tagSize));
            nonce.AsSpan(0, nonceSize).CopyTo(output.Slice(0, nonceSize));
            cipher.AsSpan(0, data.Length).CopyTo(output.Slice(nonceSize, data.Length));
            tag.AsSpan(0, tagSize).CopyTo(output.Slice(nonceSize + data.Length, tagSize));
            return totalSize;
        }
        finally
        {
            _pool.Return(nonce);
            _pool.Return(tag);
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
        _aesInstance!.Decrypt(nonce, cipher, tag, output.Slice(0, cipherLength));
        return cipherLength;
    }

    /// <summary>
    /// Disposes the AesGcm instance.
    /// </summary>
    public void Dispose()
    {
        _aesInstance?.Dispose();
    }
}
