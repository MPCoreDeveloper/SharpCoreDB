// <copyright file="DefaultEngineSelectionTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Tests;

using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.DataStructures;
using SharpCoreDB.Interfaces;
using SharpCoreDB.Storage.Hybrid;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

/// <summary>
/// Production-hardening regression: a database created with DEFAULT configuration must stay on the
/// fast, hardened path — Columnar (AppendOnly) tables with the fixed-width record layout for PK
/// tables and the single-pass contiguous DELETE fast path — and must never silently land on the
/// not-yet-OLTP-ready PageBased engine through the Auto/WorkloadHint selection.
/// </summary>
public sealed class DefaultEngineSelectionTests : IDisposable
{
    private readonly DatabaseFactory _factory;
    private readonly string _dirPath;

    public DefaultEngineSelectionTests()
    {
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        _factory = services.BuildServiceProvider().GetRequiredService<DatabaseFactory>();
        _dirPath = Path.Combine(Path.GetTempPath(), $"SCDB_DefaultEng_{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dirPath)) Directory.Delete(_dirPath, true); } catch { }
    }

    [Fact]
    public void DefaultConfig_AutoSelection_PrefersAppendOnlyOverPageBased()
    {
        // Regression: WorkloadHint.General (the default) and unknown hints used to resolve Auto to
        // PageBased, which is not OLTP-ready (measured UPDATE ~26K ops/s vs ~245K on the
        // fixed-width Columnar path). Explicit PageBased opt-in must remain possible.
        var config = new DatabaseConfig();

        Assert.Equal(StorageEngineType.Auto, config.StorageEngineType);
        Assert.Equal(StorageEngineType.AppendOnly, config.GetOptimalStorageEngine());

        var explicitPageBased = new DatabaseConfig { StorageEngineType = StorageEngineType.PageBased };
        Assert.Equal(StorageEngineType.PageBased, explicitPageBased.GetOptimalStorageEngine());
    }

    [Fact]
    public void DefaultDatabase_NewPkTable_UsesColumnarFixedWidthAndContiguousDelete()
    {
        var db = _factory.Create(_dirPath, "pw", isReadOnly: false, config: new DatabaseConfig());
        try
        {
            db.ExecuteSQL("CREATE TABLE docs (id INTEGER PRIMARY KEY, name TEXT, score REAL)");
            Assert.True(db.TryGetTable("docs", out var t));
            var table = Assert.IsType<Table>(t);

            Assert.Equal(StorageMode.Columnar, table.StorageMode);
            Assert.True(table.IsFixedWidthRecords, "default new PK table must use the fixed-width record layout");
            Assert.Equal(StorageEngineType.AppendOnly, table.GetStorageEngineType());

            var stmts = new List<string>(1000);
            for (int i = 1; i <= 1000; i++)
            {
                stmts.Add($"INSERT INTO docs VALUES ({i}, 'user{i}', {i * 0.5})");
            }

            db.ExecuteBatchSQL(stmts);
            db.Flush();

            var dels = new List<string>(500);
            for (int i = 1; i <= 500; i++)
            {
                dels.Add($"DELETE FROM docs WHERE id = {i}");
            }

            db.ExecuteBatchSQL(dels);
            db.Flush();

            Assert.Equal(1, table.BulkContiguousDeleteBatches); // fast path engaged on default config
            Assert.Equal(500, db.ExecuteQuery("SELECT id FROM docs").Count);

            // No PageBased .pages artifact may appear for a default Columnar table.
            Assert.False(Directory.EnumerateFiles(_dirPath, "*.pages").Any());
        }
        finally { (db as IDisposable)?.Dispose(); }
    }
}
