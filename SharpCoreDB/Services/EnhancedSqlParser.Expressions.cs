// <copyright file="EnhancedSqlParser.Expressions.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Services;

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

/// <summary>
/// Expression, literal, and operator parsing methods for EnhancedSqlParser.
/// Handles complex expressions, literals, operators, function calls, and IN clauses.
/// </summary>
public partial class EnhancedSqlParser
{
    private ExpressionNode ParseExpression()
    {
        return ParseOrExpression();
    }

    private ExpressionNode ParseOrExpression()
    {
        var left = ParseAndExpression();

        while (MatchKeyword("OR"))
        {
            var right = ParseAndExpression();
            left = new BinaryExpressionNode
            {
                Position = _position,
                Left = left,
                Operator = "OR",
                Right = right
            };
        }

        return left;
    }

    private ExpressionNode ParseAndExpression()
    {
        var left = ParseComparisonExpression();

        while (MatchKeyword("AND"))
        {
            var right = ParseComparisonExpression();
            left = new BinaryExpressionNode
            {
                Position = _position,
                Left = left,
                Operator = "AND",
                Right = right
            };
        }

        return left;
    }

    private ExpressionNode ParseComparisonExpression()
    {
        var left = ParsePrimaryExpression();

        // Check for IN expression
        if (MatchKeyword("IN"))
        {
            return ParseInExpression(left);
        }

        // S1066 fix: merge nested if
        if (MatchKeyword("NOT") && MatchKeyword("IN"))
        {
            return ParseInExpression(left, isNot: true);
        }

        // Check for comparison operators
        var op = ParseComparisonOperator();
        if (op != null)
        {
            var right = ParsePrimaryExpression();
            return new BinaryExpressionNode
            {
                Position = _position,
                Left = left,
                Operator = op,
                Right = right
            };
        }

        return left;
    }

    private string? ParseComparisonOperator()
    {
        var match = Regex.Match(_sql.Substring(_position), @"^\s*(<=|>=|<>|!=|=|<|>|LIKE|NOT\s+LIKE)", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            _position += match.Length;
            return match.Groups[1].Value.ToUpperInvariant();
        }
        return null;
    }

    private InExpressionNode ParseInExpression(ExpressionNode expression, bool isNot = false)
    {
        var node = new InExpressionNode
        {
            Position = _position,
            Expression = expression,
            IsNot = isNot
        };

        if (!MatchToken("("))
        {
            RecordError("Expected ( after IN");
            return node;
        }

        // Check for subquery
        if (PeekKeyword()?.ToUpperInvariant() == "SELECT")
        {
            node.Subquery = ParseSelect();
        }
        else
        {
            // Parse value list
            do
            {
                var value = ParsePrimaryExpression();
                node.Values.Add(value);
            } while (MatchToken(","));
        }

        if (!MatchToken(")"))
            RecordError("Expected ) after IN values");

        return node;
    }

    private ExpressionNode ParsePrimaryExpression()
    {
        // Check for literal
        var literal = ParseLiteral();
        if (literal is not null)
            return literal;

        // Check for function call
        var funcMatch = Regex.Match(_sql.Substring(_position), @"^\s*(\w+)\s*\(", RegexOptions.IgnoreCase);
        if (funcMatch.Success)
        {
            return ParseFunctionCall();
        }

        // Check for column reference
        var identifier = ConsumeIdentifier();
        if (identifier is not null)
        {
            if (MatchToken("."))
            {
                var column = ConsumeIdentifier() ?? "";
                return new ColumnReferenceNode
                {
                    Position = _position,
                    TableAlias = identifier,
                    ColumnName = column
                };
            }
            return new ColumnReferenceNode
            {
                Position = _position,
                ColumnName = identifier
            };
        }

        // Check for parenthesized expression
        if (MatchToken("("))
        {
            var expr = ParseExpression();
            if (!MatchToken(")"))
                RecordError("Expected ) after expression");
            return expr;
        }

        RecordError("Expected expression");
        return new LiteralNode { Position = _position, Value = null };
    }

    private LiteralNode? ParseLiteral()
    {
        // String literal
        var stringMatch = Regex.Match(_sql.Substring(_position), @"^\s*'([^']*(?:''[^']*)*)'", RegexOptions.IgnoreCase);
        if (stringMatch.Success)
        {
            _position += stringMatch.Length;
            var value = stringMatch.Groups[1].Value.Replace("''", "'");
            return new LiteralNode { Position = _position, Value = value };
        }

        // Numeric literal
        var numMatch = Regex.Match(_sql.Substring(_position), @"^\s*(\d+(?:\.\d+)?)", RegexOptions.IgnoreCase);
        if (numMatch.Success)
        {
            _position += numMatch.Length;
            var value = numMatch.Groups[1].Value;
            if (value.Contains('.'))
                return new LiteralNode { Position = _position, Value = double.Parse(value) };
            return new LiteralNode { Position = _position, Value = int.Parse(value) };
        }

        // NULL
        if (MatchKeyword("NULL"))
            return new LiteralNode { Position = _position, Value = null };

        // Boolean
        if (MatchKeyword("TRUE"))
            return new LiteralNode { Position = _position, Value = true };

        if (MatchKeyword("FALSE"))
            return new LiteralNode { Position = _position, Value = false };

        return null;
    }

    private FunctionCallNode ParseFunctionCall()
    {
        var node = new FunctionCallNode { Position = _position };

        try
        {
            node.FunctionName = ConsumeIdentifier() ?? "";

            if (!MatchToken("("))
            {
                RecordError("Expected ( after function name");
                return node;
            }

            if (MatchKeyword("DISTINCT"))
                node.IsDistinct = true;

            if (!MatchToken(")"))
            {
                do
                {
                    var arg = ParseExpression();
                    node.Arguments.Add(arg);
                } while (MatchToken(","));

                if (!MatchToken(")"))
                    RecordError("Expected ) after function arguments");
            }
        }
        catch (Exception ex)
        {
            RecordError($"Error parsing function call: {ex.Message}");
        }

        return node;
    }
}
