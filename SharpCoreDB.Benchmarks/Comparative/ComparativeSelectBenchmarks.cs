// <copyright file="ComparativeSelectBenchmarks.cs" company="MPCoreDeveloper">
// Copyright (c) 2024-2025 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using SharpCoreDB.Benchmarks.Infrastructure;
using Microsoft.Data.Sqlite;
using LiteDB;
using System.Diagnostics;

namespace SharpCoreDB.Benchmarks.Comparative;

/// <summary>
/// Comparative benchmarks for SELECT operations.
/// Tests point queries, range filters, and full table scans.
/// Includes both encrypted and non-encrypted SharpCoreDB variants.
/// NOW WITH: Fast batch population in setup for reliable benchmarking.
/// </summary>
[Config(typeof(BenchmarkConfig))]
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
public class ComparativeSelectBenchmarks : IDisposable
{
    private TestDataGenerator dataGenerator = null!;
    private string tempDir = null!;
    private const int TotalRecords = 1000;
    
    // SharpCoreDB - Encrypted
    private BenchmarkDatabaseHelper? sharpCoreDbEncrypted;
    
    // SharpCoreDB - No Encryption
    private BenchmarkDatabaseHelper? sharpCoreDbNoEncrypt;
    
    // SQLite
    private SqliteConnection? sqliteConn;
    
    // LiteDB
    private LiteDatabase? liteDb;
    private ILiteCollection<TestDataGenerator.UserRecord>? liteCollection;

