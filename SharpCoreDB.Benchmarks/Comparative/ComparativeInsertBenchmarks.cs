// <copyright file="ComparativeInsertBenchmarks.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using SharpCoreDB.Benchmarks.Infrastructure;
using Microsoft.Data.Sqlite;
using LiteDB;

namespace SharpCoreDB.Benchmarks.Comparative;

/// <summary>
/// Comparative benchmarks for single and bulk insert operations.
/// Tests SharpCoreDB (encrypted + non-encrypted) vs SQLite (memory/file) vs LiteDB.
/// NOW WITH: Fast-path (no UPSERT) and Batch variants for accurate measurements.
/// </summary>
[Config(typeof(BenchmarkConfig))]
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
public class ComparativeInsertBenchmarks : IDisposable
{
    private TestDataGenerator dataGenerator = null!;
    private string tempDir = null!;
    
    // SharpCoreDB - Encrypted
    private BenchmarkDatabaseHelper? sharpCoreDbEncrypted;
    
    // SharpCoreDB - No Encryption (for fair comparison)
    private BenchmarkDatabaseHelper? sharpCoreDbNoEncrypt;
    
    // SQLite
    private SqliteConnection? sqliteMemory;
    private SqliteConnection? sqliteFile;
    private string sqliteFilePath = null!;
    
    // LiteDB
    private LiteDatabase? liteDb;
    private ILiteCollection<TestDataGenerator.UserRecord>? liteCollection;
    private string liteDbFilePath = null!;
    
    // Track base ID per iteration to avoid conflicts
    private int currentBaseId = 0;

    [Params(1, 10, 100, 1000)]
    public int RecordCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        dataGenerator = new TestDataGenerator();
        tempDir = Path.Combine(Path.GetTempPath(), $"dbBenchmark_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        SetupSharpCoreDB();
        SetupSQLite();
        SetupLiteDB();
    }

    [IterationSetup]
    public void IterationSetup()
    {
        // Increment base ID for next iteration to avoid conflicts
        currentBaseId += 1000000;
    }

