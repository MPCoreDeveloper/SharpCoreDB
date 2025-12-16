// <copyright file="SqlAst.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Services;

/// <summary>
/// SQL Abstract Syntax Tree (AST) node definitions.
/// Provides a complete representation of SQL statements as an object tree.
/// Supports SELECT, INSERT, UPDATE, DELETE, and CREATE TABLE operations.
/// Implements the Visitor pattern for AST traversal and transformation.
/// 
/// REFACTORED TO PARTIAL CLASSES FOR MAINTAINABILITY:
/// - SqlAst.Core.cs: Base classes, interfaces, visitor pattern infrastructure
/// - SqlAst.Nodes.cs: Query nodes (SELECT, FROM, JOIN, WHERE, ORDER BY, GROUP BY, expressions)
/// - SqlAst.DML.cs: DML nodes (INSERT, UPDATE, DELETE, CREATE TABLE)
/// - SqlAst.cs (this file): Main documentation and namespace organization
/// 
/// MODERN C# 14 FEATURES USED:
/// - Collection expressions: List initialization with []
/// - Target-typed new: new() for known types
/// - Covariant return types: out TResult in ISqlVisitor
/// - Nullable reference types: Proper null annotations
/// 
/// USAGE:
/// // Build AST programmatically
/// var select = new SelectNode
/// {
///     Columns = [new ColumnNode { Name = "Id" }, new ColumnNode { Name = "Name" }],
///     From = new FromNode { TableName = "Users" }
/// };
/// 
/// // Visit with custom visitor
/// var result = select.Accept(myVisitor);
/// 
/// VISITOR PATTERN:
/// - ISqlVisitor of TResult: Generic visitor with typed returns
/// - ISqlVisitor: Non-generic visitor (returns object?)
/// - SqlNode.Accept: Accepts visitors for traversal
/// 
/// NODE TYPES:
/// - Query: SelectNode, FromNode, JoinNode, WhereNode, OrderByNode, GroupByNode
/// - Expressions: BinaryExpressionNode, LiteralNode, ColumnReferenceNode, InExpressionNode
/// - DML: InsertNode, UpdateNode, DeleteNode, CreateTableNode
/// </summary>
public static partial class SqlAst
{
    // This file intentionally left minimal.
    // All node types are implemented in partial class files:
    // - SqlAst.Core.cs (base classes and visitor pattern)
    // - SqlAst.Nodes.cs (query and expression nodes)
    // - SqlAst.DML.cs (DML statement nodes)
}
