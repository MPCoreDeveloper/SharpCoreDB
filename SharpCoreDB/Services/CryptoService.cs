// <copyright file="CryptoService.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace SharpCoreDB.Services;

using SharpCoreDB.Interfaces;
using System.Security.Cryptography;
using System.Text;

/// <summary>
/// Implementation of ICryptoService using PBKDF2 for key derivation and AES-256-GCM for encryption.
/// </summary>
public class CryptoService : ICryptoService
{
    /// <inheritdoc />
    public byte[] DeriveKey(string password, string salt)
    {
        return Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), Encoding.UTF8.GetBytes(salt), 10000, HashAlgorithmName.SHA256, 32);
    }

    /// <inheritdoc />
    public byte[] Encrypt(byte[] key, byte[] data)
    {
        using var aes = new AesGcm(key, AesGcm.TagByteSizes.MaxSize);
        var nonce = new byte[AesGcm.NonceByteSizes.MaxSize];
        RandomNumberGenerator.Fill(nonce);
        var tag = new byte[AesGcm.TagByteSizes.MaxSize];
        var cipher = new byte[data.Length];
        aes.Encrypt(nonce, data, cipher, tag);
        return nonce.Concat(cipher).Concat(tag).ToArray();
    }

    /// <inheritdoc />
    public byte[] Decrypt(byte[] key, byte[] encryptedData)
    {
        var nonce = encryptedData.Take(AesGcm.NonceByteSizes.MaxSize).ToArray();
        var tag = encryptedData.TakeLast(AesGcm.TagByteSizes.MaxSize).ToArray();
        var cipher = encryptedData.Skip(AesGcm.NonceByteSizes.MaxSize).Take(encryptedData.Length - AesGcm.NonceByteSizes.MaxSize - AesGcm.TagByteSizes.MaxSize).ToArray();
        using var aes = new AesGcm(key, AesGcm.TagByteSizes.MaxSize);
        var plain = new byte[cipher.Length];
        aes.Decrypt(nonce, cipher, tag, plain);
        return plain;
    }

    /// <inheritdoc />
    public void EncryptPage(Span<byte> page)
    {
        // Not implemented in CryptoService, use GetAesGcmEncryption
        throw new NotImplementedException();
    }

    /// <inheritdoc />
    public void DecryptPage(Span<byte> page)
    {
        // Not implemented in CryptoService, use GetAesGcmEncryption
        throw new NotImplementedException();
    }

    /// <inheritdoc />
    public SharpCoreDB.Services.AesGcmEncryption GetAesGcmEncryption(byte[] key)
    {
        return new AesGcmEncryption(key, false);
    }
}
