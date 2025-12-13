// <copyright file="ComparativeAggregateBenchmarks.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
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
/// Comparative benchmarks for aggregate operations (COUNT, SUM, AVG, MIN, MAX, GROUP BY).
/// Tests SharpCoreDB (encrypted + non-encrypted) vs SQLite vs LiteDB.
/// </summary>
[Config(typeof(BenchmarkConfig))]
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
public class ComparativeAggregateBenchmarks : IDisposable
{
    private TestDataGenerator dataGenerator = null!;
    private string tempDir = null!;
    private const int TotalRecords = 10000;
    
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
        Console.WriteLine("AGGREGATE Benchmarks - Starting Setup");
        Console.WriteLine($"{'='*60}");
        
        dataGenerator = new TestDataGenerator();
        tempDir = Path.Combine(Path.GetTempPath(), $"dbBenchmark_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        var totalSw = Stopwatch.StartNew();
        
        SetupAndPopulateSharpCoreDB();
        SetupAndPopulateSQLite();
        SetupAndPopulateLiteDB();
        
        totalSw.Stop();
        
        Console.WriteLine($"\n✅ Total setup time: {totalSw.ElapsedMilliseconds}ms");
        Console.WriteLine($"{'='*60}\n");
    }

    private void SetupAndPopulateSharpCoreDB()
    {
        Console.WriteLine("\n🔧 Setting up SharpCoreDB...");
        
        try
        {
            // ========== ENCRYPTED VARIANT ==========
            var swEncrypted = Stopwatch.StartNew();
            
            var dbPathEncrypted = Path.Combine(tempDir, "sharpcore_encrypted");
            sharpCoreDbEncrypted = new BenchmarkDatabaseHelper(dbPathEncrypted, enableEncryption: true);
            sharpCoreDbEncrypted.CreateUsersTable();

            // Populate with batch insert for fast setup
            var usersEncrypted = dataGenerator.GenerateUsers(TotalRecords);
            var userListEncrypted = usersEncrypted.Select(u => 
                (u.Id, u.Name, u.Email, u.Age, u.CreatedAt, u.IsActive)
            ).ToList();
            
            sharpCoreDbEncrypted.InsertUsersTrueBatch(userListEncrypted);
            
            swEncrypted.Stop();
            Console.WriteLine($"  ✓ SharpCoreDB (Encrypted): {TotalRecords} records in {swEncrypted.ElapsedMilliseconds}ms");
            
            // ========== NO ENCRYPTION VARIANT ==========
            var swNoEncrypt = Stopwatch.StartNew();
            
            var dbPathNoEncrypt = Path.Combine(tempDir, "sharpcore_noencrypt");
            sharpCoreDbNoEncrypt = new BenchmarkDatabaseHelper(dbPathNoEncrypt, enableEncryption: false);
            sharpCoreDbNoEncrypt.CreateUsersTable();

            // Populate with batch insert
            var usersNoEncrypt = dataGenerator.GenerateUsers(TotalRecords);
            var userListNoEncrypt = usersNoEncrypt.Select(u => 
                (u.Id, u.Name, u.Email, u.Age, u.CreatedAt, u.IsActive)
            ).ToList();
            
            sharpCoreDbNoEncrypt.InsertUsersTrueBatch(userListNoEncrypt);
            
            swNoEncrypt.Stop();
            Console.WriteLine($"  ✓ SharpCoreDB (No Encryption): {TotalRecords} records in {swNoEncrypt.ElapsedMilliseconds}ms");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ SharpCoreDB setup failed: {ex.Message}");
            throw;
        }
    }

