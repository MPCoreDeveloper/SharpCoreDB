// <copyright file="SqlToStringVisitor.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace SharpCoreDB.Services;

/// <summary>
/// SQL to string visitor for debugging/logging.
/// Type-safe visitor that always returns strings.
/// 
/// <para>
/// <b>Architecture:</b>
/// This class is split into multiple partial files for better organization:
/// </para>
/// 
/// <list type="bullet">
/// <item>
/// <term>SqlToStringVisitor.cs</term>
/// <description>Main class with constructor and dialect field</description>
/// </item>
/// <item>
/// <term>SqlToStringVisitor.Query.cs</term>
/// <description>Query-related visitors (SELECT, FROM, JOIN, WHERE, GROUP BY, ORDER BY)</description>
/// </item>
/// <item>
/// <term>SqlToStringVisitor.DML.cs</term>
/// <description>DML visitors (INSERT, UPDATE, DELETE, CREATE TABLE)</description>
/// </item>
/// </list>
/// 
/// <para>
/// <b>Base Class:</b>
/// Inherits from SqlVisitorBase&lt;string&gt; which provides:
/// </para>
/// <list type="bullet">
/// <item>Error recovery with SafeVisit wrappers</item>
/// <item>Error tracking and reporting</item>
/// <item>Abstract visitor pattern implementation</item>
/// </list>
/// 
/// <para>
/// <b>C# 14 Features:</b>
/// </para>
/// <list type="bullet">
/// <item>Collection expressions for list initialization</item>
/// <item>Spread operator for array concatenation</item>
/// <item>Enhanced is/is not null pattern matching</item>
/// <item>Switch expressions for cleaner type mapping</item>
/// <item>Null-coalescing operators</item>
/// </list>
/// 
/// <para>
/// <b>Usage Example:</b>
/// <code>
/// var visitor = new SqlToStringVisitor(SqlDialectFactory.PostgreSQL);
/// var selectNode = new SelectNode { ... };
/// string sql = selectNode.Accept(visitor);
/// Console.WriteLine(sql); // "SELECT id, name FROM users WHERE active = 1"
/// </code>
/// </para>
/// </summary>
public sealed partial class SqlToStringVisitor : SqlVisitorBase<string>
{
    private readonly ISqlDialect _dialect;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqlToStringVisitor"/> class.
    /// </summary>
    /// <param name="dialect">The SQL dialect to use (default: factory default).</param>
    public SqlToStringVisitor(ISqlDialect? dialect = null)
        : base(throwOnError: false) // Don't throw on errors, just record them
    {
        _dialect = dialect ?? SqlDialectFactory.Default;
    }
}
