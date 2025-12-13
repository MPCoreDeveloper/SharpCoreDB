// <copyright file="ComparativeUpdateDeleteBenchmarks.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using SharpCoreDB.Benchmarks.Infrastructure;
using Microsoft.Data.Sqlite;
using LiteDB;

namespace SharpCoreDB.Benchmarks.Comparative;

/// <summary>
/// Comparative benchmarks for UPDATE and DELETE operations.
/// Includes both encrypted and non-encrypted SharpCoreDB variants.
/// </summary>
[Config(typeof(BenchmarkConfig))]
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
public class ComparativeUpdateDeleteBenchmarks : IDisposable
{
    private TestDataGenerator dataGenerator = null!;
    private string tempDir = null!;
    private const int TotalRecords = 1000;
    
    private BenchmarkDatabaseHelper? sharpCoreDbEncrypted;
    private BenchmarkDatabaseHelper? sharpCoreDbNoEncrypt;
    private SqliteConnection? sqliteConn;
    private LiteDatabase? liteDb;
    private ILiteCollection<TestDataGenerator.UserRecord>? liteCollection;

    [Params(1, 10, 100)]
    public int OperationCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        dataGenerator = new TestDataGenerator();
        tempDir = Path.Combine(Path.GetTempPath(), $"dbBenchmark_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        SetupDatabases();
        PopulateDatabases();
    }

    [IterationSetup]
    public void IterationSetup()
    {
        // Ensure we have TotalRecords before each iteration
        // This handles repopulation after deletes
    }

    private void SetupDatabases()
    {
        // SharpCoreDB (Encrypted)
        var sharpCorePathEncrypted = Path.Combine(tempDir, "sharpcore_encrypted");
        sharpCoreDbEncrypted = new BenchmarkDatabaseHelper(sharpCorePathEncrypted, enableEncryption: true);
        sharpCoreDbEncrypted.CreateUsersTable();
        
        // SharpCoreDB (No Encryption)
        var sharpCorePathNoEncrypt = Path.Combine(tempDir, "sharpcore_noencrypt");
        sharpCoreDbNoEncrypt = new BenchmarkDatabaseHelper(sharpCorePathNoEncrypt, enableEncryption: false);
        sharpCoreDbNoEncrypt.CreateUsersTable();

        // SQLite
        var sqlitePath = Path.Combine(tempDir, "sqlite.db");
        sqliteConn = new SqliteConnection($"Data Source={sqlitePath}");
        sqliteConn.Open();
        
        using var cmd = sqliteConn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE users (
                id INTEGER PRIMARY KEY,
                name TEXT NOT NULL,
                email TEXT NOT NULL,
                age INTEGER NOT NULL,
                created_at TEXT NOT NULL,
                is_active INTEGER NOT NULL
            )";
        cmd.ExecuteNonQuery();

        // LiteDB
        var liteDbPath = Path.Combine(tempDir, "litedb.db");
        liteDb = new LiteDatabase(liteDbPath);
        liteCollection = liteDb.GetCollection<TestDataGenerator.UserRecord>("users");
    }

    private void PopulateDatabases()
    {
        var users = dataGenerator.GenerateUsers(TotalRecords);

        // Populate SharpCoreDB (Encrypted)
        try
        {
            foreach (var user in users)
            {
                sharpCoreDbEncrypted!.InsertUser(user.Id, user.Name, user.Email, user.Age, user.CreatedAt, user.IsActive);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"SharpCoreDB (Encrypted) populate error: {ex.Message}");
        }
        
        // Populate SharpCoreDB (No Encryption)
        try
        {
            foreach (var user in users)
            {
                sharpCoreDbNoEncrypt!.InsertUser(user.Id, user.Name, user.Email, user.Age, user.CreatedAt, user.IsActive);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"SharpCoreDB (No Encryption) populate error: {ex.Message}");
        }

        // Populate SQLite
        using var transaction = sqliteConn!.BeginTransaction();
        using var cmd = sqliteConn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO users (id, name, email, age, created_at, is_active)
            VALUES (@id, @name, @email, @age, @created_at, @is_active)";

        cmd.Parameters.Add("@id", SqliteType.Integer);
        cmd.Parameters.Add("@name", SqliteType.Text);
        cmd.Parameters.Add("@email", SqliteType.Text);
        cmd.Parameters.Add("@age", SqliteType.Integer);
        cmd.Parameters.Add("@created_at", SqliteType.Text);
        cmd.Parameters.Add("@is_active", SqliteType.Integer);

        foreach (var user in users)
        {
            cmd.Parameters["@id"].Value = user.Id;
            cmd.Parameters["@name"].Value = user.Name;
            cmd.Parameters["@email"].Value = user.Email;
            cmd.Parameters["@age"].Value = user.Age;
            cmd.Parameters["@created_at"].Value = user.CreatedAt.ToString("o");
            cmd.Parameters["@is_active"].Value = user.IsActive ? 1 : 0;
            cmd.ExecuteNonQuery();
        }
        transaction.Commit();

        // Populate LiteDB
        liteCollection!.InsertBulk(users);
    }

