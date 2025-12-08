// <copyright file="BenchmarkDatabaseHelper.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB;
using SharpCoreDB.Services;
using System.Text;

namespace SharpCoreDB.Benchmarks.Infrastructure;

/// <summary>
/// Helper class for creating and managing SharpCoreDB databases in benchmarks.
/// Simplifies API usage for benchmark scenarios.
/// Supports both encrypted and non-encrypted modes for fair comparisons.
/// </summary>
public class BenchmarkDatabaseHelper : IDisposable
{
    private readonly IServiceProvider serviceProvider;
    private readonly Database database;
    private readonly HashSet<int> insertedIds = new();
    private readonly bool isEncrypted;

    public BenchmarkDatabaseHelper(string dbPath, string password = "benchmark_password", bool enableEncryption = true)
    {
        this.isEncrypted = enableEncryption;
        
        // Create service provider with SharpCoreDB services
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        serviceProvider = services.BuildServiceProvider();

        // Create database with appropriate config
        var factory = serviceProvider.GetRequiredService<DatabaseFactory>();
        var config = enableEncryption ? DatabaseConfig.Default : DatabaseConfig.HighPerformance;
        
        database = (Database)factory.Create(dbPath, password, false, config, null);
    }

    /// <summary>
    /// Gets whether this database instance uses encryption.
    /// </summary>
    public bool IsEncrypted => isEncrypted;

