// <copyright file="SqlToStringVisitor.DML.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Services;

using System.Collections.Generic;
using System.Linq;

/// <summary>
/// SQL to string visitor for debugging/logging - DML partial class.
/// Handles data manipulation language visitors: INSERT, UPDATE, DELETE, CREATE TABLE.
/// </summary>
public sealed partial class SqlToStringVisitor
{
    /// <inheritdoc/>
    protected override string VisitInsertCore(InsertNode node)
    {
        List<string> parts = ["INSERT INTO", _dialect.QuoteIdentifier(node.TableName)]; // ✅ C# 14: Collection expression

        if (node.Columns.Any())
        {
            var cols = string.Join(", ", node.Columns.Select(_dialect.QuoteIdentifier));
            parts.Add($"({cols})");
        }

        if (node.SelectStatement is not null) // ✅ C# 14: is not null pattern
        {
            parts.Add(node.SelectStatement.Accept(this));
        }
        else
        {
            var values = node.Values.Select(v => v.Accept(this));
            parts.Add($"VALUES ({string.Join(", ", values)})");
        }

        return string.Join(" ", parts);
    }

    /// <inheritdoc/>
    protected override string VisitUpdateCore(UpdateNode node)
    {
        List<string> parts = ["UPDATE", _dialect.QuoteIdentifier(node.TableName), "SET"]; // ✅ C# 14: Collection expression

        var sets = node.Assignments.Select(kv =>
            $"{_dialect.QuoteIdentifier(kv.Key)} = {kv.Value.Accept(this)}");
        parts.Add(string.Join(", ", sets));

        if (node.Where is not null) // ✅ C# 14: is not null pattern
            parts.Add(node.Where.Accept(this));

        return string.Join(" ", parts);
    }

    /// <inheritdoc/>
    protected override string VisitDeleteCore(DeleteNode node)
    {
        List<string> parts = ["DELETE FROM", _dialect.QuoteIdentifier(node.TableName)]; // ✅ C# 14: Collection expression

        if (node.Where is not null) // ✅ C# 14: is not null pattern
            parts.Add(node.Where.Accept(this));

        return string.Join(" ", parts);
    }

    /// <inheritdoc/>
    protected override string VisitCreateTableCore(CreateTableNode node)
    {
        List<string> parts = ["CREATE TABLE"]; // ✅ C# 14: Collection expression

        if (node.IfNotExists)
            parts.Add("IF NOT EXISTS");

        parts.Add(_dialect.QuoteIdentifier(node.TableName));

        var columns = node.Columns.Select(c =>
        {
            var def = $"{_dialect.QuoteIdentifier(c.Name)} {c.DataType}";
            if (c.IsPrimaryKey) def += " PRIMARY KEY";
            if (c.IsAutoIncrement) def += " AUTO";
            if (c.IsNotNull) def += " NOT NULL";
            if (c.DefaultValue is not null) def += $" DEFAULT {c.DefaultValue}"; // ✅ C# 14: is not null
            return def;
        });

        parts.Add($"({string.Join(", ", columns)})");

        return string.Join(" ", parts);
    }
}
