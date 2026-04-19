namespace SharpCoreDB.Tests.Graph;

using Moq;
using SharpCoreDB.Interfaces;
using SharpCoreDB.Services;

public sealed class GraphRagSqlProjectionTests
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
                    ["node_id"] = 42L,
                    ["score"] = 0.88,
                    ["context"] = "ctx-a"
                }
            ];

            return Task.FromResult(rows);
        }
    }

    private static SqlParser CreateParserWithProvider()
    {
        var storage = new Mock<IStorage>(MockBehavior.Loose);
        var parser = new SqlParser(
            tables: new Dictionary<string, ITable>(StringComparer.OrdinalIgnoreCase),
            dbPath: "test-db-path",
            storage: storage.Object,
            isReadOnly: false,
            queryCache: null,
            config: null);

        parser.SetGraphRagProvider(new FakeGraphRagProvider());
        return parser;
    }

    [Fact]
    public void ExecuteQuery_GraphRagProjection_WithAliases_ShouldShapeOutput()
    {
        // Arrange
        var parser = CreateParserWithProvider();

        // Act
        var rows = parser.ExecuteQuery("SELECT score AS relevance, context AS snippet, node_id AS docId FROM documents GRAPH_RAG 'gdpr risk' LIMIT 1 WITH CONTEXT", []);

        // Assert
        Assert.Single(rows);
        Assert.Equal(3, rows[0].Count);
        Assert.Equal(0.88, Convert.ToDouble(rows[0]["relevance"], System.Globalization.CultureInfo.InvariantCulture), 3);
        Assert.Equal("ctx-a", rows[0]["snippet"]);
        Assert.Equal("42", rows[0]["docId"].ToString());
    }

    [Fact]
    public void ExecuteQuery_GraphRagProjection_WithoutAliases_ShouldKeepSelectedColumnsOnly()
    {
        // Arrange
        var parser = CreateParserWithProvider();

        // Act
        var rows = parser.ExecuteQuery("SELECT score, context FROM documents GRAPH_RAG 'gdpr risk' LIMIT 1 WITH CONTEXT", []);

        // Assert
        Assert.Single(rows);
        Assert.Equal(2, rows[0].Count);
        Assert.True(rows[0].ContainsKey("score"));
        Assert.True(rows[0].ContainsKey("context"));
        Assert.False(rows[0].ContainsKey("node_id"));
    }
}
