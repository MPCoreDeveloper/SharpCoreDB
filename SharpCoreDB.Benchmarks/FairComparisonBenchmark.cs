// Fair Comparison Benchmark - SharpCoreDB vs SQLite vs LiteDB
// Tests: 10k bulk inserts with 3 SharpCore modes, 1/8/16 threads
using System.Diagnostics;
using System.Text;
using Microsoft.Data.Sqlite;
using LiteDB;
using SharpCoreDB.Benchmarks.Infrastructure;

namespace SharpCoreDB.Benchmarks;

public class FairComparisonBenchmark
{
    private const int RECORDS = 10_000;
    private readonly string tempDir;
    private readonly TestDataGenerator dataGen;
    private readonly List<Result> results = [];

    public class Result
    {
        public string Config { get; set; } = "";
        public int Threads { get; set; }
        public string Database { get; set; } = "";
        public long TimeMs { get; set; }
        public long DbSizeBytes { get; set; }
        public long WalSizeBytes { get; set; }
        public bool IsWinner { get; set; }
    }

    public FairComparisonBenchmark()
    {
        tempDir = Path.Combine(Path.GetTempPath(), $"fairBench_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        dataGen = new TestDataGenerator();
    }

    public void Run()
    {
        Console.WriteLine("????????????????????????????????????????????????????????????????????????");
        Console.WriteLine("?     FAIR COMPARISON: 10K BULK INSERTS                                ?");
        Console.WriteLine("????????????????????????????????????????????????????????????????????????");
        Console.WriteLine();

        var threadCounts = new[] { 1, 8, 16 };

        foreach (var threads in threadCounts)
        {
            Console.WriteLine($"??? {threads} THREAD{(threads > 1 ? "S" : "")} ???");
            
            // SharpCore - 3 modes
            RunSharpCore("Default (Encrypted+Columnar)", threads, encrypted: true, highSpeed: false);
            RunSharpCore("HighSpeedInsert", threads, encrypted: true, highSpeed: true);
            RunSharpCore("No Encryption", threads, encrypted: false, highSpeed: false);
            
            // SQLite
            RunSQLite(threads);
            
            // LiteDB
            RunLiteDB(threads);
            
            Console.WriteLine();
        }

        DetermineWinners();
        PrintMarkdownTable();
        
        try { Directory.Delete(tempDir, true); } catch { }
    }

    private void RunSharpCore(string configName, int threads, bool encrypted, bool highSpeed)
    {
        var path = Path.Combine(tempDir, $"sc_{configName}_{threads}t");
        CleanupDb(path);

        var config = new DatabaseConfig
        {
            NoEncryptMode = !encrypted,
            HighSpeedInsertMode = highSpeed,
            UseGroupCommitWal = true,
            WalBufferSize = 4 * 1024 * 1024,
            EnablePageCache = true,
            PageCacheCapacity = 1024,
            UseMemoryMapping = true,
            EnableQueryCache = true,
            QueryCacheSize = 1000,
            SqlValidationMode = SharpCoreDB.Services.SqlQueryValidator.ValidationMode.Disabled
        };

        using var helper = new BenchmarkDatabaseHelper(path, "pwd", encrypted, config);
        helper.CreateUsersTable();

        var sw = Stopwatch.StartNew();

        if (threads == 1)
        {
            var users = dataGen.GenerateUsers(RECORDS);
            var list = users.Select((u, i) => (i, u.Name, u.Email, u.Age, u.CreatedAt, u.IsActive)).ToList();
            helper.InsertUsersTrueBatch(list);
        }
        else
        {
            var perThread = RECORDS / threads;
            var tasks = new Task[threads];
            for (int t = 0; t < threads; t++)
            {
                int threadId = t;
                tasks[t] = Task.Run(() =>
                {
                    var users = dataGen.GenerateUsers(perThread);
                    var list = users.Select((u, i) => (threadId * perThread + i, u.Name, u.Email, u.Age, u.CreatedAt, u.IsActive)).ToList();
                    helper.InsertUsersTrueBatch(list);
                });
            }
            Task.WaitAll(tasks);
        }

        sw.Stop();

        // ? FIX: Get directory size instead of file size
        var dbSize = GetDirectorySize(path);

        results.Add(new Result
        {
            Config = configName,
            Threads = threads,
            Database = "SharpCoreDB",
            TimeMs = sw.ElapsedMilliseconds,
            DbSizeBytes = dbSize,
            WalSizeBytes = 0 // SharpCoreDB uses directory, not separate WAL
        });

        Console.WriteLine($"  SharpCore ({configName}): {sw.ElapsedMilliseconds:N0} ms");
    }

    private void RunSQLite(int threads)
    {
        var path = Path.Combine(tempDir, $"sqlite_{threads}t.db");
        CleanupDb(path);

        using var conn = new SqliteConnection($"Data Source={path}");
        conn.Open();

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                PRAGMA journal_mode=WAL;
                PRAGMA synchronous=NORMAL;
                PRAGMA page_size=4096;";
            cmd.ExecuteNonQuery();
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "CREATE TABLE users (id INT PRIMARY KEY, name TEXT, email TEXT, age INT, created_at TEXT, is_active INT)";
            cmd.ExecuteNonQuery();
        }

        var sw = Stopwatch.StartNew();

        if (threads == 1)
        {
            var users = dataGen.GenerateUsers(RECORDS);
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
        }
        else
        {
            var perThread = RECORDS / threads;
            var tasks = new Task[threads];
            Lock lockObj = new();

            for (int t = 0; t < threads; t++)
            {
                int threadId = t;
                tasks[t] = Task.Run(() =>
                {
                    var users = dataGen.GenerateUsers(perThread);
                    lock (lockObj)
                    {
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
                            insert.Parameters["@id"].Value = threadId * perThread + i;
                            insert.Parameters["@name"].Value = u.Name;
                            insert.Parameters["@email"].Value = u.Email;
                            insert.Parameters["@age"].Value = u.Age;
                            insert.Parameters["@created_at"].Value = u.CreatedAt.ToString("o");
                            insert.Parameters["@is_active"].Value = u.IsActive ? 1 : 0;
                            insert.ExecuteNonQuery();
                        }
                        txn.Commit();
                    }
                });
            }
            Task.WaitAll(tasks);
        }

