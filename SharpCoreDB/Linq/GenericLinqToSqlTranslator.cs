// <copyright file="GenericLinqToSqlTranslator.cs" company="MPCoreDeveloper">
// Copyright (c) 2024-2025 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Linq;

using System.Linq.Expressions;
using System.Text;

/// <summary>
/// Generic LINQ-to-SQL translator with support for custom types and GROUP BY.
/// Translates LINQ Expression trees to SQL with type safety.
/// Integrates with MVCC and generic indexes for optimal performance.
/// </summary>
/// <typeparam name="T">The entity type being queried.</typeparam>
public sealed class GenericLinqToSqlTranslator<T> where T : class
{
    private readonly StringBuilder _sql = new();
    private readonly List<object?> _parameters = [];
    private readonly Dictionary<string, string> _propertyToColumnMap = [];
    private int _parameterIndex;

    /// <summary>
    /// Translates a LINQ expression to SQL.
    /// </summary>
    /// <param name="expression">The LINQ expression.</param>
    /// <returns>Tuple of (SQL query, parameters).</returns>
    public (string Sql, object?[] Parameters) Translate(Expression expression)
    {
        _sql.Clear();
        _parameters.Clear();
        _parameterIndex = 0;

        Visit(expression);

        return (_sql.ToString(), _parameters.ToArray());
    }

    /// <summary>
    /// Visits an expression node.
    /// </summary>
    private void Visit(Expression? expression)
    {
        if (expression == null)
            return;

        switch (expression.NodeType)
        {
            case ExpressionType.Call:
                VisitMethodCall((MethodCallExpression)expression);
                break;

            case ExpressionType.Lambda:
                VisitLambda((LambdaExpression)expression);
                break;

            case ExpressionType.MemberAccess:
                VisitMemberAccess((MemberExpression)expression);
                break;

            case ExpressionType.Constant:
                VisitConstant((ConstantExpression)expression);
                break;

            case ExpressionType.Equal:
            case ExpressionType.NotEqual:
            case ExpressionType.GreaterThan:
            case ExpressionType.GreaterThanOrEqual:
            case ExpressionType.LessThan:
            case ExpressionType.LessThanOrEqual:
            case ExpressionType.AndAlso:
            case ExpressionType.OrElse:
                VisitBinary((BinaryExpression)expression);
                break;

            case ExpressionType.Not:
                VisitUnary((UnaryExpression)expression);
                break;

            case ExpressionType.New:
                VisitNew((NewExpression)expression);
                break;

            case ExpressionType.MemberInit:
                VisitMemberInit((MemberInitExpression)expression);
                break;

            default:
                throw new NotSupportedException($"Expression type {expression.NodeType} is not supported");
        }
    }

    /// <summary>
    /// Visits a method call expression (Where, Select, GroupBy, OrderBy, etc.).
    /// </summary>
    private void VisitMethodCall(MethodCallExpression expression)
    {
        var methodName = expression.Method.Name;

        switch (methodName)
        {
            case "Where":
                VisitWhere(expression);
                break;

            case "Select":
                VisitSelect(expression);
                break;

            case "GroupBy":
                VisitGroupBy(expression);
                break;

            case "OrderBy":
            case "OrderByDescending":
                VisitOrderBy(expression, methodName == "OrderByDescending");
                break;

            case "Take":
                VisitTake(expression);
                break;

            case "Skip":
                VisitSkip(expression);
                break;

            case "First":
            case "FirstOrDefault":
            case "Single":
            case "SingleOrDefault":
                VisitSingleResult(expression);
                break;

            case "Count":
                VisitCount(expression);
                break;

            case "Any":
                VisitAny(expression);
                break;

            case "Sum":
            case "Average":
            case "Min":
            case "Max":
                VisitAggregate(expression, methodName);
                break;

            default:
                // If not a LINQ method, it might be a property method
                if (expression.Object != null)
                {
                    VisitPropertyMethod(expression);
                }
                else
                {
                    throw new NotSupportedException($"Method {methodName} is not supported");
                }
                break;
        }
    }

