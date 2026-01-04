// <copyright file="CryptoService.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Services;

using SharpCoreDB.Constants;
using SharpCoreDB.Interfaces;
using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

/// <summary>
/// Zero-allocation implementation of ICryptoService using PBKDF2 for key derivation and AES-256-GCM for encryption.
/// HARDWARE ACCELERATION: Automatically uses AES-NI instructions on Intel/AMD when available.
/// OPTIMIZATION: Uses stackalloc and Span&lt;byte&gt; to eliminate LINQ allocations in Encrypt/Decrypt.
/// SECURITY: Tracks GCM operations to prevent nonce exhaustion (2^32 limit).
/// </summary>
public sealed class CryptoService : ICryptoService
{
    private const int StackAllocThreshold = 256;
    
    // SECURITY: Track encryption operations to prevent GCM nonce exhaustion
    private long _encryptionCount = 0;

    /// <summary>
    /// Gets a value indicating whether AES hardware acceleration (AES-NI) is available.
    /// </summary>
    public static bool IsHardwareAccelerated
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => AesGcmEncryption.IsHardwareAccelerated;
    }

    /// <summary>
    /// Gets the current encryption operation count.
    /// Used to track when key rotation is needed (approaching 2^32 GCM limit).
    /// </summary>
    public long EncryptionCount => Interlocked.Read(ref _encryptionCount);

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
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
            return Rfc2898DeriveBytes.Pbkdf2(
                passwordBytes[..passwordLen], 
                saltBytes[..saltLen], 
                CryptoConstants.PBKDF2_ITERATIONS,
                HashAlgorithmName.SHA256, 
                CryptoConstants.AES_KEY_SIZE);
        }
        finally
        {
            // SECURITY: Clear sensitive password data
            if (passwordArray != null)
                ArrayPool<byte>.Shared.Return(passwordArray, clearArray: true);
            
            if (saltArray != null)
                ArrayPool<byte>.Shared.Return(saltArray, clearArray: true);
        }
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public byte[] Encrypt(byte[] key, byte[] data)
    {
        // SECURITY: Check for GCM nonce exhaustion
        long currentCount = Interlocked.Increment(ref _encryptionCount);
        
        if (currentCount >= CryptoConstants.MAX_GCM_OPERATIONS)
        {
            throw new InvalidOperationException(
                $"Encryption limit reached ({currentCount} operations). " +
                $"Key rotation required to prevent GCM nonce collision. " +
                $"Please export and re-import the database with a new master password.");
        }
        
        if (currentCount >= CryptoConstants.GCM_OPERATIONS_WARNING_THRESHOLD)
        {
            Console.WriteLine(
                $"⚠️  WARNING: Approaching encryption limit ({currentCount}/{CryptoConstants.MAX_GCM_OPERATIONS}). " +
                $"Plan for key rotation soon.");
        }
        
        using var aes = new AesGcm(key, CryptoConstants.GCM_TAG_SIZE);
        
        // OPTIMIZED: stackalloc for nonce and tag (small fixed-size buffers)
        Span<byte> nonce = stackalloc byte[CryptoConstants.GCM_NONCE_SIZE];
        Span<byte> tag = stackalloc byte[CryptoConstants.GCM_TAG_SIZE];
        
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
            var result = new byte[CryptoConstants.GCM_NONCE_SIZE + data.Length + CryptoConstants.GCM_TAG_SIZE];
            nonce.CopyTo(result.AsSpan(0, CryptoConstants.GCM_NONCE_SIZE));
            cipher.CopyTo(result.AsSpan(CryptoConstants.GCM_NONCE_SIZE, data.Length));
            tag.CopyTo(result.AsSpan(CryptoConstants.GCM_NONCE_SIZE + data.Length, CryptoConstants.GCM_TAG_SIZE));
            
            return result;
        }
        finally
        {
            // SECURITY: Clear cipher data
            if (cipherArray != null)
                ArrayPool<byte>.Shared.Return(cipherArray, clearArray: true);
            
            // SECURITY: Clear stack buffers
            nonce.Clear();
            tag.Clear();
        }
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public byte[] Decrypt(byte[] key, byte[] encryptedData)
    {
        var cipherLength = encryptedData.Length - CryptoConstants.GCM_NONCE_SIZE - CryptoConstants.GCM_TAG_SIZE;
        if (cipherLength < 0)
            throw new ArgumentException("Invalid encrypted data length", nameof(encryptedData));

        using var aes = new AesGcm(key, CryptoConstants.GCM_TAG_SIZE);
        
        // OPTIMIZED: Use Span slicing instead of LINQ Take/Skip/TakeLast (zero allocation)
        ReadOnlySpan<byte> nonce = encryptedData.AsSpan(0, CryptoConstants.GCM_NONCE_SIZE);
        ReadOnlySpan<byte> cipher = encryptedData.AsSpan(CryptoConstants.GCM_NONCE_SIZE, cipherLength);
        ReadOnlySpan<byte> tag = encryptedData.AsSpan(CryptoConstants.GCM_NONCE_SIZE + cipherLength, CryptoConstants.GCM_TAG_SIZE);
        
        // Decrypt directly to result
        var plaintext = new byte[cipherLength];
        aes.Decrypt(nonce, cipher, tag, plaintext);
        
        return plaintext;
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void EncryptPage(Span<byte> page) =>
        // Delegate to AesGcmEncryption for page operations
        throw new NotImplementedException("Use GetAesGcmEncryption() for page-level operations");

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void DecryptPage(Span<byte> page) =>
        // Delegate to AesGcmEncryption for page operations
        throw new NotImplementedException("Use GetAesGcmEncryption() for page-level operations");

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public AesGcmEncryption GetAesGcmEncryption(byte[] key) => new(key, false);
    
    /// <summary>
    /// Resets the encryption counter.
    /// SECURITY: Should only be called after key rotation (database export/import with new password).
    /// </summary>
    public void ResetEncryptionCounter()
    {
        Interlocked.Exchange(ref _encryptionCount, 0);
    }
}