    [GlobalSetup]
    public void Setup()
    {
        Console.WriteLine($"\n{'='*60}");
        Console.WriteLine("SELECT Benchmarks - Starting Setup");
        Console.WriteLine($"{'='*60}");
        
        dataGenerator = new TestDataGenerator();
        tempDir = Path.Combine(Path.GetTempPath(), $"dbBenchmark_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        var totalSw = Stopwatch.StartNew();
        
        SetupAndPopulateSharpCoreDB();
        SetupAndPopulateSQLite();
        SetupAndPopulateLiteDB();
        
        totalSw.Stop();
        
        // Verify all databases populated correctly
        VerifySetup();
        
        Console.WriteLine($"\n? Total setup time: {totalSw.ElapsedMilliseconds}ms");
        Console.WriteLine($"{'='*60}\n");
    }

    private void SetupAndPopulateSharpCoreDB()
    {
        Console.WriteLine("\n?? Setting up SharpCoreDB...");
        
        try
        {
            // ========== ENCRYPTED VARIANT ==========
            var swEncrypted = Stopwatch.StartNew();
            
            var dbPathEncrypted = Path.Combine(tempDir, "sharpcore_encrypted");
            sharpCoreDbEncrypted = new BenchmarkDatabaseHelper(dbPathEncrypted, enableEncryption: true);
            sharpCoreDbEncrypted.CreateUsersTable();

            // USE BATCH INSERT FOR FAST POPULATION!
            var usersEncrypted = dataGenerator.GenerateUsers(TotalRecords);
            var userListEncrypted = usersEncrypted.Select(u => 
                (u.Id, u.Name, u.Email, u.Age, u.CreatedAt, u.IsActive)
            ).ToList();
            
            sharpCoreDbEncrypted.InsertUsersBatch(userListEncrypted);
            
            swEncrypted.Stop();
            Console.WriteLine($"  ? SharpCoreDB (Encrypted): {TotalRecords} records in {swEncrypted.ElapsedMilliseconds}ms");
            
            // ========== NO ENCRYPTION VARIANT ==========
            var swNoEncrypt = Stopwatch.StartNew();
            
            var dbPathNoEncrypt = Path.Combine(tempDir, "sharpcore_noencrypt");
            sharpCoreDbNoEncrypt = new BenchmarkDatabaseHelper(dbPathNoEncrypt, enableEncryption: false);
            sharpCoreDbNoEncrypt.CreateUsersTable();

            // USE BATCH INSERT FOR FAST POPULATION!
            var usersNoEncrypt = dataGenerator.GenerateUsers(TotalRecords);
            var userListNoEncrypt = usersNoEncrypt.Select(u => 
                (u.Id, u.Name, u.Email, u.Age, u.CreatedAt, u.IsActive)
            ).ToList();
            
            sharpCoreDbNoEncrypt.InsertUsersBatch(userListNoEncrypt);
            
            swNoEncrypt.Stop();
            Console.WriteLine($"  ? SharpCoreDB (No Encryption): {TotalRecords} records in {swNoEncrypt.ElapsedMilliseconds}ms");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ? SharpCoreDB setup failed: {ex.Message}");
            Console.WriteLine($"     Stack: {ex.StackTrace}");
            throw;
        }
    }

    private void SetupAndPopulateSQLite()
    {
        Console.WriteLine("\n?? Setting up SQLite...");
        var sw = Stopwatch.StartNew();
        
        try
        {
            var dbPath = Path.Combine(tempDir, "sqlite.db");
            sqliteConn = new SqliteConnection($"Data Source={dbPath}");
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

            // Create index on age for range queries
            cmd.CommandText = "CREATE INDEX idx_age ON users(age)";
            cmd.ExecuteNonQuery();

            // Insert data using transaction
            var users = dataGenerator.GenerateUsers(TotalRecords);
            using var transaction = sqliteConn.BeginTransaction();
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
            
            sw.Stop();
            Console.WriteLine($"  ? SQLite: {TotalRecords} records in {sw.ElapsedMilliseconds}ms");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ? SQLite setup failed: {ex.Message}");
            throw;
        }
    }

    private void SetupAndPopulateLiteDB()
    {
        Console.WriteLine("\n?? Setting up LiteDB...");
        var sw = Stopwatch.StartNew();
        
        try
        {
            var dbPath = Path.Combine(tempDir, "litedb.db");
            liteDb = new LiteDatabase(dbPath);
            liteCollection = liteDb.GetCollection<TestDataGenerator.UserRecord>("users");
            
            // Create index
            liteCollection.EnsureIndex(x => x.Age);

            var users = dataGenerator.GenerateUsers(TotalRecords);
            liteCollection.InsertBulk(users);
            
            sw.Stop();
            Console.WriteLine($"  ? LiteDB: {TotalRecords} records in {sw.ElapsedMilliseconds}ms");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ? LiteDB setup failed: {ex.Message}");
            throw;
        }
    }

    private void VerifySetup()
    {
        Console.WriteLine("\n?? Verifying setup...");
        
        var errors = new List<string>();
        
        // Verify SharpCoreDB (Encrypted)
        try
        {
            var testResults = sharpCoreDbEncrypted?.SelectUserById(1);
            if (testResults == null || testResults.Count == 0)
            {
                errors.Add("SharpCoreDB (Encrypted): Cannot query user ID 1");
            }
            else
            {
                Console.WriteLine($"  ? SharpCoreDB (Encrypted): Verified (found user ID 1)");
            }
        }
        catch (Exception ex)
        {
            errors.Add($"SharpCoreDB (Encrypted): Query failed - {ex.Message}");
        }
        
        // Verify SharpCoreDB (No Encryption)
        try
        {
            var testResults = sharpCoreDbNoEncrypt?.SelectUserById(1);
            if (testResults == null || testResults.Count == 0)
            {
                errors.Add("SharpCoreDB (No Encryption): Cannot query user ID 1");
            }
            else
            {
                Console.WriteLine($"  ? SharpCoreDB (No Encryption): Verified (found user ID 1)");
            }
        }
        catch (Exception ex)
        {
            errors.Add($"SharpCoreDB (No Encryption): Query failed - {ex.Message}");
        }
        
        // Verify SQLite
        try
        {
            using var cmd = sqliteConn!.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM users";
            var count = Convert.ToInt32(cmd.ExecuteScalar());
            if (count != TotalRecords)
            {
                errors.Add($"SQLite: Expected {TotalRecords} records, got {count}");
            }
            else
            {
                Console.WriteLine($"  ? SQLite: Verified ({count} records)");
            }
        }
        catch (Exception ex)
        {
            errors.Add($"SQLite: Verification failed - {ex.Message}");
        }
        
        // Verify LiteDB
        try
        {
            var count = liteCollection?.Count() ?? 0;
            if (count != TotalRecords)
            {
                errors.Add($"LiteDB: Expected {TotalRecords} records, got {count}");
            }
            else
            {
                Console.WriteLine($"  ? LiteDB: Verified ({count} records)");
            }
        }
        catch (Exception ex)
        {
            errors.Add($"LiteDB: Verification failed - {ex.Message}");
        }
        
        // Report errors if any
        if (errors.Count > 0)
        {
            Console.WriteLine("\n? SETUP VERIFICATION FAILED:");
            foreach (var error in errors)
            {
                Console.WriteLine($"  - {error}");
            }
            throw new InvalidOperationException(
                $"Setup verification failed with {errors.Count} error(s). " +
                "Benchmarks cannot run reliably. See console output above.");
        }
        
        Console.WriteLine($"\n? All databases verified successfully!");
    }

    // ==================== POINT QUERIES ====================

    [Benchmark(Baseline = true, Description = "SQLite: Point Query by ID")]
    public int SQLite_PointQuery()
    {
        var targetId = Random.Shared.Next(1, TotalRecords + 1);
        
        using var cmd = sqliteConn!.CreateCommand();
        cmd.CommandText = "SELECT * FROM users WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", targetId);
        
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? 1 : 0;
    }

