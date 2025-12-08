// <copyright file="CryptoBenchmarks.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using SharpCoreDB.Pooling;
using SharpCoreDB.Services;
using System.Security.Cryptography;

namespace SharpCoreDB.Benchmarks;

/// <summary>
/// Benchmarks for AES-GCM encryption/decryption.
/// Compares traditional allocation vs. pooled cryptographic buffers with secure clearing.
/// </summary>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
public class CryptoBenchmarks
{
    private byte[] plaintext = null!;
    private byte[] key = null!;
    private byte[] nonce = null!;
    private byte[] ciphertext = null!;
    private byte[] tag = null!;
    private CryptoBufferPool bufferPool = null!;
    private AesGcm aesGcm = null!;
    private CryptoService cryptoService = null!;

    // Test different data sizes
    [Params(1024, 8192, 65536)] // 1KB, 8KB, 64KB (reduced for faster benchmarks)
    public int DataSize { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        // Generate test data
        plaintext = new byte[DataSize];
        Random.Shared.NextBytes(plaintext);

        // AES-256 key (32 bytes)
        key = new byte[32];
        Random.Shared.NextBytes(key);

        // 12-byte nonce (recommended for AES-GCM)
        nonce = new byte[12];
        Random.Shared.NextBytes(nonce);

        // Pre-allocate for decryption tests
        ciphertext = new byte[DataSize];
        tag = new byte[16];

        // Initialize AES-GCM
        aesGcm = new AesGcm(key, 16);

        // Encrypt once for decryption benchmarks
        aesGcm.Encrypt(nonce, plaintext, ciphertext, tag);

        // Initialize buffer pool and crypto service
        bufferPool = new CryptoBufferPool(16 * 1024 * 1024);
        cryptoService = new CryptoService();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        aesGcm?.Dispose();
        bufferPool?.Dispose();
    }

    // ==================== ENCRYPTION ====================

    [Benchmark(Baseline = true, Description = "Encrypt: Traditional (allocates)")]
    public byte[] Encrypt_Traditional()
    {
        // Traditional approach - allocates multiple buffers
        var localCiphertext = new byte[plaintext.Length];
        var localTag = new byte[16];
        var localNonce = new byte[12];
        nonce.CopyTo(localNonce, 0);

        aesGcm.Encrypt(localNonce, plaintext, localCiphertext, localTag);

        // Combine ciphertext + tag (additional allocation)
        var result = new byte[localCiphertext.Length + localTag.Length];
        localCiphertext.CopyTo(result, 0);
        localTag.CopyTo(result, localCiphertext.Length);

        return result;
    }

    [Benchmark(Description = "Encrypt: Optimized (pooled buffers)")]
    public void Encrypt_Optimized()
    {
        var ciphertextBuffer = bufferPool.RentEncryptionBuffer(plaintext.Length);
        var tagBuffer = bufferPool.RentKeyBuffer(16);
        var nonceBuffer = bufferPool.RentKeyBuffer(12);

        // Copy nonce
        nonce.CopyTo(nonceBuffer.AsSpan());

        // Encrypt using pooled buffers (zero allocation)
        aesGcm.Encrypt(
            nonceBuffer.AsSpan(),
            plaintext,
            ciphertextBuffer.AsSpan(),
            tagBuffer.AsSpan());

        ciphertextBuffer.UsedSize = plaintext.Length;
        tagBuffer.UsedSize = 16;
        nonceBuffer.UsedSize = 12;
        
        nonceBuffer.Dispose();
        tagBuffer.Dispose();
        ciphertextBuffer.Dispose();
    }

    [Benchmark(Description = "Encrypt: In-place (minimal copying)")]
    public void Encrypt_InPlace()
    {
        var buffer = bufferPool.RentEncryptionBuffer(plaintext.Length + 16);
        var nonceBuffer = bufferPool.RentKeyBuffer(12);

        nonce.CopyTo(nonceBuffer.AsSpan());
        plaintext.CopyTo(buffer.Buffer.AsSpan());

        // Encrypt in-place
        aesGcm.Encrypt(
            nonceBuffer.AsSpan(),
            buffer.Buffer.AsSpan(0, plaintext.Length),
            buffer.Buffer.AsSpan(0, plaintext.Length),
            buffer.Buffer.AsSpan(plaintext.Length, 16));

        buffer.UsedSize = plaintext.Length + 16;
        
        nonceBuffer.Dispose();
        buffer.Dispose();
    }

    // ==================== DECRYPTION ====================

    [Benchmark(Description = "Decrypt: Traditional (allocates)")]
    public byte[] Decrypt_Traditional()
    {
        // Traditional approach - allocates buffers
        var localPlaintext = new byte[ciphertext.Length];
        var localNonce = new byte[12];
        nonce.CopyTo(localNonce, 0);

        aesGcm.Decrypt(localNonce, ciphertext, tag, localPlaintext);

        return localPlaintext;
    }

