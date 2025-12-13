// <copyright file="EnhancedSqlParser.DDL.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Services;

using System;

/// <summary>
/// DDL (Data Definition Language) statement parsing methods for EnhancedSqlParser.
/// Handles CREATE TABLE and other DDL statements.
/// </summary>
public partial class EnhancedSqlParser
{
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
                if (column is not null)
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
