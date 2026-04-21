using SharpCoreDB.WebViewer.Models;
using SharpCoreDB.WebViewer.Services;

var builder = WebApplication.CreateBuilder(args);

var webViewerOptions = builder.Configuration.GetSection(WebViewerOptions.SectionName).Get<WebViewerOptions>() ?? new WebViewerOptions();
builder.WebHost.UseUrls($"https://{webViewerOptions.BindAddress}:{webViewerOptions.HttpsPort}");

builder.Services.Configure<WebViewerOptions>(builder.Configuration.GetSection(WebViewerOptions.SectionName));
builder.Services.AddSingleton<IRecentConnectionsStore, RecentConnectionsStore>();
builder.Services.AddRazorPages();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.MapRazorPages();

await app.RunAsync().ConfigureAwait(false);