    private void SetupSharpCoreDB()
    {
        try
        {
            // Encrypted variant
            var dbPathEncrypted = Path.Combine(tempDir, "sharpcore_encrypted");
            sharpCoreDbEncrypted = new BenchmarkDatabaseHelper(dbPathEncrypted, enableEncryption: true);
            sharpCoreDbEncrypted.CreateUsersTable();
            Console.WriteLine("? SharpCoreDB (Encrypted) setup complete");
            
            // Non-encrypted variant
            var dbPathNoEncrypt = Path.Combine(tempDir, "sharpcore_noencrypt");
            sharpCoreDbNoEncrypt = new BenchmarkDatabaseHelper(dbPathNoEncrypt, enableEncryption: false);
            sharpCoreDbNoEncrypt.CreateUsersTable();
            Console.WriteLine("? SharpCoreDB (No Encryption) setup complete");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"? SharpCoreDB setup failed: {ex.Message}");
            throw;
        }
    }

    private void SetupSQLite()
    {
        // SQLite in-memory
        sqliteMemory = new SqliteConnection("Data Source=:memory:");
        sqliteMemory.Open();
        CreateSQLiteTable(sqliteMemory);

        // SQLite file
        sqliteFilePath = Path.Combine(tempDir, "sqlite.db");
        sqliteFile = new SqliteConnection($"Data Source={sqliteFilePath}");
        sqliteFile.Open();
        CreateSQLiteTable(sqliteFile);
    }

    private void CreateSQLiteTable(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS users (
                id INTEGER PRIMARY KEY,
                name TEXT NOT NULL,
                email TEXT NOT NULL,
                age INTEGER NOT NULL,
                created_at TEXT NOT NULL,
                is_active INTEGER NOT NULL
            )";
        cmd.ExecuteNonQuery();
    }

    private void SetupLiteDB()
    {
        liteDbFilePath = Path.Combine(tempDir, "litedb.db");
        liteDb = new LiteDatabase(liteDbFilePath);
        liteCollection = liteDb.GetCollection<TestDataGenerator.UserRecord>("users");
    }

    // ==================== SHARPCOREDB (ENCRYPTED) - INDIVIDUAL INSERTS ====================

    [Benchmark(Description = "SharpCoreDB (Encrypted): Individual Inserts")]
    public int SharpCoreDB_Encrypted_Individual()
    {
        var users = dataGenerator.GenerateUsers(RecordCount);
        int inserted = 0;
        
        foreach (var user in users)
        {
            try
            {
                int uniqueId = currentBaseId + user.Id;
                
                // Use FAST PATH - no UPSERT overhead!
                sharpCoreDbEncrypted?.InsertUserBenchmark(
                    uniqueId, 
                    user.Name, 
                    user.Email, 
                    user.Age, 
                    user.CreatedAt, 
                    user.IsActive);
                inserted++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SharpCoreDB (Encrypted) insert error: {ex.Message}");
            }
        }
        
        return inserted;
    }

    // ==================== SHARPCOREDB (ENCRYPTED) - BATCH INSERTS ====================

    [Benchmark(Description = "SharpCoreDB (Encrypted): Batch Insert")]
    public int SharpCoreDB_Encrypted_Batch()
    {
        var users = dataGenerator.GenerateUsers(RecordCount);
        
        // Convert to batch format
        var userList = users.Select(u => 
            (currentBaseId + u.Id, u.Name, u.Email, u.Age, u.CreatedAt, u.IsActive)
        ).ToList();
        
        try
        {
            sharpCoreDbEncrypted?.InsertUsersBatch(userList);
            return RecordCount;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"SharpCoreDB (Encrypted) batch insert error: {ex.Message}");
            return 0;
        }
    }

    // ==================== SHARPCOREDB (NO ENCRYPTION) - INDIVIDUAL INSERTS ====================

    [Benchmark(Description = "SharpCoreDB (No Encryption): Individual Inserts")]
    public int SharpCoreDB_NoEncrypt_Individual()
    {
        var users = dataGenerator.GenerateUsers(RecordCount);
        int inserted = 0;
        
        foreach (var user in users)
        {
            try
            {
                int uniqueId = currentBaseId + user.Id;
                
                // Use FAST PATH - no UPSERT overhead!
                sharpCoreDbNoEncrypt?.InsertUserBenchmark(
                    uniqueId, 
                    user.Name, 
                    user.Email, 
                    user.Age, 
                    user.CreatedAt, 
                    user.IsActive);
                inserted++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SharpCoreDB (No Encryption) insert error: {ex.Message}");
            }
        }
        
        return inserted;
    }

    // ==================== SHARPCOREDB (NO ENCRYPTION) - BATCH INSERTS ====================

    [Benchmark(Description = "SharpCoreDB (No Encryption): Batch Insert")]
    public int SharpCoreDB_NoEncrypt_Batch()
    {
        var users = dataGenerator.GenerateUsers(RecordCount);
        
        // Convert to batch format
        var userList = users.Select(u => 
            (currentBaseId + u.Id, u.Name, u.Email, u.Age, u.CreatedAt, u.IsActive)
        ).ToList();
        
        try
        {
            sharpCoreDbNoEncrypt?.InsertUsersBatch(userList);
            return RecordCount;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"SharpCoreDB (No Encryption) batch insert error: {ex.Message}");
            return 0;
        }
    }

    // ==================== SQLITE MEMORY ====================

    [Benchmark(Baseline = true, Description = "SQLite Memory: Bulk Insert")]
    public void SQLite_Memory_BulkInsert()
    {
        var users = dataGenerator.GenerateUsers(RecordCount);
        
        using var transaction = sqliteMemory?.BeginTransaction();
        using var cmd = sqliteMemory?.CreateCommand();
        cmd!.CommandText = @"
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
            // Use currentBaseId for unique IDs
            cmd.Parameters["@id"].Value = currentBaseId + user.Id;
            cmd.Parameters["@name"].Value = user.Name;
            cmd.Parameters["@email"].Value = user.Email;
            cmd.Parameters["@age"].Value = user.Age;
            cmd.Parameters["@created_at"].Value = user.CreatedAt.ToString("o");
            cmd.Parameters["@is_active"].Value = user.IsActive ? 1 : 0;
            cmd.ExecuteNonQuery();
        }

        transaction?.Commit();
    }

    // ==================== SQLITE FILE ====================

    [Benchmark(Description = "SQLite File: Bulk Insert")]
    public void SQLite_File_BulkInsert()
    {
        var users = dataGenerator.GenerateUsers(RecordCount);
        
        using var transaction = sqliteFile?.BeginTransaction();
        using var cmd = sqliteFile?.CreateCommand();
        cmd!.CommandText = @"
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
            // Use currentBaseId for unique IDs
            cmd.Parameters["@id"].Value = currentBaseId + user.Id;
            cmd.Parameters["@name"].Value = user.Name;
            cmd.Parameters["@email"].Value = user.Email;
            cmd.Parameters["@age"].Value = user.Age;
            cmd.Parameters["@created_at"].Value = user.CreatedAt.ToString("o");
            cmd.Parameters["@is_active"].Value = user.IsActive ? 1 : 0;
            cmd.ExecuteNonQuery();
        }

        transaction?.Commit();
    }

    // ==================== LITEDB ====================

    [Benchmark(Description = "LiteDB: Bulk Insert")]
    public void LiteDB_BulkInsert()
    {
        var users = dataGenerator.GenerateUsers(RecordCount);
        
        // Use currentBaseId for unique IDs
        foreach (var user in users)
        {
            user.Id = currentBaseId + user.Id;
        }
        
        liteCollection?.InsertBulk(users);
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
        sqliteMemory?.Dispose();
        sqliteFile?.Dispose();
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
