// <copyright file="AesGcmEncryptionNonceTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Tests;

using SharpCoreDB.Services;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

/// <summary>
/// <see cref="AesGcmEncryption"/> constructed a fresh <see cref="System.Security.Cryptography.AesGcm"/> (key
/// import: 0.69 µs of a 1.34 µs call) and drew an OS-CSPRNG nonce on every page/blob operation. It now caches
/// one cipher per instance and builds nonces as <c>[random prefix(8)][operation counter(4)]</c>. Its holders
/// (<c>DatabaseFile</c>, <c>PageEncryption</c>, the single-file provider) keep one instance for a long time
/// and use it from several threads, so these tests pin: round-trips per key, nonce uniqueness + the counter
/// sequence, the span/page APIs, and — because the shared cipher is an assumption, not a guarantee this
/// project measured before — that concurrent use of ONE instance stays correct.
/// </summary>
public sealed class AesGcmEncryptionNonceTests
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

    private static uint CounterOf(byte[] encrypted) =>
        BinaryPrimitives.ReadUInt32LittleEndian(encrypted.AsSpan(8, 4));

    [Fact]
    public void EncryptDecrypt_RoundTrips_PerKey_AndThroughTheSpanApi()
    {
        using var cipherA = new AesGcmEncryption(Key(1));
        using var cipherB = new AesGcmEncryption(Key(90));
        var payload = System.Text.Encoding.UTF8.GetBytes("page-or-blob-payload");

        for (int i = 0; i < 10; i++)
        {
            var encA = cipherA.Encrypt(payload);
            var encB = cipherB.Encrypt(payload);
            Assert.Equal(payload, cipherA.Decrypt(encA));
            Assert.Equal(payload, cipherB.Decrypt(encB));

            // Span API (the page path) round-trips too.
            var buffer = new byte[AesGcmEncryption.OverheadSize + payload.Length];
            int written = cipherA.Encrypt(payload, buffer, ReadOnlySpan<byte>.Empty);
            Assert.Equal(buffer.Length, written);
            var plain = new byte[payload.Length];
            Assert.Equal(payload.Length, cipherA.Decrypt(buffer, plain, ReadOnlySpan<byte>.Empty));
            Assert.Equal(payload, plain);
        }
    }

    [Fact]
    public void Encrypt_ProducesUniqueNonces_WithACounterSequence()
    {
        using var cipher = new AesGcmEncryption(Key(5));
        var payload = new byte[128];
        const int count = 2_000;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        uint previous = 0;

        for (int i = 0; i < count; i++)
        {
            var encrypted = cipher.Encrypt(payload);
            var nonce = encrypted.AsSpan(0, 12).ToArray();

            Assert.True(seen.Add(Convert.ToHexString(nonce)), "nonce repeated for one key");
            Assert.Equal(previous + 1, CounterOf(encrypted));
            previous = CounterOf(encrypted);
        }
    }

    /// <summary>
    /// This test is what caught a real defect: <see cref="System.Security.Cryptography.AesGcm"/> instance
    /// one-shots are **not thread-safe off Windows** (dotnet/runtime#53320 — "thread-safe on Windows but not on
    /// other operating systems", with buffer/handle corruption and nonce reuse among the failure modes), so one
    /// cached cipher shared by every thread passed on a Windows developer box and failed on ubuntu-latest with
    /// <c>CryptographicException : Error occurred during a cryptographic operation</c> out of
    /// <c>AesGcm.EncryptCore</c>. The instance now keeps one cipher per thread; this asserts that concurrent use
    /// of ONE <see cref="AesGcmEncryption"/> instance stays correct and unique.
    /// </summary>
    [Fact]
    public void OneInstance_IsSafeUnderConcurrentUse()
    {
        using var cipher = new AesGcmEncryption(Key(9));
        var payload = new byte[512];
        Random.Shared.NextBytes(payload);
        const int perThread = 300;

        var results = new byte[Environment.ProcessorCount][][];
        Parallel.For(0, results.Length, t =>
        {
            var local = new byte[perThread][];
            for (int i = 0; i < perThread; i++)
            {
                local[i] = cipher.Encrypt(payload);
            }

            results[t] = local;
        });

        var all = results.SelectMany(static batch => batch).ToList();
        Assert.Equal(results.Length * perThread, all.Count);
        Assert.Equal(
            all.Count,
            all.Select(e => Convert.ToHexString(e.AsSpan(0, 12))).Distinct(StringComparer.Ordinal).Count());
        Assert.All(all, encrypted => Assert.Equal(payload, cipher.Decrypt(encrypted)));
    }

    /// <summary>Page encryption writes the nonce in place; two pages must not share one.</summary>
    [Fact]
    public void EncryptPage_ProducesUniqueNonces_AndRoundTrips()
    {
        using var cipher = new AesGcmEncryption(Key(13));
        var seen = new HashSet<string>(StringComparer.Ordinal);

        // Page format: [nonce(12)][ciphertext][tag(16)], and the plaintext is the page minus that overhead.
        const int pageSize = 1024;
        int dataSize = pageSize - AesGcmEncryption.OverheadSize;

        for (int i = 0; i < 200; i++)
        {
            var page = new byte[pageSize];
            Random.Shared.NextBytes(page);
            var expectedPlaintext = page.AsSpan(0, dataSize).ToArray();

            cipher.EncryptPage(page);
            Assert.True(seen.Add(Convert.ToHexString(page.AsSpan(0, 12))), "page nonce repeated");

            cipher.DecryptPage(page);
            Assert.Equal(expectedPlaintext, page.AsSpan(0, dataSize).ToArray());
        }
    }
}
