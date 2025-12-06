// <copyright file="ICryptoService.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace SharpCoreDB.Interfaces;

using SharpCoreDB.Services;

/// <summary>
/// Interface for cryptographic operations including password hashing and data encryption/decryption.
/// </summary>
public interface ICryptoService
{
    /// <summary>
    /// Derives a key from a password using Argon2id.
    /// </summary>
    /// <param name="password">The password to derive from.</param>
    /// <param name="salt">The salt to use.</param>
    /// <returns>The derived key as a byte array.</returns>
    byte[] DeriveKey(string password, string salt);

    /// <summary>
    /// Encrypts data using AES-256-GCM.
    /// </summary>
    /// <param name="key">The encryption key.</param>
    /// <param name="data">The data to encrypt.</param>
    /// <returns>The encrypted data as a byte array.</returns>
    byte[] Encrypt(byte[] key, byte[] data);

    /// <summary>
    /// Decrypts data using AES-256-GCM.
    /// </summary>
    /// <param name="key">The decryption key.</param>
    /// <param name="encryptedData">The encrypted data.</param>
    /// <returns>The decrypted data as a byte array.</returns>
    byte[] Decrypt(byte[] key, byte[] encryptedData);

    /// <summary>
    /// Encrypts a page using AES-256-GCM.
    /// </summary>
    /// <param name="page">The page data to encrypt (modified in place if buffer is large enough).</param>
    void EncryptPage(Span<byte> page);

    /// <summary>
    /// Decrypts a page using AES-256-GCM.
    /// </summary>
    /// <param name="page">The encrypted page data (modified in place to decrypted data).</param>
    void DecryptPage(Span<byte> page);

    /// <summary>
    /// Gets an AesGcmEncryption instance for the specified key.
    /// </summary>
    /// <param name="key">The encryption key.</param>
    /// <returns>An AesGcmEncryption instance.</returns>
    SharpCoreDB.Services.AesGcmEncryption GetAesGcmEncryption(byte[] key);
}