    private void SetupAndPopulateSQLite()
    {
        Console.WriteLine("\n🔧 Setting up SQLite...");
        
        var sw = Stopwatch.StartNew();
        
        var sqlitePath = Path.Combine(tempDir, "sqlite.db");
        sqliteConn = new SqliteConnection($"Data Source={sqlitePath}");
        sqliteConn.Open();
        
        // Create table
        using var createCmd = sqliteConn.CreateCommand();
        createCmd.CommandText = @"
            CREATE TABLE users (
                id INTEGER PRIMARY KEY,
                name TEXT NOT NULL,
                email TEXT NOT NULL,
                age INTEGER NOT NULL,
                created_at TEXT NOT NULL,
                is_active INTEGER NOT NULL
            )";
        createCmd.ExecuteNonQuery();

        // Create indexes for better aggregate performance
        createCmd.CommandText = "CREATE INDEX idx_age ON users(age)";
        createCmd.ExecuteNonQuery();
        createCmd.CommandText = "CREATE INDEX idx_is_active ON users(is_active)";
        createCmd.ExecuteNonQuery();

        // Populate
        var users = dataGenerator.GenerateUsers(TotalRecords);
        using var transaction = sqliteConn.BeginTransaction();
        using var insertCmd = sqliteConn.CreateCommand();
        insertCmd.CommandText = @"
            INSERT INTO users (id, name, email, age, created_at, is_active)
            VALUES (@id, @name, @email, @age, @created_at, @is_active)";

        insertCmd.Parameters.Add("@id", SqliteType.Integer);
        insertCmd.Parameters.Add("@name", SqliteType.Text);
        insertCmd.Parameters.Add("@email", SqliteType.Text);
        insertCmd.Parameters.Add("@age", SqliteType.Integer);
        insertCmd.Parameters.Add("@created_at", SqliteType.Text);
        insertCmd.Parameters.Add("@is_active", SqliteType.Integer);

        foreach (var user in users)
        {
            insertCmd.Parameters["@id"].Value = user.Id;
            insertCmd.Parameters["@name"].Value = user.Name;
            insertCmd.Parameters["@email"].Value = user.Email;
            insertCmd.Parameters["@age"].Value = user.Age;
            insertCmd.Parameters["@created_at"].Value = user.CreatedAt.ToString("o");
            insertCmd.Parameters["@is_active"].Value = user.IsActive ? 1 : 0;
            insertCmd.ExecuteNonQuery();
        }

        transaction.Commit();
        
        sw.Stop();
        Console.WriteLine($"  ✓ SQLite: {TotalRecords} records in {sw.ElapsedMilliseconds}ms");
    }

    private void SetupAndPopulateLiteDB()
    {
        Console.WriteLine("\n🔧 Setting up LiteDB...");
        
        var sw = Stopwatch.StartNew();
        
        var liteDbPath = Path.Combine(tempDir, "litedb.db");
        liteDb = new LiteDatabase(liteDbPath);
        liteCollection = liteDb.GetCollection<TestDataGenerator.UserRecord>("users");

        // Create indexes
        liteCollection.EnsureIndex(x => x.Age);
        liteCollection.EnsureIndex(x => x.IsActive);

        // Populate
        var users = dataGenerator.GenerateUsers(TotalRecords);
        liteCollection.InsertBulk(users);
        
        sw.Stop();
        Console.WriteLine($"  ✓ LiteDB: {TotalRecords} records in {sw.ElapsedMilliseconds}ms");
    }

    // ==================== COUNT OPERATIONS ====================

    [Benchmark(Description = "SharpCoreDB (Encrypted): COUNT(*)")]
    public void SharpCoreDB_Encrypted_CountAll()
    {
        sharpCoreDbEncrypted?.ExecuteQuery("SELECT COUNT(*) FROM users");
    }

    [Benchmark(Description = "SharpCoreDB (No Encryption): COUNT(*)")]
    public void SharpCoreDB_NoEncrypt_CountAll()
    {
        sharpCoreDbNoEncrypt?.ExecuteQuery("SELECT COUNT(*) FROM users");
    }

    [Benchmark(Baseline = true, Description = "SQLite: COUNT(*)")]
    public void SQLite_CountAll()
    {
        using var cmd = sqliteConn?.CreateCommand();
        cmd!.CommandText = "SELECT COUNT(*) FROM users";
        cmd.ExecuteScalar();
    }

    [Benchmark(Description = "LiteDB: COUNT()")]
    public void LiteDB_CountAll()
    {
        liteCollection?.Count();
    }

    // ==================== COUNT WITH FILTER ====================

