using System.Data;
using OrchardCore.Modules;
using SharpCoreDB.Provider.YesSql;

var builder = WebApplication.CreateBuilder(args);

// Ensure App_Data folder exists for OrchardCore data
var sitePath = Path.Combine(builder.Environment.ContentRootPath, "App_Data", "Sites", "Default");
Directory.CreateDirectory(sitePath);

// Connection string for SharpCoreDB single-file storage
var defaultConnection = $"Data Source={Path.Combine(sitePath, "DemoSharpCoreDB.scdb")};Password=orchardcore";
var connectionString = builder.Configuration.GetConnectionString("OrchardCore") ?? defaultConnection;

// Register SharpCoreDB provider factory BEFORE OrchardCore
// This makes SharpCoreDB available as a database provider
System.Diagnostics.Debug.WriteLine("Registering SharpCoreDB provider factory...");
SharpCoreDbConfigurationExtensions.RegisterProviderFactory();

// Pre-create the database file directory
System.Diagnostics.Debug.WriteLine("Ensuring database directory exists...");
SharpCoreDbSetupHelper.EnsureDatabaseFileExists(defaultConnection);

// Configure OrchardCore CMS
// Let OrchardCore handle IStore registration via its shell system
System.Diagnostics.Debug.WriteLine("Starting AddOrchardCms...");
builder.Services.AddOrchardCms();

System.Diagnostics.Debug.WriteLine("Building web application...");
var app = builder.Build();
System.Diagnostics.Debug.WriteLine("Web application built successfully.");

// Configure HTTP request pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

// Use OrchardCore middleware
app.UseOrchardCore();

await app.RunAsync();

