using Microsoft.EntityFrameworkCore.Query;
using SharpCoreDB.Interfaces;
using System.Linq.Expressions;
using System.Reflection;

namespace SharpCoreDB.EntityFrameworkCore.Query;

/// <summary>
/// Prevents EF Core from pre-evaluating SharpCoreDB DbFunctions during query translation.
/// </summary>
public sealed class SharpCoreDBEvaluatableExpressionFilterPlugin : IEvaluatableExpressionFilterPlugin
{
    private static readonly MethodInfo _graphTraverseMethod =
        typeof(SharpCoreDBDbFunctionsExtensions)
            .GetMethod(nameof(SharpCoreDBDbFunctionsExtensions.GraphTraverse),
                new[] { typeof(long), typeof(string), typeof(int), typeof(GraphTraversalStrategy) })!;

    /// <inheritdoc />
    public bool IsEvaluatableExpression(Expression expression)
    {
        return expression is MethodCallExpression methodCall
            && methodCall.Method == _graphTraverseMethod
            ? false
            : true;
    }
}
