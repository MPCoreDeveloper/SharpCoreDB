// Realistic Workload Benchmark - Mixed operations
// Tests: Bulk inserts, Individual inserts, Mixed (insert+update+query)
using System.Diagnostics;
using System.Text;
using Microsoft.Data.Sqlite;
using LiteDB;
using SharpCoreDB.Benchmarks.Infrastructure;

namespace SharpCoreDB.Benchmarks;

public class RealisticWorkloadBenchmark
{
    private const int BULK_RECORDS = 10_000;
    private const int INDIVIDUAL_RECORDS = 10_000;
    private const int MIXED_INSERTS = 5_000;
    private const int MIXED_UPDATES = 5_000;
    private const int POINT_QUERIES = 1_000;

    private readonly string tempDir;
    private readonly TestDataGenerator dataGen;
    private readonly List<WorkloadResult> results = [];

    public class WorkloadResult
    {
        public string Database { get; set; } = "";
        public string Workload { get; set; } = "";
        public long TimeMs { get; set; }
        public long DbSizeBytes { get; set; }
        public long WalSizeBytes { get; set; }
        public long VacuumTimeMs { get; set; }
        public bool IsWinner { get; set; }
    }

    public RealisticWorkloadBenchmark()
    {
        tempDir = Path.Combine(Path.GetTempPath(), $"workloadBench_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        dataGen = new TestDataGenerator();
    }

    public void Run()
    {
        Console.WriteLine("????????????????????????????????????????????????????????????????????????");
        Console.WriteLine("?     REALISTIC WORKLOAD BENCHMARKS                                    ?");
        Console.WriteLine("????????????????????????????????????????????????????????????????????????");
        Console.WriteLine();

        // Test 1: Bulk Inserts (10k in single transaction)
        Console.WriteLine("??? TEST 1: BULK INSERTS (10K, SINGLE TRANSACTION) ???");
        RunBulkInserts();
        Console.WriteLine();

        // Test 2: Individual Inserts (10k separate transactions)
        Console.WriteLine("??? TEST 2: INDIVIDUAL INSERTS (10K, SEPARATE TRANSACTIONS) ???");
        RunIndividualInserts();
        Console.WriteLine();

        // Test 3: Mixed Workload
        Console.WriteLine("??? TEST 3: MIXED WORKLOAD (5K INSERTS + 5K UPDATES + 1K QUERIES) ???");
        RunMixedWorkload();
        Console.WriteLine();

        DetermineWinners();
        PrintMarkdownTable();
        
        try { Directory.Delete(tempDir, true); } catch { }
    }

    private void RunBulkInserts()
    {
        // SharpCoreDB
        var scPath = Path.Combine(tempDir, "sc_bulk");
        CleanupDb(scPath);
        using (var helper = new BenchmarkDatabaseHelper(scPath, "pwd", false))
        {
            helper.CreateUsersTable();
            var users = dataGen.GenerateUsers(BULK_RECORDS);
            var list = users.Select((u, i) => (i, u.Name, u.Email, u.Age, u.CreatedAt, u.IsActive)).ToList();
            
            var sw = Stopwatch.StartNew();
            helper.InsertUsersTrueBatch(list);
            sw.Stop();

            var vacSw = Stopwatch.StartNew();
            // SharpCore doesn't have VACUUM, skip
            vacSw.Stop();

            results.Add(new WorkloadResult
            {
                Database = "SharpCoreDB",
                Workload = "Bulk Inserts",
                TimeMs = sw.ElapsedMilliseconds,
                DbSizeBytes = GetDirectorySize(scPath), // ? FIX: Use directory size
                WalSizeBytes = 0,
                VacuumTimeMs = 0
            });

            Console.WriteLine($"  SharpCore: {sw.ElapsedMilliseconds:N0} ms");
        }

        // SQLite
        var sqlPath = Path.Combine(tempDir, "sqlite_bulk.db");
        CleanupDb(sqlPath);
        using (var conn = new SqliteConnection($"Data Source={sqlPath}"))
        {
            conn.Open();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA page_size=4096;";
                cmd.ExecuteNonQuery();
            }
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "CREATE TABLE users (id INT PRIMARY KEY, name TEXT, email TEXT, age INT, created_at TEXT, is_active INT)";
                cmd.ExecuteNonQuery();
                cmd.CommandText = "CREATE INDEX idx_id ON users(id)";
                cmd.ExecuteNonQuery();
                cmd.CommandText = "CREATE INDEX idx_age ON users(age)";
                cmd.ExecuteNonQuery();
            }

            var users = dataGen.GenerateUsers(BULK_RECORDS);
            
            var sw = Stopwatch.StartNew();
            using var txn = conn.BeginTransaction();
            using var insert = conn.CreateCommand();
            insert.Transaction = txn;
            insert.CommandText = "INSERT INTO users VALUES (@id, @name, @email, @age, @created_at, @is_active)";
            insert.Parameters.Add("@id", SqliteType.Integer);
            insert.Parameters.Add("@name", SqliteType.Text);
            insert.Parameters.Add("@email", SqliteType.Text);
            insert.Parameters.Add("@age", SqliteType.Integer);
            insert.Parameters.Add("@created_at", SqliteType.Text);
            insert.Parameters.Add("@is_active", SqliteType.Integer);

            for (int i = 0; i < users.Count; i++)
            {
                var u = users[i];
                insert.Parameters["@id"].Value = i;
                insert.Parameters["@name"].Value = u.Name;
                insert.Parameters["@email"].Value = u.Email;
                insert.Parameters["@age"].Value = u.Age;
                insert.Parameters["@created_at"].Value = u.CreatedAt.ToString("o");
                insert.Parameters["@is_active"].Value = u.IsActive ? 1 : 0;
                insert.ExecuteNonQuery();
            }
            txn.Commit();
            sw.Stop();

            var vacSw = Stopwatch.StartNew();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "PRAGMA wal_checkpoint(FULL)";
                cmd.ExecuteNonQuery();
            }
            vacSw.Stop();

