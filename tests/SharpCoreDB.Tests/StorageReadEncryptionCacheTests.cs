// <copyright file="StorageReadEncryptionCacheTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License.
// </copyright>

using System.Security.Cryptography;
using SharpCoreDB.Interfaces;
using SharpCoreDB.Services;
using Xunit;

// NOTE: there is both a SharpCoreDB.Storage namespace and a SharpCoreDB.Services.Storage type, so the
// type is fully qualified below.


namespace SharpCoreDB.Tests;

/// <summary>
/// Guards the read-encryption memo
/// (<c>docs/performance/INSERT_UPDATE_PERFORMANCE_PLAN.md</c> §3-1a): a plaintext file must be
/// probed <b>once</b>, not decrypt-attempted on every read — and a write must never leave a stale
/// verdict behind, which would serve ciphertext as if it were plain.
/// </summary>
public sealed class StorageReadEncryptionCacheTests : IDisposable
{
    private readonly string _dir;
    private readonly byte[] _key = new byte[32];
    private readonly CountingCryptoService _crypto = new();

    public StorageReadEncryptionCacheTests()
        => _dir = Path.Combine(Path.GetTempPath(), $"SCDB_ReadEnc_{Guid.NewGuid():N}");

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir))
            {
                Directory.Delete(_dir, true);
            }
        }
        catch
        {
            // best effort
        }
    }

    [Fact]
    public void PlaintextFile_IsProbedOnce_ThenServedRaw()
    {
        Directory.CreateDirectory(_dir);
        string path = Path.Combine(_dir, $"plain-{Guid.NewGuid():N}.dat");
        byte[] content = "plain, not encrypted"u8.ToArray();
        File.WriteAllBytes(path, content);

        var storage = new SharpCoreDB.Services.Storage(_crypto, _key, new DatabaseConfig());

        for (int i = 0; i < 10; i++)
        {
            Assert.Equal(content, storage.ReadBytes(path)!);
        }

        // One probe for the whole file: the answer is remembered.
        Assert.Equal(1, _crypto.DecryptCalls);
    }

    [Fact]
    public void NoEncryptMode_DoesNotProbeAtAll()
    {
        Directory.CreateDirectory(_dir);
        string path = Path.Combine(_dir, $"plain-{Guid.NewGuid():N}.dat");
        byte[] content = "plain"u8.ToArray();
        File.WriteAllBytes(path, content);

        var storage = new SharpCoreDB.Services.Storage(_crypto, _key, new DatabaseConfig { NoEncryptMode = true });

        for (int i = 0; i < 10; i++)
        {
            Assert.Equal(content, storage.ReadBytes(path)!);
        }

        Assert.Equal(0, _crypto.DecryptCalls);
    }

    [Fact]
    public void EncryptedFile_RoundTrips_AndDecryptsOncePerRead()
    {
        Directory.CreateDirectory(_dir);
        string path = Path.Combine(_dir, $"enc-{Guid.NewGuid():N}.dat");
        byte[] content = "secret payload"u8.ToArray();

        var storage = new SharpCoreDB.Services.Storage(_crypto, _key, new DatabaseConfig());
        storage.WriteBytes(path, content);

        for (int i = 0; i < 3; i++)
        {
            Assert.Equal(content, storage.ReadBytes(path)!);
        }

        // An encrypted file decrypts once per read, and the first read doubles as the probe (which
        // succeeds), so this is exactly one decrypt per read — no extra attempt is spent.
        Assert.Equal(3, _crypto.DecryptCalls);
    }

    [Fact]
    public void Write_ReDecidesTheVerdict_SoAStaleAnswerIsImpossible()
    {
        Directory.CreateDirectory(_dir);
        string path = Path.Combine(_dir, $"flip-{Guid.NewGuid():N}.dat");
        File.WriteAllBytes(path, "plain first"u8.ToArray());

        var storage = new SharpCoreDB.Services.Storage(_crypto, _key, new DatabaseConfig());
        Assert.Equal("plain first"u8.ToArray(), storage.ReadBytes(path)!);
        Assert.Equal(1, _crypto.DecryptCalls);

        byte[] written = "now encrypted"u8.ToArray();
        storage.WriteBytes(path, written);

        // The write must have refreshed the verdict: reading back has to return the plaintext, never
        // the bytes that are now on disk.
        Assert.Equal(written, storage.ReadBytes(path)!);
        Assert.True(_crypto.DecryptCalls >= 2, $"expected a fresh probe, got {_crypto.DecryptCalls}");
    }

    /// <summary>
    /// Test double: "encryption" is a leading 0xE1 marker, so plaintext input makes
    /// <see cref="Decrypt"/> throw exactly like the real AES-GCM does — which is the cost the read
    /// path used to pay on every read of a plaintext file.
    /// </summary>
    private sealed class CountingCryptoService : ICryptoService
    {
        private const byte Marker = 0xE1;

        public int DecryptCalls;

        public int EncryptCalls;

        public byte[] DeriveKey(string password, string salt) => new byte[32];

        public byte[] Encrypt(byte[] key, byte[] data)
        {
            EncryptCalls++;
            var result = new byte[data.Length + 1];
            result[0] = Marker;
            data.CopyTo(result, 1);
            return result;
        }

        public byte[] Decrypt(byte[] key, byte[] encryptedData)
        {
            DecryptCalls++;
            if (encryptedData.Length == 0 || encryptedData[0] != Marker)
            {
                throw new CryptographicException("not a ciphertext (test double)");
            }

            return encryptedData[1..];
        }

        public void EncryptPage(Span<byte> page)
        {
        }

        public void DecryptPage(Span<byte> page)
        {
        }

        public AesGcmEncryption GetAesGcmEncryption(byte[] key) => new(key);
    }
}
