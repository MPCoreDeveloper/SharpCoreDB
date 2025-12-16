// <copyright file="SqlVisitor.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Services;

/// <summary>
/// SQL Visitor Pattern Implementation - Main Documentation.
/// 
/// <para>
/// This file serves as the main documentation for the SQL visitor pattern implementation.
/// The actual implementation is split across multiple partial class files for better organization.
/// </para>
/// 
/// <para>
/// <b>Class Hierarchy:</b>
/// </para>
/// <code>
/// ISqlVisitor&lt;TResult&gt;                    [Interface]
///     ↓
/// SqlVisitorBase&lt;TResult&gt;                  [Abstract Base - Partial Class]
///     ├── SqlVisitorBase.Core.cs         [Fields, properties, error handling]
///     └── SqlVisitorBase.Visitors.cs     [Visitor method declarations]
///     ↓
/// SqlToStringVisitor                      [Concrete Implementation - Partial Class]
///     ├── SqlToStringVisitor.cs          [Constructor, dialect field]
///     ├── SqlToStringVisitor.Query.cs    [Query visitors: SELECT, FROM, JOIN, etc.]
///     └── SqlToStringVisitor.DML.cs      [DML visitors: INSERT, UPDATE, DELETE, CREATE]
/// </code>
/// 
/// <para>
/// <b>SqlVisitorBase&lt;TResult&gt; - Abstract Base Class</b>
/// </para>
/// <para>
/// Provides error recovery infrastructure with SafeVisit wrappers.
/// Each visitor method follows this pattern:
/// </para>
/// <code>
/// public virtual TResult VisitXxx(XxxNode node) =>
///     SafeVisit(() => VisitXxxCore(node), "XXX", node);
///     
/// protected abstract TResult VisitXxxCore(XxxNode node);
/// </code>
/// 
/// <para>
/// <b>Key Features:</b>
/// </para>
/// <list type="bullet">
/// <item>
/// <term>Error Recovery</term>
/// <description>SafeVisit wrapper catches exceptions and records errors without crashing</description>
/// </item>
/// <item>
/// <term>Error Tracking</term>
/// <description>Maintains list of errors with context and node position</description>
/// </item>
/// <item>
/// <term>Flexible Error Handling</term>
/// <description>Can be configured to throw or record errors</description>
/// </item>
/// <item>
/// <term>Type Safety</term>
/// <description>Generic TResult ensures type-safe visitor implementations</description>
/// </item>
/// </list>
/// 
/// <para>
/// <b>SqlToStringVisitor - Concrete Implementation</b>
/// </para>
/// <para>
/// Converts SQL AST nodes to SQL string representation.
/// Supports multiple SQL dialects through ISqlDialect abstraction.
/// </para>
/// 
/// <para>
/// <b>Supported SQL Constructs:</b>
/// </para>
/// <list type="bullet">
/// <item><term>Queries:</term> SELECT, FROM, JOIN, WHERE, GROUP BY, HAVING, ORDER BY</item>
/// <item><term>Expressions:</term> Binary, Literal, Column Reference, IN, Function Calls</item>
/// <item><term>DML:</term> INSERT, UPDATE, DELETE</item>
/// <item><term>DDL:</term> CREATE TABLE</item>
/// </list>
/// 
/// <para>
/// <b>C# 14 Modernization:</b>
/// </para>
/// <list type="bullet">
/// <item>Collection expressions - Cleaner list initialization: <c>List&lt;string&gt; parts = ["SELECT"];</c></item>
/// <item>Spread operator - Array concatenation: <c>[.. parts, "new item"]</c></item>
/// <item>Enhanced pattern matching - More expressive null checks: <c>if (node is not null)</c></item>
/// <item>Switch expressions - Cleaner type mapping for JoinType, LiteralValue, etc.</item>
/// <item>Simplified conditionals - More concise code throughout</item>
/// </list>
/// 
/// <para>
/// <b>Design Benefits:</b>
/// </para>
/// <list type="bullet">
/// <item><b>Separation of Concerns:</b> Query logic separate from DML logic</item>
/// <item><b>Maintainability:</b> Each partial ~150 lines vs original 464 lines</item>
/// <item><b>Testability:</b> Can test query/DML visitors independently</item>
/// <item><b>Extensibility:</b> Easy to add new visitor implementations or SQL constructs</item>
/// <item><b>Error Recovery:</b> Robust error handling prevents cascading failures</item>
/// </list>
/// 
/// <para>
/// <b>Usage Example:</b>
/// </para>
/// <code>
/// // Create visitor with specific dialect
/// var visitor = new SqlToStringVisitor(SqlDialectFactory.PostgreSQL);
/// 
/// // Build AST
/// var select = new SelectNode
/// {
///     Columns = [new ColumnNode { Name = "id" }, new ColumnNode { Name = "name" }],
///     From = new FromNode { TableName = "users" },
///     Where = new WhereNode { Condition = new BinaryExpressionNode { ... } }
/// };
/// 
/// // Convert to SQL
/// string sql = select.Accept(visitor);
/// 
/// // Check for errors
/// if (visitor.HasErrors)
/// {
///     foreach (var error in visitor.Errors)
///         Console.WriteLine(error);
/// }
/// </code>
/// 
/// <para>
/// <b>Extension Example:</b>
/// </para>
/// <code>
/// // Create custom visitor for different output format
/// public class SqlToJsonVisitor : SqlVisitorBase&lt;JsonNode&gt;
/// {
///     protected override JsonNode VisitSelectCore(SelectNode node)
///     {
///         return new JsonObject
///         {
///             ["type"] = "SELECT",
///             ["columns"] = new JsonArray(node.Columns.Select(c => c.Accept(this)).ToArray()),
///             // ... more properties
///         };
///     }
///     // ... implement other visitors
/// }
/// </code>
/// </summary>
internal static class SqlVisitorDocumentation
{
    // This class exists solely for documentation purposes and contains no executable code.
    // It serves as a central documentation hub for the SQL visitor pattern implementation.
    // The actual visitor implementations are in the partial classes:
    // - SqlVisitorBase.Core.cs
    // - SqlVisitorBase.Visitors.cs
    // - SqlToStringVisitor.cs
    // - SqlToStringVisitor.Query.cs
    // - SqlToStringVisitor.DML.cs
    
    private const string Purpose = "Documentation only - see partial classes for implementation";
}
