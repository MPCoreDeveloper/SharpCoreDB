// <copyright file="SingleFileDemo.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

using SharpCoreDB;
using System;
using System.IO;

class SingleFileDemo
{
    static void Main()
    {
        Console.WriteLine("SharpCoreDB Single-File Database Demo");
        Console.WriteLine("=====================================");

        var dbPath = Path.Combine(Path.GetTempPath(), "demo.scdb");

        try
        {
            // Clean up any existing file
            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }

            // Create single-file database
            Console.WriteLine("1. Creating single-file database...");
            var options = DatabaseOptions.CreateSingleFileDefault();
            var factory = new DatabaseFactory(null);
            var db = factory.CreateWithOptions(dbPath, "demo_password", options);

            Console.WriteLine($"   Database created at: {dbPath}");
            Console.WriteLine($"   Storage mode: {db.StorageMode}");

            // Create a table
            Console.WriteLine("\n2. Creating table...");
            db.ExecuteSQL("CREATE TABLE users (id INTEGER, name TEXT, email TEXT)");

            // Insert some data
            Console.WriteLine("3. Inserting data...");
            db.ExecuteSQL("INSERT INTO users VALUES (1, 'Alice', 'alice@example.com')");
            db.ExecuteSQL("INSERT INTO users VALUES (2, 'Bob', 'bob@example.com')");

            // Query data
            Console.WriteLine("4. Querying data...");
            var results = db.ExecuteQuery("SELECT * FROM users");
            Console.WriteLine($"   Found {results.Count} rows:");
            foreach (var row in results)
            {
                Console.WriteLine($"   ID: {row["id"]}, Name: {row["name"]}, Email: {row["email"]}");
            }

            // Get storage statistics
            Console.WriteLine("\n5. Storage statistics...");
            var stats = db.GetStorageStatistics();
            Console.WriteLine($"   Total size: {stats.TotalSize} bytes");
            Console.WriteLine($"   Used space: {stats.UsedSpace} bytes");
            Console.WriteLine($"   Free space: {stats.FreeSpace} bytes");
            Console.WriteLine($"   Block count: {stats.BlockCount}");

            // Run VACUUM
            Console.WriteLine("\n6. Running VACUUM...");
            var vacuumResult = db.VacuumAsync().GetAwaiter().GetResult();
            Console.WriteLine($"   VACUUM completed in {vacuumResult.DurationMs}ms");
            Console.WriteLine($"   Success: {vacuumResult.Success}");

            Console.WriteLine("\n✅ Demo completed successfully!");

        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
        }
        finally
        {
            // Clean up
            if (File.Exists(dbPath))
            {
                try
                {
                    File.Delete(dbPath);
                    Console.WriteLine($"\nCleaned up demo file: {dbPath}");
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }
    }
}