    // ==================== UPDATE BENCHMARKS ====================

    [Benchmark(Baseline = true, Description = "SQLite: Update Records")]
    public int SQLite_Update()
    {
        using var transaction = sqliteConn!.BeginTransaction();
        using var cmd = sqliteConn.CreateCommand();
        cmd.CommandText = "UPDATE users SET age = age + 1 WHERE id <= @maxId";
        cmd.Parameters.AddWithValue("@maxId", OperationCount);
        
        var result = cmd.ExecuteNonQuery();
        transaction.Commit();
        return result;
    }

    [Benchmark(Description = "SharpCoreDB (Encrypted): Update Records")]
    public int SharpCoreDB_Encrypted_Update()
    {
        int updated = 0;
        try
        {
            for (int i = 1; i <= OperationCount; i++)
            {
                var users = sharpCoreDbEncrypted!.SelectUserById(i);
                if (users.Count > 0)
                {
                    var currentAge = Convert.ToInt32(users[0]["age"]);
                    sharpCoreDbEncrypted.UpdateUserAge(i, currentAge + 1);
                    updated++;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"SharpCoreDB (Encrypted) update error: {ex.Message}");
        }
        return updated;
    }

    [Benchmark(Description = "SharpCoreDB (No Encryption): Update Records")]
    public int SharpCoreDB_NoEncrypt_Update()
    {
        int updated = 0;
        try
        {
            for (int i = 1; i <= OperationCount; i++)
            {
                var users = sharpCoreDbNoEncrypt!.SelectUserById(i);
                if (users.Count > 0)
                {
                    var currentAge = Convert.ToInt32(users[0]["age"]);
                    sharpCoreDbNoEncrypt.UpdateUserAge(i, currentAge + 1);
                    updated++;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"SharpCoreDB (No Encryption) update error: {ex.Message}");
        }
        return updated;
    }

    [Benchmark(Description = "LiteDB: Update Records")]
    public int LiteDB_Update()
    {
        var users = liteCollection!.Find(x => x.Id <= OperationCount).ToList();
        int updated = 0;
        
        foreach (var user in users)
        {
            user.Age++;
            liteCollection.Update(user);
            updated++;
        }
        return updated;
    }

    // ==================== DELETE BENCHMARKS ====================

    [Benchmark(Description = "SQLite: Delete Records")]
    public int SQLite_Delete()
    {
        int result;
        using (var transaction = sqliteConn!.BeginTransaction())
        {
            using var cmd = sqliteConn.CreateCommand();
            cmd.CommandText = "DELETE FROM users WHERE id > @minId AND id <= @maxId";
            cmd.Parameters.AddWithValue("@minId", TotalRecords - OperationCount);
            cmd.Parameters.AddWithValue("@maxId", TotalRecords);
            
            result = cmd.ExecuteNonQuery();
            transaction.Commit();
        }
        
        // Re-insert for next iteration
        RepopulateSQLite();
        
        return result;
    }

    [Benchmark(Description = "SharpCoreDB (Encrypted): Delete Records")]
    public int SharpCoreDB_Encrypted_Delete()
    {
        int deleted = 0;
        
        try
        {
            // Delete records one by one
            for (int i = TotalRecords - OperationCount + 1; i <= TotalRecords; i++)
            {
                try
                {
                    sharpCoreDbEncrypted!.DeleteUser(i);
                    deleted++;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Delete error for ID {i}: {ex.Message}");
                    // Continue with next ID
                }
            }
            
            // Re-insert for next iteration
            RepopulateSharpCoreDB(sharpCoreDbEncrypted!);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"SharpCoreDB (Encrypted) delete benchmark error: {ex.Message}");
        }
        
        return deleted;
    }

    [Benchmark(Description = "SharpCoreDB (No Encryption): Delete Records")]
    public int SharpCoreDB_NoEncrypt_Delete()
    {
        int deleted = 0;
        
        try
        {
            // Delete records one by one
            for (int i = TotalRecords - OperationCount + 1; i <= TotalRecords; i++)
            {
                try
                {
                    sharpCoreDbNoEncrypt!.DeleteUser(i);
                    deleted++;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Delete error for ID {i}: {ex.Message}");
                    // Continue with next ID
                }
            }
            
            // Re-insert for next iteration
            RepopulateSharpCoreDB(sharpCoreDbNoEncrypt!);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"SharpCoreDB (No Encryption) delete benchmark error: {ex.Message}");
        }
        
        return deleted;
    }

