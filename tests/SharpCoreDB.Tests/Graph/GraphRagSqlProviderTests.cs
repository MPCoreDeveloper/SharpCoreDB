namespace SharpCoreDB.Tests.Graph;

using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.Graph.Advanced.GraphRAG;
using SharpCoreDB.Graph.Advanced.SqlIntegration;
using SharpCoreDB.Interfaces;

public sealed class GraphRagSqlProviderTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly IServiceProvider _serviceProvider;
    private readonly DatabaseFactory _factory;

    private sealed class FakeProvider : IGraphRagProvider
    {
        public bool CanExecute(string tableName) => tableName.Equals("documents", StringComparison.OrdinalIgnoreCase);

        public Task<IReadOnlyList<Dictionary<string, object>>> ExecuteAsync(GraphRagRequest request, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<Dictionary<string, object>> rows =
            [
                new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    ["node_id"] = 7L,
                    ["score"] = 0.91,
                    ["context"] = "ctx"
                }
            ];

            return Task.FromResult(rows);
        }
    }

    public GraphRagSqlProviderTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"SharpCoreDB_GraphRagProvider_Test_{Guid.NewGuid()}");

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
    public async Task GraphRagProvider_RequestValidation_ThrowsOnInvalidScore()
    {
        // Arrange
        var db = _factory.Create(_testDbPath, "testPassword") as Database
            ?? throw new InvalidOperationException("Expected concrete Database instance.");
        var engine = new GraphRagEngine(db, "edges", "embeddings", 16);
        var provider = new GraphRagSqlProvider(db, engine, _ => Enumerable.Repeat(1f, 16).ToArray());

        var request = new GraphRagRequest(
            TableName: "documents",
            Question: "q",
            Columns: ["*"],
            Limit: 5,
            TopK: 10,
            MinScore: 1.1,
            IncludeContext: false);

        // Act / Assert
        await Assert.ThrowsAsync<ArgumentException>(() => provider.ExecuteAsync(request));
    }

    [Fact]
    public async Task FakeGraphRagProvider_Execute_ReturnsRows()
    {
        // Arrange
        var provider = new FakeProvider();
        var request = new GraphRagRequest("documents", "What is GDPR?", ["*"], 5, 10, 0.5, true);

        // Act
        var rows = await provider.ExecuteAsync(request);

        // Assert
        Assert.Single(rows);
        Assert.Equal("7", rows[0]["node_id"].ToString());
        Assert.Equal("0.91", Convert.ToDouble(rows[0]["score"]).ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
}
