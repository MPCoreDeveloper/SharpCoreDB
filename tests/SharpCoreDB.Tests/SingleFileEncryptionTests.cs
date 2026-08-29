// <copyright file="SingleFileEncryptionTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Tests;

using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.Interfaces;
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
        var dbWrong = _factory.CreateWithOptions(_scdbPath, "unused", EncryptedOptions(wrongKey));
        try
        {
            // The wrong key cannot decrypt the table directory / data blocks: the schema is
            // empty (or GCM authentication throws), so any data access must fail.
            Assert.ThrowsAny<Exception>(() => dbWrong.ExecuteQuery("SELECT * FROM Secrets"));
        }
        finally
        {
            (dbWrong as IDisposable)?.Dispose();
        }
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
}
