// <copyright file="EnhancedSqlParser.Select.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Services;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

/// <summary>
/// SELECT statement parsing methods for EnhancedSqlParser.
/// Handles SELECT, FROM, JOIN, WHERE, GROUP BY, HAVING, ORDER BY, LIMIT/OFFSET.
/// </summary>
public partial class EnhancedSqlParser
{
    private SelectNode ParseSelect()
    {
        var node = new SelectNode { Position = _position };

        try
        {
            ConsumeKeyword(); // SELECT

            if (MatchKeyword("DISTINCT"))
                node.IsDistinct = true;

            // Parse columns
            node.Columns = ParseSelectColumns();

            // Parse FROM clause
            if (MatchKeyword("FROM"))
                node.From = ParseFrom();

            // Parse WHERE clause
            if (MatchKeyword("WHERE"))
                node.Where = ParseWhere();

            // Parse GROUP BY clause
            if (MatchKeyword("GROUP"))
            {
                if (!MatchKeyword("BY"))
                    RecordError("Expected BY after GROUP");
                else
                    node.GroupBy = ParseGroupBy();
            }

            // Parse HAVING clause
            if (MatchKeyword("HAVING"))
                node.Having = ParseHaving();

            // Parse ORDER BY clause
            if (MatchKeyword("ORDER"))
            {
                if (!MatchKeyword("BY"))
                    RecordError("Expected BY after ORDER");
                else
                    node.OrderBy = ParseOrderBy();
            }

            // Parse LIMIT/OFFSET clause
            if (MatchKeyword("LIMIT"))
            {
                node.Limit = ParseInteger();
                if (MatchKeyword("OFFSET"))
                    node.Offset = ParseInteger();
            }
        }
        catch (Exception ex)
        {
            RecordError($"Error parsing SELECT: {ex.Message}");
        }

        return node;
    }

    private List<ColumnNode> ParseSelectColumns()
    {
        List<ColumnNode> columns = [];

        try
        {
            do
            {
                var column = ParseColumn();
                if (column is not null)
                    columns.Add(column);
                else
                    break;
            } while (MatchToken(","));
        }
        catch (Exception ex)
        {
            RecordError($"Error parsing columns: {ex.Message}");
        }

        return columns;
    }

    private ColumnNode? ParseColumn()
    {
        var column = new ColumnNode { Position = _position };

        try
        {
            // Check for wildcard
            if (MatchToken("*"))
            {
                column.IsWildcard = true;
                return column;
            }

            // Check for aggregate function
            var funcMatch = Regex.Match(
                _sql.Substring(_position),
                @"^\s*(COUNT|SUM|AVG|MIN|MAX|STDDEV|STDDEV_SAMP|STDDEV_POP|VAR|VARIANCE|VAR_SAMP|VAR_POP|MEDIAN|PERCENTILE|MODE|CORR|CORRELATION|COVAR|COVARIANCE|COVAR_SAMP|COVAR_POP)\s*\(",
                RegexOptions.IgnoreCase);
            if (funcMatch.Success)
            {
                column.AggregateFunction = funcMatch.Groups[1].Value.ToUpperInvariant();
                _position += funcMatch.Length;

                if (MatchKeyword("DISTINCT"))
                {
                    // For now, treat COUNT(DISTINCT ...) as aggregate function
                }

                if (MatchToken("*"))
                {
                    column.Name = "*";
                }
                else
                {
                    var tableAlias = ConsumeIdentifier();
                    if (tableAlias != null && MatchToken("."))
                    {
                        var columnName = ConsumeIdentifier();
                        if (columnName != null)
                        {
                            column.TableAlias = tableAlias;
                            column.Name = columnName;
                        }
                        else
                        {
                            column.Name = tableAlias;
                        }
                    }
                    else
                    {
                        column.Name = tableAlias ?? "";
                    }
                }

                if (MatchToken(","))
                {
                    var literal = ParseLiteral();
                    if (literal?.Value is double doubleValue)
                    {
                        column.AggregateArgument = doubleValue;
                    }
                    else if (literal?.Value is int intValue)
                    {
                        column.AggregateArgument = intValue;
                    }
                    else
                    {
                        RecordError("Expected numeric literal for aggregate argument");
                    }
                }

                if (!MatchToken(")"))
                    RecordError("Expected ) after aggregate function");

                if (MatchKeyword("AS"))
                    column.Alias = ConsumeIdentifier();

                return column;
            }

            // Parse table.column or column
            var identifier = ConsumeIdentifier();
            if (identifier is null)
                return null;

            if (MatchToken("."))
            {
                column.TableAlias = identifier;
                column.Name = ConsumeIdentifier() ?? "";

                if (column.Name == "*")
                    column.IsWildcard = true;
            }
            else
            {
                column.Name = identifier;
            }

            // Parse alias
            if (MatchKeyword("AS"))
                column.Alias = ConsumeIdentifier();
        }
        catch (Exception ex)
        {
            RecordError($"Error parsing column: {ex.Message}");
        }

        return column;
    }

