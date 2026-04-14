using Microsoft.AspNetCore.Mvc;

namespace SharpCoreDB.CrudApp.Controllers;

/// <summary>
/// Serves the home page for the CRUD showcase.
/// </summary>
public sealed class HomeController : Controller
{
    /// <summary>
    /// Displays the home page.
    /// </summary>
    public IActionResult Index() => View();

    /// <summary>
    /// Displays a generic error view.
    /// </summary>
    public IActionResult Error() => View();
}
