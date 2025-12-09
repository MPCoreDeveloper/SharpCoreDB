// <copyright file="EnhancedSqlParser.cs" company="MPCoreDeveloper">
// Copyright (c) 2024-2025 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Services;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

/// <summary>
/// Enhanced SQL parser with support for complex queries, multiple dialects, and error recovery.
/// </summary>
public class EnhancedSqlParser
{
    private readonly ISqlDialect _dialect;
    private readonly List<string> _errors = new();
    private string _sql = string.Empty;
    private int _position;

    /// <summary>
    /// Initializes a new instance of the <see cref="EnhancedSqlParser"/> class.
    /// </summary>
    /// <param name="dialect">The SQL dialect to use.</param>
    public EnhancedSqlParser(ISqlDialect? dialect = null)
    {
        _dialect = dialect ?? SqlDialectFactory.Default;
    }

    /// <summary>
    /// Gets the list of parsing errors.
    /// </summary>
    public IReadOnlyList<string> Errors => _errors;

    /// <summary>
    /// Gets whether any errors were encountered during parsing.
    /// </summary>
    public bool HasErrors => _errors.Count > 0;

    /// <summary>
    /// Parses a SQL statement into an AST.
    /// </summary>
    /// <param name="sql">The SQL statement to parse.</param>
    /// <returns>The root AST node, or null if parsing failed critically.</returns>
    public SqlNode? Parse(string sql)
    {
        _errors.Clear();
        _sql = sql;
        _position = 0;

        try
        {
            var keyword = PeekKeyword();

            return keyword?.ToUpperInvariant() switch
            {
                "SELECT" => ParseSelect(),
                "INSERT" => ParseInsert(),
                "UPDATE" => ParseUpdate(),
                "DELETE" => ParseDelete(),
                "CREATE" => ParseCreate(),
                _ => throw new InvalidOperationException($"Unsupported statement type: {keyword}")
            };
        }
        catch (Exception ex)
        {
            RecordError($"Critical parsing error: {ex.Message}");
            return null;
        }
    }

    private void RecordError(string message)
    {
        _errors.Add($"[Position {_position}] {message}");
    }

    private string? PeekKeyword()
    {
        var match = Regex.Match(_sql.Substring(_position), @"^\s*(\w+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : null;
    }

    private string? ConsumeKeyword()
    {
        var match = Regex.Match(_sql.Substring(_position), @"^\s*(\w+)", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            _position += match.Length;
            return match.Groups[1].Value;
        }
        return null;
    }

    private bool MatchKeyword(string keyword)
    {
        var match = Regex.Match(_sql.Substring(_position), @"^\s*(" + keyword + @")\b", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            _position += match.Length;
            return true;
        }
        return false;
    }

    private string? ConsumeIdentifier()
    {
        var match = Regex.Match(_sql.Substring(_position), @"^\s*([\w]+|""[^""]+""|\[[^\]]+\]|`[^`]+`)", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            _position += match.Length;
            var identifier = match.Groups[1].Value;
            // Remove quotes
            if ((identifier.StartsWith("\"") && identifier.EndsWith("\"")) ||
                (identifier.StartsWith("[") && identifier.EndsWith("]")) ||
                (identifier.StartsWith("`") && identifier.EndsWith("`")))
            {
                identifier = identifier.Substring(1, identifier.Length - 2);
            }
            return identifier;
        }
        return null;
    }

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
        var columns = new List<ColumnNode>();

        try
        {
            do
            {
                var column = ParseColumn();
                if (column != null)
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
            var funcMatch = Regex.Match(_sql.Substring(_position), @"^\s*(COUNT|SUM|AVG|MIN|MAX)\s*\(", RegexOptions.IgnoreCase);
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
                    column.Name = ConsumeIdentifier() ?? "";
                }

                if (!MatchToken(")"))
                    RecordError("Expected ) after aggregate function");

                if (MatchKeyword("AS"))
                    column.Alias = ConsumeIdentifier();

                return column;
            }

            // Parse table.column or column
            var identifier = ConsumeIdentifier();
            if (identifier == null)
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
                if (tableName == null)
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
                if (nextKeyword != null && !IsReservedKeyword(nextKeyword))
                    node.Alias = ConsumeIdentifier();
            }

