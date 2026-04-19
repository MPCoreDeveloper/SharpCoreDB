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
    /// <summary>
    /// Parses a SELECT statement and checks for a trailing set operation (UNION / UNION ALL / INTERSECT / EXCEPT).
    /// If found, wraps both arms into a <see cref="SetOperationNode"/>.
    /// </summary>
    private SqlNode ParseSelectOrSetOperation()
    {
        var left = ParseSelect();

        var setOp = TryParseSetOperationType();
        if (setOp is null)
        {
            return left;
        }

        var right = ParseSelect(stopBeforeOrderBy: true);

        var node = new SetOperationNode
        {
            Position = _position,
            Left = left,
            Right = right,
            Operation = setOp.Value,
        };

        // Outer ORDER BY applies to the combined result
        if (MatchKeyword("ORDER"))
        {
            if (!MatchKeyword("BY"))
                RecordError("Expected BY after ORDER");
            else
                node.OrderBy = ParseOrderBy();
        }

        // Outer LIMIT/OFFSET applies to the combined result
        if (MatchKeyword("LIMIT"))
        {
            node.Limit = ParseInteger();
            if (MatchKeyword("OFFSET"))
                node.Offset = ParseInteger();
        }

        return node;
    }

    /// <summary>
    /// Attempts to consume a set operation keyword. Returns null if not found.
    /// </summary>
    private SetOperationType? TryParseSetOperationType()
    {
        if (MatchKeyword("UNION"))
        {
            return MatchKeyword("ALL") ? SetOperationType.UnionAll : SetOperationType.Union;
        }

        if (MatchKeyword("INTERSECT"))
        {
            return SetOperationType.Intersect;
        }

        if (MatchKeyword("EXCEPT"))
        {
            return SetOperationType.Except;
        }

        return null;
    }

    /// <summary>
    /// Parses WITH [RECURSIVE] cte_name [(col1, col2, ...)] AS (anchor UNION ALL recursive) outer_query.
    /// </summary>
    private SqlNode ParseWithCte()
    {
        ConsumeKeyword(); // WITH
        bool isRecursive = MatchKeyword("RECURSIVE");

        var cteName = ConsumeIdentifier() ?? "";

        // Optional column list
        List<string> cteColumns = [];
        if (MatchToken("("))
        {
            do
            {
                var col = ConsumeIdentifier();
                if (col is not null)
                    cteColumns.Add(col);
            } while (MatchToken(","));
            if (!MatchToken(")"))
                RecordError("Expected ) after CTE column list");
        }

        if (!MatchKeyword("AS"))
            RecordError("Expected AS after CTE name");

        if (!MatchToken("("))
            RecordError("Expected ( after AS");

        // Parse anchor SELECT
        var anchor = ParseSelect(stopBeforeOrderBy: true);
        SelectNode? recursive = null;

        // Check for UNION ALL (recursive arm)
        if (MatchKeyword("UNION"))
        {
            MatchKeyword("ALL"); // Optional ALL
            recursive = ParseSelect(stopBeforeOrderBy: true);
        }

        if (!MatchToken(")"))
            RecordError("Expected ) after CTE definition");

        // Parse outer query
        var outer = ParseSelectOrSetOperation();

        return new WithRecursiveNode
        {
            Position = _position,
            CteName = cteName,
            ColumnNames = cteColumns,
            IsRecursive = isRecursive,
            AnchorSelect = anchor,
            RecursiveSelect = recursive,
            OuterQuery = outer,
        };
    }

    /// <summary>
    /// Parses EXPLAIN QUERY PLAN SELECT ...
    /// </summary>
    private SqlNode ParseExplainQueryPlan()
    {
        ConsumeKeyword(); // EXPLAIN
        MatchKeyword("QUERY");
        MatchKeyword("PLAN");

        var inner = ParseSelectOrSetOperation();
        return new ExplainQueryPlanNode
        {
            Position = _position,
            InnerStatement = inner,
        };
    }

    /// <summary>
    /// Parses BEGIN [DEFERRED|IMMEDIATE|EXCLUSIVE] [TRANSACTION].
    /// </summary>
    private SqlNode ParseBeginTransaction()
    {
        ConsumeKeyword(); // BEGIN
        var mode = "DEFERRED";

        var next = PeekKeyword()?.ToUpperInvariant();
        if (next is "IMMEDIATE" or "EXCLUSIVE" or "DEFERRED")
        {
            mode = next;
            ConsumeKeyword();
        }

        MatchKeyword("TRANSACTION"); // optional

        return new BeginTransactionNode
        {
            Position = _position,
            Mode = mode,
        };
    }

    /// <summary>
    /// Parses SAVEPOINT name.
    /// </summary>
    private SqlNode ParseSavepointStatement()
    {
        ConsumeKeyword(); // SAVEPOINT
        var name = ConsumeIdentifier() ?? "";
        return new SavepointNode
        {
            Position = _position,
            Name = name,
            Action = SavepointAction.Save,
        };
    }

    /// <summary>
    /// Parses RELEASE [SAVEPOINT] name.
    /// </summary>
    private SqlNode ParseReleaseStatement()
    {
        ConsumeKeyword(); // RELEASE
        MatchKeyword("SAVEPOINT"); // optional
        var name = ConsumeIdentifier() ?? "";
        return new SavepointNode
        {
            Position = _position,
            Name = name,
            Action = SavepointAction.Release,
        };
    }

    /// <summary>
    /// Parses ROLLBACK [TO [SAVEPOINT] name].
    /// </summary>
    private SqlNode ParseRollbackStatement()
    {
        ConsumeKeyword(); // ROLLBACK
        if (MatchKeyword("TO"))
        {
            MatchKeyword("SAVEPOINT"); // optional
            var name = ConsumeIdentifier() ?? "";
            return new SavepointNode
            {
                Position = _position,
                Name = name,
                Action = SavepointAction.RollbackTo,
            };
        }

        // Plain ROLLBACK (no savepoint)
        return new BeginTransactionNode
        {
            Position = _position,
            Mode = "ROLLBACK",
        };
    }

    private SelectNode ParseSelect(bool stopBeforeOrderBy = false)
    {
        var node = new SelectNode { Position = _position };

        try
        {
            ConsumeKeyword(); // SELECT

            if (MatchKeyword("DISTINCT"))
                node.IsDistinct = true;

            // Parse columns
            node.Columns = ParseSelectColumns();

            // Parse optional OPTIONALLY keyword after select list
            if (MatchKeyword("OPTIONALLY"))
                node.IsOptionalProjection = true;

            // Parse FROM clause
            if (MatchKeyword("FROM"))
                node.From = ParseFrom();

            // Parse optional top-level GRAPH_RAG clause
            if (MatchKeyword("GRAPH_RAG"))
                node.GraphRag = ParseGraphRagClause();

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

            if (!stopBeforeOrderBy)
            {
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
        }
        catch (Exception ex)
        {
            RecordError($"Error parsing SELECT: {ex.Message}");
        }

        return node;
    }

    private GraphRagClauseNode ParseGraphRagClause()
    {
        var clause = new GraphRagClauseNode { Position = _position };

        string? question = TryConsumeGraphRagQuestionLiteral();
        if (string.IsNullOrWhiteSpace(question))
        {
            RecordError("GRAPH_RAG requires a non-empty string question literal");
            return clause;
        }

        clause.Question = question;

        bool continueParsing = true;
        while (continueParsing)
        {
            if (MatchKeyword("LIMIT"))
            {
                var limit = ParseInteger();
                if (limit is null || limit <= 0)
                    RecordError("GRAPH_RAG LIMIT must be a positive integer");
                else
                    clause.Limit = limit;

                continue;
            }

            if (MatchKeyword("TOP_K"))
            {
                var topK = ParseInteger();
                if (topK is null || topK <= 0)
                    RecordError("GRAPH_RAG TOP_K must be a positive integer");
                else
                    clause.TopK = topK;

                continue;
            }

            if (MatchKeyword("WITH"))
            {
                if (MatchKeyword("CONTEXT"))
                {
                    clause.IncludeContext = true;
                    continue;
                }

                if (MatchKeyword("SCORE"))
                {
                    if (!MatchToken(">"))
                    {
                        RecordError("GRAPH_RAG WITH SCORE requires '>' comparator");
                        continue;
                    }

                    var scoreLiteral = ParseLiteral();
                    if (scoreLiteral?.Value is int i)
                    {
                        clause.MinScore = i;
                    }
                    else if (scoreLiteral?.Value is double d)
                    {
                        clause.MinScore = d;
                    }
                    else
                    {
                        RecordError("GRAPH_RAG WITH SCORE requires numeric literal");
                    }

                    if (clause.MinScore is < 0 or > 1)
                    {
                        RecordError("GRAPH_RAG WITH SCORE must be between 0 and 1");
                    }

                    continue;
                }

                RecordError("GRAPH_RAG WITH supports only SCORE > <number> or CONTEXT");
                continue;
            }

            continueParsing = false;
        }

        return clause;
    }

    private string? TryConsumeGraphRagQuestionLiteral()
    {
        var sqlSlice = _sql.Substring(_position);

        // Standard SQL string literal: '...'
        var standard = Regex.Match(sqlSlice, @"^\s*'([^']*(?:''[^']*)*)'", RegexOptions.IgnoreCase);
        if (standard.Success)
        {
            _position += standard.Length;
            return standard.Groups[1].Value.Replace("''", "'", StringComparison.Ordinal);
        }

        // Sanitized form produced by SQL sanitizer: ''...''
        var doubled = Regex.Match(sqlSlice, @"^\s*''([^']*(?:''[^']*)*)''", RegexOptions.IgnoreCase);
        if (doubled.Success)
        {
            _position += doubled.Length;
            return doubled.Groups[1].Value.Replace("''", "'", StringComparison.Ordinal);
        }

        return null;
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

            // Scalar MIN/MAX with 2+ arguments — parse as function expression before aggregate check
            var scalarMinMaxCheck = Regex.Match(
                _sql.Substring(_position),
                @"^\s*(MIN|MAX)\s*\(\s*[a-zA-Z_]\w*\s*,",
                RegexOptions.IgnoreCase);
            if (scalarMinMaxCheck.Success)
            {
                var funcExpr = ParseFunctionCall();
                column.Expression = funcExpr;
                column.Name = funcExpr.FunctionName;

                if (MatchKeyword("AS"))
                    column.Alias = ConsumeIdentifier();

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

            // Check for window function
            var windowFuncMatch = Regex.Match(
                _sql.Substring(_position),
                @"^\s*(ROW_NUMBER|RANK|DENSE_RANK|LAG|LEAD|FIRST_VALUE|LAST_VALUE|NTILE|PERCENT_RANK|CUME_DIST)\s*\(",
                RegexOptions.IgnoreCase);
            if (windowFuncMatch.Success)
            {
                column.WindowFunction = windowFuncMatch.Groups[1].Value.ToUpperInvariant();
                _position += windowFuncMatch.Length;

                // Parse function arguments
                if (column.WindowFunction is "LAG" or "LEAD")
                {
                    // LAG/LEAD take column and optional offset
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

                    if (MatchToken(","))
                    {
                        var offsetLiteral = ParseLiteral();
                        if (offsetLiteral?.Value is int offset)
                        {
                            column.AggregateArgument = offset;
                        }
                        else
                        {
                            RecordError("Expected integer offset for LAG/LEAD");
                        }
                    }
                }
                else if (column.WindowFunction is "NTILE")
                {
                    // NTILE takes an integer bucket count
                    var literal = ParseLiteral();
                    if (literal?.Value is int n)
                    {
                        column.AggregateArgument = n;
                    }
                    else
                    {
                        RecordError("Expected integer argument for NTILE");
                    }
                    column.Name = "";
                }
                else if (column.WindowFunction is "PERCENT_RANK" or "CUME_DIST")
                {
                    // No arguments
                    column.Name = "";
                }
                else
                {
                    // Other window functions take a column
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

                if (!MatchToken(")"))
                    RecordError("Expected ) after window function");

                // Parse OVER clause
                if (!MatchKeyword("OVER"))
                    RecordError("Expected OVER clause for window function");

                if (!MatchToken("("))
                    RecordError("Expected ( after OVER");

                // Parse PARTITION BY
                if (MatchKeyword("PARTITION"))
                {
                    if (!MatchKeyword("BY"))
                        RecordError("Expected BY after PARTITION");

                    column.WindowPartitionBy = [];
                    do
                    {
                        var partCol = ConsumeIdentifier();
                        if (partCol != null)
                        {
                            column.WindowPartitionBy.Add(partCol);
                        }
                    } while (MatchToken(","));
                }

                // Parse ORDER BY
                if (MatchKeyword("ORDER"))
                {
                    if (!MatchKeyword("BY"))
                        RecordError("Expected BY after ORDER");

                    column.WindowOrderBy = [];
                    do
                    {
                        var orderCol = ConsumeIdentifier();
                        if (orderCol != null)
                        {
                            var isDescending = MatchKeyword("DESC");
                            if (!isDescending && !MatchKeyword("ASC"))
                            {
                                // Default to ASC
                            }
                            column.WindowOrderBy.Add(new OrderByItem
                            {
                                Column = new ColumnReferenceNode { ColumnName = orderCol },
                                IsAscending = !isDescending
                            });
                        }
                    } while (MatchToken(","));
                }

                // Parse optional FRAME clause (ROWS BETWEEN ... AND ... / RANGE BETWEEN ... AND ...)
                if (MatchKeyword("ROWS") || MatchKeyword("RANGE"))
                {
                    var frameType = _sql.Substring(_position - 5, 5).Trim().ToUpperInvariant().StartsWith("RANG") ? "RANGE" : "ROWS";
                    var frameParts = new System.Text.StringBuilder(frameType);
                    frameParts.Append(' ');

                    if (MatchKeyword("BETWEEN"))
                    {
                        frameParts.Append("BETWEEN ");
                        // Parse start bound
                        frameParts.Append(ConsumeFrameBound());
                        frameParts.Append(' ');
                        if (MatchKeyword("AND"))
                        {
                            frameParts.Append("AND ");
                            frameParts.Append(ConsumeFrameBound());
                        }
                    }
                    else
                    {
                        // Single bound like ROWS UNBOUNDED PRECEDING
                        frameParts.Append(ConsumeFrameBound());
                    }
                    column.WindowFrame = frameParts.ToString();
                }

                if (!MatchToken(")"))
                    RecordError("Expected ) after OVER clause");

                // Parse optional FILTER clause
                if (MatchKeyword("FILTER"))
                {
                    if (!MatchToken("("))
                        RecordError("Expected ( after FILTER");

                    if (!MatchKeyword("WHERE"))
                        RecordError("FILTER clause must use WHERE");

                    column.WindowFilter = ParseExpression();

                    if (!MatchToken(")"))
                        RecordError("Expected ) after FILTER clause");
                }

                if (MatchKeyword("AS"))
                    column.Alias = ConsumeIdentifier();

                return column;
            }

            // Parse table.column or column
            // First check for parenthesized expression (scalar subquery or grouped expression)
            var remaining = _sql.Substring(_position);
            if (remaining.TrimStart().StartsWith('('))
            {
                var expr = ParsePrimaryExpression();
                column.Expression = expr;
                column.Name = expr is SubqueryExpressionNode ? "(subquery)" : "(expr)";

                if (MatchKeyword("AS"))
                    column.Alias = ConsumeIdentifier();

                return column;
            }

            // Check for scalar function calls (COALESCE, IIF, NULLIF, etc.)
            var scalarFuncMatch = Regex.Match(
                remaining,
                @"^\s*(\w+)\s*\(",
                RegexOptions.IgnoreCase);
            if (scalarFuncMatch.Success)
            {
                // This is a scalar function — parse as expression
                var funcExpr = ParseFunctionCall();
                column.Expression = funcExpr;
                column.Name = funcExpr.FunctionName;

                if (MatchKeyword("AS"))
                    column.Alias = ConsumeIdentifier();

                return column;
            }

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
                // Peek ahead to check if it's a numeric column position
                var numMatch = Regex.Match(_sql[_position..], @"^\s*(\d+)", RegexOptions.None);
                if (numMatch.Success && int.TryParse(numMatch.Groups[1].Value, out var ordinal))
                {
                    _position += numMatch.Length;
                    var item = new OrderByItem
                    {
                        Column = new ColumnReferenceNode { ColumnName = ordinal.ToString() },
                        OrdinalPosition = ordinal,
                        IsAscending = !MatchKeyword("DESC")
                    };
                    MatchKeyword("ASC");
                    node.Items.Add(item);
                }
                else
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

                    MatchKeyword("ASC");
                    node.Items.Add(item);
                }
            } while (MatchToken(","));
        }
        catch (Exception ex)
        {
            RecordError($"Error parsing ORDER BY: {ex.Message}");
        }

        return node;
    }
}
