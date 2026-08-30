// <copyright file="SingleFileEncryptionTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Tests;

using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.Interfaces;
using SharpCoreDB.Storage;
using System;
using System.IO;
using System.Security.Cryptography;
using Xunit;

/// <summary>
/// Regression tests for GitHub issue #341: SingleFile (.scdb) mode ignored
/// DatabaseOptions.EncryptionKey, writing data in plaintext.
/// </summary>
public sealed class SingleFileEncryptionTests : IDisposable
{
    private readonly DatabaseFactory _factory;
    private readonly string _scdbPath;

    public SingleFileEncryptionTests()
    {
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        _factory = services.BuildServiceProvider().GetRequiredService<DatabaseFactory>();
        _scdbPath = Path.Combine(Path.GetTempPath(), $"SCDB_Enc_{Guid.NewGuid():N}.scdb");
    }

    public void Dispose()
    {
        try { if (File.Exists(_scdbPath)) File.Delete(_scdbPath); } catch { }
    }

    private static DatabaseOptions EncryptedOptions(byte[] key) => new()
    {
        StorageMode = StorageMode.SingleFile,
        EnableEncryption = true,
        EncryptionKey = key,
        CreateImmediately = true,
    };

    [Fact]
    public void EncryptedSingleFile_NoPlaintextOnDisk()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var db = _factory.CreateWithOptions(_scdbPath, "unused", EncryptedOptions(key));
        try
        {
            db.ExecuteSQL("CREATE TABLE Secrets (Id INT, Data TEXT)");
            db.ExecuteSQL("INSERT INTO Secrets VALUES (1, 'classified-payload')");
            db.ForceSave();
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }

