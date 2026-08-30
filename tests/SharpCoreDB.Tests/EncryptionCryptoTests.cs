// <copyright file="EncryptionCryptoTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Tests;

using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.Services;
using SharpCoreDB.Storage;
using System;
using System.IO;
using System.Security.Cryptography;
using Xunit;

/// <summary>
/// Unit tests for the envelope-encryption key model and the AES-GCM page cipher used by
/// single-file (.scdb) full at-rest encryption (block registry / FSM / WAL regions).
/// </summary>
public sealed class EncryptionCryptoTests : IDisposable
{
    private readonly DatabaseFactory _factory;
    private readonly string _scdbPath;

    public EncryptionCryptoTests()
    {
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        _factory = services.BuildServiceProvider().GetRequiredService<DatabaseFactory>();
        _scdbPath = Path.Combine(Path.GetTempPath(), $"SCDB_Crypto_{Guid.NewGuid():N}.scdb");
    }

    public void Dispose()
    {
        try { if (File.Exists(_scdbPath)) File.Delete(_scdbPath); } catch { /* best-effort cleanup */ }
    }

    // ── Password → KEK derivation (PBKDF2-HMAC-SHA256) ──────────────────────────────

    [Fact]
    public void DeriveKeyFromPassword_Returns32BytesAndIsDeterministic()
    {
        var salt = RandomNumberGenerator.GetBytes(32);

        var first = AesGcmEncryption.DeriveKeyFromPassword("correct horse", salt, 10_000);
        var second = AesGcmEncryption.DeriveKeyFromPassword("correct horse", salt, 10_000);

        Assert.Equal(32, first.Length);
        Assert.Equal(first, second);
    }

    [Fact]
    public void DeriveKeyFromPassword_DifferentSaltsProduceDifferentKeys()
    {
        var saltA = RandomNumberGenerator.GetBytes(32);
        var saltB = RandomNumberGenerator.GetBytes(32);

        var keyA = AesGcmEncryption.DeriveKeyFromPassword("password", saltA, 10_000);
        var keyB = AesGcmEncryption.DeriveKeyFromPassword("password", saltB, 10_000);

        Assert.NotEqual(keyA, keyB);
    }

    [Fact]
    public void DeriveKeyFromPassword_RejectsInvalidInput()
    {
        Assert.Throws<ArgumentException>(() => AesGcmEncryption.DeriveKeyFromPassword("pw", ReadOnlySpan<byte>.Empty, 10_000));
        Assert.Throws<ArgumentOutOfRangeException>(() => AesGcmEncryption.DeriveKeyFromPassword("pw", new byte[16], 999));
    }

    // ── DEK wrap / unwrap (envelope encryption) ─────────────────────────────────────

    [Fact]
    public void WrapKey_UnwrapKey_RoundTrips()
    {
        var kek = RandomNumberGenerator.GetBytes(32);
        var dek = RandomNumberGenerator.GetBytes(32);

        var wrapped = AesGcmEncryption.WrapKey(kek, dek);
        Assert.Equal(12 + 32 + 16, wrapped.Length); // nonce + ciphertext + tag

        var unwrapped = AesGcmEncryption.UnwrapKey(kek, wrapped);
        Assert.Equal(dek, unwrapped);
    }

    [Fact]
    public void UnwrapKey_WrongKek_ThrowsCryptographicException()
    {
        var kek = RandomNumberGenerator.GetBytes(32);
        var wrongKek = RandomNumberGenerator.GetBytes(32);
        var wrapped = AesGcmEncryption.WrapKey(kek, RandomNumberGenerator.GetBytes(32));

        Assert.ThrowsAny<CryptographicException>(() => AesGcmEncryption.UnwrapKey(wrongKek, wrapped));
    }

    [Fact]
    public void UnwrapKey_TamperedBlob_ThrowsCryptographicException()
    {
        var kek = RandomNumberGenerator.GetBytes(32);
        var wrapped = AesGcmEncryption.WrapKey(kek, RandomNumberGenerator.GetBytes(32));

        wrapped[24] ^= 0xFF; // flip a ciphertext byte

        Assert.ThrowsAny<CryptographicException>(() => AesGcmEncryption.UnwrapKey(kek, wrapped));
    }

    // ── In-place page cipher (used for the registry / FSM / WAL regions) ────────────

    [Fact]
    public void EncryptPage_DecryptPage_RoundTripsFixedRegion()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var region = new byte[16 * 1024];

        // Fill the region like a block registry: header magic at the start + entries + zero padding.
        Span<byte> magic = [0x42, 0x52, 0x45, 0x47]; // "BREG"
        magic.CopyTo(region.AsSpan(0, 4));
        for (var i = 4; i < 512; i++)
        {
            region[i] = (byte)(i % 251);
        }

        var encrypted = (byte[])region.Clone();

        using (var cipher = new AesGcmEncryption(key))
        {
            cipher.EncryptPage(encrypted);
        }

        Assert.NotEqual(region.AsSpan(0, 16).ToArray(), encrypted.AsSpan(0, 16).ToArray());

        var decrypted = (byte[])encrypted.Clone();
        using (var cipher = new AesGcmEncryption(key))
        {
            cipher.DecryptPage(decrypted);
        }

