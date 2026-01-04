// <copyright file="GenericLinqToSqlTranslator.Core.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Linq;

using System.Linq.Expressions;
using System.Text;

/// <summary>
/// GenericLinqToSqlTranslator - Core translation infrastructure.
/// Contains fields, main translation logic, and helper methods.
/// Part of the GenericLinqToSqlTranslator partial class.
/// Modern C# 14 with collection expressions and primary constructors.
/// See also: GenericLinqToSqlTranslator.Expressions.cs, GenericLinqToSqlTranslator.Queries.cs
/// </summary>
/// <typeparam name="T">The entity type being queried.</typeparam>
public sealed partial class GenericLinqToSqlTranslator<T> where T : class
{
    private readonly StringBuilder _sql = new();
    private readonly List<object?> _parameters = [];  // ✅ C# 14: collection expression
    private readonly Dictionary<string, string> _propertyToColumnMap = [];  // ✅ C# 14: collection expression
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

        return (_sql.ToString(), [.. _parameters]);  // ✅ C# 14: spread operator
    }

    /// <summary>
    /// Visits an expression node with pattern matching dispatch.
    /// ✅ C# 14: Enhanced pattern matching.
    /// </summary>
    private void Visit(Expression? expression)
    {
        if (expression is null)  // ✅ C# 14: is null pattern
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

    #region Helper Methods

    /// <summary>
    /// Gets the SQL operator for an expression type.
    /// ✅ C# 14: Switch expression.
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
        while (expression.NodeType is ExpressionType.Quote)  // ✅ C# 14: is pattern
        {
            expression = ((UnaryExpression)expression).Operand;
        }
        return expression;
    }

    /// <summary>
    /// Gets the table name for type T.
    /// ✅ C# 14: Simplified logic.
    /// </summary>
    private static string GetTableName()
    {
        // Use plural form of type name as table name
        var typeName = typeof(T).Name;
        return typeName.EndsWith('s') ? typeName : $"{typeName}s";
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
    /// ✅ C# 14: Pattern matching.
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
