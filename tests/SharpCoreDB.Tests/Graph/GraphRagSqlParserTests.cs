namespace SharpCoreDB.Tests.Graph;

using SharpCoreDB.Services;

public sealed class GraphRagSqlParserTests
{
    [Fact]
    public void ParseSelect_WithGraphRagClause_ShouldPopulateGraphRagNode()
    {
        // Arrange
        var parser = new EnhancedSqlParser();
        const string sql = "SELECT * FROM documents GRAPH_RAG 'What are GDPR risks?' LIMIT 5 WITH SCORE > 0.85 WITH CONTEXT TOP_K 20";

        // Act
        var node = parser.Parse(sql) as SelectNode;

        // Assert
        Assert.NotNull(node);
        Assert.NotNull(node!.GraphRag);
        Assert.Equal("What are GDPR risks?", node.GraphRag!.Question);
        Assert.Equal(5, node.GraphRag.Limit);
        Assert.Equal(0.85, node.GraphRag.MinScore);
        Assert.True(node.GraphRag.IncludeContext);
        Assert.Equal(20, node.GraphRag.TopK);
    }

    [Fact]
    public void ParseSelect_WithGraphRagClauseWithoutQuestion_ShouldRecordError()
    {
        // Arrange
        var parser = new EnhancedSqlParser();
        const string sql = "SELECT * FROM documents GRAPH_RAG LIMIT 5";

        // Act
        var _ = parser.Parse(sql);

        // Assert
        Assert.True(parser.HasErrors);
        Assert.Contains(parser.Errors, e => e.Contains("GRAPH_RAG requires a non-empty string question literal", StringComparison.OrdinalIgnoreCase));
    }
}
