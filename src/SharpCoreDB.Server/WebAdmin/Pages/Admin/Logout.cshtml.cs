// <copyright file="Logout.cshtml.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SharpCoreDB.Server.WebAdmin.Pages.Admin;

/// <summary>
/// POST-only logout handler. Signs out the WebAdmin cookie and redirects to login.
/// </summary>
public sealed class LogoutModel(ILogger<LogoutModel> logger) : PageModel
{
    public IActionResult OnGet() => Redirect("/admin");

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken = default)
    {
        var username = User.Identity?.Name;
        await HttpContext.SignOutAsync("WebAdmin").ConfigureAwait(false);
        logger.LogInformation("Web admin session ended for user '{Username}'", username);
        return Redirect("/admin/login");
    }
}
