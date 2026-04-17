// <copyright file="EnhancedSqlParser.DML.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Services;

using System;
using System.Collections.Generic;

/// <summary>
/// DML (Data Manipulation Language) statement parsing methods for EnhancedSqlParser.
/// Handles INSERT, UPDATE, and DELETE statements.
/// </summary>
public partial class EnhancedSqlParser
{
    private InsertNode ParseInsert()
    {
        var node = new InsertNode { Position = _position };

        try
        {
            ConsumeKeyword(); // INSERT

            if (MatchKeyword("OR"))
            {
                node.ConflictPolicy = PeekKeyword()?.ToUpperInvariant() switch
                {
                    "IGNORE" => InsertConflictPolicy.Ignore,
                    "REPLACE" => InsertConflictPolicy.Replace,
                    "FAIL" => InsertConflictPolicy.Fail,
                    "ABORT" => InsertConflictPolicy.Abort,
                    _ => node.ConflictPolicy,
                };

                if (node.ConflictPolicy is InsertConflictPolicy.None)
                {
                    RecordError("Expected IGNORE, REPLACE, FAIL, or ABORT after INSERT OR");
                }
                else
                {
                    ConsumeKeyword();
                }
            }

            if (!MatchKeyword("INTO"))
                RecordError("Expected INTO after INSERT");

            node.TableName = ConsumeIdentifier() ?? "";

            // Parse column list if present
            if (MatchToken("("))
            {
                do
                {
                    var col = ConsumeIdentifier();
                    if (col is not null)
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

            if (MatchKeyword("ON"))
            {
                if (!MatchKeyword("CONFLICT"))
                {
                    RecordError("Expected CONFLICT after ON");
                }
                else
                {
                    if (MatchToken("("))
                    {
                        do
                        {
                            var col = ConsumeIdentifier();
                            if (col is not null)
                                node.ConflictTargetColumns.Add(col);
                        } while (MatchToken(","));

                        if (!MatchToken(")"))
                            RecordError("Expected ) after ON CONFLICT target columns");
                    }

                    if (!MatchKeyword("DO"))
                    {
                        RecordError("Expected DO after ON CONFLICT");
                    }
                    else if (MatchKeyword("NOTHING"))
                    {
                        node.OnConflictAction = InsertOnConflictAction.DoNothing;
                    }
                    else if (MatchKeyword("UPDATE"))
                    {
                        node.OnConflictAction = InsertOnConflictAction.DoUpdate;

                        if (!MatchKeyword("SET"))
                            RecordError("Expected SET after DO UPDATE");

                        // Parse one or more col = expr assignments
                        do
                        {
                            var assignCol = ConsumeIdentifier();
                            if (assignCol is null)
                                break;

                            if (!MatchToken("="))
                                RecordError("Expected = in DO UPDATE SET");

                            // Capture raw SQL text for the expression
                            var exprStart = _position;
                            // Skip leading whitespace to find the true start
                            while (exprStart < _sql.Length && char.IsWhiteSpace(_sql[exprStart]))
                                exprStart++;
                            ParseExpression(); // advance _position past the expression
                            // Trim trailing whitespace/comma so the raw text is clean
                            var rawExpr = _sql[exprStart.._position].Trim().TrimEnd(',');
                            node.DoUpdateAssignments[assignCol] = rawExpr;
                        }
                        while (MatchToken(","));

                        // Optional WHERE on DO UPDATE
                        if (MatchKeyword("WHERE"))
                        {
                            var whereStart = _position;
                            while (whereStart < _sql.Length && char.IsWhiteSpace(_sql[whereStart]))
                                whereStart++;
                            ParseExpression();
                            node.DoUpdateWhere = _sql[whereStart.._position].Trim();
                        }
                    }
                    else
                    {
                        RecordError("Expected NOTHING or UPDATE after ON CONFLICT DO");
                    }
                }
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
                if (column is null)
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
}
