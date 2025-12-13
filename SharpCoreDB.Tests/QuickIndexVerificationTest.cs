// <copyright file="QuickIndexVerificationTest.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
using Xunit;
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB;
using System.Diagnostics;

namespace SharpCoreDB.Tests;

/// <summary>
/// Quick test to verify that the WHERE clause parser fix is working.
/// </summary>
public class QuickIndexVerificationTest
{
    [Fact]
    public void IndexLookup_WithParameterizedQuery_Works()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        var sp = services.BuildServiceProvider();

        var factory = sp.GetRequiredService<DatabaseFactory>();
        var config = new DatabaseConfig
        {
            UseGroupCommitWal = false,
            NoEncryptMode = true,
            EnableQueryCache = true
        };

        var testDir = Path.Combine(Path.GetTempPath(), $"indextest_{Guid.NewGuid()}");
        Directory.CreateDirectory(testDir);

        try
        {
            var db = factory.Create(testDir, "test", false, config, null);
            
            // Create table with index
            db.ExecuteSQL(@"
                CREATE TABLE users (
                    id INTEGER PRIMARY KEY,
                    name TEXT,
                    email TEXT
                )");
            
            db.ExecuteSQL("CREATE INDEX idx_users_id ON users (id)");
            
            // Insert test data
            for (int i = 1; i <= 10; i++)
            {
                var parameters = new Dictionary<string, object?>
                {
                    { "id", i },
                    { "name", $"User{i}" },
                    { "email", $"user{i}@test.com" }
                };
                db.ExecuteSQL("INSERT INTO users (id, name, email) VALUES (@id, @name, @email)", parameters);
            }
            
            // Act - Test query with parameters (this should use index)
            var sw = Stopwatch.StartNew();
            var queryParams = new Dictionary<string, object?> { { "id", 5 } };
            var results = db.ExecuteQuery("SELECT * FROM users WHERE id = @id", queryParams);
            sw.Stop();
            
            // Assert
            Assert.Single(results);
            Assert.Equal(5, results[0]["id"]);
            Assert.Equal("User5", results[0]["name"]);
            Assert.Equal("user5@test.com", results[0]["email"]);
            
            // Performance check - with index should be fast (< 100ms is reasonable for first run)
            Assert.True(sw.ElapsedMilliseconds < 100, 
                $"Query took {sw.ElapsedMilliseconds}ms, expected < 100ms with index");
            
            Console.WriteLine($"? Index lookup test passed!");
            Console.WriteLine($"   Query time: {sw.Elapsed.TotalMicroseconds:F2} ?s");
            Console.WriteLine($"   Results: {results.Count}");
        }
        finally
        {
            try { Directory.Delete(testDir, true); } catch { }
        }
    }
    
    [Fact]
    public void IndexLookup_WithLiteralQuery_Works()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        var sp = services.BuildServiceProvider();

        var factory = sp.GetRequiredService<DatabaseFactory>();
        var config = new DatabaseConfig
        {
            UseGroupCommitWal = false,
            NoEncryptMode = true
        };

        var testDir = Path.Combine(Path.GetTempPath(), $"indextest_{Guid.NewGuid()}");
        Directory.CreateDirectory(testDir);

        try
        {
            var db = factory.Create(testDir, "test", false, config, null);
            
            // Create table with index
            db.ExecuteSQL(@"
                CREATE TABLE users (
                    id INTEGER PRIMARY KEY,
                    name TEXT
                )");
            
            db.ExecuteSQL("CREATE INDEX idx_users_id ON users (id)");
            
            // Insert test data
            for (int i = 1; i <= 10; i++)
            {
                db.ExecuteSQL($"INSERT INTO users (id, name) VALUES ({i}, 'User{i}')");
            }
            
            // Act - Test query with literal value
            var results = db.ExecuteQuery("SELECT * FROM users WHERE id = 7");
            
            // Assert
            Assert.Single(results);
            Assert.Equal(7, results[0]["id"]);
            Assert.Equal("User7", results[0]["name"]);
            
            Console.WriteLine($"? Literal query test passed!");
        }
        finally
        {
            try { Directory.Delete(testDir, true); } catch { }
        }
    }
}