    [Benchmark(Description = "LiteDB: Delete Records")]
    public int LiteDB_Delete()
    {
        int deleted = liteCollection!.DeleteMany(x => x.Id > TotalRecords - OperationCount);
        
        // Re-insert for next iteration
        RepopulateLiteDB();
        
        return deleted;
    }

    private void RepopulateSQLite()
    {
        try
        {
            var users = dataGenerator.GenerateUsers(OperationCount, TotalRecords - OperationCount + 1);
            
            using var transaction = sqliteConn!.BeginTransaction();
            using var cmd = sqliteConn.CreateCommand();
            cmd.CommandText = @"
                INSERT OR REPLACE INTO users (id, name, email, age, created_at, is_active)
                VALUES (@id, @name, @email, @age, @created_at, @is_active)";

            cmd.Parameters.Add("@id", SqliteType.Integer);
            cmd.Parameters.Add("@name", SqliteType.Text);
            cmd.Parameters.Add("@email", SqliteType.Text);
            cmd.Parameters.Add("@age", SqliteType.Integer);
            cmd.Parameters.Add("@created_at", SqliteType.Text);
            cmd.Parameters.Add("@is_active", SqliteType.Integer);

            foreach (var user in users)
            {
                cmd.Parameters["@id"].Value = user.Id;
                cmd.Parameters["@name"].Value = user.Name;
                cmd.Parameters["@email"].Value = user.Email;
                cmd.Parameters["@age"].Value = user.Age;
                cmd.Parameters["@created_at"].Value = user.CreatedAt.ToString("o");
                cmd.Parameters["@is_active"].Value = user.IsActive ? 1 : 0;
                cmd.ExecuteNonQuery();
            }
            transaction.Commit();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"SQLite repopulate error: {ex.Message}");
        }
    }

    private void RepopulateSharpCoreDB(BenchmarkDatabaseHelper helper)
    {
        try
        {
            var users = dataGenerator.GenerateUsers(OperationCount, TotalRecords - OperationCount + 1);
            
            foreach (var user in users)
            {
                try
                {
                    helper.InsertUser(user.Id, user.Name, user.Email, user.Age, user.CreatedAt, user.IsActive);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"SharpCoreDB re-insert error for ID {user.Id}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"SharpCoreDB repopulate error: {ex.Message}");
        }
    }

    private void RepopulateLiteDB()
    {
        try
        {
            var users = dataGenerator.GenerateUsers(OperationCount, TotalRecords - OperationCount + 1);
            liteCollection!.InsertBulk(users);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"LiteDB repopulate error: {ex.Message}");
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        Dispose();
    }

    public void Dispose()
    {
        sharpCoreDbEncrypted?.Dispose();
        sharpCoreDbNoEncrypt?.Dispose();
        sqliteConn?.Dispose();
        liteDb?.Dispose();

        try
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }

        GC.SuppressFinalize(this);
    }
}
