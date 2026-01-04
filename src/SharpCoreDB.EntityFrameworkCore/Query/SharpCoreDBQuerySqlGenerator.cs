using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using System.Linq.Expressions;

namespace SharpCoreDB.EntityFrameworkCore.Query;

/// <summary>
/// Query SQL generator for SharpCoreDB.
/// Translates LINQ expressions to SharpCoreDB SQL including Sum, GroupBy, DateTime.Now.
/// </summary>
public class SharpCoreDBQuerySqlGenerator : QuerySqlGenerator
{
    /// <summary>
    /// Initializes a new instance of the SharpCoreDBQuerySqlGenerator class.
    /// </summary>
    public SharpCoreDBQuerySqlGenerator(QuerySqlGeneratorDependencies dependencies)
        : base(dependencies)
    {
    }

    /// <inheritdoc />
    protected override Expression VisitSqlFunction(SqlFunctionExpression sqlFunctionExpression)
    {
        // Handle custom function translations
        if (sqlFunctionExpression.Name.Equals("SUM", StringComparison.OrdinalIgnoreCase))
        {
            Sql.Append("SUM(");
            if (sqlFunctionExpression.Arguments.Count > 0)
                Visit(sqlFunctionExpression.Arguments[0]!);
            Sql.Append(")");
            return sqlFunctionExpression;
        }

        if (sqlFunctionExpression.Name.Equals("AVG", StringComparison.OrdinalIgnoreCase))
        {
            Sql.Append("AVG(");
            if (sqlFunctionExpression.Arguments.Count > 0)
                Visit(sqlFunctionExpression.Arguments[0]!);
            Sql.Append(")");
            return sqlFunctionExpression;
        }

        if (sqlFunctionExpression.Name.Equals("COUNT", StringComparison.OrdinalIgnoreCase))
        {
            Sql.Append("COUNT(");
            if (sqlFunctionExpression.Arguments.Count > 0)
            {
                Visit(sqlFunctionExpression.Arguments[0]!);
            }
            else
            {
                Sql.Append("*");
            }
            Sql.Append(")");
            return sqlFunctionExpression;
        }

        // Handle DateTime.Now => NOW()
        if (sqlFunctionExpression.Name.Equals("NOW", StringComparison.OrdinalIgnoreCase))
        {
            Sql.Append("NOW()");
            return sqlFunctionExpression;
        }

        // Handle DATEADD
        if (sqlFunctionExpression.Name.Equals("DATEADD", StringComparison.OrdinalIgnoreCase))
        {
            Sql.Append("DATEADD(");
            for (var i = 0; i < sqlFunctionExpression.Arguments.Count; i++)
            {
                if (i > 0)
                {
                    Sql.Append(", ");
                }
                Visit(sqlFunctionExpression.Arguments[i]!);
            }
            Sql.Append(")");
            return sqlFunctionExpression;
        }

        // Handle STRFTIME
        if (sqlFunctionExpression.Name.Equals("STRFTIME", StringComparison.OrdinalIgnoreCase))
        {
            Sql.Append("STRFTIME(");
            for (var i = 0; i < sqlFunctionExpression.Arguments.Count; i++)
            {
                if (i > 0)
                {
                    Sql.Append(", ");
                }
                Visit(sqlFunctionExpression.Arguments[i]!);
            }
            Sql.Append(")");
            return sqlFunctionExpression;
        }

        // Handle GROUP_CONCAT
        if (sqlFunctionExpression.Name.Equals("GROUP_CONCAT", StringComparison.OrdinalIgnoreCase))
        {
            Sql.Append("GROUP_CONCAT(");
            for (var i = 0; i < sqlFunctionExpression.Arguments.Count; i++)
            {
                if (i > 0)
                {
                    Sql.Append(", ");
                }
                Visit(sqlFunctionExpression.Arguments[i]!);
            }
            Sql.Append(")");
            return sqlFunctionExpression;
        }

        return base.VisitSqlFunction(sqlFunctionExpression);
    }

    /// <inheritdoc />
    protected override void GenerateLimitOffset(SelectExpression selectExpression)
    {
        // SharpCoreDB supports LIMIT/OFFSET syntax
        if (selectExpression.Limit != null)
        {
            Sql.AppendLine().Append("LIMIT ");
            Visit(selectExpression.Limit!);
        }

        if (selectExpression.Offset != null)
        {
            if (selectExpression.Limit == null)
            {
                Sql.AppendLine().Append("LIMIT -1");
            }

            Sql.Append(" OFFSET ");
            Visit(selectExpression.Offset!);
        }
    }

    /// <inheritdoc />
    protected override Expression VisitSqlBinary(SqlBinaryExpression sqlBinaryExpression)
    {
        // Handle binary operations with proper operator precedence
        var requiresParentheses = RequiresParentheses(sqlBinaryExpression, sqlBinaryExpression.Left);

        if (requiresParentheses)
        {
            Sql.Append("(");
        }

        Visit(sqlBinaryExpression.Left);

        if (requiresParentheses)
        {
            Sql.Append(")");
        }

        Sql.Append(GetOperator(sqlBinaryExpression));

        requiresParentheses = RequiresParentheses(sqlBinaryExpression, sqlBinaryExpression.Right);

        if (requiresParentheses)
        {
            Sql.Append("(");
        }

        Visit(sqlBinaryExpression.Right);

        if (requiresParentheses)
        {
            Sql.Append(")");
        }

        return sqlBinaryExpression;
    }

    /// <summary>
    /// Gets the operator string for the given binary expression.
    /// </summary>
    /// <param name="binaryExpression">The binary expression.</param>
    /// <returns>The operator string.</returns>
    protected override string GetOperator(SqlBinaryExpression binaryExpression)
    {
        return binaryExpression.OperatorType switch
        {
            ExpressionType.Equal => " = ",
            ExpressionType.NotEqual => " <> ",
            ExpressionType.GreaterThan => " > ",
            ExpressionType.GreaterThanOrEqual => " >= ",
            ExpressionType.LessThan => " < ",
            ExpressionType.LessThanOrEqual => " <= ",
            ExpressionType.AndAlso => " AND ",
            ExpressionType.OrElse => " OR ",
            ExpressionType.Add => " + ",
            ExpressionType.Subtract => " - ",
            ExpressionType.Multiply => " * ",
            ExpressionType.Divide => " / ",
            ExpressionType.Modulo => " % ",
            _ => throw new InvalidOperationException($"Unsupported operator: {binaryExpression.OperatorType}")
        };
    }

    private static bool RequiresParentheses(SqlBinaryExpression parent, SqlExpression child)
    {
        if (child is SqlBinaryExpression childBinary)
        {
            var precedence = GetPrecedence(parent.OperatorType);
            var childPrecedence = GetPrecedence(childBinary.OperatorType);
            return precedence > childPrecedence;
        }

        return false;
    }

    private static int GetPrecedence(ExpressionType operatorType)
    {
        return operatorType switch
        {
            ExpressionType.OrElse => 0,
            ExpressionType.AndAlso => 1,
            ExpressionType.Equal or ExpressionType.NotEqual => 2,
            ExpressionType.GreaterThan or ExpressionType.GreaterThanOrEqual or
            ExpressionType.LessThan or ExpressionType.LessThanOrEqual => 3,
            ExpressionType.Add or ExpressionType.Subtract => 4,
            ExpressionType.Multiply or ExpressionType.Divide or ExpressionType.Modulo => 5,
            _ => 6
        };
    }
}