    [Benchmark(Description = "SharpCoreDB (Encrypted): COUNT WHERE")]
    public void SharpCoreDB_Encrypted_CountWhere()
    {
        sharpCoreDbEncrypted?.ExecuteQuery("SELECT COUNT(*) FROM users WHERE age > 30");
    }

    [Benchmark(Description = "SharpCoreDB (No Encryption): COUNT WHERE")]
    public void SharpCoreDB_NoEncrypt_CountWhere()
    {
        sharpCoreDbNoEncrypt?.ExecuteQuery("SELECT COUNT(*) FROM users WHERE age > 30");
    }

    [Benchmark(Description = "SQLite: COUNT WHERE")]
    public void SQLite_CountWhere()
    {
        using var cmd = sqliteConn?.CreateCommand();
        cmd!.CommandText = "SELECT COUNT(*) FROM users WHERE age > 30";
        cmd.ExecuteScalar();
    }

    [Benchmark(Description = "LiteDB: COUNT WHERE")]
    public void LiteDB_CountWhere()
    {
        liteCollection?.Count(x => x.Age > 30);
    }

    // ==================== SUM OPERATIONS ====================

    [Benchmark(Description = "SharpCoreDB (Encrypted): SUM(age)")]
    public void SharpCoreDB_Encrypted_Sum()
    {
        sharpCoreDbEncrypted?.ExecuteQuery("SELECT SUM(age) FROM users");
    }

    [Benchmark(Description = "SharpCoreDB (No Encryption): SUM(age)")]
    public void SharpCoreDB_NoEncrypt_Sum()
    {
        sharpCoreDbNoEncrypt?.ExecuteQuery("SELECT SUM(age) FROM users");
    }

    [Benchmark(Description = "SQLite: SUM(age)")]
    public void SQLite_Sum()
    {
        using var cmd = sqliteConn?.CreateCommand();
        cmd!.CommandText = "SELECT SUM(age) FROM users";
        cmd.ExecuteScalar();
    }

    [Benchmark(Description = "LiteDB: SUM(age) - Manual")]
    public void LiteDB_Sum()
    {
        // LiteDB doesn't have native SUM, so we need to do it manually
        var sum = liteCollection?.FindAll().Sum(x => x.Age);
    }

    // ==================== AVG OPERATIONS ====================

    [Benchmark(Description = "SharpCoreDB (Encrypted): AVG(age)")]
    public void SharpCoreDB_Encrypted_Avg()
    {
        sharpCoreDbEncrypted?.ExecuteQuery("SELECT AVG(age) FROM users");
    }

    [Benchmark(Description = "SharpCoreDB (No Encryption): AVG(age)")]
    public void SharpCoreDB_NoEncrypt_Avg()
    {
        sharpCoreDbNoEncrypt?.ExecuteQuery("SELECT AVG(age) FROM users");
    }

    [Benchmark(Description = "SQLite: AVG(age)")]
    public void SQLite_Avg()
    {
        using var cmd = sqliteConn?.CreateCommand();
        cmd!.CommandText = "SELECT AVG(age) FROM users";
        cmd.ExecuteScalar();
    }

    [Benchmark(Description = "LiteDB: AVG(age) - Manual")]
    public void LiteDB_Avg()
    {
        var avg = liteCollection?.FindAll().Average(x => x.Age);
    }

    // ==================== MIN OPERATIONS ====================

    [Benchmark(Description = "SharpCoreDB (Encrypted): MIN(age)")]
    public void SharpCoreDB_Encrypted_Min()
    {
        sharpCoreDbEncrypted?.ExecuteQuery("SELECT MIN(age) FROM users");
    }

    [Benchmark(Description = "SharpCoreDB (No Encryption): MIN(age)")]
    public void SharpCoreDB_NoEncrypt_Min()
    {
        sharpCoreDbNoEncrypt?.ExecuteQuery("SELECT MIN(age) FROM users");
    }

    [Benchmark(Description = "SQLite: MIN(age)")]
    public void SQLite_Min()
    {
        using var cmd = sqliteConn?.CreateCommand();
        cmd!.CommandText = "SELECT MIN(age) FROM users";
        cmd.ExecuteScalar();
    }

