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
