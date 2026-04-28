// <copyright file="Program.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

// ============================================================
//  SharpCoreDB + FluentMigrator — Embedded Single-File Demo
//  Demonstrates schema migration on a .scdb database file.
// ============================================================

using FluentMigrator.Runner;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharpCoreDB;
using SharpCoreDB.Extensions.Extensions;
using SharpCoreDB.Extensions.Runner;
using SharpCoreDB.FluentMigratorDemo;

PrintHeader();

// ── 1. Create the embedded SharpCoreDB single-file database ──────────────────
var dbPath = Path.Combine(Path.GetTempPath(), $"sharpcoredb_fm_demo_{Guid.NewGuid():N}.scdb");
Console.WriteLine($"Database file: {dbPath}");
Console.WriteLine();

var services = new ServiceCollection();
services.AddSharpCoreDB();
var bootstrapProvider = services.BuildServiceProvider();
var factory = bootstrapProvider.GetRequiredService<DatabaseFactory>();

var dbOptions = DatabaseOptions.CreateSingleFileDefault(enableEncryption: false);
var database = factory.CreateWithOptions(dbPath, masterPassword: "demo-password", dbOptions);

// ── 2. Register SharpCoreDB + FluentMigrator DI ──────────────────────────────
var container = new ServiceCollection();

// Register the IDatabase instance so the FluentMigrator executor can resolve it
container.AddSingleton(database);

// Register SharpCoreDB core services
container.AddSharpCoreDB();

// Register FluentMigrator with the SharpCoreDB processor.
// AddSQLite() registers the SQLite SQL generator that SharpCoreDB uses internally.
// Migrations are discovered from this assembly automatically.
container.AddSharpCoreDBFluentMigrator(runner =>
    runner.AddSQLite()
          .ScanIn(typeof(M001_InitialSchema).Assembly).For.Migrations());

// Add console logging so migration progress is visible
container.AddLogging(logging =>
{
    logging.AddConsole();
    logging.SetMinimumLevel(LogLevel.Information);
});

var serviceProvider = container.BuildServiceProvider();

// ── 3. Run MigrateUp — applies all pending migrations ────────────────────────
Console.WriteLine("=== Running Migrations ===");
Console.WriteLine();

var migrationRunner = serviceProvider.GetRequiredService<ISharpCoreDbMigrationRunner>();
migrationRunner.MigrateUp();

Console.WriteLine();
Console.WriteLine("=== Migrations complete ===");
Console.WriteLine();

// ── 4. Query the seeded data ──────────────────────────────────────────────────
Console.WriteLine("=== Querying Products ===");
Console.WriteLine();

var products = database.ExecuteQuery("SELECT * FROM Products");
var categories = database.ExecuteQuery("SELECT * FROM Categories");
var categoryMap = categories.ToDictionary(
    r => r["Id"]?.ToString() ?? string.Empty,
    r => r["Name"]?.ToString() ?? string.Empty);

Console.WriteLine($"{"Id",-4} {"Product",-25} {"Category",-15} {"Price",10} {"Stock",6}");
Console.WriteLine(new string('-', 65));

foreach (var row in products)
{
    var catId = row["CategoryId"]?.ToString() ?? string.Empty;
    var catName = categoryMap.TryGetValue(catId, out var cn) ? cn : "?";
    Console.WriteLine($"{row["Id"],-4} {row["Name"],-25} {catName,-15} {row["Price"],10} {row["Stock"],6}");
}

Console.WriteLine();

// ── 5. Verify the __SharpMigrations version table ────────────────────────────
Console.WriteLine("=== Applied Migrations ===");
Console.WriteLine();

var appliedMigrations = database.ExecuteQuery("SELECT * FROM __SharpMigrations");
Console.WriteLine($"{"Version",-10} {"Description",-40} {"AppliedOn"}");
Console.WriteLine(new string('-', 75));

foreach (var row in appliedMigrations)
{
    Console.WriteLine($"{row["Version"],-10} {row["Description"],-40} {row["AppliedOn"]}");
}

Console.WriteLine();

// ── 7. Cleanup ────────────────────────────────────────────────────────────────
(database as IDisposable)?.Dispose();

try { File.Delete(dbPath); } catch { /* ignore cleanup errors */ }

Console.WriteLine("=== Demo complete ===");

static void PrintHeader()
{
    Console.WriteLine("╔══════════════════════════════════════════════════╗");
    Console.WriteLine("║   SharpCoreDB + FluentMigrator — Embedded Demo   ║");
    Console.WriteLine("║   Single-file .scdb with quoted identifiers       ║");
    Console.WriteLine("╚══════════════════════════════════════════════════╝");
    Console.WriteLine();
}
