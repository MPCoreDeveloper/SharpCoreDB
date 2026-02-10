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

    private SqlNode? ParseAlter()
    {
        ConsumeKeyword(); // ALTER

        if (MatchKeyword("TABLE"))
            return ParseAlterTable();

        RecordError("Only ALTER TABLE is supported");
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

            // Parse column definitions and table constraints
            do
            {
                if (PeekKeyword()?.ToUpperInvariant() == "FOREIGN")
                {
                    var fk = ParseForeignKeyDefinition();
                    if (fk is not null)
                        node.ForeignKeys.Add(fk);
                }
                else if (PeekKeyword()?.ToUpperInvariant() == "CHECK")
                {
                    var check = ParseCheckConstraint();
                    if (check is not null)
                        node.CheckConstraints.Add(check);
                }
                else
                {
                    var column = ParseColumnDefinition();
                    if (column is not null)
                        node.Columns.Add(column);
                }
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

    private AlterTableNode ParseAlterTable()
    {
        var node = new AlterTableNode { Position = _position };

        try
        {
            node.TableName = ConsumeIdentifier() ?? "";

            if (MatchKeyword("ADD"))
            {
                if (MatchKeyword("COLUMN"))
                {
                    node.Operation = AlterTableOperation.AddColumn;
                    node.Column = ParseColumnDefinition();
                }
                else
                {
                    RecordError("Expected COLUMN after ADD");
                }
            }
            else
            {
                RecordError("Only ADD COLUMN is supported for ALTER TABLE");
            }
        }
        catch (Exception ex)
        {
            RecordError($"Error parsing ALTER TABLE: {ex.Message}");
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
                else if (MatchKeyword("UNIQUE"))
                {
                    column.IsUnique = true;
                }
                else if (MatchKeyword("DEFAULT"))
                {
                    column.DefaultExpression = ParseDefaultExpression();
                }
                else if (MatchKeyword("CHECK"))
                {
                    if (!MatchToken("("))
                        RecordError("Expected ( after CHECK");
                    // Parse the check expression - simplified for now
                    var expr = ParseExpression();
                    if (!MatchToken(")"))
                        RecordError("Expected ) after CHECK expression");
                    column.CheckExpression = expr.ToString();
                }
                else if (MatchKeyword("COLLATE"))
                {
                    // ✅ COLLATE Phase 2: Parse COLLATE <type> in column definition
                    var collationName = ConsumeIdentifier()?.ToUpperInvariant() ?? "BINARY";
                    column.Collation = collationName switch
                    {
                        "NOCASE" => CollationType.NoCase,
                        "BINARY" => CollationType.Binary,
                        "RTRIM" => CollationType.RTrim,
                        _ => throw new InvalidOperationException(
                            $"Unknown collation '{collationName}'. Valid: NOCASE, BINARY, RTRIM")
                    };
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

    private ForeignKeyDefinition? ParseForeignKeyDefinition()
    {
        var fk = new ForeignKeyDefinition();

        try
        {
            if (!MatchKeyword("FOREIGN"))
                return null;
            if (!MatchKeyword("KEY"))
                RecordError("Expected KEY after FOREIGN");

            if (!MatchToken("("))
                RecordError("Expected ( after FOREIGN KEY");
            
            fk.ColumnName = ConsumeIdentifier() ?? "";
            
            if (!MatchToken(")"))
                RecordError("Expected ) after column name");

            if (!MatchKeyword("REFERENCES"))
                RecordError("Expected REFERENCES after FOREIGN KEY");

            fk.ReferencedTable = ConsumeIdentifier() ?? "";

            if (!MatchToken("("))
                RecordError("Expected ( after referenced table");
            
            fk.ReferencedColumn = ConsumeIdentifier() ?? "";
            
            if (!MatchToken(")"))
                RecordError("Expected ) after referenced column");

            // Parse ON DELETE and ON UPDATE actions
            while (true)
            {
                if (MatchKeyword("ON"))
                {
                    if (MatchKeyword("DELETE"))
                    {
                        fk.OnDelete = ParseFkAction();
                    }
                    else if (MatchKeyword("UPDATE"))
                    {
                        fk.OnUpdate = ParseFkAction();
                    }
                    else
                    {
                        RecordError("Expected DELETE or UPDATE after ON");
                    }
                }
                else
                {
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            RecordError($"Error parsing FOREIGN KEY: {ex.Message}");
        }

        return fk;
    }

    private FkAction ParseFkAction()
    {
        if (MatchKeyword("CASCADE"))
            return FkAction.Cascade;
        if (MatchKeyword("SET"))
        {
            if (!MatchKeyword("NULL"))
                RecordError("Expected NULL after SET");
            return FkAction.SetNull;
        }
        if (MatchKeyword("RESTRICT"))
            return FkAction.Restrict;
        if (MatchKeyword("NO"))
        {
            if (!MatchKeyword("ACTION"))
                RecordError("Expected ACTION after NO");
            return FkAction.NoAction;
        }
        
        // Default to RESTRICT if no action specified
        return FkAction.Restrict;
    }

    /// <summary>
    /// Parses a DEFAULT expression, supporting both literals and functions like CURRENT_TIMESTAMP, NEWID().
    /// </summary>
    private string? ParseDefaultExpression()
    {
        // Try to parse as a function call first (CURRENT_TIMESTAMP, NEWID(), etc.)
        var identifier = PeekKeyword()?.ToUpperInvariant();
        if (identifier is "CURRENT_TIMESTAMP" or "GETDATE" or "GETUTCDATE" or "NEWID" or "NEWSEQUENTIALID")
        {
            ConsumeKeyword(); // consume the function name
            if (MatchToken("(") && !MatchToken(")"))
            {
                RecordError("Expected ) after function call");
            }
            return identifier;
        }

        // Fall back to literal parsing
        var literal = ParseLiteral();
        return literal?.Value?.ToString();
    }

    /// <summary>
    /// Parses a CHECK constraint expression.
    /// </summary>
    private string? ParseCheckConstraint()
    {
        if (!MatchKeyword("CHECK"))
            return null;

        if (!MatchToken("("))
            RecordError("Expected ( after CHECK");

        // Parse the check expression
        var expr = ParseExpression();

        if (!MatchToken(")"))
            RecordError("Expected ) after CHECK expression");

        // Convert expression to string representation for now
        return expr.ToString();
    }
}
