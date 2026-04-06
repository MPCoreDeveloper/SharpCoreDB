// <copyright file="Metrics.cshtml.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SharpCoreDB.Server.Core.Observability;

namespace SharpCoreDB.Server.WebAdmin.Pages.Admin;

/// <summary>
/// Metrics page: displays a point-in-time MetricsCollector snapshot.
/// Requires admin role via WebAdmin cookie scheme.
/// </summary>
[Authorize(AuthenticationSchemes = "WebAdmin", Roles = "admin")]
public sealed class MetricsModel(MetricsCollector metricsCollector) : PageModel
{
    public MetricsSnapshot Snapshot { get; private set; } = null!;

    public void OnGet()
    {
        Snapshot = metricsCollector.GetSnapshot();
    }

    /// <summary>Formats a byte count as a human-readable string (B / KB / MB / GB).</summary>
    public static string FormatBytes(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):F2} GB",
    };
}
