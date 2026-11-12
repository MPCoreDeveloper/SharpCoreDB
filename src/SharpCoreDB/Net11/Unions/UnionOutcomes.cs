// <copyright file="UnionOutcomes.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

#if NET11_0_OR_GREATER
namespace SharpCoreDB.Unions;

using System.Collections.Generic;
using SharpCoreDB.DataStructures;

/// <summary>
/// The query returned rows.
/// </summary>
/// <param name="Rows">The materialized rows, in result order.</param>
public record class QuerySuccess(IReadOnlyList<Dictionary<string, object?>> Rows);

/// <summary>
/// The query ran but matched nothing. Distinct from a failure and from an empty
/// <see cref="QuerySuccess"/>, which is a successful query over an empty result set.
/// </summary>
public record class QueryNotFound;

/// <summary>
/// The query failed; <see cref="Message"/> is the engine's own message.
/// </summary>
/// <param name="Message">The failure message, unchanged from what the engine reported.</param>
public record class QueryFailure(string Message);

/// <summary>
/// The statement completed and reports how many rows it touched.
/// </summary>
/// <param name="RowsAffected">Rows inserted, updated or deleted.</param>
public record class ExecuteSuccess(long RowsAffected);

/// <summary>
/// The statement failed; <see cref="Message"/> is the engine's own message.
/// </summary>
/// <param name="Message">The failure message, unchanged from what the engine reported.</param>
public record class ExecuteFailure(string Message);

/// <summary>
/// A table with that name exists.
/// </summary>
/// <param name="Table">The table's metadata, as returned by <c>Database.GetTables()</c>.</param>
public record class TableFound(TableInfo Table);

/// <summary>
/// No table with that name exists.
/// </summary>
/// <param name="Name">The requested table name.</param>
public record class TableMissing(string Name);

/// <summary>
/// A three-way query outcome: rows, "matched nothing", or a failure. A C# 15 union type, so a
/// <c>switch</c> over it is exhaustive at compile time — dropping an arm is a build error rather than
/// a runtime fall-through.
/// <para>
/// This is an additive, opt-in surface: it never replaces <c>QueryResult</c>, <c>ExecuteResult</c> or
/// the <c>ExecuteSQL</c> signatures, all of which stay byte-identical. Consumers construct a value with
/// the compiler-generated implicit conversions, e.g. <c>QueryOutcome outcome = new QueryFailure("…");</c>.
/// </para>
/// </summary>
public union QueryOutcome(QuerySuccess, QueryNotFound, QueryFailure);

/// <summary>
/// A two-way statement outcome: rows affected or a failure. See <see cref="QueryOutcome"/> for the
/// opt-in/additive rule that applies to every union in this namespace.
/// </summary>
public union ExecuteOutcome(ExecuteSuccess, ExecuteFailure);

/// <summary>
/// A two-way table lookup, the shape the C# 15 extension indexer (<c>db["name"]</c>, which returns
/// <c>null</c> when the table is absent) converts into without a nullable hop in the caller.
/// </summary>
public union TableLookup(TableFound, TableMissing);
#endif
