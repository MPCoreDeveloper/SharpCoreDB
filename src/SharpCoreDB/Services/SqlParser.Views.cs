// <copyright file="SqlParser.Views.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Services;

using SharpCoreDB.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// SqlParser partial class for view DDL operations:
/// CREATE VIEW, CREATE MATERIALIZED VIEW, DROP VIEW.
///
/// Views are virtual tables defined by a SELECT query.
/// Querying a view rewrites the query against the base definition.
/// </summary>
public partial class SqlParser
{
    /// <summary>
    /// In-memory registry of view definitions, keyed by name.
    /// Static so views survive across SqlParser instances within the same process.
    /// </summary>
    private static readonly Dictionary<string, ViewDefinition> _views = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Lock _viewLock = new();

    /// <summary>
    /// Executes CREATE VIEW or CREATE MATERIALIZED VIEW statement.
    /// Syntax: CREATE [MATERIALIZED] VIEW name AS SELECT ...
    /// </summary>
    private void ExecuteCreateView(string sql, string[] parts, IWAL? wal)
    {
        if (isReadOnly)
            throw new InvalidOperationException("Cannot create view in readonly mode");

        bool materialized = parts[1].Equals("MATERIALIZED", StringComparison.OrdinalIgnoreCase);
        // CREATE VIEW name AS ... or CREATE MATERIALIZED VIEW name AS ...
        int nameIdx = materialized ? 3 : 2;

        if (parts.Length <= nameIdx)
            throw new ArgumentException("View name is required");

        var viewName = parts[nameIdx];

        // Find the AS keyword
        var asIdx = sql.IndexOf(" AS ", StringComparison.OrdinalIgnoreCase);
        if (asIdx < 0)
            throw new ArgumentException("CREATE VIEW requires AS clause with a SELECT query");

        var selectQuery = sql[(asIdx + 4)..].Trim().TrimEnd(';');
        if (!selectQuery.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("View definition must be a SELECT statement");

        var view = new ViewDefinition(viewName, selectQuery, materialized);

        // If materialized, eagerly compute and cache the results
        if (materialized)
        {
            var viewParts = selectQuery.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            view = view with { CachedResults = ExecuteSelectQuery(selectQuery, viewParts, false), LastRefresh = DateTime.UtcNow };
        }

        lock (_viewLock)
        {
            _views[viewName] = view;
        }
    }

    /// <summary>
    /// Executes DROP VIEW statement with optional IF EXISTS clause.
    /// Syntax: DROP VIEW [IF EXISTS] view_name
    /// </summary>
    private void ExecuteDropView(string sql, string[] parts, IWAL? wal)
    {
        if (isReadOnly)
            throw new InvalidOperationException("Cannot drop view in readonly mode");

        // ✅ Detect IF EXISTS clause
        bool ifExists = false;
        int nameIndex = 2;
        
        if (parts.Length >= 5 && parts[2].Equals("IF", StringComparison.OrdinalIgnoreCase)
            && parts[3].Equals("EXISTS", StringComparison.OrdinalIgnoreCase))
        {
            ifExists = true;
            nameIndex = 4;
        }

        if (nameIndex >= parts.Length)
            throw new ArgumentException("View name is required");

        var name = parts[nameIndex].TrimEnd(';');
        lock (_viewLock)
        {
            if (!_views.Remove(name))
            {
                if (!ifExists)
                    throw new InvalidOperationException($"View '{name}' does not exist");
                // IF EXISTS: silently skip if view doesn't exist
            }
        }
        
        wal?.Log(sql);
    }

    /// <summary>
    /// Attempts to resolve a table name to a view definition.
    /// Called from SELECT handling to transparently expand views.
    /// </summary>
    internal static ViewDefinition? TryGetView(string name)
    {
        lock (_viewLock)
        {
            return _views.TryGetValue(name, out var v) ? v : null;
        }
    }

    /// <summary>
    /// Gets all registered view names.
    /// </summary>
    public IReadOnlyList<string> GetViewNames()
    {
        lock (_viewLock)
        {
            return [.. _views.Keys];
        }
    }

    /// <summary>
    /// Checks if a view exists by name.
    /// </summary>
    public bool ViewExists(string name)
    {
        lock (_viewLock)
        {
            return _views.ContainsKey(name);
        }
    }
}

/// <summary>
/// In-memory definition of a database view (virtual table).
/// </summary>
/// <param name="Name">View name.</param>
/// <param name="SelectQuery">The SELECT query that defines the view.</param>
/// <param name="IsMaterialized">Whether results are pre-computed and cached.</param>
public sealed record ViewDefinition(string Name, string SelectQuery, bool IsMaterialized)
{
    /// <summary>Gets or sets cached results for materialized views.</summary>
    public List<Dictionary<string, object>>? CachedResults { get; init; }

    /// <summary>Gets or sets when the materialized view was last refreshed.</summary>
    public DateTime? LastRefresh { get; init; }
}
