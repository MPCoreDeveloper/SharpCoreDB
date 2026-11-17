// <copyright file="SqlParser.Upsert.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Services;

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using SharpCoreDB.DataStructures;
using SharpCoreDB.Interfaces;

/// <summary>
/// SqlParser partial — SQLite's INSERT conflict clauses: <c>INSERT OR REPLACE|IGNORE|ABORT|FAIL|ROLLBACK</c>,
/// <c>REPLACE INTO</c> and the upsert clause <c>ON CONFLICT [(cols)] DO NOTHING | DO UPDATE SET … [WHERE …]</c>.
/// <para>
/// Up to 2.1.4 none of them ran on the SQL path: <c>INSERT OR …</c> failed with "Unsupported SQL statement", and an
/// <c>ON CONFLICT</c> clause was ignored, so an upsert onto an existing key failed as a duplicate. The conflict lookups
/// and handlers in <c>SqlParser.DML.InsertConflict</c> existed but nothing called them. A <c>DO UPDATE</c> assignment is
/// an expression over the existing row, with <c>excluded.col</c> the value the INSERT tried to write.
/// </para>
/// </summary>
public partial class SqlParser
{
    private static readonly Regex InsertOrPolicyRegex = new(
        @"^\s*INSERT\s+OR\s+(REPLACE|IGNORE|ABORT|FAIL|ROLLBACK)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromSeconds(1));

    private static readonly Regex OnConflictRegex = new(
        @"^ON\s+CONFLICT\s*(?:\((?<targets>[^)]*)\))?\s*DO\s+(?:(?<nothing>NOTHING)|UPDATE\s+SET\s+(?<set>.+))\s*;?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline | RegexOptions.Compiled,
        TimeSpan.FromSeconds(1));

    /// <summary>An INSERT's conflict handling: its <c>OR</c> policy and its <c>ON CONFLICT</c> clause.</summary>
    private sealed class InsertConflictClause
    {
        public InsertConflictPolicy Policy { get; init; }

        public InsertOnConflictAction Action { get; init; }

        public List<string> Targets { get; init; } = [];

        public List<KeyValuePair<string, string>> Assignments { get; init; } = [];

        public string? Where { get; init; }
    }

    /// <summary>
    /// Reads the statement's <c>INSERT OR …</c> policy and cuts a trailing <c>ON CONFLICT …</c> clause off
    /// <paramref name="valuesText"/> (the text after VALUES, or the SELECT). Returns <c>null</c> when the statement has
    /// neither, which keeps every other INSERT on its existing paths.
    /// </summary>
    private static InsertConflictClause? ReadInsertConflictClause(string statement, ref string valuesText)
    {
        var policy = ReadInsertOrPolicy(statement);
        int onAt = SqlLiteralScanner.FindTopLevel(valuesText, 0, static (t, i) =>
            SqlLiteralScanner.IsKeywordAt(t, i, "ON") &&
            Regex.IsMatch(t[i..], @"^ON\s+CONFLICT\b", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)));
        if (onAt < 0)
        {
            return policy == InsertConflictPolicy.None ? null : new InsertConflictClause { Policy = policy };
        }

        var clauseText = valuesText[onAt..].Trim();
        valuesText = valuesText[..onAt].TrimEnd();
        var match = OnConflictRegex.Match(clauseText);
        if (!match.Success)
        {
            throw new InvalidOperationException($"Unrecognised ON CONFLICT clause: {clauseText}");
        }

        List<string> targets = match.Groups["targets"].Success
            ? [.. SqlInPredicate.SplitTopLevelComma(match.Groups["targets"].Value)
                .Where(static target => !string.IsNullOrWhiteSpace(target))
                .Select(static target => SqlIdentifierScanner.IdentifierText(target.Trim()))]
            : [];

        if (match.Groups["nothing"].Success)
        {
            return new InsertConflictClause { Policy = policy, Action = InsertOnConflictAction.DoNothing, Targets = targets };
        }

        var (setList, where) = SplitUpdateSetAndWhere(match.Groups["set"].Value);
        return new InsertConflictClause
        {
            Policy = policy,
            Action = InsertOnConflictAction.DoUpdate,
            Targets = targets,
            Assignments = ReadDoUpdateAssignments(setList),
            Where = where.Length == 0 ? null : where,
        };
    }

    /// <summary>The statement's <c>INSERT OR …</c> policy, or <see cref="InsertConflictPolicy.None"/>.</summary>
    private static InsertConflictPolicy ReadInsertOrPolicy(string statement)
    {
        var policyMatch = InsertOrPolicyRegex.Match(statement);
        if (!policyMatch.Success)
        {
            return InsertConflictPolicy.None;
        }

        return policyMatch.Groups[1].Value.ToUpperInvariant() switch
        {
            "REPLACE" => InsertConflictPolicy.Replace,
            "IGNORE" => InsertConflictPolicy.Ignore,
            "FAIL" => InsertConflictPolicy.Fail,
            _ => InsertConflictPolicy.Abort, // ABORT, and ROLLBACK (no statement-level transaction here)
        };
    }

