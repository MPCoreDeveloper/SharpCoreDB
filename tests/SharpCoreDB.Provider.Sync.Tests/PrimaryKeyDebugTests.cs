using Xunit;
using SharpCoreDB;
using SharpCoreDB.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace SharpCoreDB.Provider.Sync.Tests;

/// <summary>
/// Debug test to diagnose primary key parsing issue
/// </summary>
public sealed class PrimaryKeyDebugTests : IDisposable
{
    private readonly string _dbPath;
    private readonly IServiceProvider _serviceProvider;
    private readonly IDatabase _db;

    public PrimaryKeyDebugTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"test_pk_{Guid.NewGuid():N}.scdb");

        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        _serviceProvider = services.BuildServiceProvider();

        var factory = _serviceProvider.GetRequiredService<DatabaseFactory>();
        _db = factory.Create(_dbPath, "test", isReadOnly: false);
    }

    public void Dispose()
    {
        if (_db is IDisposable disposable)
        {
            disposable.Dispose();
        }

        if (Directory.Exists(_dbPath))
        {
            Directory.Delete(_dbPath, true);
        }
    }

    [Fact]
    public void CreateTable_WithInlinePrimaryKey_ShouldSetPrimaryKeyIndex()
    {
        // Arrange & Act
        var sql = "CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT NOT NULL)";
        Console.WriteLine($"Executing: {sql}");
        _db.ExecuteSQL(sql);

        // Assert
        var success = _db.TryGetTable("users", out var table);
        Assert.True(success, "Table 'users' should exist");
        Assert.NotNull(table);
        
        Console.WriteLine($"PrimaryKeyIndex: {table.PrimaryKeyIndex}");
        Console.WriteLine($"Columns: {string.Join(", ", table.Columns)}");
        
        // This is the critical assertion that's currently failing
        Assert.True(table.PrimaryKeyIndex >= 0, $"Primary key index should be >= 0, but was {table.PrimaryKeyIndex}");
        Assert.Equal(0, table.PrimaryKeyIndex); // 'id' is the first column (index 0)
        Assert.Equal(2, table.Columns.Count);
        Assert.Equal("id", table.Columns[0]);
        Assert.Equal("name", table.Columns[1]);
    }
}