            // Parse JOINs
            while (true)
            {
                var joinType = ParseJoinType();
                if (joinType == null)
                    break;

                var join = ParseJoin(joinType.Value);
                if (join != null)
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
                if (tableAlias != null && MatchToken("."))
                {
                    var columnName = ConsumeIdentifier() ?? "";
                    node.Columns.Add(new ColumnReferenceNode { TableAlias = tableAlias, ColumnName = columnName });
                }
                else if (tableAlias != null)
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
                if (tableAlias == null)
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

        if (MatchKeyword("NOT"))
        {
            if (MatchKeyword("IN"))
            {
                return ParseInExpression(left, isNot: true);
            }
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
        if (literal != null)
            return literal;

        // Check for function call
        var funcMatch = Regex.Match(_sql.Substring(_position), @"^\s*(\w+)\s*\(", RegexOptions.IgnoreCase);
        if (funcMatch.Success)
        {
            return ParseFunctionCall();
        }

        // Check for column reference
        var identifier = ConsumeIdentifier();
        if (identifier != null)
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

    private int? ParseInteger()
    {
        var match = Regex.Match(_sql.Substring(_position), @"^\s*(\d+)", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            _position += match.Length;
            return int.Parse(match.Groups[1].Value);
        }
        return null;
    }

    private bool MatchToken(string token)
    {
        var match = Regex.Match(_sql.Substring(_position), @"^\s*(" + Regex.Escape(token) + @")", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            _position += match.Length;
            return true;
        }
        return false;
    }

    private bool IsReservedKeyword(string keyword)
    {
        var reserved = new[] { "SELECT", "FROM", "WHERE", "JOIN", "LEFT", "RIGHT", "FULL", "INNER", "OUTER", "CROSS", "ON", "GROUP", "BY", "HAVING", "ORDER", "LIMIT", "OFFSET" };
        return reserved.Contains(keyword.ToUpperInvariant());
    }

    private InsertNode ParseInsert()
    {
        var node = new InsertNode { Position = _position };

        try
        {
            ConsumeKeyword(); // INSERT
            if (!MatchKeyword("INTO"))
                RecordError("Expected INTO after INSERT");

            node.TableName = ConsumeIdentifier() ?? "";

            // Parse column list if present
            if (MatchToken("("))
            {
                do
                {
                    var col = ConsumeIdentifier();
                    if (col != null)
                        node.Columns.Add(col);
                } while (MatchToken(","));

                if (!MatchToken(")"))
                    RecordError("Expected ) after column list");
            }

            // Parse VALUES or SELECT
            if (MatchKeyword("VALUES"))
            {
                if (!MatchToken("("))
                    RecordError("Expected ( after VALUES");

                do
                {
                    var value = ParseExpression();
                    node.Values.Add(value);
                } while (MatchToken(","));

                if (!MatchToken(")"))
                    RecordError("Expected ) after values");
            }
            else if (PeekKeyword()?.ToUpperInvariant() == "SELECT")
            {
                node.SelectStatement = ParseSelect();
            }
        }
        catch (Exception ex)
        {
            RecordError($"Error parsing INSERT: {ex.Message}");
        }

        return node;
    }

    private UpdateNode ParseUpdate()
    {
        var node = new UpdateNode { Position = _position };

        try
        {
            ConsumeKeyword(); // UPDATE
            node.TableName = ConsumeIdentifier() ?? "";

            if (!MatchKeyword("SET"))
                RecordError("Expected SET after table name");

            // Parse SET assignments
            do
            {
                var column = ConsumeIdentifier();
                if (column == null)
                    break;

                if (!MatchToken("="))
                    RecordError("Expected = in SET clause");

                var value = ParseExpression();
                node.Assignments[column] = value;
            } while (MatchToken(","));

            // Parse WHERE clause
            if (MatchKeyword("WHERE"))
                node.Where = ParseWhere();
        }
        catch (Exception ex)
        {
            RecordError($"Error parsing UPDATE: {ex.Message}");
        }

        return node;
    }

    private DeleteNode ParseDelete()
    {
        var node = new DeleteNode { Position = _position };

        try
        {
            ConsumeKeyword(); // DELETE
            if (!MatchKeyword("FROM"))
                RecordError("Expected FROM after DELETE");

            node.TableName = ConsumeIdentifier() ?? "";

            // Parse WHERE clause
            if (MatchKeyword("WHERE"))
                node.Where = ParseWhere();
        }
        catch (Exception ex)
        {
            RecordError($"Error parsing DELETE: {ex.Message}");
        }

        return node;
    }

    private SqlNode? ParseCreate()
    {
        ConsumeKeyword(); // CREATE

        if (MatchKeyword("TABLE"))
            return ParseCreateTable();

        RecordError("Only CREATE TABLE is supported");
        return null;
    }

    private CreateTableNode ParseCreateTable()
    {
        var node = new CreateTableNode { Position = _position };

        try
        {
            if (MatchKeyword("IF"))
            {
                if (!MatchKeyword("NOT"))
                    RecordError("Expected NOT after IF");
                if (!MatchKeyword("EXISTS"))
                    RecordError("Expected EXISTS after IF NOT");
                node.IfNotExists = true;
            }

            node.TableName = ConsumeIdentifier() ?? "";

            if (!MatchToken("("))
                RecordError("Expected ( after table name");

            // Parse column definitions
            do
            {
                var column = ParseColumnDefinition();
                if (column != null)
                    node.Columns.Add(column);
            } while (MatchToken(","));

            if (!MatchToken(")"))
                RecordError("Expected ) after column definitions");
        }
        catch (Exception ex)
        {
            RecordError($"Error parsing CREATE TABLE: {ex.Message}");
        }

        return node;
    }

    private ColumnDefinition? ParseColumnDefinition()
    {
        var column = new ColumnDefinition();

        try
        {
            column.Name = ConsumeIdentifier() ?? "";
            column.DataType = ConsumeIdentifier() ?? "";

            // Parse column constraints
            while (true)
            {
                if (MatchKeyword("PRIMARY"))
                {
                    if (!MatchKeyword("KEY"))
                        RecordError("Expected KEY after PRIMARY");
                    column.IsPrimaryKey = true;
                }
                else if (MatchKeyword("AUTO") || MatchKeyword("AUTOINCREMENT"))
                {
                    column.IsAutoIncrement = true;
                }
                else if (MatchKeyword("NOT"))
                {
                    if (!MatchKeyword("NULL"))
                        RecordError("Expected NULL after NOT");
                    column.IsNotNull = true;
                }
                else if (MatchKeyword("DEFAULT"))
                {
                    var defaultValue = ParseLiteral();
                    column.DefaultValue = defaultValue?.Value;
                }
                else
                {
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            RecordError($"Error parsing column definition: {ex.Message}");
        }

        return column;
    }
}
