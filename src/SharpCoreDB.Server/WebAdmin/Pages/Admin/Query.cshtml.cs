// <copyright file="Query.cshtml.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using SharpCoreDB.Server.Core;

namespace SharpCoreDB.Server.WebAdmin.Pages.Admin;

/// <summary>
/// Guarded ad-hoc query page. All executions are logged. Admin only.
/// Truncates results at 500 rows to prevent unbounded response sizes.
/// </summary>
[Authorize(AuthenticationSchemes = "WebAdmin", Roles = "admin")]
public sealed class QueryModel(
    DatabaseRegistry registry,
    IOptions<ServerConfiguration> config,
    ILogger<QueryModel> logger) : PageModel
{
    private const int MaxRows = 500;

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public IReadOnlyList<string> DatabaseNames { get; private set; } = [];
    public string? ErrorMessage { get; private set; }
    public QueryResult? Result { get; private set; }

    public void OnGet()
    {
        LoadDatabaseNames();
        if (DatabaseNames.Count > 0 && string.IsNullOrEmpty(Input.Database))
        {
            Input = Input with { Database = DatabaseNames[0] };
        }
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken = default)
    {
        LoadDatabaseNames();

        if (!ModelState.IsValid)
        {
            return Page();
        }

        if (string.IsNullOrWhiteSpace(Input.Sql))
        {
            ErrorMessage = "SQL is required.";
            return Page();
        }

        var db = registry.GetDatabase(Input.Database);
        if (db is null)
        {
            ErrorMessage = $"Database '{Input.Database}' is not available.";
            return Page();
        }

        logger.LogWarning(
            "Web admin query executed by '{User}' on database '{Database}': {Sql}",
            User.Identity?.Name, Input.Database, Input.Sql);

        var sw = Stopwatch.StartNew();
        try
        {
            await using var connection = await db.GetConnectionAsync(cancellationToken).ConfigureAwait(false);
            var rows = connection.Database.ExecuteQuery(Input.Sql, []);
            sw.Stop();

            if (rows.Count == 0)
            {
                Result = new QueryResult([], [], 0, sw.ElapsedMilliseconds, false);
                return Page();
            }

            var columns = rows[0].Keys.ToArray();
            var truncated = rows.Count > MaxRows;
            var displayRows = rows
                .Take(MaxRows)
                .Select(r => columns.Select(c => r.TryGetValue(c, out var v) ? v?.ToString() : null).ToArray())
                .ToArray();

            Result = new QueryResult(columns, displayRows, truncated ? MaxRows : rows.Count, sw.ElapsedMilliseconds, truncated);
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.LogError(ex, "Web admin query failed on database '{Database}'", Input.Database);
            ErrorMessage = ex.Message;
        }

        return Page();
    }

    private void LoadDatabaseNames()
    {
        DatabaseNames = config.Value.Databases
            .Where(d => !d.IsReadOnly || true) // admin may query read-only databases too
            .Select(d => d.Name)
            .ToList();
    }

    public sealed record InputModel
    {
        [Required]
        public string Database { get; init; } = string.Empty;

        public string Sql { get; init; } = string.Empty;
    }

    public sealed record QueryResult(
        string[] Columns,
        string?[][] Rows,
        int RowCount,
        long ElapsedMs,
        bool Truncated);
}
