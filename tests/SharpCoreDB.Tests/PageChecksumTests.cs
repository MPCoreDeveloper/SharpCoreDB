// <copyright file="PageChecksumTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Tests;

using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

/// <summary>
/// PageBased pages carry a CRC32C checksum in their header (known issue §19).
/// <para>
/// Up to 2.1.4 a page had no checksum and its slot directory sits at its end, so a torn write — the tail of a page not
/// written — read as a page whose slots were empty: the rows vanished without an error and the table went on accepting
/// writes on top of the damage. A damaged page now raises an error naming it. Pages written before carry no checksum
/// (flag byte 0) and are read as they always were.
/// </para>
/// </summary>
public sealed class PageChecksumTests : IDisposable
{
    private const int PageSize = 8192;
    private readonly DatabaseFactory _factory;
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"SCDB_PageCrc_{Guid.NewGuid():N}");

    public PageChecksumTests()
    {
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        _factory = services.BuildServiceProvider().GetRequiredService<DatabaseFactory>();
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup of a temp directory.
        }
    }

    private string CreateTable(int rows)
    {
        var path = Path.Combine(_root, $"db_{Guid.NewGuid():N}");
        var db = _factory.Create(path, "pw");
        db.ExecuteSQL("CREATE TABLE t (id INTEGER PRIMARY KEY, n INTEGER, pad TEXT) STORAGE = PAGE_BASED");
        var pad = new string('z', 300);
        db.ExecuteBatchSQL([.. Enumerable.Range(1, rows).Select(i => $"INSERT INTO t (id, n, pad) VALUES ({i}, {i}, '{pad}')")]);
        (db as IDisposable)?.Dispose();
        return path;
    }

    private static string PageFile(string path) =>
        Directory.GetFiles(path, "table_*.pages").OrderByDescending(f => new FileInfo(f).Length).First();

    [Fact]
    public void Read_OfATornPage_RaisesAChecksumError_InsteadOfLosingRowsSilently()
    {
        var path = CreateTable(40);
        var file = PageFile(path);
        var bytes = File.ReadAllBytes(file);
        Array.Clear(bytes, PageSize / 2, PageSize / 2); // page 1's second half never written
        File.WriteAllBytes(file, bytes);

        var db = _factory.Create(path, "pw");
        var error = Assert.ThrowsAny<Exception>(() => db.ExecuteQuery("SELECT id FROM t"));
        Assert.Contains("checksum", error.ToString(), StringComparison.OrdinalIgnoreCase);
        (db as IDisposable)?.Dispose();
    }

    [Fact]
    public void Read_OfAPageWithOneFlippedDataByte_RaisesAChecksumError()
    {
        var path = CreateTable(10);
        var file = PageFile(path);
        var bytes = File.ReadAllBytes(file);
        bytes[200] ^= 0x40;
        File.WriteAllBytes(file, bytes);

        var db = _factory.Create(path, "pw");
        var error = Assert.ThrowsAny<Exception>(() => db.ExecuteQuery("SELECT id, pad FROM t"));
        Assert.Contains("checksum", error.ToString(), StringComparison.OrdinalIgnoreCase);
        (db as IDisposable)?.Dispose();
    }

    [Fact]
    public void Read_OfPagesWrittenWithoutAChecksum_WorksAsBefore()
    {
        var path = CreateTable(40);
        var file = PageFile(path);
        var bytes = File.ReadAllBytes(file);
        for (int page = 0; page + PageSize <= bytes.Length; page += PageSize)
        {
            Array.Clear(bytes, page + 9, 5); // checksum and flag byte: the layout of a page written before checksums
        }

        File.WriteAllBytes(file, bytes);

        var db = _factory.Create(path, "pw");
        Assert.Equal(40, db.ExecuteQuery("SELECT id FROM t").Count);
        db.ExecuteSQL("UPDATE t SET n = 0 WHERE id = 1");
        (db as IDisposable)?.Dispose();

        var reopened = _factory.Create(path, "pw");
        Assert.Equal(40, reopened.ExecuteQuery("SELECT id FROM t").Count);
        Assert.Equal("0", Convert.ToString(reopened.ExecuteQuery("SELECT n FROM t WHERE id = 1").Single()["n"], CultureInfo.InvariantCulture));
        (reopened as IDisposable)?.Dispose();
    }
}
