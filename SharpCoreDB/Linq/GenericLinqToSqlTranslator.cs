// <copyright file="GenericLinqToSqlTranslator.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Linq;

/// <summary>
/// Generic LINQ-to-SQL translator with support for custom types and GROUP BY.
/// Translates LINQ Expression trees to SQL with type safety.
/// Integrates with MVCC and generic indexes for optimal performance.
/// 
/// REFACTORED TO PARTIAL CLASSES FOR MAINTAINABILITY:
/// - GenericLinqToSqlTranslator.Core.cs: Core translation logic, fields, helpers
/// - GenericLinqToSqlTranslator.Expressions.cs: Expression visitors (binary, unary, lambda, member, etc.)
/// - GenericLinqToSqlTranslator.Queries.cs: Query method visitors (Where, Select, GroupBy, OrderBy, etc.)
/// - GenericLinqToSqlTranslator.cs (this file): Main documentation and class declaration
/// 
/// MODERN C# 14 FEATURES USED:
/// - Collection expressions: List of T = [] instead of new List of T()
/// - Enhanced pattern matching: is null, is not null, property patterns
/// - Spread operator: [.. collection]
/// - Switch expressions: nodeType switch
/// - Char literals: Append('x') instead of Append("x")
/// - Property patterns for value extraction
/// 
/// USAGE:
/// var translator = new GenericLinqToSqlTranslator of User();
/// var (sql, parameters) = translator.Translate(query.Expression);
/// 
/// SUPPORTED LINQ METHODS:
/// - Where, Select, GroupBy, OrderBy/OrderByDescending
/// - Take, Skip, First, FirstOrDefault, Single, SingleOrDefault
/// - Count, Any, Sum, Average, Min, Max
/// - String methods: Contains, StartsWith, EndsWith
/// </summary>
public sealed partial class GenericLinqToSqlTranslator<T> where T : class
{
    // This file intentionally left minimal.
    // All functionality is implemented in partial class files:
    // - GenericLinqToSqlTranslator.Core.cs
    // - GenericLinqToSqlTranslator.Expressions.cs
    // - GenericLinqToSqlTranslator.Queries.cs
}
