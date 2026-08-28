// <copyright file="Program.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License.
// </copyright>

// Native AOT smoke test for SharpCoreDB v2.
//
// Publish (Windows x64):
//   dotnet publish tools/SharpCoreDB.AotSmoke -c Release -r win-x64 -p:PublishAot=true
// Run (JIT):
//   dotnet run -c Release --project tools/SharpCoreDB.AotSmoke
//
// Exercises the core paths that must work under Native AOT:
//   CREATE TABLE / CREATE INDEX, InsertBatch, parameterized ExecuteQuery,
//   and the zero-allocation ExecuteQueryStruct fast path.

using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB;

var dbPath = Path.Combine(Path.GetTempPath(), $"scdb-aot-smoke-{Guid.NewGuid()}");

try
{
    var services = new ServiceCollection();
    services.AddSharpCoreDB();
    var sp = services.BuildServiceProvider();
    var factory = sp.GetRequiredService<DatabaseFactory>();

    var config = new DatabaseConfig
    {
        NoEncryptMode = true,
        EnableHashIndexes = true,
        EnableQueryCache = true,
        EnableCompiledPlanCache = false, // avoid the Lambda.Compile path under AOT
        EnableBTreeSelection = false,    // avoid the B-tree reflection path under AOT
        SqlValidationMode = SharpCoreDB.Services.SqlQueryValidator.ValidationMode.Disabled
    };

    using var db = (SharpCoreDB.Database)factory.Create(dbPath, "aot123", isReadOnly: false, config: config);

    db.ExecuteSQL("CREATE TABLE docs (name TEXT NOT NULL, email TEXT, age INTEGER, score REAL, data TEXT)");
    db.ExecuteSQL("CREATE INDEX idx_docs_name ON docs(name)");

    var batch = new List<Dictionary<string, object>>(1000);
    for (int i = 0; i < 1000; i++)
    {
        batch.Add(new Dictionary<string, object>
        {
            ["name"] = $"User{i}",
            ["email"] = $"user{i}@test.com",
            ["age"] = 20 + i % 60,
            ["score"] = i * 0.1,
            ["data"] = $"payload-{i}"
        });
    }

    db.InsertBatch("docs", batch);
    db.Flush();

    // Dictionary-returning query path (plan-cache fast path).
    var found = db.ExecuteQuery(
        "SELECT * FROM docs WHERE name = @name",
        new Dictionary<string, object?> { ["@name"] = "User42" });

    if (found.Count != 1 || !Equals(found[0]["name"], "User42"))
    {
        Console.WriteLine("FAIL: ExecuteQuery returned the wrong result.");
        return 1;
    }

    // Zero-allocation StructRow fast path.
    int matched = 0;
    foreach (var row in db.ExecuteQueryStruct(
        "SELECT * FROM docs WHERE name = @name",
        new Dictionary<string, object?> { ["@name"] = "User7" }))
    {
        matched++;
    }

    if (matched != 1)
    {
        Console.WriteLine("FAIL: ExecuteQueryStruct returned the wrong result.");
        return 1;
    }

    // Full-scan StructRow path.
    int all = 0;
    foreach (var row in db.ExecuteQueryStruct("SELECT * FROM docs"))
    {
        all++;
    }

    if (all != 1000)
    {
        Console.WriteLine($"FAIL: Full scan returned {all} rows (expected 1000).");
        return 1;
    }

    db.Flush();

    // Reopen the database to exercise LoadMetadata (metadata JSON round-trip) under AOT.
    db.Dispose();
    using (var reopened = (SharpCoreDB.Database)factory.Create(dbPath, "aot123", isReadOnly: false, config: config))
    {
        var reopenedCount = reopened.ExecuteQueryStruct("SELECT * FROM docs").Count();
        if (reopenedCount != 1000)
        {
            Console.WriteLine($"FAIL: Reopen returned {reopenedCount} rows (expected 1000).");
            return 1;
        }
    }

    Console.WriteLine("PASS: SharpCoreDB Native AOT smoke test OK (1000 inserts, point lookup, StructRow point + full scan, reopen).");
    return 0;
}
finally
{
    try
    {
        if (Directory.Exists(dbPath))
        {
            Directory.Delete(dbPath, true);
        }
    }
    catch
    {
        // Best-effort cleanup.
    }
}
