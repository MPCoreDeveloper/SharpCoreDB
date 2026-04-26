using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Update;
using System.Linq.Expressions;

namespace SharpCoreDB.EntityFrameworkCore.Infrastructure;

/// <summary>
/// Database provider implementation for SharpCoreDB.
/// Provides core database operations and query compilation for EF Core.
/// </summary>
public class SharpCoreDBDatabaseProvider : IDatabase
{
    /// <summary>
    /// Initializes a new instance of the SharpCoreDBDatabaseProvider class.
    /// </summary>
    public SharpCoreDBDatabaseProvider()
    {
    }

    /// <inheritdoc />
    public string? DatabaseProductName => "SharpCoreDB";

    /// <inheritdoc />
    public string? DatabaseProductVersion => "1.0.0";

    /// <inheritdoc />
    public int SaveChanges(IList<IUpdateEntry> entries)
    {
        return entries.Count;
    }

    /// <inheritdoc />
    public async Task<int> SaveChangesAsync(IList<IUpdateEntry> entries, CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        return entries.Count;
    }

    /// <inheritdoc />
    public Func<QueryContext, TResult> CompileQuery<TResult>(Expression query, bool async)
    {
        ArgumentNullException.ThrowIfNull(query);

        var compiled = TryCompileQuery<TResult>(query);
        if (compiled is null)
        {
            throw new NotSupportedException("Unable to compile the provided EF Core query expression for SharpCoreDB.");
        }

        return compiled;
    }

    /// <inheritdoc />
    public Expression<Func<QueryContext, TResult>> CompileQueryExpression<TResult>(Expression query, bool async)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (TryCreateQueryLambda<TResult>(query) is { } lambda)
        {
            return lambda;
        }

        throw new NotSupportedException("Unable to create a compiled EF Core query expression for SharpCoreDB.");
    }

    private static Func<QueryContext, TResult>? TryCompileQuery<TResult>(Expression query)
    {
        if (TryCreateQueryLambda<TResult>(query) is { } lambda)
        {
            return lambda.Compile();
        }

        return null;
    }

    private static Expression<Func<QueryContext, TResult>>? TryCreateQueryLambda<TResult>(Expression query)
    {
        if (query is Expression<Func<QueryContext, TResult>> typedLambda)
        {
            return typedLambda;
        }

        if (query is LambdaExpression lambda)
        {
            if (lambda.Parameters.Count != 1 || lambda.Parameters[0].Type != typeof(QueryContext))
            {
                return null;
            }

            var body = lambda.ReturnType == typeof(TResult)
                ? lambda.Body
                : Expression.Convert(lambda.Body, typeof(TResult));

            return Expression.Lambda<Func<QueryContext, TResult>>(body, lambda.Parameters[0]);
        }

        var contextParameter = Expression.Parameter(typeof(QueryContext), "context");
        var directBody = query.Type == typeof(TResult)
            ? query
            : Expression.Convert(query, typeof(TResult));

        return Expression.Lambda<Func<QueryContext, TResult>>(directBody, contextParameter);
    }
}