    /// <summary>The <c>col = expression</c> pairs of a DO UPDATE SET list.</summary>
    private static List<KeyValuePair<string, string>> ReadDoUpdateAssignments(string setList)
    {
        var assignments = new List<KeyValuePair<string, string>>();
        foreach (var assignment in SqlInPredicate.SplitTopLevelComma(setList))
        {
            int eq = assignment.IndexOf('=');
            if (eq <= 0)
            {
                throw new InvalidOperationException($"Invalid DO UPDATE assignment: {assignment}");
            }

            assignments.Add(new(SqlIdentifierScanner.IdentifierText(assignment[..eq].Trim()), assignment[(eq + 1)..].Trim()));
        }

        return assignments;
    }

    /// <summary>
    /// Applies the conflict clause to one row about to be inserted. Returns <c>true</c> when the row was handled and must
    /// not be inserted (skipped, or merged into the existing row by DO UPDATE); <c>false</c> to insert it — no conflict,
    /// or REPLACE removed the conflicting rows. ABORT/FAIL, and a conflict the clause does not cover, raise the error.
    /// </summary>
    private static bool ResolveInsertConflict(
        ITable table,
        Dictionary<string, object> row,
        InsertConflictClause clause,
        List<Dictionary<string, object>>? returningRows,
        ref int changes)
    {
        if (!TryDetectInsertConflict(table, row, clause.Targets, out var conflictingKey, out var message))
        {
            return false;
        }

        switch (clause.Action)
        {
            case InsertOnConflictAction.DoNothing:
                return true;
            case InsertOnConflictAction.DoUpdate when conflictingKey is not null:
                if (ApplyDoUpdate(table, conflictingKey, row, clause))
                {
                    changes++;
                    if (returningRows is not null && table.FindByPrimaryKey(conflictingKey) is { } updated)
                    {
                        returningRows.Add(new Dictionary<string, object>(updated, StringComparer.OrdinalIgnoreCase));
                    }
                }

                return true;
        }

        switch (clause.Policy)
        {
            case InsertConflictPolicy.Ignore:
                return true;
            case InsertConflictPolicy.Replace when conflictingKey is not null:
                DeleteConflictingRows(table, row, conflictingKey);
                return false;
            default:
                throw new InvalidOperationException(string.IsNullOrEmpty(message) ? "UNIQUE constraint failed" : message);
        }
    }

    /// <summary>
    /// REPLACE: deletes every row the new one conflicts with — a PRIMARY KEY and a UNIQUE conflict can be two rows.
    /// </summary>
    private static void DeleteConflictingRows(ITable table, Dictionary<string, object> row, object conflictingKey)
    {
        object? next = conflictingKey;
        for (int guard = 0; guard < 64 && next is not null && table.DeleteByPrimaryKey(next); guard++)
        {
            TryDetectInsertConflict(table, row, [], out next, out _);
        }
    }

    /// <summary>
    /// Runs a DO UPDATE on the conflicting row: each assignment is an expression over the existing row, with
    /// <c>excluded.col</c> the incoming value; the optional WHERE decides whether the row is updated at all.
    /// </summary>
    private static bool ApplyDoUpdate(ITable table, object conflictingKey, Dictionary<string, object> incoming, InsertConflictClause clause)
    {
        var existing = table.FindByPrimaryKey(conflictingKey);
        if (existing is null)
        {
            return false;
        }

        var scope = new Dictionary<string, object>(existing, StringComparer.OrdinalIgnoreCase);
        foreach (var (column, value) in incoming)
        {
            scope["excluded." + column] = value;
        }

        if (clause.Where is { } where &&
            (!SqlScalarExpression.TryEvaluateFilter(where, scope, out var matches) ? !EvaluateDoUpdateWhere(where, existing, incoming, table) : !matches))
        {
            return false;
        }

        var updates = new Dictionary<string, object>();
        foreach (var (column, expression) in clause.Assignments)
        {
            int index = table.Columns.FindIndex(c => c.Equals(column, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                throw new InvalidOperationException($"Column '{column}' does not exist (ON CONFLICT DO UPDATE).");
            }

            var type = table.ColumnTypes[index];
            updates[table.Columns[index]] = (SqlScalarExpression.TryParse(expression, out var node)
                ? ConvertComputedSetValue(SqlScalarExpression.Evaluate(node, scope), type)
                : ParseSetLiteral(expression, type)) ?? DBNull.Value;
        }

        if (updates.Count == 0)
        {
            return false;
        }

        if (table is Table concrete)
        {
            return concrete.UpdateByPrimaryKey(conflictingKey, updates);
        }

        var pkColumn = table.Columns[table.PrimaryKeyIndex];
        return table.UpdateAffectedCount(Table.BuildStructuredWhereText(pkColumn, conflictingKey), updates) > 0;
    }
}
