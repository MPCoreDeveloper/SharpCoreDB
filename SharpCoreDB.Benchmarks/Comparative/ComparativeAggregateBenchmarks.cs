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
        Console.WriteLine($"\n{new string('=', 60)}");
        Console.WriteLine("AGGREGATE Benchmarks - Starting Setup");
        Console.WriteLine($"{new string('=', 60)}");
        
        dataGenerator = new TestDataGenerator();
        tempDir = Path.Combine(Path.GetTempPath(), $"dbBenchmark_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        var totalSw = Stopwatch.StartNew();
        
        SetupAndPopulateSharpCoreDB();
        SetupAndPopulateSQLite();
        SetupAndPopulateLiteDB();
        
        totalSw.Stop();
        
        Console.WriteLine($"\n✅ Total setup time: {totalSw.ElapsedMilliseconds}ms");
        Console.WriteLine($"{new string('=', 60)}\n");
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
    public int SharpCoreDB_Encrypted_CountAll()
    {
        var results = sharpCoreDbEncrypted?.ExecuteQuery("SELECT COUNT(*) FROM users");
        return results?.Count ?? 0;
    }

    [Benchmark(Description = "SharpCoreDB (No Encryption): COUNT(*)")]
    public int SharpCoreDB_NoEncrypt_CountAll()
    {
        var results = sharpCoreDbNoEncrypt?.ExecuteQuery("SELECT COUNT(*) FROM users");
        return results?.Count ?? 0;
    }

    [Benchmark(Baseline = true, Description = "SQLite: COUNT(*)")]
    public long SQLite_CountAll()
    {
        using var cmd = sqliteConn?.CreateCommand();
        cmd!.CommandText = "SELECT COUNT(*) FROM users";
        var result = cmd.ExecuteScalar();
        return result != null ? Convert.ToInt64(result) : 0;
    }

    [Benchmark(Description = "LiteDB: COUNT()")]
    public int LiteDB_CountAll()
    {
        return liteCollection?.Count() ?? 0;
    }

    // ==================== COUNT WITH FILTER ====================

    [Benchmark(Description = "SharpCoreDB (Encrypted): COUNT WHERE")]
    public int SharpCoreDB_Encrypted_CountWhere()
    {
        var results = sharpCoreDbEncrypted?.ExecuteQuery("SELECT COUNT(*) FROM users WHERE age > 30");
        return results?.Count ?? 0;
    }

    [Benchmark(Description = "SharpCoreDB (No Encryption): COUNT WHERE")]
    public int SharpCoreDB_NoEncrypt_CountWhere()
    {
        var results = sharpCoreDbNoEncrypt?.ExecuteQuery("SELECT COUNT(*) FROM users WHERE age > 30");
        return results?.Count ?? 0;
    }

    [Benchmark(Description = "SQLite: COUNT WHERE")]
    public long SQLite_CountWhere()
    {
        using var cmd = sqliteConn?.CreateCommand();
        cmd!.CommandText = "SELECT COUNT(*) FROM users WHERE age > 30";
        var result = cmd.ExecuteScalar();
        return result != null ? Convert.ToInt64(result) : 0;
    }

    [Benchmark(Description = "LiteDB: COUNT WHERE")]
    public int LiteDB_CountWhere()
    {
        return liteCollection?.Count(x => x.Age > 30) ?? 0;
    }

    // ==================== SUM OPERATIONS ====================

    [Benchmark(Description = "SharpCoreDB (Encrypted): SUM(age)")]
    public int SharpCoreDB_Encrypted_Sum()
    {
        var results = sharpCoreDbEncrypted?.ExecuteQuery("SELECT SUM(age) FROM users");
        return results?.Count ?? 0;
    }

    [Benchmark(Description = "SharpCoreDB (No Encryption): SUM(age)")]
    public int SharpCoreDB_NoEncrypt_Sum()
    {
        var results = sharpCoreDbNoEncrypt?.ExecuteQuery("SELECT SUM(age) FROM users");
        return results?.Count ?? 0;
    }

    [Benchmark(Description = "SQLite: SUM(age)")]
    public long SQLite_Sum()
    {
        using var cmd = sqliteConn?.CreateCommand();
        cmd!.CommandText = "SELECT SUM(age) FROM users";
        var result = cmd.ExecuteScalar();
        return result != null ? Convert.ToInt64(result) : 0;
    }

    [Benchmark(Description = "LiteDB: SUM(age) - Manual LINQ")]
    public int LiteDB_Sum()
    {
        // NOTE: LiteDB doesn't have native SQL SUM, so we use LINQ
        // This is less efficient as it loads all records into memory first
        return liteCollection?.FindAll().Sum(x => x.Age) ?? 0;
    }

    // ==================== AVG OPERATIONS ====================

    [Benchmark(Description = "SharpCoreDB (Encrypted): AVG(age)")]
    public int SharpCoreDB_Encrypted_Avg()
    {
        var results = sharpCoreDbEncrypted?.ExecuteQuery("SELECT AVG(age) FROM users");
        return results?.Count ?? 0;
    }

    [Benchmark(Description = "SharpCoreDB (No Encryption): AVG(age)")]
    public int SharpCoreDB_NoEncrypt_Avg()
    {
        var results = sharpCoreDbNoEncrypt?.ExecuteQuery("SELECT AVG(age) FROM users");
        return results?.Count ?? 0;
    }

    [Benchmark(Description = "SQLite: AVG(age)")]
    public double SQLite_Avg()
    {
        using var cmd = sqliteConn?.CreateCommand();
        cmd!.CommandText = "SELECT AVG(age) FROM users";
        var result = cmd.ExecuteScalar();
        return result != null ? Convert.ToDouble(result) : 0;
    }

    [Benchmark(Description = "LiteDB: AVG(age) - Manual LINQ")]
    public double LiteDB_Avg()
    {
        // NOTE: LiteDB doesn't have native SQL AVG, so we use LINQ
        // This is less efficient as it loads all records into memory first
        return liteCollection?.FindAll().Average(x => x.Age) ?? 0;
    }

    // ==================== MIN OPERATIONS ====================

    [Benchmark(Description = "SharpCoreDB (Encrypted): MIN(age)")]
    public int SharpCoreDB_Encrypted_Min()
    {
        var results = sharpCoreDbEncrypted?.ExecuteQuery("SELECT MIN(age) FROM users");
        return results?.Count ?? 0;
    }

    [Benchmark(Description = "SharpCoreDB (No Encryption): MIN(age)")]
    public int SharpCoreDB_NoEncrypt_Min()
    {
        var results = sharpCoreDbNoEncrypt?.ExecuteQuery("SELECT MIN(age) FROM users");
        return results?.Count ?? 0;
    }

    [Benchmark(Description = "SQLite: MIN(age)")]
    public long SQLite_Min()
    {
        using var cmd = sqliteConn?.CreateCommand();
        cmd!.CommandText = "SELECT MIN(age) FROM users";
        var result = cmd.ExecuteScalar();
        return result != null ? Convert.ToInt64(result) : 0;
    }

    [Benchmark(Description = "LiteDB: MIN(age) - Manual LINQ")]
    public int LiteDB_Min()
    {
        // NOTE: LiteDB doesn't have native SQL MIN, so we use LINQ
        // This is less efficient as it loads all records into memory first
        return liteCollection?.FindAll().Min(x => x.Age) ?? 0;
    }

    // ==================== MAX OPERATIONS ====================

    [Benchmark(Description = "SharpCoreDB (Encrypted): MAX(age)")]
    public int SharpCoreDB_Encrypted_Max()
    {
        var results = sharpCoreDbEncrypted?.ExecuteQuery("SELECT MAX(age) FROM users");
        return results?.Count ?? 0;
    }

    [Benchmark(Description = "SharpCoreDB (No Encryption): MAX(age)")]
    public int SharpCoreDB_NoEncrypt_Max()
    {
        var results = sharpCoreDbNoEncrypt?.ExecuteQuery("SELECT MAX(age) FROM users");
        return results?.Count ?? 0;
    }

    [Benchmark(Description = "SQLite: MAX(age)")]
    public long SQLite_Max()
    {
        using var cmd = sqliteConn?.CreateCommand();
        cmd!.CommandText = "SELECT MAX(age) FROM users";
        var result = cmd.ExecuteScalar();
        return result != null ? Convert.ToInt64(result) : 0;
    }

    [Benchmark(Description = "LiteDB: MAX(age) - Manual LINQ")]
    public int LiteDB_Max()
    {
        // NOTE: LiteDB doesn't have native SQL MAX, so we use LINQ
        // This is less efficient as it loads all records into memory first
        return liteCollection?.FindAll().Max(x => x.Age) ?? 0;
    }

    // ==================== GROUP BY OPERATIONS ====================

    [Benchmark(Description = "SharpCoreDB (Encrypted): GROUP BY age")]
    public int SharpCoreDB_Encrypted_GroupBy()
    {
        var results = sharpCoreDbEncrypted?.ExecuteQuery("SELECT age, COUNT(*) FROM users GROUP BY age");
        return results?.Count ?? 0;
    }

    [Benchmark(Description = "SharpCoreDB (No Encryption): GROUP BY age")]
    public int SharpCoreDB_NoEncrypt_GroupBy()
    {
        var results = sharpCoreDbNoEncrypt?.ExecuteQuery("SELECT age, COUNT(*) FROM users GROUP BY age");
        return results?.Count ?? 0;
    }

    [Benchmark(Description = "SQLite: GROUP BY age")]
    public int SQLite_GroupBy()
    {
        using var cmd = sqliteConn?.CreateCommand();
        cmd!.CommandText = "SELECT age, COUNT(*) FROM users GROUP BY age";
        using var reader = cmd.ExecuteReader();
        int count = 0;
        while (reader.Read()) { count++; }
        return count;
    }

    [Benchmark(Description = "LiteDB: GROUP BY age - Manual LINQ")]
    public int LiteDB_GroupBy()
    {
        // NOTE: LiteDB doesn't have native SQL GROUP BY, so we use LINQ
        // This is less efficient as it loads all records into memory first
        var groups = liteCollection?.FindAll().GroupBy(x => x.Age).ToList();
        return groups?.Count ?? 0;
    }

    // ==================== COMPLEX AGGREGATES ====================

    [Benchmark(Description = "SharpCoreDB (Encrypted): Complex Aggregate")]
    public int SharpCoreDB_Encrypted_ComplexAggregate()
    {
        var results = sharpCoreDbEncrypted?.ExecuteQuery(@"
            SELECT age, COUNT(*) as cnt, AVG(age) as avg_age 
            FROM users 
            WHERE is_active = 1 
            GROUP BY age");
        return results?.Count ?? 0;
    }

    [Benchmark(Description = "SharpCoreDB (No Encryption): Complex Aggregate")]
    public int SharpCoreDB_NoEncrypt_ComplexAggregate()
    {
        var results = sharpCoreDbNoEncrypt?.ExecuteQuery(@"
            SELECT age, COUNT(*) as cnt, AVG(age) as avg_age 
            FROM users 
            WHERE is_active = 1 
            GROUP BY age");
        return results?.Count ?? 0;
    }

    [Benchmark(Description = "SQLite: Complex Aggregate")]
    public int SQLite_ComplexAggregate()
    {
        using var cmd = sqliteConn?.CreateCommand();
        cmd!.CommandText = @"
            SELECT age, COUNT(*) as cnt, AVG(age) as avg_age 
            FROM users 
            WHERE is_active = 1 
            GROUP BY age";
        using var reader = cmd.ExecuteReader();
        int count = 0;
        while (reader.Read()) { count++; }
        return count;
    }

    [Benchmark(Description = "LiteDB: Complex Aggregate - Manual LINQ")]
    public int LiteDB_ComplexAggregate()
    {
        // NOTE: LiteDB doesn't have native SQL GROUP BY, so we use LINQ
        // This is less efficient as it loads all records into memory first
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
        return result?.Count ?? 0;
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
