namespace SharpCoreDB.EntityFrameworkCore.Tests.Infrastructure;

using SharpCoreDB.EntityFrameworkCore.Infrastructure;

public sealed class SharpCoreDBDatabaseProviderTests
{
    /// <summary>
    /// Verifies that SharpCoreDBDatabaseProvider is a concrete type that can be resolved via DI
    /// (inherits from RelationalDatabase which handles CompileQuery via EF Core's pipeline).
    /// </summary>
    [Fact]
    public void SharpCoreDBDatabaseProvider_InheritsRelationalDatabase()
    {
        // The type must be a subclass of RelationalDatabase so EF's full query pipeline is used.
        Assert.True(
            typeof(Microsoft.EntityFrameworkCore.Storage.RelationalDatabase)
                .IsAssignableFrom(typeof(SharpCoreDBDatabaseProvider)));
    }
}