    private FromNode ParseFrom()
    {
        var node = new FromNode { Position = _position };

        try
        {
            // Check for subquery
            if (MatchToken("("))
            {
                node.Subquery = ParseSelect();
                if (!MatchToken(")"))
                    RecordError("Expected ) after subquery");
            }
            else
            {
                var tableName = ConsumeIdentifier();
                if (tableName is null)
                {
                    RecordError("Expected table name after FROM");
                    node.TableName = "";
                }
                else
                {
                    node.TableName = tableName;
                }
            }

            // Parse alias
            if (MatchKeyword("AS"))
                node.Alias = ConsumeIdentifier();
            else
            {
                // Implicit alias
                var nextKeyword = PeekKeyword();
                if (nextKeyword is not null && !IsReservedKeyword(nextKeyword))
                    node.Alias = ConsumeIdentifier();
            }

            // Parse JOINs
            while (true)
            {
                var joinType = ParseJoinType();
                if (joinType is null)
                    break;

                var join = ParseJoin(joinType.Value);
                if (join is not null)
                    node.Joins.Add(join);
            }
        }
        catch (Exception ex)
        {
            RecordError($"Error parsing FROM: {ex.Message}");
        }

        return node;
    }

    private JoinNode.JoinType? ParseJoinType()
    {
        if (MatchKeyword("CROSS"))
        {
            if (!MatchKeyword("JOIN"))
                RecordError("Expected JOIN after CROSS");
            return JoinNode.JoinType.Cross;
        }

        if (MatchKeyword("INNER"))
        {
            if (!MatchKeyword("JOIN"))
                RecordError("Expected JOIN after INNER");
            return JoinNode.JoinType.Inner;
        }

        if (MatchKeyword("LEFT"))
        {
            MatchKeyword("OUTER"); // Optional
            if (!MatchKeyword("JOIN"))
                RecordError("Expected JOIN after LEFT [OUTER]");
            return JoinNode.JoinType.Left;
        }

        if (MatchKeyword("RIGHT"))
        {
            MatchKeyword("OUTER"); // Optional
            if (!MatchKeyword("JOIN"))
                RecordError("Expected JOIN after RIGHT [OUTER]");
            return JoinNode.JoinType.Right;
        }

        if (MatchKeyword("FULL"))
        {
            MatchKeyword("OUTER"); // Optional
            if (!MatchKeyword("JOIN"))
                RecordError("Expected JOIN after FULL [OUTER]");
            return JoinNode.JoinType.Full;
        }

        if (MatchKeyword("JOIN"))
        {
            return JoinNode.JoinType.Inner;
        }

        return null;
    }

    private JoinNode? ParseJoin(JoinNode.JoinType joinType)
    {
        var node = new JoinNode { Position = _position, Type = joinType };

        try
        {
            node.Table = ParseFrom();

            if (joinType != JoinNode.JoinType.Cross && MatchKeyword("ON"))
            {
                node.OnCondition = ParseExpression();
            }
        }
        catch (Exception ex)
        {
            RecordError($"Error parsing JOIN: {ex.Message}");
        }

        return node;
    }

    private WhereNode ParseWhere()
    {
        var node = new WhereNode { Position = _position };

        try
        {
            node.Condition = ParseExpression();
        }
        catch (Exception ex)
        {
            RecordError($"Error parsing WHERE: {ex.Message}");
        }

        return node;
    }

    private GroupByNode ParseGroupBy()
    {
        var node = new GroupByNode { Position = _position };

        try
        {
            do
            {
                var tableAlias = ConsumeIdentifier();
                if (tableAlias is not null && MatchToken("."))
                {
                    var columnName = ConsumeIdentifier() ?? "";
                    node.Columns.Add(new ColumnReferenceNode { TableAlias = tableAlias, ColumnName = columnName });
                }
                else if (tableAlias is not null)
                {
                    node.Columns.Add(new ColumnReferenceNode { ColumnName = tableAlias });
                }
            } while (MatchToken(","));
        }
        catch (Exception ex)
        {
            RecordError($"Error parsing GROUP BY: {ex.Message}");
        }

        return node;
    }

    private HavingNode ParseHaving()
    {
        var node = new HavingNode { Position = _position };

        try
        {
            node.Condition = ParseExpression();
        }
        catch (Exception ex)
        {
            RecordError($"Error parsing HAVING: {ex.Message}");
        }

        return node;
    }

    private OrderByNode ParseOrderBy()
    {
        var node = new OrderByNode { Position = _position };

        try
        {
            do
            {
                var tableAlias = ConsumeIdentifier();
                if (tableAlias is null)
                    break;

                string columnName;
                if (MatchToken("."))
                {
                    columnName = ConsumeIdentifier() ?? "";
                }
                else
                {
                    columnName = tableAlias;
                    tableAlias = null;
                }

                var item = new OrderByItem
                {
                    Column = new ColumnReferenceNode { TableAlias = tableAlias, ColumnName = columnName },
                    IsAscending = !MatchKeyword("DESC")
                };

                MatchKeyword("ASC"); // Optional
                node.Items.Add(item);
            } while (MatchToken(","));
        }
        catch (Exception ex)
        {
            RecordError($"Error parsing ORDER BY: {ex.Message}");
        }

        return node;
    }
}
