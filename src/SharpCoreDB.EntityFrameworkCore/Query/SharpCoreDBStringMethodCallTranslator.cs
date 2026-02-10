using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using System.Reflection;

namespace SharpCoreDB.EntityFrameworkCore.Query;

/// <summary>
/// Translates common <see cref="string"/> LINQ methods to SharpCoreDB SQL expressions.
/// Supports Contains, StartsWith, EndsWith, ToUpper, ToLower, Trim, Replace, Substring, and EF.Functions.Like.
/// ✅ EF Core COLLATE Phase 4: Added string.Equals(StringComparison) translation.
/// </summary>
public class SharpCoreDBStringMethodCallTranslator(ISqlExpressionFactory sqlExpressionFactory) : IMethodCallTranslator
{
    private static readonly MethodInfo _containsMethod =
        typeof(string).GetRuntimeMethod(nameof(string.Contains), [typeof(string)])!;

    private static readonly MethodInfo _startsWithMethod =
        typeof(string).GetRuntimeMethod(nameof(string.StartsWith), [typeof(string)])!;

    private static readonly MethodInfo _endsWithMethod =
        typeof(string).GetRuntimeMethod(nameof(string.EndsWith), [typeof(string)])!;

    private static readonly MethodInfo _toUpperMethod =
        typeof(string).GetRuntimeMethod(nameof(string.ToUpper), Type.EmptyTypes)!;

    private static readonly MethodInfo _toLowerMethod =
        typeof(string).GetRuntimeMethod(nameof(string.ToLower), Type.EmptyTypes)!;

    private static readonly MethodInfo _trimMethod =
        typeof(string).GetRuntimeMethod(nameof(string.Trim), Type.EmptyTypes)!;

    private static readonly MethodInfo _replaceMethod =
        typeof(string).GetRuntimeMethod(nameof(string.Replace), [typeof(string), typeof(string)])!;

    private static readonly MethodInfo _substringMethodOneArg =
        typeof(string).GetRuntimeMethod(nameof(string.Substring), [typeof(int)])!;

    private static readonly MethodInfo _substringMethodTwoArgs =
        typeof(string).GetRuntimeMethod(nameof(string.Substring), [typeof(int), typeof(int)])!;

    private static readonly MethodInfo _likeMethod =
        typeof(DbFunctionsExtensions).GetRuntimeMethod(
            nameof(DbFunctionsExtensions.Like), [typeof(DbFunctions), typeof(string), typeof(string)])!;

    // ✅ EF Core COLLATE Phase 4: string.Equals(string, StringComparison)
    private static readonly MethodInfo _equalsWithComparisonMethod =
        typeof(string).GetRuntimeMethod(nameof(string.Equals), [typeof(string), typeof(StringComparison)])!;

    private readonly ISqlExpressionFactory _sqlExpressionFactory = sqlExpressionFactory
        ?? throw new ArgumentNullException(nameof(sqlExpressionFactory));

    /// <inheritdoc />
    public SqlExpression? Translate(
        SqlExpression? instance,
        MethodInfo method,
        IReadOnlyList<SqlExpression> arguments,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        // ✅ EF Core COLLATE Phase 4: Translate string.Equals(string, StringComparison)
        if (method == _equalsWithComparisonMethod && instance is not null && arguments.Count == 2)
        {
            var comparisonExpression = arguments[1];
            
            // Extract StringComparison value from constant
            if (comparisonExpression is SqlConstantExpression { Value: StringComparison comparison })
            {
                var leftOperand = instance;
                var rightOperand = arguments[0];
                
                // Map StringComparison to COLLATE clause
                return comparison switch
                {
                    StringComparison.OrdinalIgnoreCase or 
                    StringComparison.CurrentCultureIgnoreCase or 
                    StringComparison.InvariantCultureIgnoreCase =>
                        // Apply NOCASE collation using CollateExpression
                        _sqlExpressionFactory.Equal(
                            new CollateExpression(leftOperand, "NOCASE"),
                            new CollateExpression(rightOperand, "NOCASE")),
                    
                    StringComparison.Ordinal or 
                    StringComparison.CurrentCulture or 
                    StringComparison.InvariantCulture =>
                        // Use binary comparison (default)
                        _sqlExpressionFactory.Equal(leftOperand, rightOperand),
                    
                    _ => _sqlExpressionFactory.Equal(leftOperand, rightOperand)
                };
            }
        }

        if (method == _containsMethod && instance is not null)
        {
            // LIKE '%' || @p || '%'
            return _sqlExpressionFactory.Like(
                instance,
                _sqlExpressionFactory.Add(
                    _sqlExpressionFactory.Add(
                        _sqlExpressionFactory.Constant("%"),
                        arguments[0]),
                    _sqlExpressionFactory.Constant("%")));
        }

        if (method == _startsWithMethod && instance is not null)
        {
            // LIKE @p || '%'
            return _sqlExpressionFactory.Like(
                instance,
                _sqlExpressionFactory.Add(
                    arguments[0],
                    _sqlExpressionFactory.Constant("%")));
        }

        if (method == _endsWithMethod && instance is not null)
        {
            // LIKE '%' || @p
            return _sqlExpressionFactory.Like(
                instance,
                _sqlExpressionFactory.Add(
                    _sqlExpressionFactory.Constant("%"),
                    arguments[0]));
        }

        if (method == _toUpperMethod && instance is not null)
        {
            return _sqlExpressionFactory.Function(
                "UPPER", [instance], nullable: true, argumentsPropagateNullability: [true],
                typeof(string));
        }

        if (method == _toLowerMethod && instance is not null)
        {
            return _sqlExpressionFactory.Function(
                "LOWER", [instance], nullable: true, argumentsPropagateNullability: [true],
                typeof(string));
        }

        if (method == _trimMethod && instance is not null)
        {
            return _sqlExpressionFactory.Function(
                "TRIM", [instance], nullable: true, argumentsPropagateNullability: [true],
                typeof(string));
        }

        if (method == _replaceMethod && instance is not null)
        {
            return _sqlExpressionFactory.Function(
                "REPLACE", [instance, arguments[0], arguments[1]],
                nullable: true, argumentsPropagateNullability: [true, true, true],
                typeof(string));
        }

        if (method == _substringMethodOneArg && instance is not null)
        {
            return _sqlExpressionFactory.Function(
                "SUBSTR",
                [instance, _sqlExpressionFactory.Add(arguments[0], _sqlExpressionFactory.Constant(1))],
                nullable: true, argumentsPropagateNullability: [true, true],
                typeof(string));
        }

        if (method == _substringMethodTwoArgs && instance is not null)
        {
            return _sqlExpressionFactory.Function(
                "SUBSTR",
                [instance, _sqlExpressionFactory.Add(arguments[0], _sqlExpressionFactory.Constant(1)), arguments[1]],
                nullable: true, argumentsPropagateNullability: [true, true, true],
                typeof(string));
        }

        // EF.Functions.Like(column, pattern)
        if (method == _likeMethod)
        {
            return _sqlExpressionFactory.Like(arguments[1], arguments[2]);
        }

        return null;
    }
}
