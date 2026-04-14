using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SharpCoreDB;
using SharpCoreDB.CrudApp.Configuration;
using SharpCoreDB.CrudApp.Data;
using SharpCoreDB.CrudApp.Services;
using SharpCoreDB.EntityFrameworkCore;
using SharpCoreDB.Identity;
using SharpCoreDB.Identity.Options;
using SharpCoreDB.Identity.Security;
using SharpCoreDB.Identity.Storage;
using SharpCoreDB.Interfaces;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

builder.Services.Configure<SharpCoreDbAppOptions>(builder.Configuration.GetSection(SharpCoreDbAppOptions.SectionName));
builder.Services.Configure<SharpCoreIdentityOptions>(builder.Configuration.GetSection("SharpCoreIdentity"));

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/Login";
        options.SlidingExpiration = true;
    });

builder.Services.AddAuthorization();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSharpCoreDB();

builder.Services.AddSingleton<SharpCoreDbPasswordHasher>();
builder.Services.AddSingleton<IdentityDatabaseInitializer>();
builder.Services.AddSingleton<SharpCoreCrudDatabaseService>();

builder.Services.AddScoped<IDatabase>(serviceProvider =>
{
    var databaseService = serviceProvider.GetRequiredService<SharpCoreCrudDatabaseService>();
    return databaseService.CreateDatabase();
});

builder.Services.AddScoped<SharpCoreDbIdentityService>();
builder.Services.AddScoped<ProductCrudService>();

builder.Services.AddDbContext<SharpCoreCrudDbContext>((serviceProvider, optionsBuilder) =>
{
    var dbOptions = serviceProvider.GetRequiredService<IOptions<SharpCoreDbAppOptions>>().Value;
    var databaseFilePath = Path.IsPathRooted(dbOptions.DatabaseFilePath)
        ? dbOptions.DatabaseFilePath
        : Path.Combine(AppContext.BaseDirectory, dbOptions.DatabaseFilePath);

    var connectionString = $"Data Source={databaseFilePath};Password={dbOptions.EncryptionPassword};Encryption=Full";
    optionsBuilder.UseSharpCoreDB(connectionString);
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var databaseService = scope.ServiceProvider.GetRequiredService<SharpCoreCrudDatabaseService>();
    await databaseService.EnsureInitializedAsync(seedAdminUser: app.Environment.IsDevelopment()).ConfigureAwait(false);

    var dbContext = scope.ServiceProvider.GetRequiredService<SharpCoreCrudDbContext>();
    await dbContext.Database.EnsureCreatedAsync().ConfigureAwait(false);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

await app.RunAsync().ConfigureAwait(false);