    /// <summary>
    /// Visits a WHERE clause.
    /// </summary>
    private void VisitWhere(MethodCallExpression expression)
    {
        // Visit source (could be another LINQ method)
        Visit(expression.Arguments[0]);

        // Add WHERE clause
        if (_sql.Length > 0 && !_sql.ToString().Contains("WHERE"))
        {
            _sql.Append(" WHERE ");
        }
        else if (_sql.ToString().Contains("WHERE"))
        {
            _sql.Append(" AND ");
        }

        // Visit predicate
        var lambda = (LambdaExpression)StripQuotes(expression.Arguments[1]);
        Visit(lambda.Body);
    }

    /// <summary>
    /// Visits a SELECT clause.
    /// </summary>
    private void VisitSelect(MethodCallExpression expression)
    {
        // Visit source
        Visit(expression.Arguments[0]);

        // For simple selects, we might just keep the existing SELECT *
        // For projections, we need to handle the selector
        var lambda = (LambdaExpression)StripQuotes(expression.Arguments[1]);
        
        // If it's a simple property access, we can optimize
        if (lambda.Body is MemberExpression memberExpr)
        {
            // Replace SELECT * with SELECT column
            var existingSql = _sql.ToString();
            if (existingSql.Contains("SELECT *"))
            {
                var columnName = GetColumnName(memberExpr.Member.Name);
                _sql.Clear();
                _sql.Append(existingSql.Replace("SELECT *", $"SELECT {columnName}"));
            }
        }
    }

    /// <summary>
    /// Visits a GROUP BY clause with generic type support.
    /// </summary>
    private void VisitGroupBy(MethodCallExpression expression)
    {
        // Visit source
        Visit(expression.Arguments[0]);

        _sql.Append(" GROUP BY ");

        // Visit key selector
        var keySelector = (LambdaExpression)StripQuotes(expression.Arguments[1]);
        
        // Handle different key types
        if (keySelector.Body is MemberExpression memberExpr)
        {
            // Single column grouping
            _sql.Append(GetColumnName(memberExpr.Member.Name));
        }
        else if (keySelector.Body is NewExpression newExpr)
        {
            // Multi-column grouping (anonymous type)
            var columns = new List<string>();
            foreach (var arg in newExpr.Arguments)
            {
                if (arg is MemberExpression member)
                {
                    columns.Add(GetColumnName(member.Member.Name));
                }
            }
            _sql.Append(string.Join(", ", columns));
        }
        else
        {
            throw new NotSupportedException($"GROUP BY expression type {keySelector.Body.NodeType} not supported");
        }

        // Handle element selector if present
        if (expression.Arguments.Count > 2)
        {
            // This would be for .GroupBy(x => x.Key, x => x.Value) scenarios
            // For now, we keep the existing SELECT clause
        }
    }

    /// <summary>
    /// Visits an ORDER BY clause.
    /// </summary>
    private void VisitOrderBy(MethodCallExpression expression, bool descending)
    {
        // Visit source
        Visit(expression.Arguments[0]);

        _sql.Append(" ORDER BY ");

        // Visit key selector
        var lambda = (LambdaExpression)StripQuotes(expression.Arguments[1]);
        Visit(lambda.Body);

        if (descending)
        {
            _sql.Append(" DESC");
        }
    }

    /// <summary>
    /// Visits a TAKE clause (LIMIT).
    /// </summary>
    private void VisitTake(MethodCallExpression expression)
    {
        // Visit source
        Visit(expression.Arguments[0]);

        // Add LIMIT
        var count = (ConstantExpression)expression.Arguments[1];
        _sql.Append($" LIMIT {count.Value}");
    }

    /// <summary>
    /// Visits a SKIP clause (OFFSET).
    /// </summary>
    private void VisitSkip(MethodCallExpression expression)
    {
        // Visit source
        Visit(expression.Arguments[0]);

        // Add OFFSET
        var count = (ConstantExpression)expression.Arguments[1];
        _sql.Append($" OFFSET {count.Value}");
    }

