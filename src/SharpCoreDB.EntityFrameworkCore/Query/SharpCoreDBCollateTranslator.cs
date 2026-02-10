using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using System.Linq.Expressions;
using System.Reflection;

namespace SharpCoreDB.EntityFrameworkCore.Query;

/// <summary>
/// Translates EF.Functions.Collate() calls to COLLATE SQL clauses.
/// ✅ EF Core COLLATE Phase 3: Enables query-level collation override.
/// </summary>
/// <example>
/// <code>
/// // Query with explicit collation
/// var users = context.Users
///     .Where(u => EF.Functions.Collate(u.Name, "NOCASE") == "alice")
///     .ToList();
/// </code>
/// </example>
public class SharpCoreDBCollateTranslator : IMethodCallTranslator
{
    private static readonly MethodInfo _collateMethod = typeof(SharpCoreDBDbFunctionsExtensions)
        .GetRuntimeMethod(nameof(SharpCoreDBDbFunctionsExtensions.Collate), [typeof(DbFunctions), typeof(string), typeof(string)])!;

    private readonly ISqlExpressionFactory _sqlExpressionFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="SharpCoreDBCollateTranslator"/> class.
    /// </summary>
    /// <param name="sqlExpressionFactory">The SQL expression factory.</param>
    public SharpCoreDBCollateTranslator(ISqlExpressionFactory sqlExpressionFactory)
    {
        _sqlExpressionFactory = sqlExpressionFactory;
    }

    /// <inheritdoc />
    public SqlExpression? Translate(
        SqlExpression? instance,
        MethodInfo method,
        IReadOnlyList<SqlExpression> arguments,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        if (method != _collateMethod || arguments.Count != 3)
        {
            return null;
        }

        // arguments[0] is DbFunctions (ignored)
        // arguments[1] is the string expression
        // arguments[2] is the collation name (should be a constant)

        var stringExpression = arguments[1];
        var collationExpression = arguments[2];

        // Extract collation name from constant expression
        if (collationExpression is SqlConstantExpression { Value: string collationName })
        {
            // Create a CollateExpression manually
            return new CollateExpression(stringExpression, collationName);
        }

        // If collation is not a constant, we can't translate it
        return null;
    }
}

/// <summary>
/// Extension methods for SharpCoreDB-specific database functions.
/// ✅ EF Core COLLATE Phase 3: Provides EF.Functions.Collate() for query-level collation.
/// </summary>
public static class SharpCoreDBDbFunctionsExtensions
{
    /// <summary>
    /// Applies a collation to a string expression for comparison.
    /// ✅ EF Core COLLATE Phase 3: Translates to COLLATE clause in SQL.
    /// </summary>
    /// <param name="functions">The DbFunctions instance.</param>
    /// <param name="value">The string value to apply collation to.</param>
    /// <param name="collation">The collation name (NOCASE, BINARY, RTRIM).</param>
    /// <returns>The string with collation applied.</returns>
    /// <example>
    /// <code>
    /// // Case-insensitive search
    /// var users = context.Users
    ///     .Where(u => EF.Functions.Collate(u.Name, "NOCASE") == "alice")
    ///     .ToList();
    /// 
    /// // Generates: SELECT * FROM Users WHERE Name COLLATE NOCASE = 'alice'
    /// </code>
    /// </example>
    public static string Collate(
        this DbFunctions _,
        string value,
        string collation)
    {
        // This method is never executed - it's translated to SQL by SharpCoreDBCollateTranslator
        throw new InvalidOperationException(
            $"{nameof(Collate)} is a database function and can only be used in LINQ to Entities queries.");
    }
}
