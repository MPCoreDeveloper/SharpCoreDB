namespace SharpCoreDB.Tests;

using SharpCoreDB.Services;

public sealed class OptionallySqlParserTests
{
    [Fact]
    public void ParseSelect_WithOptionally_ShouldSetOptionalProjectionFlag()
    {
        // Arrange
        var parser = new EnhancedSqlParser();

        // Act
        var ast = parser.Parse("SELECT id, email OPTIONALLY FROM users") as SelectNode;

        // Assert
        Assert.NotNull(ast);
        Assert.True(ast!.IsOptionalProjection);
    }

    [Fact]
    public void ParseWhere_WithIsSome_ShouldParseBinaryOperator()
    {
        // Arrange
        var parser = new EnhancedSqlParser();

        // Act
        var ast = parser.Parse("SELECT id FROM users WHERE email IS SOME") as SelectNode;

        // Assert
        Assert.NotNull(ast);
        Assert.NotNull(ast!.Where);
        var binary = Assert.IsType<BinaryExpressionNode>(ast.Where!.Condition);
        Assert.Equal("IS SOME", binary.Operator);
    }

    [Fact]
    public void ParseWhere_WithIsNone_ShouldParseBinaryOperator()
    {
        // Arrange
        var parser = new EnhancedSqlParser();

        // Act
        var ast = parser.Parse("SELECT id FROM users WHERE email IS NONE") as SelectNode;

        // Assert
        Assert.NotNull(ast);
        Assert.NotNull(ast!.Where);
        var binary = Assert.IsType<BinaryExpressionNode>(ast.Where!.Condition);
        Assert.Equal("IS NONE", binary.Operator);
    }
}
