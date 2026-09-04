// <copyright file="HashIndexDuplicateKeyBatchDeleteTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Tests;

using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.Interfaces;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Xunit;

/// <summary>
/// Regression coverage for duplicate-key hash-index removal: batch-DELETEs that hit a non-unique
/// indexed column with large position groups exercise HashIndex.RemoveBatchKeys's deferred
/// duplicate-key compaction (previously one O(list) List.Remove shift per duplicate).
/// </summary>
public sealed class HashIndexDuplicateKeyBatchDeleteTests : IDisposable
{
    private readonly DatabaseFactory _factory;
    private readonly string _dirPath;

    public HashIndexDuplicateKeyBatchDeleteTests()
    {
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        _factory = services.BuildServiceProvider().GetRequiredService<DatabaseFactory>();
        _dirPath = Path.Combine(Path.GetTempPath(), $"SCDB_HashDup_{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dirPath)) Directory.Delete(_dirPath, true); } catch { }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BatchDelete_DuplicatedNameGroups_RemovesOnlyThoseKeys_AcrossReopen(bool useUnsafeEqualityIndex)
    {
        IDatabase? db = _factory.Create(_dirPath, "pw", isReadOnly: false,
            config: new DatabaseConfig
            {
                NoEncryptMode = true,
                AutoFixedWidthRecords = false,
                EnableUnsafeEqualityIndex = useUnsafeEqualityIndex,
            });
        try
        {
            db.ExecuteSQL("CREATE TABLE docs (id INTEGER PRIMARY KEY, name TEXT, score REAL)");
            db.ExecuteSQL("CREATE INDEX idx_docs_name ON docs(name)");

            // 2000 rows, 200 rows per duplicated name group (dup0..dup9).
            var stmts = new List<string>(2000);
            for (int i = 1; i <= 2000; i++)
            {
                stmts.Add(string.Format(CultureInfo.InvariantCulture,
                    "INSERT INTO docs VALUES ({0}, 'dup{1}', {2})", i, i % 10, i * 0.5));
            }

            db.ExecuteBatchSQL(stmts);
            db.Flush();

            Assert.Equal(2000, db.ExecuteQuery("SELECT id FROM docs").Count);

            // Delete three full duplicate groups in one batch: dup1, dup5, dup9 (600 rows).
            db.ExecuteBatchSQL(
            [
                "DELETE FROM docs WHERE name = 'dup1'",
                "DELETE FROM docs WHERE name = 'dup5'",
                "DELETE FROM docs WHERE name = 'dup9'",
            ]);
            db.Flush();

            Assert.Equal(1400, db.ExecuteQuery("SELECT id FROM docs").Count);
            Assert.Empty(db.ExecuteQuery("SELECT id FROM docs WHERE name = 'dup1'"));
            Assert.Empty(db.ExecuteQuery("SELECT id FROM docs WHERE name = 'dup5'"));
            Assert.Empty(db.ExecuteQuery("SELECT id FROM docs WHERE name = 'dup9'"));
            Assert.Equal(200, db.ExecuteQuery("SELECT id FROM docs WHERE name = 'dup2'").Count);
            Assert.Equal(200, db.ExecuteQuery("SELECT id FROM docs WHERE name = 'dup0'").Count);

            // Partial group delete: remove half of dup2 by id, keeping the rest reachable.
            var partial = new List<string>(100);
            for (int i = 2; i <= 2000; i += 20)
            {
                partial.Add($"DELETE FROM docs WHERE id = {i}");
            }

            db.ExecuteBatchSQL(partial);
            db.Flush();
            Assert.Equal(100, db.ExecuteQuery("SELECT id FROM docs WHERE name = 'dup2'").Count);
        }
        finally { (db as IDisposable)?.Dispose(); }

        // Reopen: tombstoned rows stay gone, live duplicated-key groups stay fully reachable.
        db = _factory.Create(_dirPath, "pw", isReadOnly: false,
            config: new DatabaseConfig
            {
                NoEncryptMode = true,
                AutoFixedWidthRecords = false,
                EnableUnsafeEqualityIndex = useUnsafeEqualityIndex,
            });
        try
        {
            Assert.Equal(1300, db.ExecuteQuery("SELECT id FROM docs").Count);
            Assert.Empty(db.ExecuteQuery("SELECT id FROM docs WHERE name = 'dup1'"));
            Assert.Empty(db.ExecuteQuery("SELECT id FROM docs WHERE name = 'dup5'"));
            Assert.Empty(db.ExecuteQuery("SELECT id FROM docs WHERE name = 'dup9'"));
            Assert.Equal(100, db.ExecuteQuery("SELECT id FROM docs WHERE name = 'dup2'").Count);
            Assert.Equal(200, db.ExecuteQuery("SELECT id FROM docs WHERE name = 'dup0'").Count);
        }
        finally { (db as IDisposable)?.Dispose(); }
    }
}
