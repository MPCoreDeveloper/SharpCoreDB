namespace SharpCoreDB.Tests.Graph;

using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.Graph.Advanced.SqlIntegration;
using SharpCoreDB.Interfaces;

public sealed class GraphRagServiceCollectionExtensionsTests : IDisposable
{
    private readonly string _testDbPath = Path.Combine(Path.GetTempPath(), $"SharpCoreDB_GraphRag_DI_{Guid.NewGuid()}");

    public void Dispose()
    {
        if (Directory.Exists(_testDbPath))
        {
            Directory.Delete(_testDbPath, true);
        }
    }

    [Fact]
    public void AddSharpCoreDBGraphRagSql_ShouldRegisterGraphRagProvider()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        services.AddSingleton<IDatabase>(sp =>
            sp.GetRequiredService<DatabaseFactory>().Create(_testDbPath, "testPassword"));

        // Act
        services.AddSharpCoreDBGraphRagSql(options =>
        {
            options.GraphTableName = "edges";
            options.EmbeddingTableName = "embeddings";
            options.EmbeddingDimensions = 16;
        });

        var provider = services.BuildServiceProvider();

        // Assert
        Assert.NotNull(provider.GetService<IGraphRagProvider>());
    }

    [Fact]
    public void AddSharpCoreDBGraphRagSql_WithInvalidOptions_ShouldThrowArgumentException()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSharpCoreDB();

        // Act / Assert
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            services.AddSharpCoreDBGraphRagSql(options =>
            {
                options.GraphTableName = "edges";
                options.EmbeddingTableName = "embeddings";
                options.EmbeddingDimensions = 0;
            }));
    }
}
