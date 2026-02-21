using Microsoft.EntityFrameworkCore.Query;
using System.Linq.Expressions;
using System.Reflection;

namespace SharpCoreDB.EntityFrameworkCore.Query;

/// <summary>
/// Rewrites SharpCoreDB-specific LINQ methods into translatable query patterns.
/// </summary>
public sealed class SharpCoreDBQueryTranslationPreprocessor(
    QueryTranslationPreprocessorDependencies dependencies,
    RelationalQueryTranslationPreprocessorDependencies relationalDependencies,
    QueryCompilationContext queryCompilationContext)
    : RelationalQueryTranslationPreprocessor(dependencies, relationalDependencies, queryCompilationContext)
{
    /// <inheritdoc />
    public override Expression Process(Expression query)
    {
        var rewritten = new TraverseMethodRewritingVisitor().Visit(query);
        return base.Process(rewritten);
    }

    private sealed class TraverseMethodRewritingVisitor : ExpressionVisitor
    {
        private static readonly MethodInfo _traverseMethod =
            typeof(GraphTraversalQueryableExtensions)
                .GetMethods()
                .First(m => m.Name == nameof(GraphTraversalQueryableExtensions.Traverse)
                    && m.GetParameters().Length == 5);

        private static readonly MethodInfo _selectMethod =
            typeof(Queryable)
                .GetMethods()
                .First(m => m.Name == nameof(Queryable.Select)
                    && m.GetParameters().Length == 2);

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Method.IsGenericMethod
                && node.Method.GetGenericMethodDefinition() == _traverseMethod)
            {
                return RewriteTraverse(node);
            }

            return base.VisitMethodCall(node);
        }

        private static Expression RewriteTraverse(MethodCallExpression node)
        {
            var source = node.Arguments[0];
            var elementType = source.Type.GetGenericArguments().First();
            var parameter = Expression.Parameter(elementType, "e");

            var graphTraverseCall = Expression.Call(
                typeof(SharpCoreDBDbFunctionsExtensions),
                nameof(SharpCoreDBDbFunctionsExtensions.GraphTraverse),
                Type.EmptyTypes,
                node.Arguments[1],
                node.Arguments[2],
                node.Arguments[3],
                node.Arguments[4]);

            var selector = Expression.Lambda(graphTraverseCall, parameter);
            var selectMethod = _selectMethod.MakeGenericMethod(elementType, typeof(long));

            return Expression.Call(null, selectMethod, source, selector);
        }
    }
}
