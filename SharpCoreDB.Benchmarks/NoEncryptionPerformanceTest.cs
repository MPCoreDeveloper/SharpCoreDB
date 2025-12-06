using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;

namespace SharpCoreDB.Benchmarks;

/// <summary>
/// Simple performance test to demonstrate NoEncryption speedup.
/// </summary>
public static class NoEncryptionPerformanceTest
{
    public static void RunPerformanceTest()
    {
        Console.WriteLine("=== SharpCoreDB NoEncryption Performance Test ===\n");

        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<DatabaseFactory>();

        const int insertCount = 10000;

        // Test with encryption
        var encryptedPath = Path.Combine(Path.GetTempPath(), "perf_test_encrypted");
        if (Directory.Exists(encryptedPath))
            Directory.Delete(encryptedPath, true);

        var dbEncrypted = factory.Create(encryptedPath, "testPassword", false, DatabaseConfig.Default);
        dbEncrypted.ExecuteSQL("CREATE TABLE time_entries (id INTEGER, project TEXT, task TEXT, start_time DATETIME, duration INTEGER, user TEXT)");

        var swEncrypted = Stopwatch.StartNew();
        for (int i = 0; i < insertCount; i++)
        {
            dbEncrypted.ExecuteSQL($"INSERT INTO time_entries VALUES ('{i}', 'Project{i % 10}', 'Task{i % 5}', '2024-01-{(i % 28) + 1:00} 09:00:00', '{480}', 'User{i % 3}')");
        }
        swEncrypted.Stop();
        Console.WriteLine($"Encrypted Insert {insertCount} records: {swEncrypted.ElapsedMilliseconds}ms");

        // Test with NoEncryption
        var noEncryptPath = Path.Combine(Path.GetTempPath(), "perf_test_noencrypt");
        if (Directory.Exists(noEncryptPath))
            Directory.Delete(noEncryptPath, true);

        var dbNoEncrypt = factory.Create(noEncryptPath, "testPassword", false, DatabaseConfig.HighPerformance);
        dbNoEncrypt.ExecuteSQL("CREATE TABLE time_entries (id INTEGER, project TEXT, task TEXT, start_time DATETIME, duration INTEGER, user TEXT)");

        var swNoEncrypt = Stopwatch.StartNew();
        for (int i = 0; i < insertCount; i++)
        {
            dbNoEncrypt.ExecuteSQL($"INSERT INTO time_entries VALUES ('{i}', 'Project{i % 10}', 'Task{i % 5}', '2024-01-{(i % 28) + 1:00} 09:00:00', '{480}', 'User{i % 3}')");
        }
        swNoEncrypt.Stop();
        Console.WriteLine($"NoEncrypt Insert {insertCount} records: {swNoEncrypt.ElapsedMilliseconds}ms");

        var speedup = (double)swEncrypted.ElapsedMilliseconds / swNoEncrypt.ElapsedMilliseconds;
        Console.WriteLine($"\n=== Speedup: {speedup:F2}x ===");
        Console.WriteLine($"Performance improvement: {((speedup - 1) * 100):F1}%\n");

        // Test Select performance
        var swEncryptedSelect = Stopwatch.StartNew();
        for (int i = 0; i < 100; i++)
        {
            dbEncrypted.ExecuteSQL("SELECT * FROM time_entries WHERE project = 'Project1'");
        }
        swEncryptedSelect.Stop();
        Console.WriteLine($"Encrypted Select (100 queries): {swEncryptedSelect.ElapsedMilliseconds}ms");

        var swNoEncryptSelect = Stopwatch.StartNew();
        for (int i = 0; i < 100; i++)
        {
            dbNoEncrypt.ExecuteSQL("SELECT * FROM time_entries WHERE project = 'Project1'");
        }
        swNoEncryptSelect.Stop();
        Console.WriteLine($"NoEncrypt Select (100 queries): {swNoEncryptSelect.ElapsedMilliseconds}ms");

        var selectSpeedup = (double)swEncryptedSelect.ElapsedMilliseconds / swNoEncryptSelect.ElapsedMilliseconds;
        Console.WriteLine($"Select Speedup: {selectSpeedup:F2}x\n");

        // Cleanup
        if (Directory.Exists(encryptedPath))
            Directory.Delete(encryptedPath, true);
        if (Directory.Exists(noEncryptPath))
            Directory.Delete(noEncryptPath, true);

        Console.WriteLine("Performance test completed successfully!");
    }
}
