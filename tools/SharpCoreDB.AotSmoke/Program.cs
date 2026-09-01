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
//   the zero-allocation ExecuteQueryStruct fast path, reopen, single-file
//   full VACUUM (issue #343), and full at-rest encryption + password/key
//   rotation (PBKDF2/AES-GCM envelope key model).

using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB;

// Smoke-test tool: throwaway local password, never a real credential.
const string AotPassword = "aot123"; // NOSONAR:S2068 - intentional test fixture value

var services = new ServiceCollection();
services.AddSharpCoreDB();
var sp = services.BuildServiceProvider();
var factory = sp.GetRequiredService<DatabaseFactory>();
return await RunAotSmokeAsync(factory);

/// <summary>
/// Executes the AOT smoke scenario: CREATE TABLE / CREATE INDEX, InsertBatch, parameterized
/// ExecuteQuery, the zero-allocation ExecuteQueryStruct fast path, reopen, single-file full
/// VACUUM (issue #343), and full at-rest encryption + password/key rotation.
/// </summary>
static async Task<int> RunAotSmokeAsync(DatabaseFactory factory)
{
    var dbPath = Path.Combine(Path.GetTempPath(), $"scdb-aot-smoke-{Guid.NewGuid()}");
    var scdbPath = Path.Combine(Path.GetTempPath(), $"scdb-aot-smoke-{Guid.NewGuid()}.scdb");

    try
    {
        if (await RunDataScenarioAsync(factory, dbPath) != 0)
        {
            return 1;
        }

        // Issue #343: full VACUUM on a single-file (.scdb) database must survive .NET trimming /
        // Native AOT. Previously the stream swap used reflection (GetField on a private readonly
        // field), which returns null under AOT and crashed with ObjectDisposedException. The row
        // cache JSON serialization is AOT-safe through the source-generated SingleFileTableJsonContext.
        if (await RunVacuumScenarioAsync(factory, scdbPath) != 0)
        {
            return 1;
        }

        // Full at-rest encryption + password/key rotation must also be Native AOT safe:
        // the envelope key model uses PBKDF2/AES-GCM, no reflection, no dynamic, no Expression.
        if (await RunEncryptionScenarioAsync(factory) != 0)
        {
            return 1;
        }

        Console.WriteLine("PASS: SharpCoreDB Native AOT smoke test OK (1000 inserts, point lookup, StructRow point + full scan, reopen, full vacuum, full-at-rest encryption + password/key rotation).");
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

        try
        {
            if (File.Exists(scdbPath))
            {
                File.Delete(scdbPath);
            }
        }
        catch
        {
            // Best-effort cleanup.
        }
    }
}

/// <summary>
/// Exercises CREATE TABLE / CREATE INDEX, batch INSERT, parameterized ExecuteQuery,
/// the zero-allocation ExecuteQueryStruct fast path (point + full scan) and reopen under
/// Native AOT. Returns 0 on success.
/// </summary>
static async Task<int> RunDataScenarioAsync(DatabaseFactory factory, string dbPath)
{
    var config = new DatabaseConfig
    {
        NoEncryptMode = true,
        EnableHashIndexes = true,
        EnableQueryCache = true,
        EnableCompiledPlanCache = false, // avoid the Lambda.Compile path under AOT
        EnableBTreeSelection = false,    // avoid the B-tree reflection path under AOT
        SqlValidationMode = SharpCoreDB.Services.SqlQueryValidator.ValidationMode.Disabled
    };

    await using var db = (SharpCoreDB.Database)factory.Create(dbPath, AotPassword, isReadOnly: false, config: config);

    await db.ExecuteSQLAsync("CREATE TABLE docs (name TEXT NOT NULL, email TEXT, age INTEGER, score REAL, data TEXT)");
    await db.ExecuteSQLAsync("CREATE INDEX idx_docs_name ON docs(name)");

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

    await db.InsertBatchAsync("docs", batch);
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
    await using (var reopened = (SharpCoreDB.Database)factory.Create(dbPath, AotPassword, isReadOnly: false, config: config))
    {
        var reopenedCount = reopened.ExecuteQueryStruct("SELECT * FROM docs").Count();
        if (reopenedCount != 1000)
        {
            Console.WriteLine($"FAIL: Reopen returned {reopenedCount} rows (expected 1000).");
            return 1;
        }
    }

    return 0;
}

