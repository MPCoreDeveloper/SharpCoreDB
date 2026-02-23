// <copyright file="TriggerCrossTableDmlTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace SharpCoreDB.Tests;

/// <summary>
/// Tests trigger execution when trigger body writes to a different table.
/// Validates cross-table DML support for sync change tracking.
/// </summary>
public class TriggerCrossTableDmlTests : IDisposable
{
    private readonly string testDbPath;
    private readonly Database db;

    public TriggerCrossTableDmlTests()
    {
        testDbPath = Path.Combine(Path.GetTempPath(), $"trigger_cross_{Guid.NewGuid()}");
        Directory.CreateDirectory(testDbPath);

        var config = DatabaseConfig.Benchmark;
        db = new Database(
            new ServiceCollection().AddSharpCoreDB().BuildServiceProvider(),
            testDbPath,
            "test_password",
            isReadOnly: false,
            config: config);

        db.ExecuteSQL("CREATE TABLE source_events (id INTEGER PRIMARY KEY, payload TEXT)");
        db.ExecuteSQL("CREATE TABLE audit_events (id INTEGER PRIMARY KEY, payload TEXT)");
        db.ExecuteSQL(
            "CREATE TRIGGER trg_source_insert AFTER INSERT ON source_events BEGIN " +
            "INSERT INTO audit_events VALUES (NEW.id, 'NEW.payload'); END");
    }

    public void Dispose()
    {
        try { db.Dispose(); } catch { }
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Thread.Sleep(250);

        if (Directory.Exists(testDbPath))
        {
            try
            {
                for (int i = 0; i < 5; i++)
                {
                    try { Directory.Delete(testDbPath, recursive: true); break; }
                    catch when (i < 4) { Thread.Sleep(150 * (i + 1)); }
                }
            }
            catch { }
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void InsertIntoSource_WhenTriggerDefined_ShouldWriteToAuditTable()
    {
        // Arrange
        db.ExecuteSQL("INSERT INTO source_events VALUES (1, 'payload')");

        // Act
        var rows = db.ExecuteQuery("SELECT * FROM audit_events");

        // Assert
        Assert.Single(rows);
    }
}
