using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using SharpCoreDB.WebViewer.Models;
using SharpCoreDB.WebViewer.Services;

namespace SharpCoreDB.WebViewer.Pages;

/// <summary>
/// Hosts the web viewer landing page and startup state.
/// </summary>
public sealed class IndexModel(IRecentConnectionsStore recentConnectionsStore, IOptions<WebViewerOptions> options) : PageModel
{
    private readonly IRecentConnectionsStore _recentConnectionsStore = recentConnectionsStore;
    private readonly WebViewerOptions _options = options.Value;

    public IReadOnlyList<ConnectionProfile> RecentConnections { get; private set; } = [];

    public string EndpointDisplay => $"https://{_options.BindAddress}:{_options.HttpsPort}";

    /// <summary>
    /// Loads local viewer startup data.
    /// </summary>
    /// <returns>A task that completes when page data is prepared.</returns>
    public async Task OnGetAsync()
    {
        RecentConnections = await _recentConnectionsStore.LoadAsync(HttpContext.RequestAborted).ConfigureAwait(false);
    }
}
