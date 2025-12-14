// Quick 10k benchmark test
using SharpCoreDB.Benchmarks.Infrastructure;
using Microsoft.Data.Sqlite;
using LiteDB;
using System.Diagnostics;

namespace SharpCoreDB.Quick10kBenchmark;

class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("???????????????????????????????????????????????????????");
        Console.WriteLine("  SharpCoreDB vs SQLite vs LiteDB - 10K Records Test");
        Console.WriteLine("???????????????????????????????????????????????????????\n");

        var tempDir = Path.Combine(Path.GetTempPath(), $"dbBenchmark_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        var dataGenerator = new TestDataGenerator();
        const int RecordCount = 10000;

        try
        {
            // ??? SharpCoreDB (No Encryption) ???
            Console.WriteLine("?? Testing SharpCoreDB (No Encryption)...");
            var dbPath = Path.Combine(tempDir, "sharpcore_noencrypt");
            using (var sharpDb = new BenchmarkDatabaseHelper(dbPath, enableEncryption: false))
            {
                sharpDb.CreateUsersTable();
                
                var users = dataGenerator.GenerateUsers(RecordCount);
                var userList = users.Select(u => (u.Id, u.Name, u.Email, u.Age, u.CreatedAt, u.IsActive)).ToList();
                
                var sw = Stopwatch.StartNew();
                sharpDb.InsertUsersTrueBatch(userList);
                sw.Stop();
                
                Console.WriteLine($"   Time: {sw.ElapsedMilliseconds}ms ({sw.ElapsedMilliseconds / (double)RecordCount:F3}ms per record)");
                Console.WriteLine($"   Throughput: {RecordCount / sw.Elapsed.TotalSeconds:N0} records/sec\n");
            }
            
            // ??? SharpCoreDB (Encrypted) ???
            Console.WriteLine("?? Testing SharpCoreDB (Encrypted)...");
            var dbPathEnc = Path.Combine(tempDir, "sharpcore_encrypted");
            using (var sharpDbEnc = new BenchmarkDatabaseHelper(dbPathEnc, enableEncryption: true))
            {
                sharpDbEnc.CreateUsersTable();
                
                var users = dataGenerator.GenerateUsers(RecordCount);
                var userList = users.Select(u => (u.Id, u.Name, u.Email, u.Age, u.CreatedAt, u.IsActive)).ToList();
                
                var sw = Stopwatch.StartNew();
                sharpDbEnc.InsertUsersTrueBatch(userList);
                sw.Stop();
                
                Console.WriteLine($"   Time: {sw.ElapsedMilliseconds}ms ({sw.ElapsedMilliseconds / (double)RecordCount:F3}ms per record)");
                Console.WriteLine($"   Throughput: {RecordCount / sw.Elapsed.TotalSeconds:N0} records/sec\n");
            }
            
            // ??? SQLite Memory ???
            Console.WriteLine("?? Testing SQLite (Memory)...");
            using (var sqliteConn = new SqliteConnection("Data Source=:memory:"))
            {
                sqliteConn.Open();
                
                using (var cmd = sqliteConn.CreateCommand())
                {
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
                }
                
                var users = dataGenerator.GenerateUsers(RecordCount);
                var sw = Stopwatch.StartNew();
                
                using (var transaction = sqliteConn.BeginTransaction())
                {
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
                }
                
                sw.Stop();
                Console.WriteLine($"   Time: {sw.ElapsedMilliseconds}ms ({sw.ElapsedMilliseconds / (double)RecordCount:F3}ms per record)");
                Console.WriteLine($"   Throughput: {RecordCount / sw.Elapsed.TotalSeconds:N0} records/sec\n");
            }
            
            // ??? SQLite File + WAL + FullSync ???
            Console.WriteLine("?? Testing SQLite (File + WAL + FullSync)...");
            var sqliteFilePath = Path.Combine(tempDir, "sqlite_wal.db");
            using (var sqliteFile = new SqliteConnection($"Data Source={sqliteFilePath}"))
            {
                sqliteFile.Open();
                
                using (var cmd = sqliteFile.CreateCommand())
                {
                    cmd.CommandText = "PRAGMA journal_mode = WAL";
                    cmd.ExecuteNonQuery();
                    cmd.CommandText = "PRAGMA synchronous = FULL";
                    cmd.ExecuteNonQuery();
                    
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
                }
                
                var users = dataGenerator.GenerateUsers(RecordCount);
                var sw = Stopwatch.StartNew();
                
                using (var transaction = sqliteFile.BeginTransaction())
                {
                    using var cmd = sqliteFile.CreateCommand();
                    cmd.Transaction = transaction;
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
                }
                
                sw.Stop();
                Console.WriteLine($"   Time: {sw.ElapsedMilliseconds}ms ({sw.ElapsedMilliseconds / (double)RecordCount:F3}ms per record)");
                Console.WriteLine($"   Throughput: {RecordCount / sw.Elapsed.TotalSeconds:N0} records/sec\n");
            }
            
            // ??? LiteDB ???
            Console.WriteLine("?? Testing LiteDB...");
            var liteDbPath = Path.Combine(tempDir, "litedb.db");
            using (var liteDb = new LiteDatabase(liteDbPath))
            {
                var collection = liteDb.GetCollection<TestDataGenerator.UserRecord>("users");
                
                var users = dataGenerator.GenerateUsers(RecordCount);
                var sw = Stopwatch.StartNew();
                
                collection.InsertBulk(users);
                
                sw.Stop();
                Console.WriteLine($"   Time: {sw.ElapsedMilliseconds}ms ({sw.ElapsedMilliseconds / (double)RecordCount:F3}ms per record)");
                Console.WriteLine($"   Throughput: {RecordCount / sw.Elapsed.TotalSeconds:N0} records/sec\n");
            }
            
            Console.WriteLine("???????????????????????????????????????????????????????");
            Console.WriteLine("  Benchmark Complete!");
            Console.WriteLine("???????????????????????????????????????????????????????");
        }
        finally
        {
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
        }
    }
}
