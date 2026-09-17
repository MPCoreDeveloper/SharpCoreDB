// <copyright file="CryptoService.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Services;

using SharpCoreDB.Constants;
using SharpCoreDB.Interfaces;
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
public sealed class CryptoService : ICryptoService, IDisposable
{
    private const int StackAllocThreshold = 256;

    /// <summary>Bytes of the GCM nonce reserved for the per-instance random fixed field.</summary>
    private const int GcmNoncePrefixSize = 8;

    // SECURITY: Track encryption operations to prevent GCM nonce exhaustion
    private long _encryptionCount = 0;

    /// <summary>
    /// SECURITY: the 64-bit random *fixed field* of this instance's GCM nonces. Every nonce is
    /// <c>[prefix(8)][counter(4)]</c> where the counter is <see cref="_encryptionCount"/> — whose 2^32 cap is
    /// already enforced below — so a nonce can never repeat for any key used with this instance, and two
    /// instances collide only if their 64-bit prefixes do (2^-64 per pair, independent of how many records
    /// are written). In NIST SP 800-38D terms this is a deterministic construction with a unique fixed field
    /// plus a unique invocation field, which is explicitly acceptable; a random nonce per call was not — its
    /// collision probability grows with the number of records, which is why the 2^32 guard existed.
    /// Swapped (never mutated in place) so a reader can never observe a torn prefix.
    /// </summary>
    private byte[] _noncePrefix = CreateNoncePrefix();

    /// <summary>
    /// PERF: one <see cref="AesGcm"/> per key *and per thread* instead of one per call. Constructing the
    /// cipher imports the key — measured **0.69 µs per call, 59% of an Encrypt call** on this machine — and
    /// the storage layer makes one Encrypt/Decrypt call per record *and* per overflow-arena block, so a row
    /// with three TEXT columns paid that import three to four times (the whole measured at-rest INSERT tax on
    /// the acceptance shape).
    ///
    /// SECURITY: the cache is keyed by thread as well as by key because <see cref="AesGcm"/> instance
    /// one-shots are **not thread-safe off Windows**. dotnet/runtime#53320 (Microsoft's crypto lead):
    /// "AesGcm.Encrypt is thread-safe on Windows but not on other operating systems", and the failure mode is
    /// "corruption of the managed buffer, corruption of the underlying native handle, or even nonce reuse
    /// (which would destroy GHASH)". One cipher per key shared across threads therefore passed on a Windows
    /// developer box and failed on Linux: <c>CryptoServiceNonceTests.Encrypt_IsThreadSafe_AndNoncesStayUniqueUnderConcurrency</c>
    /// fails on ubuntu-latest with <c>CryptographicException : Error occurred during a cryptographic
    /// operation</c> out of <c>AesGcm.EncryptCore</c>. One instance per (key, thread) keeps the key-import win
    /// with no lock, no contention and no cross-thread use of one native handle.
    ///
    /// The outer dictionary's key is the caller-independent key COPY (see <see cref="GetCipher"/>): the
    /// structural comparer must never be able to match an entry built from a mutated array.
    /// </summary>
    private readonly ConcurrentDictionary<byte[], ConcurrentDictionary<int, AesGcm>> _ciphers =
        new(KeyComparer.Instance);

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
        // SECURITY: the nonce is [prefix(8)][counter(4)], and ResetEncryptionCounter swaps the prefix *and*
        // resets the counter. The two must therefore be read as one unit: a call that took counter 5 before a
        // reset must not pair it with the post-reset prefix, because a later call can legitimately reach
        // counter 5 again under that same prefix — a repeated GCM nonce, which destroys GHASH. The re-read
        // below is a seqlock: it retries only when a reset landed inside this call, so the hot path pays one
        // extra cached load and can never emit a mixed pair.
        byte[] prefix;
        long currentCount;
        do
        {
            prefix = Volatile.Read(ref _noncePrefix);
            currentCount = Interlocked.Increment(ref _encryptionCount);
        }
        while (!ReferenceEquals(prefix, Volatile.Read(ref _noncePrefix)));

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
        
        // PERF: ciphertext and tag go straight into the result buffer. The previous code rented a pooled
        // buffer for the ciphertext and then copied it into the result, so the rent bought nothing except a
        // second buffer; the pooled cipher removes the per-call key import (see _ciphers).
        var result = new byte[CryptoConstants.GCM_NONCE_SIZE + data.Length + CryptoConstants.GCM_TAG_SIZE];
        Span<byte> nonce = result.AsSpan(0, CryptoConstants.GCM_NONCE_SIZE);
        Span<byte> cipher = result.AsSpan(CryptoConstants.GCM_NONCE_SIZE, data.Length);
        Span<byte> tag = result.AsSpan(CryptoConstants.GCM_NONCE_SIZE + data.Length, CryptoConstants.GCM_TAG_SIZE);

