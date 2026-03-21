// <copyright file="AutoRowIdTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Tests;

using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.Constants;
using SharpCoreDB.DataStructures;
using SharpCoreDB.Interfaces;
using Xunit;

/// <summary>
/// Tests for the auto-generated ULID _rowid feature.
/// Verifies that tables without an explicit PRIMARY KEY automatically receive
/// a hidden _rowid column (ULID type) that is invisible in SELECT * but
/// queryable via explicit column reference.
/// </summary>
public sealed class AutoRowIdTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly IServiceProvider _serviceProvider;
    private readonly DatabaseFactory _factory;

    public AutoRowIdTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"SharpCoreDB_AutoRowId_{Guid.NewGuid()}");
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        _serviceProvider = services.BuildServiceProvider();
        _factory = _serviceProvider.GetRequiredService<DatabaseFactory>();
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDbPath))
        {
            Directory.Delete(_testDbPath, true);
        }
    }

    [Fact]
    public void CreateTable_WithoutPrimaryKey_ShouldInjectRowId()
    {
        // Arrange
        var db = _factory.Create(_testDbPath, "password");

        // Act
        db.ExecuteSQL("CREATE TABLE logs (message TEXT, level INTEGER)");

        // Assert
        Assert.True(db.TryGetTable("logs", out var table));
        var concreteTable = table as Table;
        Assert.NotNull(concreteTable);
        Assert.True(concreteTable.HasInternalRowId);
        Assert.Equal(0, concreteTable.PrimaryKeyIndex);
        Assert.Equal(PersistenceConstants.InternalRowIdColumnName, concreteTable.Columns[0]);
        Assert.Equal(DataType.Ulid, concreteTable.ColumnTypes[0]);
        Assert.True(concreteTable.IsAuto[0]);
    }

    [Fact]
    public void CreateTable_WithExplicitPrimaryKey_ShouldNotInjectRowId()
    {
        // Arrange
        var db = _factory.Create(_testDbPath, "password");

        // Act
        db.ExecuteSQL("CREATE TABLE users (id INTEGER PRIMARY KEY AUTO, name TEXT)");

        // Assert
        Assert.True(db.TryGetTable("users", out var table));
        var concreteTable = table as Table;
        Assert.NotNull(concreteTable);
        Assert.False(concreteTable.HasInternalRowId);
        Assert.Equal(0, concreteTable.PrimaryKeyIndex);
        Assert.Equal("id", concreteTable.Columns[0]);
    }

    [Fact]
    public void Insert_IntoTableWithRowId_ShouldAutoGenerateUlid()
    {
        // Arrange
        var db = _factory.Create(_testDbPath, "password");
        db.ExecuteSQL("CREATE TABLE events (name TEXT, data TEXT)");

        // Act
        db.ExecuteSQL("INSERT INTO events VALUES ('click', 'button1')");
        db.ExecuteSQL("INSERT INTO events VALUES ('hover', 'menu')");

        // Assert: SELECT * should NOT include _rowid
        var results = db.ExecuteQuery("SELECT * FROM events");
        Assert.Equal(2, results.Count);
        Assert.False(results[0].ContainsKey(PersistenceConstants.InternalRowIdColumnName));
        Assert.Equal("click", results[0]["name"]?.ToString());
        Assert.Equal("button1", results[0]["data"]?.ToString());
    }

    [Fact]
    public void Insert_WithExplicitColumns_ShouldAutoGenerateRowId()
    {
        // Arrange
        var db = _factory.Create(_testDbPath, "password");
        db.ExecuteSQL("CREATE TABLE items (name TEXT, price REAL)");

        // Act
        db.ExecuteSQL("INSERT INTO items (name, price) VALUES ('Widget', 9.99)");

        // Assert
        var results = db.ExecuteQuery("SELECT * FROM items");
        Assert.Single(results);
        Assert.Equal("Widget", results[0]["name"]?.ToString());
        Assert.False(results[0].ContainsKey(PersistenceConstants.InternalRowIdColumnName));
    }

    [Fact]
    public void SelectStar_ShouldHideRowId()
    {
        // Arrange
        var db = _factory.Create(_testDbPath, "password");
        db.ExecuteSQL("CREATE TABLE notes (title TEXT, body TEXT)");
        db.ExecuteSQL("INSERT INTO notes VALUES ('Hello', 'World')");

        // Act
        var results = db.ExecuteQuery("SELECT * FROM notes");

        // Assert: Only user-visible columns
        Assert.Single(results);
        Assert.Equal(2, results[0].Count);
        Assert.True(results[0].ContainsKey("title"));
        Assert.True(results[0].ContainsKey("body"));
        Assert.False(results[0].ContainsKey(PersistenceConstants.InternalRowIdColumnName));
    }

    [Fact]
    public void SelectExplicitRowId_ShouldBeVisible()
    {
        // Arrange
        var db = _factory.Create(_testDbPath, "password");
        db.ExecuteSQL("CREATE TABLE tags (label TEXT)");
        db.ExecuteSQL("INSERT INTO tags VALUES ('important')");

        // Act
        var results = db.ExecuteQuery("SELECT _rowid, label FROM tags");

        // Assert: _rowid should be present and be a valid ULID
        Assert.Single(results);
        Assert.True(results[0].ContainsKey(PersistenceConstants.InternalRowIdColumnName));
        var rowId = results[0][PersistenceConstants.InternalRowIdColumnName]?.ToString();
        Assert.NotNull(rowId);
        Assert.Equal(26, rowId.Length); // ULID is 26 characters
    }

    [Fact]
    public void Delete_ViaRowIdPrimaryKey_ShouldWork()
    {
        // Arrange: Create table without explicit PK — gets internal _rowid
        var db = _factory.Create(_testDbPath, "password");
        db.ExecuteSQL("CREATE TABLE temp (value TEXT)");
        db.ExecuteSQL("INSERT INTO temp VALUES ('keep')");
        db.ExecuteSQL("INSERT INTO temp VALUES ('delete_me')");

        // Get the _rowid of the row we want to delete
        var rows = db.ExecuteQuery("SELECT _rowid, value FROM temp");
        Assert.Equal(2, rows.Count);

        var targetRowId = rows
            .First(r => r["value"]?.ToString() == "delete_me")
            [PersistenceConstants.InternalRowIdColumnName]?.ToString();
        Assert.NotNull(targetRowId);

        // Act: Delete by _rowid (the auto-generated PK)
        db.ExecuteSQL($"DELETE FROM temp WHERE _rowid = '{targetRowId}'");

        // Assert
        var after = db.ExecuteQuery("SELECT * FROM temp");
        Assert.Single(after);
        Assert.Equal("keep", after[0]["value"]?.ToString());
    }

    [Fact]
    public void MultiRowInsert_ShouldGenerateUniqueRowIds()
    {
        // Arrange
        var db = _factory.Create(_testDbPath, "password");
        db.ExecuteSQL("CREATE TABLE multi (name TEXT)");

        // Act: Insert multiple rows
        db.ExecuteSQL("INSERT INTO multi VALUES ('a'), ('b'), ('c')");

        // Assert: All rows should have unique _rowid values
        var results = db.ExecuteQuery("SELECT _rowid, name FROM multi");
        Assert.Equal(3, results.Count);

        var rowIds = results
            .Select(r => r[PersistenceConstants.InternalRowIdColumnName]?.ToString())
            .ToList();

        // All should be unique
        Assert.Equal(3, rowIds.Distinct().Count());

        // All should be valid ULIDs (26 chars)
        Assert.All(rowIds, id => Assert.Equal(26, id?.Length));
    }

    [Fact]
    public void ColumnMetadata_ShouldMarkRowIdAsHidden()
    {
        // Arrange
        var db = _factory.Create(_testDbPath, "password");
        db.ExecuteSQL("CREATE TABLE meta_test (name TEXT, age INTEGER)");

        // Act: Default GetColumns should exclude _rowid (SQLite PRAGMA table_info pattern)
        var meta = db as IMetadataProvider;
        Assert.NotNull(meta);
        var visibleColumns = meta!.GetColumns("meta_test");
        Assert.Equal(2, visibleColumns.Count);
        Assert.DoesNotContain(visibleColumns, c => c.Name == PersistenceConstants.InternalRowIdColumnName);

        // Act: GetColumnsIncludingHidden should include _rowid with IsHidden flag
        var database = db as Database;
        Assert.NotNull(database);
        var allColumns = database!.GetColumnsIncludingHidden("meta_test");
        Assert.Equal(3, allColumns.Count);

        var rowIdCol = allColumns.FirstOrDefault(c => c.Name == PersistenceConstants.InternalRowIdColumnName);
        Assert.NotNull(rowIdCol);
        Assert.True(rowIdCol.IsHidden);
        Assert.False(rowIdCol.IsNullable);

        // User columns should not be hidden
        var nameCol = allColumns.FirstOrDefault(c => c.Name == "name");
        Assert.NotNull(nameCol);
        Assert.False(nameCol.IsHidden);
    }
}
