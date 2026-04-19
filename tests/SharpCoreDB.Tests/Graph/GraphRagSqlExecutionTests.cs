namespace SharpCoreDB.Tests.Graph;

using Moq;
using SharpCoreDB.Interfaces;
using SharpCoreDB.Services;

public sealed class GraphRagSqlExecutionTests
{
    private sealed class FakeGraphRagProvider : IGraphRagProvider
    {
        public bool CanExecute(string tableName) => tableName.Equals("documents", StringComparison.OrdinalIgnoreCase);

        public Task<IReadOnlyList<Dictionary<string, object>>> ExecuteAsync(GraphRagRequest request, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<Dictionary<string, object>> rows =
            [
                new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    ["node_id"] = 1L,
                    ["score"] = 0.93,
                    ["context"] = "doc-context"
                }
            ];

            return Task.FromResult(rows);
        }
    }

    private static SqlParser CreateParserWithOptionalProvider(IGraphRagProvider? provider = null)
    {
        var storage = new Mock<IStorage>(MockBehavior.Loose);
        var parser = new SqlParser(
            tables: new Dictionary<string, ITable>(StringComparer.OrdinalIgnoreCase),
            dbPath: "test-db-path",
            storage: storage.Object,
            isReadOnly: false,
            queryCache: null,
            config: null);

        if (provider is not null)
        {
            parser.SetGraphRagProvider(provider);
        }

        return parser;
    }

    [Fact]
    public void ExecuteQuery_WithGraphRagAndProvider_ShouldReturnRows()
    {
        // Arrange
        var parser = CreateParserWithOptionalProvider(new FakeGraphRagProvider());

        // Act
        var rows = parser.ExecuteQuery("SELECT * FROM documents GRAPH_RAG 'gdpr risk' LIMIT 1 WITH SCORE > 0.5 WITH CONTEXT", []);

        // Assert
        Assert.Single(rows);
        Assert.Equal("1", rows[0]["node_id"].ToString());
        Assert.Equal(0.93, Convert.ToDouble(rows[0]["score"], System.Globalization.CultureInfo.InvariantCulture), 3);
        Assert.Equal("doc-context", rows[0]["context"]);
    }

    [Fact]
    public void ExecuteQuery_WithGraphRagWithoutProvider_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var parser = CreateParserWithOptionalProvider();

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() =>
            parser.ExecuteQuery("SELECT * FROM documents GRAPH_RAG 'gdpr risk' LIMIT 1", []));

        Assert.Contains("provider is not configured", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