        // DecryptPage restores region[0 .. length - OverheadSize]; the trailing overhead bytes
        // are ciphertext remnants and must not be parsed.
        Assert.Equal(region.AsSpan(0, region.Length - AesGcmEncryption.OverheadSize).ToArray(),
            decrypted.AsSpan(0, region.Length - AesGcmEncryption.OverheadSize).ToArray());
        Assert.Equal(0x42, decrypted[0]);
        Assert.Equal(0x47, decrypted[3]);
    }

    [Fact]
    public void DecryptPage_WrongKey_ThrowsCryptographicException()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var wrongKey = RandomNumberGenerator.GetBytes(32);
        var region = new byte[16 * 1024];
        region.AsSpan().Fill(0xAB);

        using (var cipher = new AesGcmEncryption(key))
        {
            cipher.EncryptPage(region);
        }

        using (var cipher = new AesGcmEncryption(wrongKey))
        {
            Assert.ThrowsAny<CryptographicException>(() => cipher.DecryptPage(region));
        }
    }

    // ── DatabaseOptions validation ──────────────────────────────────────────────────

    [Fact]
    public void DatabaseOptions_EncryptionValidation()
    {
        // Password-only is valid.
        var passwordOnly = new DatabaseOptions
        {
            StorageMode = StorageMode.SingleFile,
            EnableEncryption = true,
            EncryptionPassword = "pw",
        };
        passwordOnly.Validate();

        // Raw key-only is valid.
        var keyOnly = new DatabaseOptions
        {
            StorageMode = StorageMode.SingleFile,
            EnableEncryption = true,
            EncryptionKey = RandomNumberGenerator.GetBytes(32),
        };
        keyOnly.Validate();

        // Both set → ambiguous.
        var both = new DatabaseOptions
        {
            StorageMode = StorageMode.SingleFile,
            EnableEncryption = true,
            EncryptionKey = RandomNumberGenerator.GetBytes(32),
            EncryptionPassword = "pw",
        };
        Assert.Throws<ArgumentException>(() => both.Validate());

        // Neither set → invalid.
        var neither = new DatabaseOptions
        {
            StorageMode = StorageMode.SingleFile,
            EnableEncryption = true,
        };
        Assert.Throws<ArgumentException>(() => neither.Validate());

        // Wrong key length → invalid.
        var shortKey = new DatabaseOptions
        {
            StorageMode = StorageMode.SingleFile,
            EnableEncryption = true,
            EncryptionKey = new byte[16],
        };
        Assert.Throws<ArgumentException>(() => shortKey.Validate());
    }

    // ── Rotation failure modes return Failed results (no exception) ────────────────

    [Fact]
    public async Task ChangePassword_OnRawKeyDatabase_ReturnsFailedResult()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var db = _factory.CreateWithOptions(_scdbPath, "unused", new DatabaseOptions
        {
            StorageMode = StorageMode.SingleFile,
            EnableEncryption = true,
            EncryptionKey = key,
            CreateImmediately = true,
        });
        try
        {
            var result = await db.ChangeEncryptionPasswordAsync("new-password");
            Assert.False(result.Success);
            Assert.NotNull(result.ErrorMessage);
            Assert.Equal(EncryptionRotationOperation.PasswordChanged, result.Operation);
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public async Task RotateKey_WithAmbiguousOrMissingMaterial_ReturnsFailedResult()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var db = _factory.CreateWithOptions(_scdbPath, "unused", new DatabaseOptions
        {
            StorageMode = StorageMode.SingleFile,
            EnableEncryption = true,
            EncryptionKey = key,
            CreateImmediately = true,
        });
        try
        {
            var ambiguous = await db.RotateEncryptionKeyAsync(
                newKey: RandomNumberGenerator.GetBytes(32), newPassword: "pw");
            Assert.False(ambiguous.Success);
            Assert.Contains("exactly one", ambiguous.ErrorMessage, StringComparison.OrdinalIgnoreCase);

            var neither = await db.RotateEncryptionKeyAsync();
            Assert.False(neither.Success);

            var badLength = await db.RotateEncryptionKeyAsync(newKey: new byte[16]);
            Assert.False(badLength.Success);
            Assert.Contains("32 bytes", badLength.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }

    // ── Tamper detection: a flipped registry byte must fail loudly on reopen ───────

    [Fact]
    public void TamperedRegistryRegion_FailsOnReopen()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var options = new DatabaseOptions
        {
            StorageMode = StorageMode.SingleFile,
            EnableEncryption = true,
            EncryptionKey = key,
            CreateImmediately = true,
        };

        var db = _factory.CreateWithOptions(_scdbPath, "unused", options);
        try
        {
            db.ExecuteSQL("CREATE TABLE T (Id INT)");
            db.ExecuteSQL("INSERT INTO T VALUES (1)");
            db.ForceSave();
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }

        // Flip one byte inside the block-registry chunk (dynamic-metadata layout, issue #345).
        // Read the registry root offset from the header (0x20) instead of the legacy fixed 4096.
        ulong registryRootOffset;
        using (var probe = new FileStream(_scdbPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            probe.Position = 0x20;
            Span<byte> buf = stackalloc byte[8];
            probe.ReadExactly(buf);
            registryRootOffset = System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(buf);
        }

        using (var fs = new FileStream(_scdbPath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
        {
            fs.Position = (long)registryRootOffset + 128;
            var b = (byte)fs.ReadByte();
            fs.Position = (long)registryRootOffset + 128;
            fs.WriteByte((byte)(b ^ 0xFF));
            fs.Flush(true);
        }

        // Reopening must throw (GCM authentication failure on the tampered registry region).
        Assert.ThrowsAny<CryptographicException>(() =>
        {
            var tampered = _factory.CreateWithOptions(_scdbPath, "unused", options);
            (tampered as IDisposable)?.Dispose();
        });
    }
}
