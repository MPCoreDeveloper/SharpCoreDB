using Xunit;
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB;
using SharpCoreDB.Interfaces;
using SharpCoreDB.Services;
using SharpCoreDB.DataStructures;

namespace SharpCoreDB.Tests;

public class NewFeaturesTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly IDatabase _db;
    private readonly IServiceProvider _services;

    public NewFeaturesTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"test_new_features_{Guid.NewGuid()}");
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
    public void ConnectionStringBuilder_Parse_Success()
    {
        var connectionString = "Data Source=test.db;Password=secret123;ReadOnly=True;Cache=Shared";
        var builder = new ConnectionStringBuilder(connectionString);

        Assert.Equal("test.db", builder.DataSource);
        Assert.Equal("secret123", builder.Password);
        Assert.True(builder.ReadOnly);
        Assert.Equal("Shared", builder.Cache);
    }

    [Fact]
    public void ConnectionStringBuilder_Build_Success()
    {
        var builder = new ConnectionStringBuilder
        {
            DataSource = "mydb.sharpcoredb",
            Password = "MySecret",
            ReadOnly = false,
            Cache = "Private"
        };

        var connectionString = builder.BuildConnectionString();
        Assert.Contains("Data Source=mydb.sharpcoredb", connectionString);
        Assert.Contains("Password=MySecret", connectionString);
        Assert.Contains("Cache=Private", connectionString);
    }

    [Fact]
    public void DatabasePool_GetDatabase_Success()
    {
        using var pool = new DatabasePool(_services, maxPoolSize: 5);
        var db1 = pool.GetDatabase(_testDbPath + "_pool", "password");
        var db2 = pool.GetDatabase(_testDbPath + "_pool", "password");

        Assert.NotNull(db1);
        Assert.NotNull(db2);
        Assert.Same(db1, db2); // Should return same instance from pool

        var stats = pool.GetPoolStatistics();
        Assert.True(stats["TotalConnections"] > 0);
    }

    [Fact]
    public void DatabasePool_ReturnDatabase_Success()
    {
        using var pool = new DatabasePool(_services, maxPoolSize: 5);
        var db = pool.GetDatabase(_testDbPath + "_pool2", "password");

        pool.ReturnDatabase(db);
        
        var stats = pool.GetPoolStatistics();
        Assert.True(stats["TotalConnections"] > 0);
    }

    [Fact]
    public void SqlFunctions_Now_ReturnsDateTime()
    {
        var now = SqlFunctions.Now();
        Assert.IsType<DateTime>(now);
        Assert.True(now <= DateTime.UtcNow);
    }

    [Fact]
    public void SqlFunctions_DateAdd_Success()
    {
        var baseDate = new DateTime(2024, 1, 1);
        var result = SqlFunctions.DateAdd(baseDate, 5, "days");
        
        Assert.Equal(new DateTime(2024, 1, 6), result);
    }

    [Fact]
    public void SqlFunctions_Sum_Success()
    {
        var values = new object[] { 1, 2, 3, 4, 5 };
        var sum = SqlFunctions.Sum(values);
        
        Assert.Equal(15m, sum);
    }

    [Fact]
    public void SqlFunctions_Avg_Success()
    {
        var values = new object[] { 10, 20, 30 };
        var avg = SqlFunctions.Avg(values);
        
        Assert.Equal(20m, avg);
    }

    [Fact]
    public void SqlFunctions_CountDistinct_Success()
    {
        var values = new object[] { 1, 2, 2, 3, 3, 3 };
        var count = SqlFunctions.CountDistinct(values);
        
        Assert.Equal(3, count);
    }

    [Fact]
    public void SqlFunctions_GroupConcat_Success()
    {
        var values = new object[] { "apple", "banana", "cherry" };
        var result = SqlFunctions.GroupConcat(values, "|");
        
        Assert.Equal("apple|banana|cherry", result);
    }

    [Fact]
    public void DatabaseIndex_AddAndLookup_Success()
    {
        var index = new DatabaseIndex("idx_test", "test_table", "test_column");
        
        index.Add("key1", 1);
        index.Add("key2", 2);
        index.Add("key1", 3); // Duplicate key allowed for non-unique index
        
        var results = index.Lookup("key1");
        Assert.Equal(2, results.Count);
        Assert.Contains(1, results);
        Assert.Contains(3, results);
    }

    [Fact]
    public void DatabaseIndex_UniqueConstraint_Throws()
    {
        var index = new DatabaseIndex("idx_unique", "test_table", "test_column", isUnique: true);
        
        index.Add("key1", 1);
        
        Assert.Throws<InvalidOperationException>(() => index.Add("key1", 2));
    }

    [Fact]
    public void DatabaseIndex_Remove_Success()
    {
        var index = new DatabaseIndex("idx_test", "test_table", "test_column");
        
        index.Add("key1", 1);
        index.Add("key1", 2);
        
        index.Remove("key1", 1);
        
        var results = index.Lookup("key1");
        Assert.Single(results);
        Assert.Contains(2, results);
    }

    [Fact]
    public void AutoMaintenanceService_IncrementWriteCount_Success()
    {
        using var service = new AutoMaintenanceService(_db, intervalSeconds: 60, writeThreshold: 10);
        
        service.IncrementWriteCount();
        Assert.Equal(1, service.WriteCount);
        
        service.IncrementWriteCount();
        Assert.Equal(2, service.WriteCount);
    }

    [Fact]
    public void AutoMaintenanceService_TriggerMaintenance_Success()
    {
        using var service = new AutoMaintenanceService(_db, intervalSeconds: 3600, writeThreshold: 1000);
        
        // Should not throw
        service.TriggerMaintenance();
    }
}
