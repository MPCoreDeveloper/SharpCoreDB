// <copyright file="Databases.cshtml.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using SharpCoreDB.Server.Core;

namespace SharpCoreDB.Server.WebAdmin.Pages.Admin;

/// <summary>
/// Databases page: shows all configured databases with runtime status.
/// Requires admin role via WebAdmin cookie scheme.
/// </summary>
[Authorize(AuthenticationSchemes = "WebAdmin", Roles = "admin")]
public sealed class DatabasesModel(
    DatabaseRegistry registry,
    IOptions<ServerConfiguration> config) : PageModel
{
    public IReadOnlyList<DatabaseRow> Databases { get; private set; } = [];

    public void OnGet()
    {
        Databases = config.Value.Databases
            .Select(d => new DatabaseRow(
                Name: d.Name,
                StorageMode: d.StorageMode,
                Path: d.DatabasePath,
                Encrypted: d.EncryptionEnabled,
                IsSystem: d.IsSystemDatabase,
                IsReadOnly: d.IsReadOnly,
                IsOnline: registry.DatabaseExists(d.Name)))
            .ToList();
    }

    /// <summary>View model row for a single database entry.</summary>
    public sealed record DatabaseRow(
        string Name,
        string StorageMode,
        string Path,
        bool Encrypted,
        bool IsSystem,
        bool IsReadOnly,
        bool IsOnline);
}
