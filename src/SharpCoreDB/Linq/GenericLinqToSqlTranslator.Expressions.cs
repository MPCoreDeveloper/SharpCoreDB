// <copyright file="GenericLinqToSqlTranslator.Expressions.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Linq;

using System.Linq.Expressions;

/// <summary>
/// GenericLinqToSqlTranslator - Expression visitor implementations.
/// Contains visitors for binary, unary, lambda, member, constant, and other expressions.
/// Part of the GenericLinqToSqlTranslator partial class.
/// Modern C# 14 with enhanced pattern matching.
/// See also: GenericLinqToSqlTranslator.Core.cs, GenericLinqToSqlTranslator.Queries.cs
/// </summary>
public sealed partial class GenericLinqToSqlTranslator<T> where T : class
{
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
    /// ✅ C# 14: Pattern matching with is.
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
        _sql.Append('(');  // ✅ C# 14: char literal for single character
        Visit(expression.Left);

        _sql.Append(' ');
        _sql.Append(GetOperator(expression.NodeType));
        _sql.Append(' ');

        Visit(expression.Right);
        _sql.Append(')');
    }

    /// <summary>
    /// Visits a unary expression (NOT, etc.).
    /// ✅ C# 14: is pattern matching.
    /// </summary>
    private void VisitUnary(UnaryExpression expression)
    {
        if (expression.NodeType is ExpressionType.Not)
        {
            _sql.Append("NOT (");
            Visit(expression.Operand);
            _sql.Append(')');
        }
        else
        {
            Visit(expression.Operand);
        }
    }

    /// <summary>
    /// Visits a NEW expression (anonymous types).
    /// ✅ C# 14: Collection expressions.
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
                if (expression.Members is not null && expression.Members.Count > i)  // ✅ C# 14: is not null
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
    /// ✅ C# 14: Property pattern matching.
    /// </summary>
    private void VisitMemberInit(MemberInitExpression expression)
    {
        // Similar to NEW but with property assignments
        var columns = new List<string>();
        foreach (var binding in expression.Bindings)
        {
            if (binding is MemberAssignment { Expression: MemberExpression member })  // ✅ C# 14: property pattern
            {
                var columnName = GetColumnName(member.Member.Name);
                columns.Add($"{columnName} AS {binding.Member.Name}");
            }
        }

        _sql.Append(string.Join(", ", columns));
    }
}