        var fileBytes = File.ReadAllBytes(_scdbPath);
        var secretBytes = System.Text.Encoding.UTF8.GetBytes("classified-payload");
        Assert.False(
            fileBytes.AsSpan().IndexOf(secretBytes.AsSpan()) >= 0,
            "Plaintext secret must not appear in the .scdb file.");
    }

    [Fact]
    public void EncryptedSingleFile_CorrectKey_RoundTrips()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var db = _factory.CreateWithOptions(_scdbPath, "unused", EncryptedOptions(key));
        try
        {
            db.ExecuteSQL("CREATE TABLE Secrets (Id INT, Data TEXT)");
            db.ExecuteSQL("INSERT INTO Secrets VALUES (1, 'classified-payload')");
            db.ForceSave();
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }

        var reopened = _factory.CreateWithOptions(_scdbPath, "unused", EncryptedOptions(key));
        try
        {
            var rows = reopened.ExecuteQuery("SELECT * FROM Secrets");
            Assert.Single(rows);
            Assert.Equal("classified-payload", rows[0]["Data"]);
        }
        finally
        {
            (reopened as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void EncryptedSingleFile_WrongKey_Throws()
    {
        var correctKey = RandomNumberGenerator.GetBytes(32);
        var db = _factory.CreateWithOptions(_scdbPath, "unused", EncryptedOptions(correctKey));
        try
        {
            db.ExecuteSQL("CREATE TABLE Secrets (Id INT, Data TEXT)");
            db.ExecuteSQL("INSERT INTO Secrets VALUES (1, 'classified-payload')");
            db.ForceSave();
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }

        var wrongKey = RandomNumberGenerator.GetBytes(32);
        // With full-at-rest encryption the block registry is ciphertext, so opening with the
        // wrong key fails GCM authentication during metadata load — construction must throw.
        Assert.ThrowsAny<Exception>(() =>
        {
            var dbWrong = _factory.CreateWithOptions(_scdbPath, "unused", EncryptedOptions(wrongKey));
            (dbWrong as IDisposable)?.Dispose();
        });
    }

    [Fact]
    public void EncryptedSingleFile_OpenWithoutEncryption_Throws()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var db = _factory.CreateWithOptions(_scdbPath, "unused", EncryptedOptions(key));
        try
        {
            db.ExecuteSQL("CREATE TABLE Secrets (Id INT, Data TEXT)");
            db.ExecuteSQL("INSERT INTO Secrets VALUES (1, 'classified-payload')");
            db.ForceSave();
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }

        var plainOptions = new DatabaseOptions
        {
            StorageMode = StorageMode.SingleFile,
            EnableEncryption = false,
            CreateImmediately = false,
        };

        Assert.Throws<InvalidOperationException>(() =>
        {
            var plain = _factory.CreateWithOptions(_scdbPath, "unused", plainOptions);
            (plain as IDisposable)?.Dispose();
        });
    }

    private DatabaseOptions PasswordOptions(string password, bool createImmediately = true) => new()
    {
        StorageMode = StorageMode.SingleFile,
        EnableEncryption = true,
        EncryptionPassword = password,
        CreateImmediately = createImmediately,
    };

    private void CreateSecretDb(DatabaseOptions options)
    {
        var db = _factory.CreateWithOptions(_scdbPath, "unused", options);
        try
        {
            db.ExecuteSQL("CREATE TABLE Secrets (Id INT, Data TEXT)");
            db.ExecuteSQL("INSERT INTO Secrets VALUES (1, 'classified-payload')");
            db.ForceSave();
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public void EncryptedSingleFile_NoPlaintextMetadataOnDisk()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        CreateSecretDb(EncryptedOptions(key));

        var fileBytes = File.ReadAllBytes(_scdbPath);

        // Full-at-rest encryption must hide the payload AND the metadata: block names,
        // table names and the plaintext registry "BREG" magic must not appear on disk.
        foreach (var needle in new[] { "classified-payload", "Secrets", "sys:abledir", "BREG" })
        {
            var needleBytes = System.Text.Encoding.UTF8.GetBytes(needle);
            Assert.False(
                fileBytes.AsSpan().IndexOf(needleBytes.AsSpan()) >= 0,
                $"'{needle}' must not appear in plaintext in the .scdb file.");
        }
    }

    [Fact]
    public void PasswordEncryptedSingleFile_RoundTripsAndRejectsWrongPassword()
    {
        const string password = "correct-horse-battery";
        CreateSecretDb(PasswordOptions(password));

        // Reopen with the correct password — data is intact.
        var reopened = _factory.CreateWithOptions(_scdbPath, "unused", PasswordOptions(password));
        try
        {
            var rows = reopened.ExecuteQuery("SELECT * FROM Secrets");
            Assert.Single(rows);
            Assert.Equal("classified-payload", rows[0]["Data"]);
        }
        finally
        {
            (reopened as IDisposable)?.Dispose();
        }

        // Wrong password: the wrapped DEK cannot be unwrapped — construction must throw.
        Assert.ThrowsAny<Exception>(() =>
        {
            var wrong = _factory.CreateWithOptions(_scdbPath, "unused", PasswordOptions("wrong-password"));
            (wrong as IDisposable)?.Dispose();
        });
    }

    [Fact]
    public async Task PasswordEncryptedSingleFile_ChangePassword_RewrapsAndKeepsData()
    {
        const string oldPassword = "old-password";
        const string newPassword = "new-password";
        CreateSecretDb(PasswordOptions(oldPassword));

        var db = _factory.CreateWithOptions(_scdbPath, "unused", PasswordOptions(oldPassword));
        try
        {
            var result = await db.ChangeEncryptionPasswordAsync(newPassword);
            Assert.True(result.Success, result.ErrorMessage);
            Assert.Equal(EncryptionRotationOperation.PasswordChanged, result.Operation);
            Assert.Equal(2, result.KeyId); // key id increments on every rotation
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }

        // New password opens the database and the data survives.
        var reopened = _factory.CreateWithOptions(_scdbPath, "unused", PasswordOptions(newPassword));
        try
        {
            var rows = reopened.ExecuteQuery("SELECT * FROM Secrets");
            Assert.Single(rows);
            Assert.Equal("classified-payload", rows[0]["Data"]);
        }
        finally
        {
            (reopened as IDisposable)?.Dispose();
        }

        // The old password must no longer work.
        Assert.ThrowsAny<Exception>(() =>
        {
            var stale = _factory.CreateWithOptions(_scdbPath, "unused", PasswordOptions(oldPassword));
            (stale as IDisposable)?.Dispose();
        });
    }

    [Fact]
    public async Task RawKeyEncryptedSingleFile_RotateKey_RekeysAndKeepsData()
    {
        var originalKey = RandomNumberGenerator.GetBytes(32);
        CreateSecretDb(EncryptedOptions(originalKey));

        var db = _factory.CreateWithOptions(_scdbPath, "unused", EncryptedOptions(originalKey));
        var newKey = RandomNumberGenerator.GetBytes(32);
        try
        {
            var result = await db.RotateEncryptionKeyAsync(newKey: newKey);
            Assert.True(result.Success, result.ErrorMessage);
            Assert.Equal(EncryptionRotationOperation.KeyRotated, result.Operation);
            Assert.True(result.BlocksReEncrypted > 0, "The re-key must re-encrypt blocks.");
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }

        // New key opens the database and the data survives.
        var reopened = _factory.CreateWithOptions(_scdbPath, "unused", EncryptedOptions(newKey));
        try
        {
            var rows = reopened.ExecuteQuery("SELECT * FROM Secrets");
            Assert.Single(rows);
            Assert.Equal("classified-payload", rows[0]["Data"]);
        }
        finally
        {
            (reopened as IDisposable)?.Dispose();
        }

        // The old key must no longer open the database.
        Assert.ThrowsAny<Exception>(() =>
        {
            var stale = _factory.CreateWithOptions(_scdbPath, "unused", EncryptedOptions(originalKey));
            (stale as IDisposable)?.Dispose();
        });
    }

    [Fact]
    public async Task EncryptedSingleFile_VacuumFull_SurvivesOnEncryptedDatabase()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var db = _factory.CreateWithOptions(_scdbPath, "unused", EncryptedOptions(key));
        try
        {
            db.ExecuteSQL("CREATE TABLE BigTable (Id INT, Data TEXT)");
            for (var i = 0; i < 100; i++)
            {
                db.ExecuteSQL($"INSERT INTO BigTable VALUES ({i}, 'row-{i}-classified')");
            }
            db.ForceSave();

            var result = await db.VacuumAsync(VacuumMode.Full);
            Assert.True(result.Success, result.ErrorMessage);

            // Data must survive the full-vacuum file swap on an encrypted database.
            // The query engine canonicalizes COUNT(*) to the "cnt" column.
            var rows = db.ExecuteQuery("SELECT COUNT(*) AS c FROM BigTable");
            Assert.Single(rows);
            Assert.Equal(100L, Convert.ToInt64(rows[0]["cnt"]));
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }

        // And the file must reopen with the same key.
        var reopened = _factory.CreateWithOptions(_scdbPath, "unused", EncryptedOptions(key));
        try
        {
            var rows = reopened.ExecuteQuery("SELECT COUNT(*) AS c FROM BigTable");
            Assert.Single(rows);
            Assert.Equal(100L, Convert.ToInt64(rows[0]["cnt"]));
        }
        finally
        {
            (reopened as IDisposable)?.Dispose();
        }
    }
}
