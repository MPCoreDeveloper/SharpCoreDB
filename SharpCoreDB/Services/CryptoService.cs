// <copyright file="CryptoService.cs" company="MPCoreDeveloper">
// Copyright (c) 2024-2025 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Services;

using SharpCoreDB.Interfaces;
using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;

/// <summary>
/// Zero-allocation implementation of ICryptoService using PBKDF2 for key derivation and AES-256-GCM for encryption.
/// OPTIMIZATION: Uses stackalloc and Span<byte> to eliminate LINQ allocations in Encrypt/Decrypt.
/// </summary>
public class CryptoService : ICryptoService
{
    private const int NonceSize = 12; // AesGcm.NonceByteSizes.MaxSize
    private const int TagSize = 16;   // AesGcm.TagByteSizes.MaxSize
    private const int StackAllocThreshold = 256;

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public byte[] DeriveKey(string password, string salt)
    {
        // OPTIMIZED: Use Span<byte> for UTF8 encoding to avoid intermediate allocations
        int maxPasswordBytes = Encoding.UTF8.GetMaxByteCount(password.Length);
        int maxSaltBytes = Encoding.UTF8.GetMaxByteCount(salt.Length);
        
        byte[]? passwordArray = null;
        byte[]? saltArray = null;
        
        try
        {
            // Use stackalloc for small strings, ArrayPool for large ones
            scoped Span<byte> passwordBytes;
            if (maxPasswordBytes <= StackAllocThreshold)
            {
                Span<byte> stackPassword = stackalloc byte[maxPasswordBytes];
                passwordBytes = stackPassword;
            }
            else
            {
                passwordArray = ArrayPool<byte>.Shared.Rent(maxPasswordBytes);
                passwordBytes = passwordArray.AsSpan(0, maxPasswordBytes);
            }
            
            scoped Span<byte> saltBytes;
            if (maxSaltBytes <= StackAllocThreshold)
            {
                Span<byte> stackSalt = stackalloc byte[maxSaltBytes];
                saltBytes = stackSalt;
            }
            else
            {
                saltArray = ArrayPool<byte>.Shared.Rent(maxSaltBytes);
                saltBytes = saltArray.AsSpan(0, maxSaltBytes);
            }
            
            // Encode to bytes
            int passwordLen = Encoding.UTF8.GetBytes(password, passwordBytes);
            int saltLen = Encoding.UTF8.GetBytes(salt, saltBytes);
            
            // SECURITY FIX: Derive key using PBKDF2 with 600,000 iterations (OWASP/NIST 2024 recommendation)
            // Previous value of 10,000 was dangerously low against GPU brute force attacks
            // See: https://cheatsheetseries.owasp.org/cheatsheets/Password_Storage_Cheat_Sheet.html
            var key = Rfc2898DeriveBytes.Pbkdf2(
                passwordBytes.Slice(0, passwordLen), 
                saltBytes.Slice(0, saltLen), 
                600000,  // SECURITY: Increased from 10,000 to 600,000 iterations
                HashAlgorithmName.SHA256, 
                32);
            
            return key;
        }
        finally
        {
            // SECURITY: Clear sensitive password data
            if (passwordArray != null)
            {
                ArrayPool<byte>.Shared.Return(passwordArray, clearArray: true);
            }
            if (saltArray != null)
            {
                ArrayPool<byte>.Shared.Return(saltArray, clearArray: true);
            }
        }
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public byte[] Encrypt(byte[] key, byte[] data)
    {
        using var aes = new AesGcm(key, TagSize);
        
        // OPTIMIZED: stackalloc for nonce and tag (small fixed-size buffers)
        Span<byte> nonce = stackalloc byte[NonceSize];
        Span<byte> tag = stackalloc byte[TagSize];
        
        RandomNumberGenerator.Fill(nonce);
        
        byte[]? cipherArray = null;
        try
        {
            // OPTIMIZED: Rent from pool for cipher data
            cipherArray = ArrayPool<byte>.Shared.Rent(data.Length);
            Span<byte> cipher = cipherArray.AsSpan(0, data.Length);
            
            // Encrypt
            aes.Encrypt(nonce, data, cipher, tag);
            
            // OPTIMIZED: Build result using Span.CopyTo instead of LINQ Concat
            var result = new byte[NonceSize + data.Length + TagSize];
            nonce.CopyTo(result.AsSpan(0, NonceSize));
            cipher.CopyTo(result.AsSpan(NonceSize, data.Length));
            tag.CopyTo(result.AsSpan(NonceSize + data.Length, TagSize));
            
            return result;
        }
        finally
        {
            // SECURITY: Clear cipher data
            if (cipherArray != null)
            {
                ArrayPool<byte>.Shared.Return(cipherArray, clearArray: true);
            }
            
            // SECURITY: Clear stack buffers
            nonce.Clear();
            tag.Clear();
        }
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public byte[] Decrypt(byte[] key, byte[] encryptedData)
    {
        var cipherLength = encryptedData.Length - NonceSize - TagSize;
        if (cipherLength < 0)
            throw new ArgumentException("Invalid encrypted data length", nameof(encryptedData));

        using var aes = new AesGcm(key, TagSize);
        
        // OPTIMIZED: Use Span slicing instead of LINQ Take/Skip/TakeLast (zero allocation)
        ReadOnlySpan<byte> nonce = encryptedData.AsSpan(0, NonceSize);
        ReadOnlySpan<byte> cipher = encryptedData.AsSpan(NonceSize, cipherLength);
        ReadOnlySpan<byte> tag = encryptedData.AsSpan(NonceSize + cipherLength, TagSize);
        
        // Decrypt directly to result
        var plaintext = new byte[cipherLength];
        aes.Decrypt(nonce, cipher, tag, plaintext);
        
        return plaintext;
    }

    /// <inheritdoc />
    public void EncryptPage(Span<byte> page)
    {
        // Delegate to AesGcmEncryption for page operations
        throw new NotImplementedException("Use GetAesGcmEncryption() for page-level operations");
    }

    /// <inheritdoc />
    public void DecryptPage(Span<byte> page)
    {
        // Delegate to AesGcmEncryption for page operations
        throw new NotImplementedException("Use GetAesGcmEncryption() for page-level operations");
    }

    /// <inheritdoc />
    public AesGcmEncryption GetAesGcmEncryption(byte[] key)
    {
        return new AesGcmEncryption(key, false);
    }
}
