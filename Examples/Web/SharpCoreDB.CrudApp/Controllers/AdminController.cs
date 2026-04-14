using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SharpCoreDB.CrudApp.Services;

namespace SharpCoreDB.CrudApp.Controllers;

/// <summary>
/// Provides development-only administrative actions.
/// </summary>
[Authorize]
public sealed class AdminController(IWebHostEnvironment environment, SharpCoreCrudDatabaseService databaseService) : Controller
{
    private readonly IWebHostEnvironment _environment = environment ?? throw new ArgumentNullException(nameof(environment));
    private readonly SharpCoreCrudDatabaseService _databaseService = databaseService ?? throw new ArgumentNullException(nameof(databaseService));

    /// <summary>
    /// Displays reset confirmation page.
    /// </summary>
    [HttpGet]
    public IActionResult ResetDatabase()
    {
        if (!IsDevelopmentAdmin())
        {
            return NotFound();
        }

        return View();
    }

    /// <summary>
    /// Resets the encrypted database and seeds baseline records.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetDatabaseConfirmed(CancellationToken cancellationToken)
    {
        if (!IsDevelopmentAdmin())
        {
            return NotFound();
        }

        await _databaseService.ResetDatabaseAsync(seedAdminUser: true, cancellationToken).ConfigureAwait(false);
        TempData["SuccessMessage"] = "Database reset completed. Default admin account was seeded.";
        return RedirectToAction(nameof(ResetDatabase));
    }

    private bool IsDevelopmentAdmin()
    {
        if (!_environment.IsDevelopment())
        {
            return false;
        }

        return string.Equals(User.Identity?.Name, "admin", StringComparison.OrdinalIgnoreCase);
    }
}