    [Benchmark(Description = "Decrypt: Optimized (pooled buffers)")]
    public void Decrypt_Optimized()
    {
        var plaintextBuffer = bufferPool.RentDecryptionBuffer(ciphertext.Length);
        var nonceBuffer = bufferPool.RentKeyBuffer(12);

        nonce.CopyTo(nonceBuffer.AsSpan());

        // Decrypt using pooled buffers
        aesGcm.Decrypt(
            nonceBuffer.AsSpan(),
            ciphertext,
            tag,
            plaintextBuffer.AsSpan());

        plaintextBuffer.UsedSize = ciphertext.Length;
        
        nonceBuffer.Dispose();
        plaintextBuffer.Dispose();
    }

    // ==================== ROUND-TRIP ====================

    [Benchmark(Description = "Round-trip: Traditional (multiple allocs)")]
    public byte[] RoundTrip_Traditional()
    {
        // Encrypt
        var encrypted = Encrypt_Traditional();

        // Decrypt
        var localCiphertext = new byte[plaintext.Length];
        var localTag = new byte[16];
        Array.Copy(encrypted, 0, localCiphertext, 0, plaintext.Length);
        Array.Copy(encrypted, plaintext.Length, localTag, 0, 16);

        var decrypted = new byte[plaintext.Length];
        var localNonce = new byte[12];
        nonce.CopyTo(localNonce, 0);

        aesGcm.Decrypt(localNonce, localCiphertext, localTag, decrypted);

        return decrypted;
    }

    [Benchmark(Description = "Round-trip: Optimized (zero alloc)")]
    public void RoundTrip_Optimized()
    {
        var ciphertextBuffer = bufferPool.RentEncryptionBuffer(plaintext.Length);
        var tagBuffer = bufferPool.RentKeyBuffer(16);
        var nonceBuffer = bufferPool.RentKeyBuffer(12);
        var plaintextBuffer = bufferPool.RentDecryptionBuffer(plaintext.Length);

        nonce.CopyTo(nonceBuffer.AsSpan());

        // Encrypt
        aesGcm.Encrypt(
            nonceBuffer.AsSpan(),
            plaintext,
            ciphertextBuffer.AsSpan(),
            tagBuffer.AsSpan());

        // Decrypt
        aesGcm.Decrypt(
            nonceBuffer.AsSpan(),
            ciphertextBuffer.Buffer.AsSpan(0, plaintext.Length),
            tagBuffer.AsSpan(),
            plaintextBuffer.AsSpan());
            
        plaintextBuffer.Dispose();
        nonceBuffer.Dispose();
        tagBuffer.Dispose();
        ciphertextBuffer.Dispose();
    }

    // ==================== BUFFER POOL PERFORMANCE ====================

    [Benchmark(Description = "Crypto Pool: Rent 3 buffers")]
    public void CryptoPool_RentMultiple()
    {
        var buffer1 = bufferPool.RentEncryptionBuffer(DataSize);
        var buffer2 = bufferPool.RentKeyBuffer(32);
        var buffer3 = bufferPool.RentKeyBuffer(16);
        
        buffer3.Dispose();
        buffer2.Dispose();
        buffer1.Dispose();
    }

    [Benchmark(Description = "Crypto Pool: 100x Rent/Return")]
    public void CryptoPool_ManyOperations()
    {
        for (int i = 0; i < 100; i++)
        {
            var buffer = bufferPool.RentEncryptionBuffer(1024);
            buffer.Dispose();
        }
    }

    // ==================== SECURE CLEARING ====================

    [Benchmark(Description = "Clear: Array.Clear (not guaranteed)")]
    public void Clear_ArrayClear()
    {
        var buffer = new byte[DataSize];
        Random.Shared.NextBytes(buffer);
        Array.Clear(buffer);
    }

    [Benchmark(Description = "Clear: CryptographicOperations.ZeroMemory")]
    public void Clear_CryptoZero()
    {
        var buffer = new byte[DataSize];
        Random.Shared.NextBytes(buffer);
        CryptographicOperations.ZeroMemory(buffer);
    }

    [Benchmark(Description = "Clear: Via CryptoBufferPool (auto)")]
    public void Clear_ViaCryptoPool()
    {
        var buffer = bufferPool.RentKeyBuffer(DataSize);
        Random.Shared.NextBytes(buffer.AsSpan());
        buffer.UsedSize = DataSize;
        buffer.Dispose();
        // Automatically cleared on dispose
    }

    // ==================== AES-GCM CRYPTO SERVICE ====================

    [Benchmark(Description = "CryptoService: Encrypt (allocates)")]
    public byte[] CryptoService_Encrypt()
    {
        return cryptoService.Encrypt(key, plaintext);
    }

    [Benchmark(Description = "CryptoService: Decrypt (allocates)")]
    public byte[] CryptoService_Decrypt()
    {
        var encrypted = cryptoService.Encrypt(key, plaintext);
        return cryptoService.Decrypt(key, encrypted);
    }

    // ==================== SIMD COMPARISON ====================

    [Benchmark(Description = "Compare: Traditional (byte-by-byte)")]
    public bool Compare_Traditional()
    {
        if (plaintext.Length != ciphertext.Length)
            return false;

        for (int i = 0; i < plaintext.Length; i++)
        {
            if (plaintext[i] != ciphertext[i])
                return false;
        }
        return true;
    }

    [Benchmark(Description = "Compare: SIMD (vectorized)")]
    public bool Compare_Simd()
    {
        return SimdHelper.SequenceEqual(plaintext, ciphertext);
    }
}
