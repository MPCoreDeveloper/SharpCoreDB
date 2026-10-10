// <copyright file="AesGcmEncryption.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Services;

using System;
using System.Buffers;
using System.Collections.Concurrent;
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

    /// <summary>
    /// PERF: one cipher per (INSTANCE, THREAD) instead of one per call. Constructing <see cref="AesGcm"/>
    /// imports the key — measured **0.69 µs of a 1.34 µs Encrypt call, 59%** on this machine — and the
    /// holders of this class (<c>DatabaseFile</c>, <c>PageEncryption</c> and the single-file provider) keep
    /// one instance for a long time and use it from several threads, so the cache is effective.
    ///
    /// SECURITY: it is keyed by thread because <see cref="AesGcm"/> instance one-shots are **not
    /// thread-safe off Windows**. dotnet/runtime#53320 (Microsoft's crypto lead) states it plainly:
    /// "AesGcm.Encrypt is thread-safe on Windows but not on other operating systems", and the failure mode
    /// is "corruption of the managed buffer, corruption of the underlying native handle, or even nonce reuse
    /// (which would destroy GHASH)". One cipher shared across threads therefore passed on a Windows
    /// developer box and failed on Linux: <c>AesGcmEncryptionNonceTests.OneInstance_IsSafeUnderConcurrentUse</c>
    /// fails on ubuntu-latest with <c>CryptographicException : Error occurred during a cryptographic
    /// operation</c> out of <c>AesGcm.EncryptCore</c>. A thread-affine instance keeps the key-import win
    /// with no lock, no contention and no cross-thread use of one native handle.
    /// </summary>
    private readonly ConcurrentDictionary<int, AesGcm> _ciphersByThread = new();

    /// <summary>
    /// SECURITY: the 64-bit random fixed field of this instance's GCM nonces; every nonce is
    /// <c>[prefix(8)][counter(4)]</c> with <see cref="_operationCount"/> as the invocation field, capped at
    /// <see cref="Constants.CryptoConstants.MAX_GCM_OPERATIONS"/> so it cannot wrap. Two instances with the
    /// same key collide only if their 64-bit prefixes do, independent of how many records they write — a
    /// random nonce per call had a collision probability that grew with the operation count instead.
    /// </summary>
    private byte[] _noncePrefix = CreateNoncePrefix();

    /// <summary>Invocation field of this instance's nonces; see <see cref="_noncePrefix"/>.</summary>
    private long _operationCount;

    /// <summary>Bytes of the nonce reserved for the per-instance random fixed field.</summary>
    private const int NoncePrefixSize = 8;

    /// <summary>Draws a fresh 64-bit nonce prefix from the OS CSPRNG.</summary>
    private static byte[] CreateNoncePrefix()
    {
        var prefix = new byte[NoncePrefixSize];
        RandomNumberGenerator.Fill(prefix);
        return prefix;
    }

    /// <summary>
    /// This thread's cipher for the instance key, created once per thread and disposed with the instance.
    /// </summary>
    private AesGcm Cipher
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            int threadId = Environment.CurrentManagedThreadId;
            return _ciphersByThread.TryGetValue(threadId, out var cached) ? cached : CreateCipher(threadId);
        }
    }

    private AesGcm CreateCipher(int threadId)
    {
        var created = new AesGcm(_key, TagSize);
        var stored = _ciphersByThread.GetOrAdd(threadId, created);
        if (!ReferenceEquals(stored, created))
        {
            created.Dispose(); // the entry already existed (managed thread ids are reused after a thread dies)
        }

        return stored;
    }

    /// <summary>
    /// Writes <c>[prefix(8)][counter(4)]</c> into <paramref name="nonce"/>. Throws rather than wrapping the
    /// invocation field: a repeated GCM nonce under one key leaks the keystream.
    /// </summary>
    private void BuildNonce(Span<byte> nonce)
    {
        long count = Interlocked.Increment(ref _operationCount);
        if (count >= Constants.CryptoConstants.MAX_GCM_OPERATIONS)
        {
            throw new InvalidOperationException(
                $"Encryption limit reached ({count} operations). Key rotation required to prevent GCM nonce reuse.");
        }

        Volatile.Read(ref _noncePrefix).CopyTo(nonce);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(nonce[NoncePrefixSize..], (uint)count);
    }

    // Size constants for AES-GCM
    private const int NonceSize = 12; // AesGcm.NonceByteSizes.MaxSize = 12
    private const int TagSize = 16;   // AesGcm.TagByteSizes.MaxSize = 16
    private const int StackAllocThreshold = 256; // Use stackalloc for buffers <= 256 bytes

    /// <summary>Total overhead (nonce + tag) added to every encrypted blob or page.</summary>
    public const int OverheadSize = NonceSize + TagSize;

    /// <summary>
    /// Derives a 32-byte key-encryption-key from a password using PBKDF2-HMAC-SHA256.
    /// Uses the OWASP-2024 recommended iteration count by default.
    /// </summary>
    /// <param name="password">The user password/passphrase.</param>
    /// <param name="salt">Per-file random salt (at least 16 bytes recommended).</param>
    /// <param name="iterations">PBKDF2 iteration count.</param>
    /// <returns>The derived 32-byte key.</returns>
    public static byte[] DeriveKeyFromPassword(string password, ReadOnlySpan<byte> salt, int iterations)
    {
        ArgumentNullException.ThrowIfNull(password);
        if (salt.Length == 0)
            throw new ArgumentException("Salt must not be empty.", nameof(salt));
        if (iterations < 1000)
            throw new ArgumentOutOfRangeException(nameof(iterations), "Iterations must be >= 1000.");

        var passwordBytes = System.Text.Encoding.UTF8.GetBytes(password);
        try
        {
            return Rfc2898DeriveBytes.Pbkdf2(
                passwordBytes,
                salt,
                iterations,
                HashAlgorithmName.SHA256,
                Constants.CryptoConstants.AES_KEY_SIZE);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
        }
    }

    /// <summary>
    /// Wraps a data-encryption-key with a key-encryption-key using AES-256-GCM.
    /// Output format: [nonce(12)][ciphertext][tag(16)].
    /// </summary>
    /// <param name="kek">The wrapping key (32 bytes).</param>
    /// <param name="dek">The key to wrap (32 bytes).</param>
    /// <returns>The wrapped key blob.</returns>
    public static byte[] WrapKey(byte[] kek, byte[] dek)
    {
        ArgumentNullException.ThrowIfNull(kek);
        ArgumentNullException.ThrowIfNull(dek);

        using var aes = new AesGcm(kek, TagSize);

        Span<byte> nonce = stackalloc byte[NonceSize];
        Span<byte> tag = stackalloc byte[TagSize];
        RandomNumberGenerator.Fill(nonce);

        var cipher = new byte[dek.Length];
        aes.Encrypt(nonce, dek, cipher, tag);

        var result = new byte[NonceSize + dek.Length + TagSize];
        nonce.CopyTo(result.AsSpan(0, NonceSize));
        cipher.CopyTo(result.AsSpan(NonceSize, dek.Length));
        tag.CopyTo(result.AsSpan(NonceSize + dek.Length, TagSize));

        nonce.Clear();
        tag.Clear();
        CryptographicOperations.ZeroMemory(cipher);

        return result;
    }

    /// <summary>
    /// Unwraps a data-encryption-key wrapped with <see cref="WrapKey"/>.
    /// Throws <see cref="CryptographicException"/> when the KEK is wrong or the blob was tampered with.
    /// </summary>
    /// <param name="kek">The unwrapping key (32 bytes).</param>
    /// <param name="wrapped">The wrapped key blob: [nonce(12)][ciphertext][tag(16)].</param>
    /// <returns>The unwrapped key (32 bytes).</returns>
    public static byte[] UnwrapKey(byte[] kek, ReadOnlySpan<byte> wrapped)
    {
        ArgumentNullException.ThrowIfNull(kek);
        if (wrapped.Length <= NonceSize + TagSize)
            throw new ArgumentException("Wrapped key blob is too short.", nameof(wrapped));

        using var aes = new AesGcm(kek, TagSize);

        var cipherLength = wrapped.Length - NonceSize - TagSize;
        var plain = new byte[cipherLength];
        aes.Decrypt(
            wrapped[..NonceSize],
            wrapped.Slice(NonceSize, cipherLength),
            wrapped[(NonceSize + cipherLength)..],
            plain);
        return plain;
    }

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

        var aes = Cipher;

        Span<byte> nonce = stackalloc byte[NonceSize];
        Span<byte> tag = stackalloc byte[TagSize];

        BuildNonce(nonce);

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

        var aes = Cipher;

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

        var aes = Cipher;

        Span<byte> nonce = stackalloc byte[NonceSize];
        Span<byte> tag = stackalloc byte[TagSize];

        BuildNonce(nonce);

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

        var aes = Cipher;

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

        var aes = Cipher;

        Span<byte> nonce = stackalloc byte[NonceSize];
        Span<byte> tag = stackalloc byte[TagSize];

        BuildNonce(nonce);

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

        var aes = Cipher;

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
        // Dispose every thread's cipher as well: leaving one alive would let a reuse-after-dispose silently
        // encrypt with the pre-clear key, which is exactly the trap the key clearing below exists to avoid.
        foreach (var cipher in _ciphersByThread.Values)
        {
            cipher.Dispose();
        }

        _ciphersByThread.Clear();

        if (_key.Length > 0)
            Array.Clear(_key);

        Array.Clear(_noncePrefix);
    }
}
