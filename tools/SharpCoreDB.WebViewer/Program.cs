using SafeWebCore.Extensions;
using SharpCoreDB;
using SharpCoreDB.WebViewer.Models;
using SharpCoreDB.WebViewer.Services;

var builder = WebApplication.CreateBuilder(args);

var webViewerOptions = builder.Configuration.GetSection(WebViewerOptions.SectionName).Get<WebViewerOptions>() ?? new WebViewerOptions();
builder.WebHost.UseUrls($"https://{webViewerOptions.BindAddress}:{webViewerOptions.HttpsPort}");

builder.Services.Configure<WebViewerOptions>(builder.Configuration.GetSection(WebViewerOptions.SectionName));
builder.Services.AddHttpContextAccessor();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.Cookie.Name = ".SharpCoreDB.WebViewer.Session";
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.IdleTimeout = TimeSpan.FromMinutes(20);
});

const string CspSelf = "'self'";

builder.Services.AddNetSecureHeadersStrictAPlus(options =>
{
    options.Csp = options.Csp with
    {
        StyleSrc = CspSelf,
        StyleSrcElem = CspSelf,
        ImgSrc = $"{CspSelf} data:",
        FontSrc = CspSelf,
        ConnectSrc = CspSelf
    };
});
builder.Services.AddSharpCoreDB();
builder.Services.AddSingleton<IRecentConnectionsStore, RecentConnectionsStore>();
builder.Services.AddSingleton<IQueryWorkspaceStore, QueryWorkspaceStore>();
builder.Services.AddScoped<IViewerConnectionService, ViewerConnectionService>();
builder.Services.AddScoped<IViewerTransactionService, ViewerTransactionService>();
builder.Services.AddScoped<IMetadataService, MetadataService>();
builder.Services.AddScoped<IViewerQueryService, ViewerQueryService>();
builder.Services.AddRazorPages();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseNetSecureHeaders();
app.UseStaticFiles();
app.UseRouting();
app.UseSession();
app.MapRazorPages();

await app.RunAsync().ConfigureAwait(false);
