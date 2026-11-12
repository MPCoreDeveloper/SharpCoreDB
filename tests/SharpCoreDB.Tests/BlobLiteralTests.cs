// <copyright file="BlobLiteralTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Tests;

using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

/// <summary>
/// A blob value reaches a statement in three textual forms: SharpCoreDB's own parameter renderer writes base64,
/// SQLite's hex blob literal is <c>X'0102'</c>, and SQL generators that render binary as SQL — linq2db's SQLite
/// dialect among them — write <c>0x0102</c>. Only base64 used to be accepted, so a <c>byte[]</c> inserted
/// through generated SQL failed with "The input is not a valid Base-64 string".
/// </summary>
public sealed class BlobLiteralTests : IDisposable
{
    private readonly DatabaseFactory _factory;
    private readonly string _dirPath;
    private int _dbCounter;

    public BlobLiteralTests()
    {
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        _factory = services.BuildServiceProvider().GetRequiredService<DatabaseFactory>();
        _dirPath = Path.Combine(Path.GetTempPath(), $"SCDB_BlobLiteral_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dirPath);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dirPath)) Directory.Delete(_dirPath, true); } catch { }
    }

    private IDatabase Open()
    {
        var dir = Path.Combine(_dirPath, $"db{_dbCounter++}");
        Directory.CreateDirectory(dir);
        var db = _factory.Create(dir, "pw", isReadOnly: false, config: new DatabaseConfig { NoEncryptMode = true });
        db.ExecuteSQL("CREATE TABLE blobs (id INTEGER PRIMARY KEY, data BLOB)");
        return db;
    }

    [Theory]
    [InlineData("0x0102030405FF")]
    [InlineData("X'0102030405FF'")]
    public void Insert_HexBlobLiteral_StoresBytes(string literal)
    {
        var db = Open();

        db.ExecuteSQL($"INSERT INTO blobs (id, data) VALUES (1, {literal})");

        var rows = db.ExecuteQuery("SELECT data FROM blobs WHERE id = 1");
        Assert.Single(rows);
        Assert.Equal(new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0xFF }, Assert.IsType<byte[]>(rows[0]["data"]));
    }

    [Fact]
    public void Insert_Base64BlobLiteral_StillStoresBytes()
    {
        var db = Open();
        var base64 = Convert.ToBase64String([0x0A, 0x0B, 0x0C]);

        db.ExecuteSQL($"INSERT INTO blobs (id, data) VALUES (2, '{base64}')");

        var rows = db.ExecuteQuery("SELECT data FROM blobs WHERE id = 2");
        Assert.Equal(new byte[] { 0x0A, 0x0B, 0x0C }, Assert.IsType<byte[]>(rows[0]["data"]));
    }
}
