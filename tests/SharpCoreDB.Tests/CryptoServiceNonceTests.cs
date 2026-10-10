// <copyright file="CryptoServiceNonceTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Tests;

using SharpCoreDB.Constants;
using SharpCoreDB.Services;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

/// <summary>
/// The per-call cost of <see cref="CryptoService.Encrypt"/> was dominated by constructing an
/// <see cref="System.Security.Cryptography.AesGcm"/> (key import: 0.69 µs of a 1.34 µs call) and by drawing a
/// nonce from the OS CSPRNG, while the storage layer calls it once per record *and* once per overflow-arena
/// block. The cipher is now cached per key and the nonce is <c>[random prefix(8)][counter(4)]</c>, where the
/// counter is the operation counter that already guards GCM exhaustion.
///
/// That is a SECURITY-relevant change — a repeated GCM nonce under one key leaks the keystream and the
/// authentication key — so these tests pin the properties the design relies on instead of trusting them:
/// uniqueness within an instance, correctness across key switches, thread safety, and a fresh prefix on
/// <see cref="CryptoService.ResetEncryptionCounter"/> so a counter reset can never replay a nonce.
/// </summary>
public sealed class CryptoServiceNonceTests
{
    private static byte[] Key(byte seed)
    {
        var key = new byte[32];
        for (int i = 0; i < key.Length; i++)
        {
            key[i] = (byte)(seed + i);
        }

        return key;
    }

    private static byte[] NonceOf(byte[] encrypted) =>
        encrypted.AsSpan(0, CryptoConstants.GCM_NONCE_SIZE).ToArray();

    private static uint CounterOf(byte[] nonce) =>
        BinaryPrimitives.ReadUInt32LittleEndian(nonce.AsSpan(8));

    /// <summary>Round-trip is the floor, not the point: this also proves the cached cipher is per key.</summary>
    [Fact]
    public void EncryptDecrypt_RoundTrips_AcrossKeySwitches()
    {
        using var crypto = new CryptoService();
        var keyA = Key(1);
        var keyB = Key(200);
        var payload = System.Text.Encoding.UTF8.GetBytes("sharpcoredb record payload");

        // Interleaved, so a cache keyed on the wrong thing (or a stale cipher) would produce garbage.
        for (int i = 0; i < 20; i++)
        {
            var encryptedA = crypto.Encrypt(keyA, payload);
            var encryptedB = crypto.Encrypt(keyB, payload);
            var encryptedA2 = crypto.Encrypt(keyA, payload);

            Assert.Equal(payload, crypto.Decrypt(keyA, encryptedA));
            Assert.Equal(payload, crypto.Decrypt(keyB, encryptedB));
            Assert.Equal(payload, crypto.Decrypt(keyA, encryptedA2));

            // A cipher mix-up surfaces as an authentication failure, not as a silent success.
            Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(
                () => crypto.Decrypt(keyB, encryptedA));
        }
    }

    /// <summary>Distinct nonces for identical plaintext, with the operation counter as invocation field.</summary>
    [Fact]
    public void Encrypt_ProducesUniqueNonces_AndACounterSequence()
    {
        using var crypto = new CryptoService();
        var key = Key(7);
        var payload = new byte[64];
        const int count = 2_000;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        uint previous = 0;

        for (int i = 0; i < count; i++)
        {
            var nonce = NonceOf(crypto.Encrypt(key, payload));

            Assert.True(seen.Add(Convert.ToHexString(nonce)), "nonce repeated for one key");
            uint counter = CounterOf(nonce);
            Assert.Equal(previous + 1, counter);
            previous = counter;
        }

        Assert.True(crypto.EncryptionCount >= count);
    }

    /// <summary>
    /// The prefix is the only thing keeping two instances apart, so a counter reset MUST swap it — otherwise a
    /// reset that is (wrongly) not accompanied by a key rotation would replay nonce #1 under the same key.
    /// </summary>
    [Fact]
    public void ResetEncryptionCounter_ChangesThePrefix_SoNoNonceReplays()
    {
        using var crypto = new CryptoService();
        var key = Key(11);
        var payload = new byte[32];

        var firstNonce = NonceOf(crypto.Encrypt(key, payload));
        crypto.ResetEncryptionCounter();
        var afterReset = NonceOf(crypto.Encrypt(key, payload));

        Assert.NotEqual(Convert.ToHexString(firstNonce), Convert.ToHexString(afterReset));

        // Both carry counter 1 (it restarted), so only the prefix can be responsible for the difference.
        Assert.Equal(1u, CounterOf(firstNonce));
        Assert.Equal(1u, CounterOf(afterReset));
    }

    /// <summary>
    /// Concurrent writers are the normal case for a DI singleton: the cipher cache must never hand a thread a
    /// half-built (or another thread's) cipher, and nonces must stay unique without a lock. This test caught a
    /// real defect on Linux — <see cref="System.Security.Cryptography.AesGcm"/> instance one-shots are not
    /// thread-safe off Windows (dotnet/runtime#53320), so the service now caches one cipher per (key, thread).
    /// </summary>
    [Fact]
    public void Encrypt_IsThreadSafe_AndNoncesStayUniqueUnderConcurrency()
    {
        using var crypto = new CryptoService();
        var key = Key(3);
        var payload = new byte[48];
        const int perThread = 500;

        var results = new byte[4][][];
        Parallel.For(0, results.Length, t =>
        {
            var local = new byte[perThread][];
            for (int i = 0; i < perThread; i++)
            {
                local[i] = crypto.Encrypt(key, payload);
            }

            results[t] = local;
        });

        var all = results.SelectMany(static batch => batch).ToList();
        Assert.Equal(4 * perThread, all.Count);
        Assert.Equal(
            all.Count,
            all.Select(e => Convert.ToHexString(NonceOf(e))).Distinct(StringComparer.Ordinal).Count());
        Assert.All(all, encrypted => Assert.Equal(payload, crypto.Decrypt(key, encrypted)));
    }

    /// <summary>Two instances must not share a prefix, so their nonces differ for the same key</summary>
    [Fact]
    public void TwoInstances_ProduceDifferentNonces_ForTheSameKey()
    {
        using var first = new CryptoService();
        using var second = new CryptoService();
        var key = Key(5);
        var payload = new byte[16];

        var a = NonceOf(first.Encrypt(key, payload));
        var b = NonceOf(second.Encrypt(key, payload));

        Assert.NotEqual(Convert.ToHexString(a), Convert.ToHexString(b));
    }
}