    /// <summary>
    /// Creates a table with standard columns for benchmarking.
    /// </summary>
    public void CreateUsersTable()
    {
        database.ExecuteSQL(@"
            CREATE TABLE users (
                id INTEGER PRIMARY KEY,
                name TEXT,
                email TEXT,
                age INTEGER,
                created_at TEXT,
                is_active INTEGER
            )");
    }

    // ==================== BENCHMARK METHODS (FAST PATH - NO UPSERT) ====================

    /// <summary>
    /// Fast-path insert for benchmarks - NO UPSERT overhead.
    /// Use this for accurate performance measurements.
    /// Does NOT check for duplicates or handle primary key violations.
    /// </summary>
    /// <remarks>
    /// This method is optimized for benchmarking and assumes:
    /// - IDs are unique (no duplicates)
    /// - No primary key violations will occur
    /// - Maximum performance is desired
    /// 
    /// For production use with UPSERT semantics, use InsertUser() instead.
    /// </remarks>
    public void InsertUserBenchmark(int id, string name, string email, int age, DateTime createdAt, bool isActive)
    {
        var parameters = new Dictionary<string, object?>
        {
            { "id", id },
            { "name", name },
            { "email", email },
            { "age", age },
            { "created_at", createdAt.ToString("o") },
            { "is_active", isActive ? 1 : 0 }
        };

        database.ExecuteSQL(@"
            INSERT INTO users (id, name, email, age, created_at, is_active) 
            VALUES (@id, @name, @email, @age, @created_at, @is_active)", 
            parameters);
    }

    /// <summary>
    /// Batch insert for benchmarks - single transaction for maximum performance.
    /// Inserts multiple users in a single transaction (10-50x faster than individual inserts).
    /// </summary>
    /// <param name="users">List of users to insert</param>
    /// <remarks>
    /// Uses ExecuteBatchSQL which:
    /// - Creates a single transaction
    /// - Reduces fsync() calls from N to 1
    /// - Minimizes WAL overhead
    /// - Expected speedup: 10-50x vs individual inserts
    /// </remarks>
    public void InsertUsersBatch(List<(int id, string name, string email, int age, DateTime createdAt, bool isActive)> users)
    {
        if (users == null || users.Count == 0)
            return;

        var statements = new List<string>(users.Count);
        
        foreach (var user in users)
        {
            // Escape single quotes in strings
            var safeName = user.name.Replace("'", "''");
            var safeEmail = user.email.Replace("'", "''");
            var isActiveInt = user.isActive ? 1 : 0;
            var createdAtStr = user.createdAt.ToString("o");
            
            var sql = $@"INSERT INTO users (id, name, email, age, created_at, is_active) 
                         VALUES ({user.id}, '{safeName}', '{safeEmail}', {user.age}, '{createdAtStr}', {isActiveInt})";
            statements.Add(sql);
        }
        
        database.ExecuteBatchSQL(statements);
    }

    /// <summary>
    /// Batch insert using StringBuilder for better memory efficiency.
    /// Alternative implementation that reduces string allocations.
    /// </summary>
    public void InsertUsersBatchOptimized(List<(int id, string name, string email, int age, DateTime createdAt, bool isActive)> users)
    {
        if (users == null || users.Count == 0)
            return;

        // Pre-allocate StringBuilder with estimated size
        var estimatedSize = users.Count * 150; // ~150 chars per INSERT statement
        var sb = new StringBuilder(estimatedSize);
        
        var statements = new List<string>(users.Count);
        
        foreach (var user in users)
        {
            sb.Clear();
            sb.Append("INSERT INTO users (id, name, email, age, created_at, is_active) VALUES (");
            sb.Append(user.id);
            sb.Append(", '");
            sb.Append(user.name.Replace("'", "''"));
            sb.Append("', '");
            sb.Append(user.email.Replace("'", "''"));
            sb.Append("', ");
            sb.Append(user.age);
            sb.Append(", '");
            sb.Append(user.createdAt.ToString("o"));
            sb.Append("', ");
            sb.Append(user.isActive ? 1 : 0);
            sb.Append(')');
            
            statements.Add(sb.ToString());
        }
        
        database.ExecuteBatchSQL(statements);
    }

    // ==================== PRODUCTION METHODS (WITH UPSERT) ====================

    /// <summary>
    /// Inserts a user record with duplicate detection.
    /// If user exists, updates instead (UPSERT behavior).
    /// USE FOR PRODUCTION - includes safety checks and UPSERT logic.
    /// </summary>
    /// <remarks>
    /// This method provides UPSERT semantics but has overhead:
    /// - HashSet lookup: O(1) but allocates memory
    /// - Duplicate handling: SELECT + UPDATE if duplicate found
    /// - Exception handling: Catches primary key violations
    /// 
    /// For benchmarking, use InsertUserBenchmark() instead for accurate measurements.
    /// </remarks>
    public void InsertUser(int id, string name, string email, int age, DateTime createdAt, bool isActive)
    {
        // Check if ID already inserted
        if (insertedIds.Contains(id))
        {
            // Update instead of insert
            UpdateUser(id, name, email, age, createdAt, isActive);
            return;
        }

        try
        {
            var parameters = new Dictionary<string, object?>
            {
                { "id", id },
                { "name", name },
                { "email", email },
                { "age", age },
                { "created_at", createdAt.ToString("o") },
                { "is_active", isActive ? 1 : 0 }
            };

            database.ExecuteSQL(@"
                INSERT INTO users (id, name, email, age, created_at, is_active) 
                VALUES (@id, @name, @email, @age, @created_at, @is_active)", 
                parameters);
            
            insertedIds.Add(id);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Primary key violation"))
        {
            // Primary key violation - update instead
            UpdateUser(id, name, email, age, createdAt, isActive);
            insertedIds.Add(id);
        }
    }

    /// <summary>
    /// Updates an existing user record (used for UPSERT).
    /// </summary>
    private void UpdateUser(int id, string name, string email, int age, DateTime createdAt, bool isActive)
    {
        var parameters = new Dictionary<string, object?>
        {
            { "name", name },
            { "email", email },
            { "age", age },
            { "created_at", createdAt.ToString("o") },
            { "is_active", isActive ? 1 : 0 },
            { "id", id }
        };

        database.ExecuteSQL(@"
            UPDATE users 
            SET name = @name, email = @email, age = @age, 
                created_at = @created_at, is_active = @is_active 
            WHERE id = @id", 
            parameters);
    }

    // ==================== HELPER METHODS ====================

    /// <summary>
    /// Inserts a user with a generated unique ID to avoid conflicts.
    /// </summary>
    public int InsertUserWithUniqueId(string name, string email, int age, DateTime createdAt, bool isActive)
    {
        // Generate unique ID based on current count + random offset
        int id = insertedIds.Count + Random.Shared.Next(1000000, 9000000);
        
        while (insertedIds.Contains(id))
        {
            id = Random.Shared.Next(1000000, 9000000);
        }
        
        InsertUser(id, name, email, age, createdAt, isActive);
        return id;
    }

    /// <summary>
    /// Selects users by ID.
    /// </summary>
    public List<Dictionary<string, object>> SelectUserById(int id)
    {
        try
        {
            var parameters = new Dictionary<string, object?>
            {
                { "id", id }
            };
            return database.ExecuteQuery("SELECT * FROM users WHERE id = @id", parameters);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Select error for ID {id}: {ex.Message}");
            return new List<Dictionary<string, object>>();
        }
    }

    /// <summary>
    /// Selects users by age range.
    /// </summary>
    public List<Dictionary<string, object>> SelectUsersByAgeRange(int minAge, int maxAge)
    {
        try
        {
            var parameters = new Dictionary<string, object?>
            {
                { "minAge", minAge },
                { "maxAge", maxAge }
            };
            return database.ExecuteQuery("SELECT * FROM users WHERE age >= @minAge AND age <= @maxAge", parameters);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Range query error: {ex.Message}");
            return new List<Dictionary<string, object>>();
        }
    }

    /// <summary>
    /// Selects active users.
    /// </summary>
    public List<Dictionary<string, object>> SelectActiveUsers()
    {
        try
        {
            return database.ExecuteQuery("SELECT * FROM users WHERE is_active = 1");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Active users query error: {ex.Message}");
            return new List<Dictionary<string, object>>();
        }
    }

    /// <summary>
    /// Updates user age.
    /// </summary>
    public void UpdateUserAge(int id, int newAge)
    {
        try
        {
            var parameters = new Dictionary<string, object?>
            {
                { "age", newAge },
                { "id", id }
            };
            database.ExecuteSQL("UPDATE users SET age = @age WHERE id = @id", parameters);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Update error for ID {id}: {ex.Message}");
        }
    }

    /// <summary>
    /// Deletes a user by ID.
    /// </summary>
    public void DeleteUser(int id)
    {
        try
        {
            var parameters = new Dictionary<string, object?>
            {
                { "id", id }
            };
            database.ExecuteSQL("DELETE FROM users WHERE id = @id", parameters);
            insertedIds.Remove(id);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Delete error for ID {id}: {ex.Message}");
            throw; // Re-throw to let benchmark handle it
        }
    }

    /// <summary>
    /// Clears all inserted IDs tracking (useful for cleanup between iterations).
    /// </summary>
    public void ClearInsertedIdsTracking()
    {
        insertedIds.Clear();
    }

    /// <summary>
    /// Gets count of tracked inserted IDs.
    /// </summary>
    public int GetInsertedCount() => insertedIds.Count;

    /// <summary>
    /// Executes a batch of SQL statements.
    /// </summary>
    public void ExecuteBatch(IEnumerable<string> statements)
    {
        database.ExecuteBatchSQL(statements);
    }

    public void Dispose()
    {
        // Database doesn't implement IDisposable, so just clean up service provider
        if (serviceProvider is IDisposable disposable)
        {
            disposable.Dispose();
        }
        insertedIds.Clear();
        GC.SuppressFinalize(this);
    }
}
