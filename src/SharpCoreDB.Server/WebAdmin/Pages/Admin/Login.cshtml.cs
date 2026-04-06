// <copyright file="Login.cshtml.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SharpCoreDB.Server.Core;
using SharpCoreDB.Server.Core.Security;

namespace SharpCoreDB.Server.WebAdmin.Pages.Admin;

/// <summary>
/// Web admin login page. Anonymous; validates credentials and issues a WebAdmin cookie session.
/// </summary>
[AllowAnonymous]
public sealed class LoginModel(
    UserAuthenticationService authService,
    ILogger<LoginModel> logger) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? ErrorMessage { get; private set; }

    public IActionResult OnGet(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return Redirect("/admin");
        }

        ViewData["ReturnUrl"] = returnUrl;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null, CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var result = authService.Authenticate(Input.Username, Input.Password, Guid.NewGuid().ToString("N"));

        if (!result.IsAuthenticated)
        {
            logger.LogWarning("Web admin login failed for user '{Username}'", Input.Username);
            ErrorMessage = "Invalid username or password.";
            Input = Input with { Password = string.Empty };
            return Page();
        }

        if (result.Role != DatabaseRole.Admin)
        {
            logger.LogWarning("Web admin login denied: user '{Username}' has role '{Role}' (admin required)", Input.Username, result.Role);
            ErrorMessage = "Access denied. Admin role required.";
            Input = Input with { Password = string.Empty };
            return Page();
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, Input.Username),
            new(ClaimTypes.Role, "admin"),
        };

        var identity = new ClaimsIdentity(claims, "WebAdmin");
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync("WebAdmin", principal, new AuthenticationProperties
        {
            IsPersistent = false,
            ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8),
        }).ConfigureAwait(false);

        logger.LogInformation("Web admin session created for user '{Username}'", Input.Username);

        var safe = Url.IsLocalUrl(returnUrl) ? returnUrl : "/admin";
        return Redirect(safe);
    }

    public sealed record InputModel
    {
        [Required]
        public string Username { get; init; } = string.Empty;

        [Required]
        public string Password { get; init; } = string.Empty;
    }
}
