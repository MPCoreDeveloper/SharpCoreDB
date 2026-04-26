namespace SharpCoreDB.EntityFrameworkCore.Tests.Infrastructure;

using Microsoft.EntityFrameworkCore.Query;
using SharpCoreDB.EntityFrameworkCore.Infrastructure;
using System.Linq.Expressions;

public sealed class SharpCoreDBDatabaseProviderTests
{
    /// <summary>
    /// Verifies CompileQuery compiles typed QueryContext lambda expressions without returning default/null values.
    /// </summary>
    [Fact]
    public void CompileQuery_WithTypedLambda_ShouldReturnCompiledDelegate()
    {
        // Arrange
        var provider = new SharpCoreDBDatabaseProvider();
        Expression<Func<QueryContext, int>> query = _ => 42;

        // Act
        var compiled = provider.CompileQuery<int>(query, async: false);
        var result = compiled(null!);

        // Assert
        Assert.Equal(42, result);
    }

    /// <summary>
    /// Verifies CompileQueryExpression rejects incompatible lambda signatures instead of silently accepting invalid expressions.
    /// </summary>
    [Fact]
    public void CompileQueryExpression_WithInvalidLambdaSignature_ShouldThrowNotSupportedException()
    {
        // Arrange
        var provider = new SharpCoreDBDatabaseProvider();
        Expression invalidQuery = () => 10;

        // Act & Assert
        _ = Assert.Throws<NotSupportedException>(() => provider.CompileQueryExpression<int>(invalidQuery, async: false));
    }

    /// <summary>
    /// Verifies CompileQuery can compile constant expressions and preserve the expected value.
    /// </summary>
    [Fact]
    public void CompileQuery_WithConstantExpression_ShouldReturnConstantResult()
    {
        // Arrange
        var provider = new SharpCoreDBDatabaseProvider();
        Expression query = Expression.Constant(7);

        // Act
        var compiled = provider.CompileQuery<int>(query, async: false);
        var result = compiled(null!);

        // Assert
        Assert.Equal(7, result);
    }
}