        BuildNonce(nonce, prefix, currentCount);
        GetCipher(key).Encrypt(nonce, data, cipher, tag);

        return result;
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public byte[] Decrypt(byte[] key, byte[] encryptedData)
    {
        var cipherLength = encryptedData.Length - CryptoConstants.GCM_NONCE_SIZE - CryptoConstants.GCM_TAG_SIZE;
        if (cipherLength < 0)
            throw new ArgumentException("Invalid encrypted data length", nameof(encryptedData));

        var aes = GetCipher(key);
        
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
    public void EncryptPage(Span<byte> page)
    {
        // Compatibility path: page-level encryption is handled by AesGcmEncryption in storage pipeline.
        // Keep as no-op to avoid runtime failures in legacy call sites.
        _ = page;
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void DecryptPage(Span<byte> page)
    {
        // Compatibility path: page-level decryption is handled by AesGcmEncryption in storage pipeline.
        _ = page;
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public AesGcmEncryption GetAesGcmEncryption(byte[] key) => new(key, false);
    
    /// <summary>
    /// Resets the encryption counter.
    /// SECURITY: Should only be called after key rotation (database export/import with new password).
    /// </summary>
    /// <remarks>
    /// The counter is half of every nonce, so a reset must never replay one: a fresh random prefix is swapped
    /// in FIRST, which makes every (prefix, counter) pair from before the reset unreachable — even if a caller
    /// resets the counter without rotating the key (which the contract forbids, but silent nonce reuse would
    /// be catastrophic, so it is defended against rather than trusted).
    /// </remarks>
    public void ResetEncryptionCounter()
    {
        Interlocked.Exchange(ref _noncePrefix, CreateNoncePrefix());
        Interlocked.Exchange(ref _encryptionCount, 0);
    }

    /// <summary>Releases the cached ciphers (each holds a native key handle) for every key and thread.</summary>
    public void Dispose()
    {
        foreach (var byThread in _ciphers.Values)
        {
            foreach (var cipher in byThread.Values)
            {
                cipher.Dispose();
            }

            byThread.Clear();
        }

        _ciphers.Clear();
    }

    /// <summary>Draws a fresh 64-bit nonce prefix from the OS CSPRNG.</summary>
    private static byte[] CreateNoncePrefix()
    {
        var prefix = new byte[GcmNoncePrefixSize];
        RandomNumberGenerator.Fill(prefix);
        return prefix;
    }

    /// <summary>
    /// Writes <c>[prefix(8)][counter(4)]</c> into <paramref name="nonce"/>. Only the low 32 bits of the counter
    /// are used, which is sound because the exhaustion guard above throws before the counter reaches 2^32 — so
    /// the invocation field never repeats within this instance, and with a fixed prefix the nonce never does.
    /// The prefix is passed in rather than re-read here so that the pair the caller validated (see the seqlock
    /// in <see cref="Encrypt"/>) is exactly the pair that is written.
    /// </summary>
    private static void BuildNonce(Span<byte> nonce, byte[] prefix, long counter)
    {
        prefix.CopyTo(nonce);
        BinaryPrimitives.WriteUInt32LittleEndian(nonce[GcmNoncePrefixSize..], (uint)counter);
    }

    /// <summary>
    /// Returns this thread's cached cipher for <paramref name="key"/>, creating it once. The outer dictionary
    /// key is a COPY of the caller's key: if a caller mutated its array in place, an entry built from the old
    /// bytes must never match the new bytes — that would encrypt with the wrong key.
    /// </summary>
    private AesGcm GetCipher(byte[] key)
    {
        if (!_ciphers.TryGetValue(key, out var byThread))
        {
            var keyCopy = (byte[])key.Clone();
            byThread = _ciphers.GetOrAdd(keyCopy, static _ => new ConcurrentDictionary<int, AesGcm>());
        }

        int threadId = Environment.CurrentManagedThreadId;
        if (byThread.TryGetValue(threadId, out var cached))
        {
            return cached;
        }

        var created = new AesGcm(key, CryptoConstants.GCM_TAG_SIZE);
        var stored = byThread.GetOrAdd(threadId, created);
        if (!ReferenceEquals(stored, created))
        {
            created.Dispose(); // the entry already existed (managed thread ids are reused after a thread dies)
        }

        return stored;
    }

    /// <summary>Structural equality over the full key bytes — no fingerprinting, so no false match.</summary>
    private sealed class KeyComparer : IEqualityComparer<byte[]>
    {
        internal static readonly KeyComparer Instance = new();

        public bool Equals(byte[]? x, byte[]? y) =>
            ReferenceEquals(x, y) || (x is not null && y is not null && x.AsSpan().SequenceEqual(y));

        public int GetHashCode(byte[] obj)
        {
            var hash = new HashCode();
            hash.AddBytes(obj);
            return hash.ToHashCode();
        }
    }
}