    /// <summary>
    /// Visits single result methods (First, Single, etc.).
    /// </summary>
    private void VisitSingleResult(MethodCallExpression expression)
    {
        // Visit source
        Visit(expression.Arguments[0]);

        // Add LIMIT 1 for optimization
        _sql.Append(" LIMIT 1");
    }

    /// <summary>
    /// Visits COUNT aggregation.
    /// </summary>
    private void VisitCount(MethodCallExpression expression)
    {
        // Start with SELECT COUNT(*)
        _sql.Append("SELECT COUNT(*) FROM ");
        _sql.Append(GetTableName());

        // If there's a predicate, add WHERE
        if (expression.Arguments.Count > 1)
        {
            _sql.Append(" WHERE ");
            var lambda = (LambdaExpression)StripQuotes(expression.Arguments[1]);
            Visit(lambda.Body);
        }
    }

    /// <summary>
    /// Visits ANY check.
    /// </summary>
    private void VisitAny(MethodCallExpression expression)
    {
        // Similar to Count, but returns EXISTS
        _sql.Append("SELECT EXISTS(SELECT 1 FROM ");
        _sql.Append(GetTableName());

        if (expression.Arguments.Count > 1)
        {
            _sql.Append(" WHERE ");
            var lambda = (LambdaExpression)StripQuotes(expression.Arguments[1]);
            Visit(lambda.Body);
        }

        _sql.Append(")");
    }

    /// <summary>
    /// Visits aggregate functions (Sum, Average, Min, Max).
    /// </summary>
    private void VisitAggregate(MethodCallExpression expression, string aggregateName)
    {
        _sql.Append($"SELECT {aggregateName.ToUpper()}(");
        
        // Get the selector
        if (expression.Arguments.Count > 1)
        {
            var lambda = (LambdaExpression)StripQuotes(expression.Arguments[1]);
            Visit(lambda.Body);
        }
        else
        {
            _sql.Append("*");
        }

        _sql.Append(") FROM ");
        _sql.Append(GetTableName());
    }

    /// <summary>
    /// Visits property method calls (like string.Contains, string.StartsWith).
    /// </summary>
    private void VisitPropertyMethod(MethodCallExpression expression)
    {
        var methodName = expression.Method.Name;

        if (expression.Object is MemberExpression member)
        {
            var columnName = GetColumnName(member.Member.Name);

            switch (methodName)
            {
                case "Contains":
                    _sql.Append($"{columnName} LIKE ");
                    var containsValue = GetExpressionValue(expression.Arguments[0]);
                    AddParameter($"%{containsValue}%");
                    break;

                case "StartsWith":
                    _sql.Append($"{columnName} LIKE ");
                    var startsValue = GetExpressionValue(expression.Arguments[0]);
                    AddParameter($"{startsValue}%");
                    break;

                case "EndsWith":
                    _sql.Append($"{columnName} LIKE ");
                    var endsValue = GetExpressionValue(expression.Arguments[0]);
                    AddParameter($"%{endsValue}");
                    break;

                default:
                    throw new NotSupportedException($"Property method {methodName} not supported");
            }
        }
    }

    /// <summary>
    /// Visits a lambda expression.
    /// </summary>
    private void VisitLambda(LambdaExpression expression)
    {
        Visit(expression.Body);
    }

    /// <summary>
    /// Visits member access (property access).
    /// </summary>
    private void VisitMemberAccess(MemberExpression expression)
    {
        var columnName = GetColumnName(expression.Member.Name);
        _sql.Append(columnName);
    }

    /// <summary>
    /// Visits a constant expression.
    /// </summary>
    private void VisitConstant(ConstantExpression expression)
    {
        if (expression.Value is IQueryable)
        {
            // This is the source table
            _sql.Append("SELECT * FROM ");
            _sql.Append(GetTableName());
        }
        else
        {
            // This is a parameter value
            AddParameter(expression.Value);
        }
    }

    /// <summary>
    /// Visits a binary expression (comparison, logical operators).
    /// </summary>
    private void VisitBinary(BinaryExpression expression)
    {
        _sql.Append("(");
        Visit(expression.Left);

        _sql.Append(" ");
        _sql.Append(GetOperator(expression.NodeType));
        _sql.Append(" ");

        Visit(expression.Right);
        _sql.Append(")");
    }