    [Benchmark(Description = "SharpCoreDB (Encrypted): Point Query by ID")]
    public int SharpCoreDB_Encrypted_PointQuery()
    {
        try
        {
            var targetId = Random.Shared.Next(1, TotalRecords + 1);
            var results = sharpCoreDbEncrypted?.SelectUserById(targetId);
            return results?.Count ?? 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"SharpCoreDB (Encrypted) point query error: {ex.Message}");
            return 0;
        }
    }

    [Benchmark(Description = "SharpCoreDB (No Encryption): Point Query by ID")]
    public int SharpCoreDB_NoEncrypt_PointQuery()
    {
        try
        {
            var targetId = Random.Shared.Next(1, TotalRecords + 1);
            var results = sharpCoreDbNoEncrypt?.SelectUserById(targetId);
            return results?.Count ?? 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"SharpCoreDB (No Encryption) point query error: {ex.Message}");
            return 0;
        }
    }

    [Benchmark(Description = "LiteDB: Point Query by ID")]
    public int LiteDB_PointQuery()
    {
        var targetId = Random.Shared.Next(1, TotalRecords + 1);
        var result = liteCollection?.FindById(targetId);
        return result != null ? 1 : 0;
    }

    // ==================== RANGE QUERIES ====================

    [Benchmark(Description = "SQLite: Range Query (Age 25-35)")]
    public int SQLite_RangeQuery()
    {
        using var cmd = sqliteConn!.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM users WHERE age BETWEEN 25 AND 35";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    [Benchmark(Description = "SharpCoreDB (Encrypted): Range Query (Age 25-35)")]
    public int SharpCoreDB_Encrypted_RangeQuery()
    {
        try
        {
            var results = sharpCoreDbEncrypted?.SelectUsersByAgeRange(25, 35);
            return results?.Count ?? 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"SharpCoreDB (Encrypted) range query error: {ex.Message}");
            return 0;
        }
    }

    [Benchmark(Description = "SharpCoreDB (No Encryption): Range Query (Age 25-35)")]
    public int SharpCoreDB_NoEncrypt_RangeQuery()
    {
        try
        {
            var results = sharpCoreDbNoEncrypt?.SelectUsersByAgeRange(25, 35);
            return results?.Count ?? 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"SharpCoreDB (No Encryption) range query error: {ex.Message}");
            return 0;
        }
    }

    [Benchmark(Description = "LiteDB: Range Query (Age 25-35)")]
    public int LiteDB_RangeQuery()
    {
        var results = liteCollection?.Find(x => x.Age >= 25 && x.Age <= 35);
        return results?.Count() ?? 0;
    }

    // ==================== FULL TABLE SCANS ====================

    [Benchmark(Description = "SQLite: Full Scan (Active Users)")]
    public int SQLite_FullScan()
    {
        using var cmd = sqliteConn!.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM users WHERE is_active = 1";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    [Benchmark(Description = "SharpCoreDB (Encrypted): Full Scan (Active Users)")]
    public int SharpCoreDB_Encrypted_FullScan()
    {
        try
        {
            var results = sharpCoreDbEncrypted?.SelectActiveUsers();
            return results?.Count ?? 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"SharpCoreDB (Encrypted) full scan error: {ex.Message}");
            return 0;
        }
    }

    [Benchmark(Description = "SharpCoreDB (No Encryption): Full Scan (Active Users)")]
    public int SharpCoreDB_NoEncrypt_FullScan()
    {
        try
        {
            var results = sharpCoreDbNoEncrypt?.SelectActiveUsers();
            return results?.Count ?? 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"SharpCoreDB (No Encryption) full scan error: {ex.Message}");
            return 0;
        }
    }

    [Benchmark(Description = "LiteDB: Full Scan (Active Users)")]
    public int LiteDB_FullScan()
    {
        var results = liteCollection?.Find(x => x.IsActive);
        return results?.Count() ?? 0;
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
