#nullable enable

using Xunit;

namespace SharpCoreDB.Provider.Sync.Tests;

/// <summary>
/// Verifies SharpCoreDBSyncProvider compiles and integrates with Dotmim.Sync.
/// </summary>
public class ProviderInitializationTests
{
    [Fact]
    public void Provider_CanBeInstantiated()
    {
        // Arrange
        var connectionString = "Path=:memory:;Password=test";
        var options = new SyncProviderOptions();

        // Act
        var provider = new SharpCoreDBSyncProvider(connectionString, options);

        // Assert
        Assert.NotNull(provider);
        Assert.Equal(connectionString, provider.ConnectionString);
        Assert.NotNull(provider.Options);
    }

    [Fact]
    public void Provider_CreateConnection_ReturnsConnection()
    {
        // Arrange
        var connectionString = "Path=:memory:;Password=test";
        var options = new SyncProviderOptions();
        var provider = new SharpCoreDBSyncProvider(connectionString, options);

        // Act
        using var connection = provider.CreateConnection();

        // Assert
        Assert.NotNull(connection);
    }

    [Fact]
    public void Provider_GetDatabaseName_ReturnsValidName()
    {
        // Arrange
        var connectionString = "Path=C:\\data\\test.scdb;Password=secret";
        var options = new SyncProviderOptions();
        var provider = new SharpCoreDBSyncProvider(connectionString, options);

        // Act
        var dbName = provider.GetDatabaseName();

        // Assert
        Assert.NotNull(dbName);
        Assert.NotEmpty(dbName);
        Assert.Equal("test", dbName);
    }
}
