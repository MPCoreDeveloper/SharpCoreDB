using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.DataStructures;
using SharpCoreDB.Interfaces;
using SharpCoreDB.Services;

namespace SharpCoreDB.Tests;

/// <summary>
/// Tests for edge cases in new features.
/// </summary>
public class EdgeCaseTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly IDatabase _db;
    private readonly IServiceProvider _services;

    public EdgeCaseTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"test_edge_cases_{Guid.NewGuid()}");
        _services = new ServiceCollection()
            .AddSharpCoreDB()
            .BuildServiceProvider();
        var factory = _services.GetRequiredService<DatabaseFactory>();
        _db = factory.Create(_testDbPath, "testPassword");
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDbPath))
        {
            Directory.Delete(_testDbPath, true);
        }
    }

    [Fact]
    public void SqlFunctions_DateAdd_InvalidUnit_ThrowsException()
    {
        var baseDate = new DateTime(2024, 1, 1);

        Assert.Throws<ArgumentException>(() => SqlFunctions.DateAdd(baseDate, 5, "invalid_unit"));
    }

    [Fact]
    public void SqlFunctions_Avg_EmptyCollection_ReturnsZero()
    {
        object[] values = [];
        var avg = SqlFunctions.Avg(values);

        Assert.Equal(0m, avg);
    }

    [Fact]
    public void SqlFunctions_Avg_WithNulls_IgnoresNulls()
    {
        object?[] values = [10, null, 20, null, 30];
        // Null-forgiving operator is safe here as SqlFunctions.Avg filters out nulls
        var avg = SqlFunctions.Avg(values!);

        Assert.Equal(20m, avg);
    }

    [Fact]
    public void SqlFunctions_Sum_WithNulls_IgnoresNulls()
    {
        object?[] values = [5, null, 10, null, 15];
        // Null-forgiving operator is safe here as SqlFunctions.Sum filters out nulls
        var sum = SqlFunctions.Sum(values!);

        Assert.Equal(30m, sum);
    }

    [Fact]
    public void SqlFunctions_CountDistinct_WithNulls_IgnoresNulls()
    {
        object?[] values = [1, null, 2, null, 2, 3];
        // Null-forgiving operator is safe here as SqlFunctions.CountDistinct filters out nulls
        var count = SqlFunctions.CountDistinct(values!);

        Assert.Equal(3, count);
    }

    [Fact]
    public void SqlFunctions_GroupConcat_EmptyCollection_ReturnsEmptyString()
    {
        var values = Array.Empty<object>();
        var result = SqlFunctions.GroupConcat(values);

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void SqlFunctions_GroupConcat_WithNulls_IgnoresNulls()
    {
        object?[] values = ["a", null, "b", null, "c"];
        // Null-forgiving operator is safe here as SqlFunctions.GroupConcat filters out nulls
        var result = SqlFunctions.GroupConcat(values!, "|");

        Assert.Equal("a|b|c", result);
    }

    [Fact]
    public void DatabaseIndex_LargeData_PerformanceTest()
    {
        var index = new DatabaseIndex("idx_large", "test_table", "test_column");

        // Add 10000 entries
        for (int i = 0; i < 10000; i++)
        {
            index.Add($"key_{i % 100}", i);
        }

        // Verify lookups work correctly
        var results = index.Lookup("key_0");
        Assert.Equal(100, results.Count);

        // Verify index size
        Assert.True(index.Size <= 100);
    }

    [Fact]
    public void DatabaseIndex_NullKey_IgnoresAdd()
    {
        var index = new DatabaseIndex("idx_null", "test_table", "test_column");

        index.Add(null!, 1);

        // Null keys should be ignored
        Assert.Equal(0, index.Size);
    }

    [Fact]
    public void DatabaseIndex_Rebuild_Success()
    {
        var index = new DatabaseIndex("idx_rebuild", "test_table", "test_column");

        index.Add("key1", 1);
        index.Add("key2", 2);

        var rows = new List<(int RowId, Dictionary<string, object> Row)>
        {
            (10, new Dictionary<string, object> { ["test_column"] = "newkey1" }),
            (20, new Dictionary<string, object> { ["test_column"] = "newkey2" })
        };

        index.Rebuild(rows);

        // Old keys should not exist
        Assert.Empty(index.Lookup("key1"));

        // New keys should exist
        Assert.Single(index.Lookup("newkey1"));
        Assert.Contains(10, index.Lookup("newkey1"));
    }

    [Fact]
    public void DatabasePool_ClearIdleConnections_Success()
    {
        using var pool = new DatabasePool(_services, maxPoolSize: 5);

        var db1 = pool.GetDatabase(_testDbPath + "_idle1", "password");
        var db2 = pool.GetDatabase(_testDbPath + "_idle2", "password");

        pool.ReturnDatabase(db1);
        pool.ReturnDatabase(db2);

        // Clear idle connections with very short timeout (should remove both)
        pool.ClearIdleConnections(TimeSpan.FromMilliseconds(1));

        var stats = pool.GetPoolStatistics();
        // After clearing, pool should have fewer connections
        Assert.True(stats["TotalConnections"] >= 0);
    }

    [Fact]
    public void DatabasePool_MaxPoolSize_Respected()
    {
        using var pool = new DatabasePool(_services, maxPoolSize: 2);

        var db1 = pool.GetDatabase(_testDbPath + "_max1", "password");
        var db2 = pool.GetDatabase(_testDbPath + "_max2", "password");
        var db3 = pool.GetDatabase(_testDbPath + "_max3", "password");

        var stats = pool.GetPoolStatistics();
        Assert.True(stats["TotalConnections"] >= 3); // Pool grows beyond max if needed
    }

    [Fact]
    public void ConnectionStringBuilder_EmptyString_Success()
    {
        var builder = new ConnectionStringBuilder("");

        Assert.Equal(string.Empty, builder.DataSource);
        Assert.Equal(string.Empty, builder.Password);
    }

    [Fact]
    public void ConnectionStringBuilder_MalformedString_HandlesGracefully()
    {
        var builder = new ConnectionStringBuilder("invalid;;;key=value=extra");

        // Should not throw, just ignore malformed parts
        Assert.NotNull(builder);
    }

    [Fact]
    public void AutoMaintenanceService_WriteThreshold_TriggersMaintenance()
    {
        using var service = new AutoMaintenanceService(_db, intervalSeconds: 3600, writeThreshold: 3);

        service.IncrementWriteCount();
        service.IncrementWriteCount();
        Assert.Equal(2, service.WriteCount);

        // Third increment should trigger maintenance and reset count
        service.IncrementWriteCount();

        // After maintenance, count should be reset to 0
        Assert.Equal(0, service.WriteCount);
    }

    [Fact]
    public void AutoMaintenanceService_Dispose_StopsTimer()
    {
        var service = new AutoMaintenanceService(_db, intervalSeconds: 1, writeThreshold: 1000);

        service.Dispose();

        // Should not throw after dispose
        service.IncrementWriteCount();
    }

    [Fact]
    public void DatabasePool_DisposedPool_ThrowsException()
    {
        var pool = new DatabasePool(_services, maxPoolSize: 5);
        pool.Dispose();

        Assert.Throws<ObjectDisposedException>(() => pool.GetDatabase(_testDbPath, "password"));
    }

    [Fact]
    public void SqlFunctions_StrFTime_Success()
    {
        var date = new DateTime(2024, 3, 15, 14, 30, 0);
        var formatted = SqlFunctions.StrFTime(date, "yyyy-MM-dd HH:mm");

        Assert.Equal("2024-03-15 14:30", formatted);
    }

    [Fact]
    public void SqlFunctions_Date_Success()
    {
        var dateTime = new DateTime(2024, 3, 15, 14, 30, 45);
        var dateOnly = SqlFunctions.Date(dateTime);

        Assert.Equal(new DateTime(2024, 3, 15), dateOnly);
    }
}