    /// <summary>
    /// Visits a unary expression (NOT, etc.).
    /// </summary>
    private void VisitUnary(UnaryExpression expression)
    {
        if (expression.NodeType == ExpressionType.Not)
        {
            _sql.Append("NOT (");
            Visit(expression.Operand);
            _sql.Append(")");
        }
        else
        {
            Visit(expression.Operand);
        }
    }

    /// <summary>
    /// Visits a NEW expression (anonymous types).
    /// </summary>
    private void VisitNew(NewExpression expression)
    {
        // For SELECT projections
        var columns = new List<string>();
        for (int i = 0; i < expression.Arguments.Count; i++)
        {
            if (expression.Arguments[i] is MemberExpression member)
            {
                var columnName = GetColumnName(member.Member.Name);
                if (expression.Members != null && expression.Members.Count > i)
                {
                    var alias = expression.Members[i].Name;
                    columns.Add($"{columnName} AS {alias}");
                }
                else
                {
                    columns.Add(columnName);
                }
            }
        }

        _sql.Append(string.Join(", ", columns));
    }

    /// <summary>
    /// Visits a member initialization expression.
    /// </summary>
    private void VisitMemberInit(MemberInitExpression expression)
    {
        // Similar to NEW but with property assignments
        var columns = new List<string>();
        foreach (var binding in expression.Bindings)
        {
            if (binding is MemberAssignment assignment &&
                assignment.Expression is MemberExpression member)
            {
                var columnName = GetColumnName(member.Member.Name);
                columns.Add($"{columnName} AS {binding.Member.Name}");
            }
        }

        _sql.Append(string.Join(", ", columns));
    }

    #region Helper Methods

    /// <summary>
    /// Gets the SQL operator for an expression type.
    /// </summary>
    private static string GetOperator(ExpressionType nodeType) => nodeType switch
    {
        ExpressionType.Equal => "=",
        ExpressionType.NotEqual => "!=",
        ExpressionType.GreaterThan => ">",
        ExpressionType.GreaterThanOrEqual => ">=",
        ExpressionType.LessThan => "<",
        ExpressionType.LessThanOrEqual => "<=",
        ExpressionType.AndAlso => "AND",
        ExpressionType.OrElse => "OR",
        _ => throw new NotSupportedException($"Operator {nodeType} not supported")
    };

    /// <summary>
    /// Strips quote expressions.
    /// </summary>
    private static Expression StripQuotes(Expression expression)
    {
        while (expression.NodeType == ExpressionType.Quote)
        {
            expression = ((UnaryExpression)expression).Operand;
        }
        return expression;
    }

    /// <summary>
    /// Gets the table name for type T.
    /// </summary>
    private static string GetTableName()
    {
        // Use plural form of type name as table name
        var typeName = typeof(T).Name;
        return typeName.EndsWith("s") ? typeName : $"{typeName}s";
    }

    /// <summary>
    /// Gets the column name for a property.
    /// </summary>
    private string GetColumnName(string propertyName)
    {
        // Check if we have a custom mapping
        if (_propertyToColumnMap.TryGetValue(propertyName, out var columnName))
        {
            return columnName;
        }

        // Default: use property name as column name
        return propertyName;
    }

    /// <summary>
    /// Adds a parameter and returns the placeholder.
    /// </summary>
    private void AddParameter(object? value)
    {
        _parameters.Add(value);
        _sql.Append($"@p{_parameterIndex++}");
    }

    /// <summary>
    /// Gets the value from an expression.
    /// </summary>
    private static object? GetExpressionValue(Expression expression)
    {
        if (expression is ConstantExpression constant)
        {
            return constant.Value;
        }

        // Compile and execute the expression
        var lambda = Expression.Lambda(expression);
        return lambda.Compile().DynamicInvoke();
    }

    /// <summary>
    /// Adds a custom property-to-column mapping.
    /// </summary>
    public void AddColumnMapping(string propertyName, string columnName)
    {
        _propertyToColumnMap[propertyName] = columnName;
    }

    #endregion
}