        sw.Stop();

        results.Add(new Result
        {
            Config = "WAL+Normal",
            Threads = threads,
            Database = "SQLite",
            TimeMs = sw.ElapsedMilliseconds,
            DbSizeBytes = GetFileSize(path),
            WalSizeBytes = GetFileSize(path + "-wal")
        });

        Console.WriteLine($"  SQLite: {sw.ElapsedMilliseconds:N0} ms");
    }

    private void RunLiteDB(int threads)
    {
        var path = Path.Combine(tempDir, $"litedb_{threads}t.db");
        CleanupDb(path);

        using var db = new LiteDatabase(path);
        var col = db.GetCollection<TestDataGenerator.UserRecord>("users");

        var sw = Stopwatch.StartNew();

        if (threads == 1)
        {
            var users = dataGen.GenerateUsers(RECORDS);
            int baseId = Random.Shared.Next(1000000, 9000000);
            for (int i = 0; i < users.Count; i++)
                users[i].Id = baseId + i;
            col.InsertBulk(users);
        }
        else
        {
            var perThread = RECORDS / threads;
            var tasks = new Task[threads];
            var lockObj = new object();

            for (int t = 0; t < threads; t++)
            {
                int threadId = t;
                tasks[t] = Task.Run(() =>
                {
                    var users = dataGen.GenerateUsers(perThread);
                    int baseId = Random.Shared.Next(1000000, 9000000) + (threadId * RECORDS);
                    for (int i = 0; i < users.Count; i++)
                        users[i].Id = baseId + i;
                    
                    lock (lockObj)
                    {
                        col.InsertBulk(users);
                    }
                });
            }
            Task.WaitAll(tasks);
        }

        sw.Stop();

        results.Add(new Result
        {
            Config = "Default",
            Threads = threads,
            Database = "LiteDB",
            TimeMs = sw.ElapsedMilliseconds,
            DbSizeBytes = GetFileSize(path),
            WalSizeBytes = 0
        });

        Console.WriteLine($"  LiteDB: {sw.ElapsedMilliseconds:N0} ms");
    }

    private void DetermineWinners()
    {
        foreach (var threadCount in new[] { 1, 8, 16 })
        {
            var group = results.Where(r => r.Threads == threadCount).OrderBy(r => r.TimeMs).ToList();
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
        sb.AppendLine("## ?? Fair Comparison: 10K Bulk Inserts");
        sb.AppendLine();
        sb.AppendLine("| Config | Threads | SharpCoreDB (ms) | SQLite (ms) | LiteDB (ms) | Winner | Inserts/sec |");
        sb.AppendLine("|--------|---------|------------------|-------------|-------------|--------|-------------|");

        foreach (var threadCount in new[] { 1, 8, 16 })
        {
            var scResults = results.Where(r => r.Database == "SharpCoreDB" && r.Threads == threadCount).ToList();
            var sqliteResult = results.FirstOrDefault(r => r.Database == "SQLite" && r.Threads == threadCount);
            var litedbResult = results.FirstOrDefault(r => r.Database == "LiteDB" && r.Threads == threadCount);

            foreach (var sc in scResults)
            {
                var winner = sc.IsWinner ? "??" : sqliteResult?.IsWinner == true ? "?? SQLite" : litedbResult?.IsWinner == true ? "?? LiteDB" : "";
                var throughput = (int)(RECORDS / (sc.TimeMs / 1000.0));
                sb.AppendLine($"| {sc.Config,-25} | {threadCount,7} | {sc.TimeMs,16:N0} | {sqliteResult?.TimeMs ?? 0,11:N0} | {litedbResult?.TimeMs ?? 0,11:N0} | {winner,-12} | {throughput,11:N0} |");
            }
        }

        sb.AppendLine();
        sb.AppendLine("### ?? File Sizes");
        sb.AppendLine();
        sb.AppendLine("| Database | Config | Threads | DB Size | WAL Size | Total |");
        sb.AppendLine("|----------|--------|---------|---------|----------|-------|");

        foreach (var r in results.OrderBy(r => r.Threads).ThenBy(r => r.Database))
        {
            sb.AppendLine($"| {r.Database,-8} | {r.Config,-20} | {r.Threads,7} | {FormatBytes(r.DbSizeBytes),7} | {FormatBytes(r.WalSizeBytes),8} | {FormatBytes(r.DbSizeBytes + r.WalSizeBytes),7} |");
        }

        Console.WriteLine(sb.ToString());
        
        var mdPath = Path.Combine(tempDir, "fair_comparison_results.md");
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
