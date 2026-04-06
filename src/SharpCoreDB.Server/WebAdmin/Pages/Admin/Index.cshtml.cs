// <copyright file="Index.cshtml.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SharpCoreDB.Server.Core.Observability;

namespace SharpCoreDB.Server.WebAdmin.Pages.Admin;

/// <summary>
/// Dashboard page: server health summary, uptime, and per-check status.
/// Requires admin role via WebAdmin cookie scheme.
/// </summary>
[Authorize(AuthenticationSchemes = "WebAdmin", Roles = "admin")]
public sealed class IndexModel(HealthCheckService healthCheckService) : PageModel
{
    public ServerHealthInfo Health { get; private set; } = null!;

    public string UptimeDisplay { get; private set; } = string.Empty;

    public void OnGet()
    {
        Health = healthCheckService.GetDetailedHealth();
        UptimeDisplay = FormatUptime(Health.UptimeSeconds);
    }

    private static string FormatUptime(long seconds)
    {
        var ts = TimeSpan.FromSeconds(seconds);
        if (ts.TotalDays >= 1) return $"{(int)ts.TotalDays}d {ts.Hours}h {ts.Minutes}m";
        if (ts.TotalHours >= 1) return $"{(int)ts.TotalHours}h {ts.Minutes}m";
        return $"{ts.Minutes}m {ts.Seconds}s";
    }
}
