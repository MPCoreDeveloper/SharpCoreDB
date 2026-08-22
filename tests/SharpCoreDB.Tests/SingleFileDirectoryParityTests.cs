// <copyright file="SingleFileDirectoryParityTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Tests;

using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

/// <summary>
/// Regression tests asserting that single-file (.scdb) mode and directory mode produce
/// identical SQL results for the shared SqlParser feature set (aggregates, WHERE operators).
/// </summary>
public sealed class SingleFileDirectoryParityTests : IDisposable
{
    private readonly DatabaseFactory _factory;
    private readonly string _dirPath;
    private readonly string _scdbPath;

    public SingleFileDirectoryParityTests()
    {
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        _factory = services.BuildServiceProvider().GetRequiredService<DatabaseFactory>();
        _dirPath = Path.Combine(Path.GetTempPath(), $"SCDB_Parity_Dir_{Guid.NewGuid():N}");
        _scdbPath = Path.Combine(Path.GetTempPath(), $"SCDB_Parity_Scdb_{Guid.NewGuid():N}.scdb");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dirPath)) Directory.Delete(_dirPath, true); } catch { }
        try { if (File.Exists(_scdbPath)) File.Delete(_scdbPath); } catch { }
    }

    private static IDatabase CreateScdb(DatabaseFactory factory, string path)
        => factory.CreateWithOptions(path, "pw", DatabaseOptions.CreateSingleFileDefault());

    private static IDatabase CreateDir(DatabaseFactory factory, string path)
        => factory.Create(path, "pw");

    private static void SeedData(IDatabase db)
    {
        db.ExecuteSQL("CREATE TABLE items (id INTEGER PRIMARY KEY, name TEXT, price REAL, qty INTEGER)");
        db.ExecuteSQL("INSERT INTO items VALUES (1, 'alpha', 10.5, 2)");
        db.ExecuteSQL("INSERT INTO items VALUES (2, 'beta', 20.0, 3)");
        db.ExecuteSQL("INSERT INTO items VALUES (3, 'gamma', 15.25, 1)");
        db.ExecuteSQL("INSERT INTO items VALUES (4, NULL, 5.0, 0)");
    }

    /// <summary>
    /// Seeds a fresh pair of databases and returns the directory-mode result for <paramref name="sql"/>.
    /// </summary>
    private List<Dictionary<string, object>> SeedRunDir(string sql)
    {
        if (File.Exists(_scdbPath)) File.Delete(_scdbPath);
        if (Directory.Exists(_dirPath)) Directory.Delete(_dirPath, true);
        Directory.CreateDirectory(_dirPath);

        var scdb = CreateScdb(_factory, _scdbPath);
        var dir = CreateDir(_factory, _dirPath);
        try
        {
            SeedData(scdb);
            SeedData(dir);
        }
        finally
        {
            (scdb as IDisposable)?.Dispose();
            (dir as IDisposable)?.Dispose();
        }

        var scdb2 = CreateScdb(_factory, _scdbPath);
        var dir2 = CreateDir(_factory, _dirPath);
        try
        {
            return dir2.ExecuteQuery(sql);
        }
        finally
        {
            (scdb2 as IDisposable)?.Dispose();
            (dir2 as IDisposable)?.Dispose();
        }
    }

    /// <summary>
    /// Seeds a fresh pair of databases and returns the single-file result for <paramref name="sql"/>.
    /// </summary>
    private List<Dictionary<string, object>> SeedRunScdb(string sql)
    {
        if (File.Exists(_scdbPath)) File.Delete(_scdbPath);
        if (Directory.Exists(_dirPath)) Directory.Delete(_dirPath, true);
        Directory.CreateDirectory(_dirPath);

        var scdb = CreateScdb(_factory, _scdbPath);
        var dir = CreateDir(_factory, _dirPath);
        try
        {
            SeedData(scdb);
            SeedData(dir);
        }
        finally
        {
            (scdb as IDisposable)?.Dispose();
            (dir as IDisposable)?.Dispose();
        }

        var scdb2 = CreateScdb(_factory, _scdbPath);
        var dir2 = CreateDir(_factory, _dirPath);
        try
        {
            return scdb2.ExecuteQuery(sql);
        }
        finally
        {
            (scdb2 as IDisposable)?.Dispose();
            (dir2 as IDisposable)?.Dispose();
        }
    }

    [Theory]
    [InlineData("SELECT * FROM items")]
    [InlineData("SELECT COUNT(*) FROM items")]
    [InlineData("SELECT COUNT(name) FROM items")]
    [InlineData("SELECT SUM(price) FROM items")]
    [InlineData("SELECT AVG(price) FROM items")]
    [InlineData("SELECT MIN(price) FROM items")]
    [InlineData("SELECT MAX(price) FROM items")]
    [InlineData("SELECT qty, COUNT(*) FROM items GROUP BY qty")]
    [InlineData("SELECT name FROM items WHERE id = 2")]
    [InlineData("SELECT name FROM items WHERE id IN (1,3)")]
    [InlineData("SELECT name FROM items WHERE name LIKE 'a%'")]
    [InlineData("SELECT name FROM items WHERE name IS NULL")]
    [InlineData("SELECT name FROM items WHERE name IS NOT NULL")]
    [InlineData("SELECT name FROM items WHERE price BETWEEN 10 AND 16")]
    [InlineData("SELECT name FROM items ORDER BY qty, name")]
    [InlineData("SELECT name FROM items ORDER BY id LIMIT 2")]
    [InlineData("SELECT DISTINCT qty FROM items")]
    public void SingleFile_Matches_Directory(string sql)
    {
        var dirRows = SeedRunDir(sql);
        var scdbRows = SeedRunScdb(sql);
        Assert.Equal(
            System.Text.Json.JsonSerializer.Serialize(dirRows),
            System.Text.Json.JsonSerializer.Serialize(scdbRows));
    }
}