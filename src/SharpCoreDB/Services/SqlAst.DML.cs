// <copyright file="SqlAst.DML.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Services;

/// <summary>
/// SqlAst - DML (Data Manipulation Language) nodes.
/// Contains INSERT, UPDATE, DELETE, and CREATE TABLE statement nodes.
/// Part of the SqlAst partial class infrastructure.
/// Modern C# 14 with collection expressions and target-typed new.
/// See also: SqlAst.Core.cs, SqlAst.Nodes.cs
/// </summary>
public static partial class SqlAst
{
    // Marker for DML partial
}

/// <summary>
/// Represents an INSERT statement.
/// ✅ C# 14: Collection expressions.
/// </summary>
public class InsertNode : SqlNode
{
    /// <summary>
    /// Gets or sets the table name.
    /// </summary>
    public string TableName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the list of columns.
    /// ✅ C# 14: Collection expression.
    /// </summary>
    public List<string> Columns { get; set; } = [];

    /// <summary>
    /// Gets or sets the list of values.
    /// ✅ C# 14: Collection expression.
    /// </summary>
    public List<ExpressionNode> Values { get; set; } = [];

    /// <summary>
    /// Gets or sets a SELECT statement for INSERT INTO ... SELECT.
    /// </summary>
    public SelectNode? SelectStatement { get; set; }

    /// <inheritdoc/>
    public override TResult Accept<TResult>(ISqlVisitor<TResult> visitor) => visitor.VisitInsert(this);
}

/// <summary>
/// Represents an UPDATE statement.
/// ✅ C# 14: Collection expression for Dictionary.
/// </summary>
public class UpdateNode : SqlNode
{
    /// <summary>
    /// Gets or sets the table name.
    /// </summary>
    public string TableName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the SET assignments.
    /// ✅ C# 14: Collection expression for Dictionary.
    /// </summary>
    public Dictionary<string, ExpressionNode> Assignments { get; set; } = [];

    /// <summary>
    /// Gets or sets the WHERE clause.
    /// </summary>
    public WhereNode? Where { get; set; }

    /// <inheritdoc/>
    public override TResult Accept<TResult>(ISqlVisitor<TResult> visitor) => visitor.VisitUpdate(this);
}

/// <summary>
/// Represents a DELETE statement.
/// </summary>
public class DeleteNode : SqlNode
{
    /// <summary>
    /// Gets or sets the table name.
    /// </summary>
    public string TableName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the WHERE clause.
    /// </summary>
    public WhereNode? Where { get; set; }

    /// <inheritdoc/>
    public override TResult Accept<TResult>(ISqlVisitor<TResult> visitor) => visitor.VisitDelete(this);
}

/// <summary>
/// Represents a CREATE TABLE statement.
/// ✅ C# 14: Collection expression.
/// </summary>
public class CreateTableNode : SqlNode
{
    /// <summary>
    /// Gets or sets the table name.
    /// </summary>
    public string TableName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the column definitions.
    /// ✅ C# 14: Collection expression.
    /// </summary>
    public List<ColumnDefinition> Columns { get; set; } = [];

    /// <summary>
    /// Gets or sets the foreign key constraints.
    /// ✅ C# 14: Collection expression.
    /// </summary>
    public List<ForeignKeyDefinition> ForeignKeys { get; set; } = [];

    /// <summary>
    /// Gets or sets the CHECK constraints.
    /// ✅ C# 14: Collection expression.
    /// </summary>
    public List<string> CheckConstraints { get; set; } = [];

    /// <summary>
    /// Gets or sets whether to use IF NOT EXISTS.
    /// </summary>
    public bool IfNotExists { get; set; }

    /// <inheritdoc/>
    public override TResult Accept<TResult>(ISqlVisitor<TResult> visitor) => visitor.VisitCreateTable(this);
}

/// <summary>
/// Represents a column definition in CREATE TABLE.
/// </summary>
public class ColumnDefinition
{
    /// <summary>
    /// Gets or sets the column name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the data type.
    /// </summary>
    public string DataType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether this is a primary key.
    /// </summary>
    public bool IsPrimaryKey { get; set; }

    /// <summary>
    /// Gets or sets whether this is auto-increment.
    /// </summary>
    public bool IsAutoIncrement { get; set; }

    /// <summary>
    /// Gets or sets whether this is NOT NULL.
    /// </summary>
    public bool IsNotNull { get; set; }

    /// <summary>
    /// Gets or sets whether this is UNIQUE.
    /// </summary>
    public bool IsUnique { get; set; }

    /// <summary>
    /// Gets or sets the default value.
    /// </summary>
    public object? DefaultValue { get; set; }

    /// <summary>
    /// Gets or sets the default expression (for functions like CURRENT_TIMESTAMP).
    /// </summary>
    public string? DefaultExpression { get; set; }

    /// <summary>
    /// Gets or sets the CHECK constraint expression for this column.
    /// </summary>
    public string? CheckExpression { get; set; }

    /// <summary>
    /// Gets or sets the vector dimensions for VECTOR(N) columns.
    /// Null for non-vector columns.
    /// </summary>
    public int? Dimensions { get; set; }
}

/// <summary>
/// Represents a foreign key constraint definition.
/// </summary>
public class ForeignKeyDefinition
{
    /// <summary>
    /// Gets or sets the column name in this table.
    /// </summary>
    public string ColumnName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the referenced table name.
    /// </summary>
    public string ReferencedTable { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the referenced column name.
    /// </summary>
    public string ReferencedColumn { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the ON DELETE action.
    /// </summary>
    public FkAction OnDelete { get; set; }

    /// <summary>
    /// Gets or sets the ON UPDATE action.
    /// </summary>
    public FkAction OnUpdate { get; set; }
}

/// <summary>
/// Represents an ALTER TABLE statement.
/// </summary>
public class AlterTableNode : SqlNode
{
    /// <summary>
    /// Gets or sets the table name.
    /// </summary>
    public string TableName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the ALTER TABLE operation.
    /// </summary>
    public AlterTableOperation Operation { get; set; }

    /// <summary>
    /// Gets or sets the column definition for ADD COLUMN.
    /// </summary>
    public ColumnDefinition? Column { get; set; }

    /// <inheritdoc/>
    public override TResult Accept<TResult>(ISqlVisitor<TResult> visitor) => visitor.VisitAlterTable(this);
}

/// <summary>
/// ALTER TABLE operation types.
/// </summary>
public enum AlterTableOperation
{
    /// <summary>Add a column.</summary>
    AddColumn,

    /// <summary>Drop a column.</summary>
    DropColumn,

    /// <summary>Modify a column.</summary>
    ModifyColumn,

    /// <summary>Rename a column.</summary>
    RenameColumn,

    /// <summary>Add a constraint.</summary>
    AddConstraint,

    /// <summary>Drop a constraint.</summary>
    DropConstraint
}