    [Benchmark(Description = "LiteDB: MIN(age) - Manual")]
    public void LiteDB_Min()
    {
        var min = liteCollection?.FindAll().Min(x => x.Age);
    }

    // ==================== MAX OPERATIONS ====================

    [Benchmark(Description = "SharpCoreDB (Encrypted): MAX(age)")]
    public void SharpCoreDB_Encrypted_Max()
    {
        sharpCoreDbEncrypted?.ExecuteQuery("SELECT MAX(age) FROM users");
    }

    [Benchmark(Description = "SharpCoreDB (No Encryption): MAX(age)")]
    public void SharpCoreDB_NoEncrypt_Max()
    {
        sharpCoreDbNoEncrypt?.ExecuteQuery("SELECT MAX(age) FROM users");
    }

    [Benchmark(Description = "SQLite: MAX(age)")]
    public void SQLite_Max()
    {
        using var cmd = sqliteConn?.CreateCommand();
        cmd!.CommandText = "SELECT MAX(age) FROM users";
        cmd.ExecuteScalar();
    }

    [Benchmark(Description = "LiteDB: MAX(age) - Manual")]
    public void LiteDB_Max()
    {
        var max = liteCollection?.FindAll().Max(x => x.Age);
    }

    // ==================== GROUP BY OPERATIONS ====================

    [Benchmark(Description = "SharpCoreDB (Encrypted): GROUP BY age")]
    public void SharpCoreDB_Encrypted_GroupBy()
    {
        sharpCoreDbEncrypted?.ExecuteQuery("SELECT age, COUNT(*) FROM users GROUP BY age");
    }

    [Benchmark(Description = "SharpCoreDB (No Encryption): GROUP BY age")]
    public void SharpCoreDB_NoEncrypt_GroupBy()
    {
        sharpCoreDbNoEncrypt?.ExecuteQuery("SELECT age, COUNT(*) FROM users GROUP BY age");
    }

    [Benchmark(Description = "SQLite: GROUP BY age")]
    public void SQLite_GroupBy()
    {
        using var cmd = sqliteConn?.CreateCommand();
        cmd!.CommandText = "SELECT age, COUNT(*) FROM users GROUP BY age";
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) { /* Consume results */ }
    }

    [Benchmark(Description = "LiteDB: GROUP BY age - Manual")]
    public void LiteDB_GroupBy()
    {
        var groups = liteCollection?.FindAll().GroupBy(x => x.Age).ToList();
    }

    // ==================== COMPLEX AGGREGATES ====================

    [Benchmark(Description = "SharpCoreDB (Encrypted): Complex Aggregate")]
    public void SharpCoreDB_Encrypted_ComplexAggregate()
    {
        sharpCoreDbEncrypted?.ExecuteQuery(@"
            SELECT age, COUNT(*) as cnt, AVG(age) as avg_age 
            FROM users 
            WHERE is_active = 1 
            GROUP BY age");
    }

    [Benchmark(Description = "SharpCoreDB (No Encryption): Complex Aggregate")]
    public void SharpCoreDB_NoEncrypt_ComplexAggregate()
    {
        sharpCoreDbNoEncrypt?.ExecuteQuery(@"
            SELECT age, COUNT(*) as cnt, AVG(age) as avg_age 
            FROM users 
            WHERE is_active = 1 
            GROUP BY age");
    }

    [Benchmark(Description = "SQLite: Complex Aggregate")]
    public void SQLite_ComplexAggregate()
    {
        using var cmd = sqliteConn?.CreateCommand();
        cmd!.CommandText = @"
            SELECT age, COUNT(*) as cnt, AVG(age) as avg_age 
            FROM users 
            WHERE is_active = 1 
            GROUP BY age";
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) { /* Consume results */ }
    }

    [Benchmark(Description = "LiteDB: Complex Aggregate - Manual")]
    public void LiteDB_ComplexAggregate()
    {
        var result = liteCollection?
            .Find(x => x.IsActive)
            .GroupBy(x => x.Age)
            .Select(g => new
            {
                Age = g.Key,
                Count = g.Count(),
                AvgAge = g.Average(x => x.Age)
            })
            .ToList();
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
