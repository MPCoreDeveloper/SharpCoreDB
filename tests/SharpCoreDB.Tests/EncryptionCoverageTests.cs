// <copyright file="EncryptionCoverageTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Tests;

using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

/// <summary>
/// Plan §3-1c deliverable 4: a permanent guard for what "encrypted" actually means per configuration.
/// The audit found that the DEFAULT configuration stores table data (records and the overflow arena) as
/// PLAINTEXT while still encrypting metadata and transaction files — so this test pins the posture, and
/// doubles as the tripwire: flipping <c>EnableAtRestRecordEncryption</c> to the default changes the
/// <see cref="Kind.Default"/> expectation, and that must only happen together with the work package
/// listed in plan §3-1c deliverable 2.
/// </summary>
public sealed class EncryptionCoverageTests : IDisposable
{
    /// <summary>Distinctive row value; if it appears in a table data file, that file is plaintext.</summary>
    private const string Marker = "PLAINTEXT_MARKER_9F3A";

    public enum Kind
    {
        /// <summary>Default config: NoEncryptMode=false, at-rest records off.</summary>
        Default,

        /// <summary>The documented raw-speed opt-out.</summary>
        Raw,

        /// <summary>Per-record encryption opted in.</summary>
        AtRest,
    }

    private readonly DatabaseFactory _factory;
    private readonly string _dirPath;

    public EncryptionCoverageTests()
    {
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        _factory = services.BuildServiceProvider().GetRequiredService<DatabaseFactory>();
        _dirPath = Path.Combine(Path.GetTempPath(), $"SCDB_Coverage_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dirPath);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dirPath)) Directory.Delete(_dirPath, true); } catch { }
    }

    private static DatabaseConfig ConfigFor(Kind kind) => kind switch
    {
        Kind.Raw => new DatabaseConfig { NoEncryptMode = true },
        Kind.AtRest => new DatabaseConfig { NoEncryptMode = false, EnableAtRestRecordEncryption = true },
        _ => new DatabaseConfig(),
    };

    private IDatabase Open(string dir, Kind kind) =>
        _factory.Create(dir, "pw", isReadOnly: false, config: ConfigFor(kind));

    private static bool ContainsMarker(byte[] fileBytes) =>
        fileBytes.AsSpan().IndexOf(Encoding.UTF8.GetBytes(Marker)) >= 0;

    [Theory]
    [InlineData(Kind.Default, true)]
    [InlineData(Kind.Raw, true)]
    [InlineData(Kind.AtRest, false)]
    public void TableDataFiles_MarkerPresence_MatchesTheDocumentedPosture(Kind kind, bool expectPlaintext)
    {
        var dir = Path.Combine(_dirPath, kind.ToString());
        Directory.CreateDirectory(dir);

        var db = Open(dir, kind);
        db.ExecuteSQL("CREATE TABLE docs (id INTEGER PRIMARY KEY, name TEXT, score REAL)");
        for (int i = 1; i <= 5; i++)
        {
            db.ExecuteSQL($"INSERT INTO docs VALUES ({i}, '{Marker}_{i}', {i * 0.5})");
        }

        db.Flush();

        // Round-trip sanity: whatever the posture, the rows must be readable through SQL.
        var count = db.ExecuteQuery("SELECT COUNT(*) AS n FROM docs");
        Assert.Equal(5L, Convert.ToInt64(count[0].Values.First()));
        (db as IDisposable)?.Dispose();

        // Only table payload files are scanned on purpose: journal/WAL files legitimately contain the
        // INSERT statement text (and therefore the marker) in every configuration.
        var payloadFiles = Directory.GetFiles(dir, "*.dat", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(dir, "*.ovf", SearchOption.AllDirectories))
            .ToArray();
        Assert.NotEmpty(payloadFiles);

        var withMarker = payloadFiles.Where(f => ContainsMarker(File.ReadAllBytes(f))).ToArray();
        if (expectPlaintext)
        {
            Assert.NotEmpty(withMarker);
        }
        else
        {
            Assert.Empty(withMarker);
        }

        // And a reopen still serves the row (ciphertext must round-trip, plaintext must too).
        db = Open(dir, kind);
        try
        {
            Assert.Single(db.ExecuteQuery($"SELECT id FROM docs WHERE name = '{Marker}_3'"));
        }
        finally { (db as IDisposable)?.Dispose(); }
    }
}