            results.Add(new WorkloadResult
            {
                Database = "SQLite",
                Workload = "Bulk Inserts",
                TimeMs = sw.ElapsedMilliseconds,
                DbSizeBytes = GetFileSize(sqlPath),
                WalSizeBytes = GetFileSize(sqlPath + "-wal"),
                VacuumTimeMs = vacSw.ElapsedMilliseconds
            });

            Console.WriteLine($"  SQLite: {sw.ElapsedMilliseconds:N0} ms (checkpoint: {vacSw.ElapsedMilliseconds:N0} ms)");
        }

        // LiteDB
        var litePath = Path.Combine(tempDir, "litedb_bulk.db");
        CleanupDb(litePath);
        using (var db = new LiteDatabase(litePath))
        {
            var col = db.GetCollection<TestDataGenerator.UserRecord>("users");
            col.EnsureIndex(x => x.Id);
            col.EnsureIndex(x => x.Age);

            var users = dataGen.GenerateUsers(BULK_RECORDS);
            int baseId = Random.Shared.Next(1000000, 9000000);
            for (int i = 0; i < users.Count; i++)
                users[i].Id = baseId + i;

            var sw = Stopwatch.StartNew();
            col.InsertBulk(users);
            sw.Stop();

            var vacSw = Stopwatch.StartNew();
            db.Checkpoint();
            vacSw.Stop();

            results.Add(new WorkloadResult
            {
                Database = "LiteDB",
                Workload = "Bulk Inserts",
                TimeMs = sw.ElapsedMilliseconds,
                DbSizeBytes = GetFileSize(litePath),
                WalSizeBytes = 0,
                VacuumTimeMs = vacSw.ElapsedMilliseconds
            });

            Console.WriteLine($"  LiteDB: {sw.ElapsedMilliseconds:N0} ms (checkpoint: {vacSw.ElapsedMilliseconds:N0} ms)");
        }
    }

    private void RunIndividualInserts()
    {
        // SharpCoreDB
        var scPath = Path.Combine(tempDir, "sc_individual");
        CleanupDb(scPath);
        using (var helper = new BenchmarkDatabaseHelper(scPath, "pwd", false))
        {
            helper.CreateUsersTable();
            var users = dataGen.GenerateUsers(INDIVIDUAL_RECORDS);
            
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < users.Count; i++)
            {
                var u = users[i];
                helper.InsertUserBenchmark(i, u.Name, u.Email, u.Age, u.CreatedAt, u.IsActive);
            }
            sw.Stop();

            results.Add(new WorkloadResult
            {
                Database = "SharpCoreDB",
                Workload = "Individual Inserts",
                TimeMs = sw.ElapsedMilliseconds,
                DbSizeBytes = GetDirectorySize(scPath), // ? FIX: Use directory size
                WalSizeBytes = 0,
                VacuumTimeMs = 0
            });

            Console.WriteLine($"  SharpCore: {sw.ElapsedMilliseconds:N0} ms");
        }

        // SQLite
        var sqlPath = Path.Combine(tempDir, "sqlite_individual.db");
        CleanupDb(sqlPath);
        using (var conn = new SqliteConnection($"Data Source={sqlPath}"))
        {
            conn.Open();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA page_size=4096;";
                cmd.ExecuteNonQuery();
            }
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "CREATE TABLE users (id INT PRIMARY KEY, name TEXT, email TEXT, age INT, created_at TEXT, is_active INT)";
                cmd.ExecuteNonQuery();
            }

            var users = dataGen.GenerateUsers(INDIVIDUAL_RECORDS);
            
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < users.Count; i++)
            {
                var u = users[i];
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "INSERT INTO users VALUES (@id, @name, @email, @age, @created_at, @is_active)";
                cmd.Parameters.AddWithValue("@id", i);
                cmd.Parameters.AddWithValue("@name", u.Name);
                cmd.Parameters.AddWithValue("@email", u.Email);
                cmd.Parameters.AddWithValue("@age", u.Age);
                cmd.Parameters.AddWithValue("@created_at", u.CreatedAt.ToString("o"));
                cmd.Parameters.AddWithValue("@is_active", u.IsActive ? 1 : 0);
                cmd.ExecuteNonQuery();
            }
            sw.Stop();

            results.Add(new WorkloadResult
            {
                Database = "SQLite",
                Workload = "Individual Inserts",
                TimeMs = sw.ElapsedMilliseconds,
                DbSizeBytes = GetFileSize(sqlPath),
                WalSizeBytes = GetFileSize(sqlPath + "-wal"),
                VacuumTimeMs = 0
            });

            Console.WriteLine($"  SQLite: {sw.ElapsedMilliseconds:N0} ms");
        }

        // LiteDB
        var litePath = Path.Combine(tempDir, "litedb_individual.db");
        CleanupDb(litePath);
        using (var db = new LiteDatabase(litePath))
        {
            var col = db.GetCollection<TestDataGenerator.UserRecord>("users");
            var users = dataGen.GenerateUsers(INDIVIDUAL_RECORDS);
            int baseId = Random.Shared.Next(1000000, 9000000);

            var sw = Stopwatch.StartNew();
            for (int i = 0; i < users.Count; i++)
            {
                users[i].Id = baseId + i;
                col.Insert(users[i]);
            }
            sw.Stop();

            results.Add(new WorkloadResult
            {
                Database = "LiteDB",
                Workload = "Individual Inserts",
                TimeMs = sw.ElapsedMilliseconds,
                DbSizeBytes = GetFileSize(litePath),
                WalSizeBytes = 0,
                VacuumTimeMs = 0
            });

            Console.WriteLine($"  LiteDB: {sw.ElapsedMilliseconds:N0} ms");
        }
    }

    private void RunMixedWorkload()
    {
        // SharpCoreDB
        var scPath = Path.Combine(tempDir, "sc_mixed");
        CleanupDb(scPath);
        using (var helper = new BenchmarkDatabaseHelper(scPath, "pwd", false))
        {
            helper.CreateUsersTable();
            
            var sw = Stopwatch.StartNew();
            
            // 5K inserts
            var insertUsers = dataGen.GenerateUsers(MIXED_INSERTS);
            var insertList = insertUsers.Select((u, i) => (i, u.Name, u.Email, u.Age, u.CreatedAt, u.IsActive)).ToList();
            helper.InsertUsersTrueBatch(insertList);
            
            // 5K updates
            for (int i = 0; i < MIXED_UPDATES; i++)
            {
                helper.UpdateUserAge(i, Random.Shared.Next(18, 80));
            }
            
            // 1K point queries
            for (int i = 0; i < POINT_QUERIES; i++)
            {
                helper.SelectUserById(Random.Shared.Next(0, MIXED_INSERTS));
            }
            
            sw.Stop();

            results.Add(new WorkloadResult
            {
                Database = "SharpCoreDB",
                Workload = "Mixed (5K+5K+1K)",
                TimeMs = sw.ElapsedMilliseconds,
                DbSizeBytes = GetDirectorySize(scPath), // ? FIX: Use directory size
                WalSizeBytes = 0,
                VacuumTimeMs = 0
            });

            Console.WriteLine($"  SharpCore: {sw.ElapsedMilliseconds:N0} ms");
        }

        // SQLite
        var sqlPath = Path.Combine(tempDir, "sqlite_mixed.db");
        CleanupDb(sqlPath);
        using (var conn = new SqliteConnection($"Data Source={sqlPath}"))
        {
            conn.Open();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA page_size=4096;";
                cmd.ExecuteNonQuery();
            }
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "CREATE TABLE users (id INT PRIMARY KEY, name TEXT, email TEXT, age INT, created_at TEXT, is_active INT)";
                cmd.ExecuteNonQuery();
                cmd.CommandText = "CREATE INDEX idx_id ON users(id)";
                cmd.ExecuteNonQuery();
            }

            var sw = Stopwatch.StartNew();
            
            // 5K inserts
            var insertUsers = dataGen.GenerateUsers(MIXED_INSERTS);
            using (var txn = conn.BeginTransaction())
            {
                using var insert = conn.CreateCommand();
                insert.Transaction = txn;
                insert.CommandText = "INSERT INTO users VALUES (@id, @name, @email, @age, @created_at, @is_active)";
                insert.Parameters.Add("@id", SqliteType.Integer);
                insert.Parameters.Add("@name", SqliteType.Text);
                insert.Parameters.Add("@email", SqliteType.Text);
                insert.Parameters.Add("@age", SqliteType.Integer);
                insert.Parameters.Add("@created_at", SqliteType.Text);
                insert.Parameters.Add("@is_active", SqliteType.Integer);

                for (int i = 0; i < insertUsers.Count; i++)
                {
                    var u = insertUsers[i];
                    insert.Parameters["@id"].Value = i;
                    insert.Parameters["@name"].Value = u.Name;
                    insert.Parameters["@email"].Value = u.Email;
                    insert.Parameters["@age"].Value = u.Age;
                    insert.Parameters["@created_at"].Value = u.CreatedAt.ToString("o");
                    insert.Parameters["@is_active"].Value = u.IsActive ? 1 : 0;
                    insert.ExecuteNonQuery();
                }
                txn.Commit();
            }
            
            // 5K updates
            for (int i = 0; i < MIXED_UPDATES; i++)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "UPDATE users SET age = @age WHERE id = @id";
                cmd.Parameters.AddWithValue("@age", Random.Shared.Next(18, 80));
                cmd.Parameters.AddWithValue("@id", i);
                cmd.ExecuteNonQuery();
            }
            
            // 1K point queries
            for (int i = 0; i < POINT_QUERIES; i++)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT * FROM users WHERE id = @id";
                cmd.Parameters.AddWithValue("@id", Random.Shared.Next(0, MIXED_INSERTS));
                using var reader = cmd.ExecuteReader();
                while (reader.Read()) { }
            }
            
            sw.Stop();

            results.Add(new WorkloadResult
            {
                Database = "SQLite",
                Workload = "Mixed (5K+5K+1K)",
                TimeMs = sw.ElapsedMilliseconds,
                DbSizeBytes = GetFileSize(sqlPath),
                WalSizeBytes = GetFileSize(sqlPath + "-wal"),
                VacuumTimeMs = 0
            });

            Console.WriteLine($"  SQLite: {sw.ElapsedMilliseconds:N0} ms");
        }

        // LiteDB
        var litePath = Path.Combine(tempDir, "litedb_mixed.db");
        CleanupDb(litePath);
        using (var db = new LiteDatabase(litePath))
        {
            var col = db.GetCollection<TestDataGenerator.UserRecord>("users");
            col.EnsureIndex(x => x.Id);
            
            var sw = Stopwatch.StartNew();
            
            // 5K inserts
            var insertUsers = dataGen.GenerateUsers(MIXED_INSERTS);
            int baseId = Random.Shared.Next(1000000, 9000000);
            for (int i = 0; i < insertUsers.Count; i++)
                insertUsers[i].Id = baseId + i;
            col.InsertBulk(insertUsers);
            
            // 5K updates
            for (int i = 0; i < MIXED_UPDATES; i++)
            {
                var user = col.FindById(baseId + i);
                if (user != null)
                {
                    user.Age = Random.Shared.Next(18, 80);
                    col.Update(user);
                }
            }
            
            // 1K point queries
            for (int i = 0; i < POINT_QUERIES; i++)
            {
                col.FindById(baseId + Random.Shared.Next(0, MIXED_INSERTS));
            }
            
            sw.Stop();

            results.Add(new WorkloadResult
            {
                Database = "LiteDB",
                Workload = "Mixed (5K+5K+1K)",
                TimeMs = sw.ElapsedMilliseconds,
                DbSizeBytes = GetFileSize(litePath),
                WalSizeBytes = 0,
                VacuumTimeMs = 0
            });

            Console.WriteLine($"  LiteDB: {sw.ElapsedMilliseconds:N0} ms");
        }
    }

    private void DetermineWinners()
    {
        foreach (var workload in new[] { "Bulk Inserts", "Individual Inserts", "Mixed (5K+5K+1K)" })
        {
            var group = results.Where(r => r.Workload == workload).OrderBy(r => r.TimeMs).ToList();
            if (group.Count > 0)
                group[0].IsWinner = true;
        }
    }

    private void PrintMarkdownTable()
    {
        Console.WriteLine();
        Console.WriteLine("???????????????????????????????????????????????????????????????????????");
        Console.WriteLine("  MARKDOWN TABLE FOR README");
        Console.WriteLine("???????????????????????????????????????????????????????????????????????");
        Console.WriteLine();

        var sb = new StringBuilder();
        sb.AppendLine("## ?? Realistic Workload Benchmarks");
        sb.AppendLine();
        sb.AppendLine("| Workload | SharpCoreDB (ms) | SQLite (ms) | LiteDB (ms) | Winner | Checkpoint |");
        sb.AppendLine("|----------|------------------|-------------|-------------|--------|------------|");

        foreach (var workload in new[] { "Bulk Inserts", "Individual Inserts", "Mixed (5K+5K+1K)" })
        {
            var scResult = results.FirstOrDefault(r => r.Database == "SharpCoreDB" && r.Workload == workload);
            var sqliteResult = results.FirstOrDefault(r => r.Database == "SQLite" && r.Workload == workload);
            var litedbResult = results.FirstOrDefault(r => r.Database == "LiteDB" && r.Workload == workload);

            var winner = scResult?.IsWinner == true ? "?? SC" : sqliteResult?.IsWinner == true ? "?? SQLite" : litedbResult?.IsWinner == true ? "?? LiteDB" : "";
            var checkpoint = $"{sqliteResult?.VacuumTimeMs ?? 0}ms / {litedbResult?.VacuumTimeMs ?? 0}ms";

            sb.AppendLine($"| {workload,-25} | {scResult?.TimeMs ?? 0,16:N0} | {sqliteResult?.TimeMs ?? 0,11:N0} | {litedbResult?.TimeMs ?? 0,11:N0} | {winner,-10} | {checkpoint,10} |");
        }

        sb.AppendLine();
        sb.AppendLine("### ?? Final Database Sizes");
        sb.AppendLine();
        sb.AppendLine("| Workload | Database | DB Size | WAL Size | Total |");
        sb.AppendLine("|----------|----------|---------|----------|-------|");

        foreach (var r in results.OrderBy(r => r.Workload).ThenBy(r => r.Database))
        {
            sb.AppendLine($"| {r.Workload,-25} | {r.Database,-12} | {FormatBytes(r.DbSizeBytes),7} | {FormatBytes(r.WalSizeBytes),8} | {FormatBytes(r.DbSizeBytes + r.WalSizeBytes),7} |");
        }

        Console.WriteLine(sb.ToString());
        
        var mdPath = Path.Combine(tempDir, "realistic_workload_results.md");
        File.WriteAllText(mdPath, sb.ToString());
        Console.WriteLine($"?? Saved to: {mdPath}");
    }

    private void CleanupDb(string basePath)
    {
        foreach (var ext in new[] { "", ".db", ".wal", ".shm", "-wal", "-shm" })
        {
            try { File.Delete(basePath + ext); } catch { }
        }
        
        // ? FIX: Also delete directory (for SharpCoreDB)
        try 
        { 
            if (Directory.Exists(basePath))
                Directory.Delete(basePath, true); 
        } 
        catch { }
    }

    private long GetFileSize(string path) => File.Exists(path) ? new FileInfo(path).Length : 0;
    
    private long GetDirectorySize(string path)
    {
        if (!Directory.Exists(path))
            return 0;

        var dirInfo = new DirectoryInfo(path);
        return dirInfo.EnumerateFiles("*", SearchOption.AllDirectories)
                      .Sum(file => file.Length);
    }

    private string FormatBytes(long bytes)
    {
        if (bytes == 0) return "0 B";
        string[] units = { "B", "KB", "MB", "GB" };
        int log = (int)Math.Log(bytes, 1024);
        return $"{bytes / Math.Pow(1024, log):0.##} {units[log]}";
    }
}
