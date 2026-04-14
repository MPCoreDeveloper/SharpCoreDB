using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using SharpCoreDB.CrudApp.Models.ViewModels;
using SharpCoreDB.Identity;
using SharpCoreDB.Identity.Entities;
using SharpCoreDB.Identity.Options;

namespace SharpCoreDB.CrudApp.Controllers;

/// <summary>
/// Handles authentication actions backed by SharpCoreDB.Identity.
/// </summary>
public sealed class AccountController(SharpCoreDbIdentityService identityService) : Controller
{
    private readonly SharpCoreDbIdentityService _identityService = identityService ?? throw new ArgumentNullException(nameof(identityService));

    /// <summary>
    /// Shows the registration page.
    /// </summary>
    [HttpGet]
    public IActionResult Register() => View(new RegisterViewModel());

    /// <summary>
    /// Creates a new user account.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        try
        {
            var user = new SharpCoreUser
            {
                UserName = model.UserName,
                NormalizedUserName = string.Empty,
                Email = model.Email,
                NormalizedEmail = string.Empty,
                PasswordHash = string.Empty,
                FullName = model.FullName,
                BirthDate = model.BirthDate,
                IsActive = true,
                EmailConfirmed = true
            };

            var createdUser = await _identityService.CreateUserAsync(user, model.Password, cancellationToken).ConfigureAwait(false);
            await SignInUserAsync(createdUser, isPersistent: false).ConfigureAwait(false);

            TempData["SuccessMessage"] = "Registration successful.";
            return RedirectToAction("Index", "Products");
        }
        catch (ArgumentException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(model);
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(model);
        }
    }

    /// <summary>
    /// Shows the login page.
    /// </summary>
    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        ViewData[nameof(returnUrl)] = returnUrl;
        return View(new LoginViewModel());
    }

    /// <summary>
    /// Authenticates an existing account.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (!ModelState.IsValid)
        {
            ViewData[nameof(returnUrl)] = returnUrl;
            return View(model);
        }

        var signInResult = await _identityService.PasswordSignInAsync(model.UserName, model.Password, true, cancellationToken).ConfigureAwait(false);
        if (!signInResult.Succeeded)
        {
            if (signInResult.IsLockedOut)
            {
                ModelState.AddModelError(string.Empty, "User is temporarily locked out.");
            }
            else if (signInResult.IsNotAllowed)
            {
                ModelState.AddModelError(string.Empty, "User is not allowed to sign in.");
            }
            else
            {
                ModelState.AddModelError(string.Empty, "Invalid login attempt.");
            }

            ViewData[nameof(returnUrl)] = returnUrl;
            return View(model);
        }

        var user = await _identityService.FindByNameAsync(model.UserName, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            ModelState.AddModelError(string.Empty, "User not found.");
            ViewData[nameof(returnUrl)] = returnUrl;
            return View(model);
        }

        await SignInUserAsync(user, model.RememberMe).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }

        return RedirectToAction("Index", "Products");
    }

    /// <summary>
    /// Signs out the current user.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme).ConfigureAwait(false);
        return RedirectToAction("Index", "Home");
    }

    private async Task SignInUserAsync(SharpCoreUser user, bool isPersistent)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString("D")),
            new(ClaimTypes.Name, user.UserName),
            new(ClaimTypes.Email, user.Email ?? string.Empty)
        };

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties { IsPersistent = isPersistent })
            .ConfigureAwait(false);
    }
}
