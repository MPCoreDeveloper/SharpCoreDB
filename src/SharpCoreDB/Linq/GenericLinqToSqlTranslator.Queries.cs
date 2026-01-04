// <copyright file="GenericLinqToSqlTranslator.Queries.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Linq;

using System.Linq.Expressions;

/// <summary>
/// GenericLinqToSqlTranslator - Query method visitors.
/// Contains visitors for LINQ query methods (Where, Select, GroupBy, OrderBy, etc.).
/// Part of the GenericLinqToSqlTranslator partial class.
/// Modern C# 14 with switch expressions and pattern matching.
/// See also: GenericLinqToSqlTranslator.Core.cs, GenericLinqToSqlTranslator.Expressions.cs
/// </summary>
public sealed partial class GenericLinqToSqlTranslator<T> where T : class
{
    /// <summary>
    /// Visits a method call expression (Where, Select, GroupBy, OrderBy, etc.).
    /// ✅ C# 14: Switch expression pattern.
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
                VisitOrderBy(expression, methodName is "OrderByDescending");  // ✅ C# 14: is pattern
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
                if (expression.Object is not null)  // ✅ C# 14: is not null
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
        var sqlText = _sql.ToString();
        if (_sql.Length > 0 && !sqlText.Contains("WHERE"))
        {
            _sql.Append(" WHERE ");
        }
        else if (sqlText.Contains("WHERE"))
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
    /// ✅ C# 14: Enhanced pattern matching and collection expressions.
    /// </summary>
    private void VisitGroupBy(MethodCallExpression expression)
    {
        // Visit source
        Visit(expression.Arguments[0]);

        _sql.Append(" GROUP BY ");

        // Visit key selector
        var keySelector = (LambdaExpression)StripQuotes(expression.Arguments[1]);
        
        // Handle different key types using pattern matching
        switch (keySelector.Body)
        {
            case MemberExpression memberExpr:
                // Single column grouping
                _sql.Append(GetColumnName(memberExpr.Member.Name));
                break;

            case NewExpression newExpr:
                {
                    // Multi-column grouping (anonymous type)
                    // ✅ C# 14: LINQ with pattern matching
                    var columns = newExpr.Arguments
                        .OfType<MemberExpression>()
                        .Select(member => GetColumnName(member.Member.Name))
                        .ToList();
                    
                    _sql.Append(string.Join(", ", columns));
                    break;
                }

            default:
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
    /// ✅ C# 14: Pattern matching for constant.
    /// </summary>
    private void VisitTake(MethodCallExpression expression)
    {
        // Visit source
        Visit(expression.Arguments[0]);

        // Add LIMIT
        if (expression.Arguments[1] is ConstantExpression { Value: int count })  // ✅ C# 14: property pattern
        {
            _sql.Append($" LIMIT {count}");
        }
    }

    /// <summary>
    /// Visits a SKIP clause (OFFSET).
    /// ✅ C# 14: Pattern matching for constant.
    /// </summary>
    private void VisitSkip(MethodCallExpression expression)
    {
        // Visit source
        Visit(expression.Arguments[0]);

        // Add OFFSET
        if (expression.Arguments[1] is ConstantExpression { Value: int count })  // ✅ C# 14: property pattern
        {
            _sql.Append($" OFFSET {count}");
        }
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

        _sql.Append(')');
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
            _sql.Append('*');  // ✅ C# 14: char literal
        }

        _sql.Append(") FROM ");
        _sql.Append(GetTableName());
    }

    /// <summary>
    /// Visits property method calls (like string.Contains, string.StartsWith).
    /// ✅ C# 14: Switch expression for cleaner method dispatch.
    /// </summary>
    private void VisitPropertyMethod(MethodCallExpression expression)
    {
        var methodName = expression.Method.Name;

        if (expression.Object is not MemberExpression member)  // ✅ C# 14: is not pattern
            return;

        var columnName = GetColumnName(member.Member.Name);

        switch (methodName)
        {
            case "Contains":
                {
                    _sql.Append($"{columnName} LIKE ");
                    var containsValue = GetExpressionValue(expression.Arguments[0]);
                    AddParameter($"%{containsValue}%");
                    break;
                }

            case "StartsWith":
                {
                    _sql.Append($"{columnName} LIKE ");
                    var startsValue = GetExpressionValue(expression.Arguments[0]);
                    AddParameter($"{startsValue}%");
                    break;
                }

            case "EndsWith":
                {
                    _sql.Append($"{columnName} LIKE ");
                    var endsValue = GetExpressionValue(expression.Arguments[0]);
                    AddParameter($"%{endsValue}");
                    break;
                }

            default:
                throw new NotSupportedException($"Property method {methodName} not supported");
        }
    }
}
