#nullable enable

using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace SharpCoreDB.Provider.Sync.Tests;

/// <summary>
/// Verifies DI registration and service resolution work correctly.
/// M2 milestone: DI Integration Works
/// </summary>
public class DependencyInjectionTests
{
    [Fact]
    public void AddSharpCoreDBSync_RegistersProvider()
    {
        // Arrange
        var services = new ServiceCollection();
        var connectionString = "Path=:memory:;Password=test";

        // Act
        services.AddSharpCoreDBSync(connectionString);
        var provider = services.BuildServiceProvider();

        // Assert
        var syncProvider = provider.GetRequiredService<SharpCoreDBSyncProvider>();
        Assert.NotNull(syncProvider);
    }

    [Fact]
    public void AddSharpCoreDBSync_RegistersSyncProviderOptions()
    {
        // Arrange
        var services = new ServiceCollection();
        var connectionString = "Path=:memory:;Password=test";

        // Act
        services.AddSharpCoreDBSync(connectionString, opts =>
        {
            opts.EnableAutoTracking = false;
            opts.TombstoneRetentionDays = 60;
        });
        var provider = services.BuildServiceProvider();

        // Assert
        var options = provider.GetRequiredService<SyncProviderOptions>();
        Assert.NotNull(options);
        Assert.False(options.EnableAutoTracking);
        Assert.Equal(60, options.TombstoneRetentionDays);
    }

    [Fact]
    public void AddSharpCoreDBSync_RegistersChangeTrackingManager()
    {
        // Arrange
        var services = new ServiceCollection();
        var connectionString = "Path=:memory:;Password=test";

        // Act
        services.AddSharpCoreDBSync(connectionString);
        var provider = services.BuildServiceProvider();

        // Assert
        var manager = provider.GetRequiredService<SharpCoreDB.Provider.Sync.ChangeTracking.IChangeTrackingManager>();
        Assert.NotNull(manager);
    }

    [Fact]
    public void AddSharpCoreDBSync_RegistersTombstoneManager()
    {
        // Arrange
        var services = new ServiceCollection();
        var connectionString = "Path=:memory:;Password=test";

        // Act
        services.AddSharpCoreDBSync(connectionString);
        var provider = services.BuildServiceProvider();

        // Assert
        var manager = provider.GetRequiredService<SharpCoreDB.Provider.Sync.ChangeTracking.ITombstoneManager>();
        Assert.NotNull(manager);
    }
}