/// <summary>
/// Exercises a full VACUUM on a single-file (.scdb) database, which must survive .NET trimming /
/// Native AOT (issue #343). Returns 0 on success.
/// </summary>
static async Task<int> RunVacuumScenarioAsync(DatabaseFactory factory, string scdbPath)
{
    var scdbOptions = SharpCoreDB.DatabaseOptions.CreateSingleFileDefault();
    await using (var scdb = factory.CreateWithOptions(scdbPath, AotPassword, scdbOptions))
    {
        await scdb.ExecuteSQLAsync("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT)");
        var inserts = new List<string>(50);
        for (int i = 0; i < 50; i++)
        {
            inserts.Add($"INSERT INTO t (id, name) VALUES ({i}, 'User{i}')");
        }
        await scdb.ExecuteBatchSQLAsync(inserts);
        scdb.Flush();

        var vacuum = await scdb.VacuumAsync(VacuumMode.Full, CancellationToken.None);
        if (!vacuum.Success)
        {
            Console.WriteLine($"FAIL: VacuumAsync(VacuumMode.Full) failed: {vacuum.ErrorMessage}");
            return 1;
        }

        var rowsAfter = scdb.ExecuteQuery("SELECT * FROM t");
        if (rowsAfter.Count != 50)
        {
            Console.WriteLine($"FAIL: After full vacuum expected 50 rows, got {rowsAfter.Count}.");
            return 1;
        }
    }

    return 0;
}

/// <summary>
/// Exercises full at-rest encryption (password + key rotation, encrypted reopen + full vacuum)
/// under Native AOT. Returns 0 on success.
/// </summary>
static async Task<int> RunEncryptionScenarioAsync(DatabaseFactory factory)
{
    var encryptedScdbPath = Path.Combine(Path.GetTempPath(), $"scdb-aot-smoke-{Guid.NewGuid():N}.scdb");
    var encryptedOptions = new SharpCoreDB.DatabaseOptions
    {
        StorageMode = StorageMode.SingleFile,
        EnableEncryption = true,
        EncryptionPassword = "aot-password",
        CreateImmediately = true,
    };

    await using (var enc = factory.CreateWithOptions(encryptedScdbPath, AotPassword, encryptedOptions))
    {
        await enc.ExecuteSQLAsync("CREATE TABLE s (id INTEGER PRIMARY KEY, secret TEXT)");
        await enc.ExecuteSQLAsync("INSERT INTO s VALUES (1, 'classified-under-aot')");
        enc.ForceSave();

        // Password rotation — O(1) re-wrap of the same DEK.
        var passwordChanged = await enc.ChangeEncryptionPasswordAsync("aot-password-rotated");
        if (!passwordChanged.Success)
        {
            Console.WriteLine($"FAIL: ChangeEncryptionPasswordAsync: {passwordChanged.ErrorMessage}");
            return 1;
        }

        // Full DEK rotation — re-encrypts every block + registry + FSM + WAL.
        var rekeyed = await enc.RotateEncryptionKeyAsync(newPassword: "aot-password-final");
        if (!rekeyed.Success)
        {
            Console.WriteLine($"FAIL: RotateEncryptionKeyAsync: {rekeyed.ErrorMessage}");
            return 1;
        }

        if (rekeyed.BlocksReEncrypted <= 0)
        {
            Console.WriteLine("FAIL: RotateEncryptionKeyAsync reported 0 re-encrypted blocks.");
            return 1;
        }

        var encRows = enc.ExecuteQuery("SELECT * FROM s");
        if (encRows.Count != 1 || !Equals(encRows[0]["secret"], "classified-under-aot"))
        {
            Console.WriteLine("FAIL: Encrypted read after rotation returned the wrong result.");
            return 1;
        }
    }

    // Reopen with the rotated password and run a full VACUUM on the encrypted file.
    await using (var encReopened = factory.CreateWithOptions(encryptedScdbPath, AotPassword,
        new SharpCoreDB.DatabaseOptions
        {
            StorageMode = StorageMode.SingleFile,
            EnableEncryption = true,
            EncryptionPassword = "aot-password-final",
        }))
    {
        var reopenedEnc = encReopened.ExecuteQuery("SELECT * FROM s");
        if (reopenedEnc.Count != 1 || !Equals(reopenedEnc[0]["secret"], "classified-under-aot"))
        {
            Console.WriteLine("FAIL: Encrypted reopen returned the wrong result.");
            return 1;
        }

        var encryptedVacuum = await encReopened.VacuumAsync(VacuumMode.Full, CancellationToken.None);
        if (!encryptedVacuum.Success)
        {
            Console.WriteLine($"FAIL: Encrypted VacuumAsync(Full): {encryptedVacuum.ErrorMessage}");
            return 1;
        }
    }

    try { File.Delete(encryptedScdbPath); } catch { /* best-effort cleanup */ }

    return 0;
}
