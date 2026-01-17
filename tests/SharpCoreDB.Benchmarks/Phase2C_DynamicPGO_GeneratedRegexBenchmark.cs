using BenchmarkDotNet.Attributes;
using SharpCoreDB;
using SharpCoreDB.Benchmarks.Infrastructure;
using System;
using System.Text.RegularExpressions;

namespace SharpCoreDB.Benchmarks;

/// <summary>
/// Phase 2C: Dynamic PGO & Generated Regex Benchmarks
/// 
/// Measures performance improvements from:
/// - Dynamic PGO: JIT compiler optimization based on runtime profiling
/// - Generated Regex: Compile-time regex generation vs runtime compilation
/// 
/// Expected improvements:
/// - Dynamic PGO: 1.2-2x for hot paths
/// - Generated Regex: 1.5-2x (2-20x for first call)
/// - Combined: 2-3x improvement
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 5)]
public class Phase2CDynamicPGOBenchmark
{
    private BenchmarkDatabaseHelper db = null!;
    private const int DATASET_SIZE = 10000;
    private const int ITERATIONS = 1000;  // Simulate hot path with many iterations

    [GlobalSetup]
    public void Setup()
    {
        // Create test database
        db = new BenchmarkDatabaseHelper(
            "phase2c_pgo_" + Guid.NewGuid().ToString("N").Substring(0, 8),
            "testpassword",
            enableEncryption: false);

        // Create test table
        db.CreateUsersTable();

        // Populate dataset
        PopulateTestData(DATASET_SIZE);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        db?.Dispose();
    }

    /// <summary>
    /// Hot path benchmark: Execute same query many times
    /// Dynamic PGO optimizes this path aggressively
    /// Expected: With PGO enabled, 1.2-2x faster than without
    /// </summary>
    [Benchmark(Description = "Dynamic PGO hot path - Repeated simple query")]
    public int DynamicPGOHotPath_SimpleQuery()
    {
        int totalCount = 0;
        
        // Simulate hot path: same query executed repeatedly
        for (int i = 0; i < ITERATIONS; i++)
        {
            var result = db.Database.ExecuteQuery("SELECT * FROM users WHERE age > 30");
            totalCount += result.Count;
        }
        
        return totalCount / ITERATIONS;  // Average per iteration
    }

    /// <summary>
    /// Hot path benchmark: Complex WHERE clause executed repeatedly
    /// PGO learns branch patterns and optimizes decision trees
    /// Expected: 1.5-2x improvement with PGO
    /// </summary>
    [Benchmark(Description = "Dynamic PGO hot path - Complex WHERE clause")]
    public int DynamicPGOHotPath_ComplexWhere()
    {
        int totalCount = 0;
        
        // Complex WHERE with multiple conditions
        for (int i = 0; i < ITERATIONS; i++)
        {
            var result = db.Database.ExecuteQuery(
                "SELECT * FROM users WHERE age > 20 AND age < 60 AND is_active = 1");
            totalCount += result.Count;
        }
        
        return totalCount / ITERATIONS;
    }

    /// <summary>
    /// Cold path benchmark: Different queries (no PGO optimization possible)
    /// Expected: Minimal overhead, no improvement expected
    /// </summary>
    [Benchmark(Description = "Dynamic PGO cold path - Random different queries")]
    public int DynamicPGOColdPath_RandomQueries()
    {
        int totalCount = 0;
        
        // Cold path: different queries each time (prevents PGO optimization)
        for (int i = 0; i < 100; i++)
        {
            var age = 20 + (i % 50);
            var result = db.Database.ExecuteQuery($"SELECT * FROM users WHERE age > {age}");
            totalCount += result.Count;
        }
        
        return totalCount;
    }

    private void PopulateTestData(int rowCount)
    {
        var random = new Random(42);

        for (int i = 0; i < rowCount; i++)
        {
            var id = i;
            var name = $"User{i}";
            var email = $"user{i}@test.com";
            var age = 18 + random.Next(65);
            var createdAt = DateTime.Now.ToString("o");
            var isActive = random.Next(2);

            try
            {
                db.Database.ExecuteSQL($@"
                    INSERT INTO users (id, name, email, age, created_at, is_active)
                    VALUES ({id}, '{name}', '{email}', {age}, '{createdAt}', {isActive})
                ");
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Primary key"))
            {
                continue;
            }
        }
    }
}

/// <summary>
/// Phase 2C: Generated Regex Benchmarks
/// 
/// Compares traditional Regex compilation vs C# 14 [GeneratedRegex]
/// 
/// [GeneratedRegex] uses Roslyn to generate optimized regex code at compile-time
/// instead of compiling regex patterns at runtime.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 5)]
public partial class Phase2CGeneratedRegexBenchmark
{
    private string testEmail = "user@example.com";
    private string testSqlKeyword = "SELECT";
    private string testInvalidEmail = "not-an-email";
    private string[] testEmails = null!;

    [GlobalSetup]
    public void Setup()
    {
        // Generate test data
        testEmails = new string[1000];
        var random = new Random(42);
        for (int i = 0; i < testEmails.Length; i++)
        {
            testEmails[i] = $"user{random.Next(10000)}@example.com";
        }
    }

    /// <summary>
    /// Baseline: Traditional Regex using new Regex()
    /// Pattern compiled at runtime
    /// Expected: Slower first call, normal subsequent calls
    /// </summary>
    [Benchmark(Description = "Regex Traditional - Email validation")]
    public bool RegexTraditional_EmailValidation()
    {
        // Create regex every time (worst case, but shows runtime compilation)
        var regex = new Regex(@"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}");
        return regex.IsMatch(testEmail);
    }

    /// <summary>
    /// Optimized: [GeneratedRegex] email validation
    /// Pattern generated at compile-time by Roslyn
    /// Expected: Much faster, no runtime compilation
    /// </summary>
    [Benchmark(Description = "Regex Generated - Email validation")]
    public bool RegexGenerated_EmailValidation()
    {
        return GeneratedEmailRegex().IsMatch(testEmail);
    }

    /// <summary>
    /// Bulk processing: Match many strings
    /// Shows accumulated benefit of [GeneratedRegex]
    /// </summary>
    [Benchmark(Description = "Regex Generated - Bulk email validation")]
    public int RegexGenerated_BulkValidation()
    {
        int validCount = 0;
        var regex = GeneratedEmailRegex();
        
        foreach (var email in testEmails)
        {
            if (regex.IsMatch(email))
                validCount++;
        }
        
        return validCount;
    }

    /// <summary>
    /// SQL keyword detection benchmark
    /// Demonstrates regex usage in query parsing
    /// </summary>
    [Benchmark(Description = "Regex Generated - SQL keyword detection")]
    public bool RegexGenerated_KeywordDetection()
    {
        return GeneratedKeywordRegex().IsMatch(testSqlKeyword);
    }

    /// <summary>
    /// Multiple regex patterns working together
    /// Shows benefit of [GeneratedRegex] with multiple patterns
    /// </summary>
    [Benchmark(Description = "Regex Generated - Multiple patterns")]
    public bool RegexGenerated_MultiplePatterns()
    {
        // Test multiple regex patterns in sequence
        bool result = true;
        result &= GeneratedEmailRegex().IsMatch(testEmail);
        result &= GeneratedKeywordRegex().IsMatch("SELECT");
        result &= GeneratedNumberRegex().IsMatch("12345");
        
        return result;
    }

    // Generated Regex patterns using C# 14 [GeneratedRegex]
    
    [GeneratedRegex(@"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}", 
        RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex GeneratedEmailRegex();

    [GeneratedRegex(@"^\s*(SELECT|FROM|WHERE|ORDER|GROUP|INSERT|UPDATE|DELETE)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex GeneratedKeywordRegex();

    [GeneratedRegex(@"^\d+$", RegexOptions.Compiled)]
    private static partial Regex GeneratedNumberRegex();
}

/// <summary>
/// Combined Phase 2C benchmark showing cumulative impact
/// Combines Dynamic PGO hot path + Generated Regex patterns
/// </summary>
[MemoryDiagnoser]
public partial class Phase2CCombinedBenchmark
{
    private BenchmarkDatabaseHelper db = null!;

    [GlobalSetup]
    public void Setup()
    {
        db = new BenchmarkDatabaseHelper(
            "phase2c_combined_" + Guid.NewGuid().ToString("N").Substring(0, 8),
            "testpassword",
            enableEncryption: false);
        db.CreateUsersTable();
        
        // Small dataset for quick test
        for (int i = 0; i < 1000; i++)
        {
            db.Database.ExecuteSQL($@"
                INSERT INTO users (id, name, email, age, created_at, is_active)
                VALUES ({i}, 'User{i}', 'user{i}@test.com', {20 + (i % 50)}, 
                        '{DateTime.Now:o}', {i % 2})
            ");
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        db?.Dispose();
    }

    /// <summary>
    /// Combined: Hot path query + regex pattern matching
    /// Demonstrates cumulative benefits of Dynamic PGO + Generated Regex
    /// Expected: 2-3x improvement from both optimizations combined
    /// </summary>
    [Benchmark(Description = "Phase 2C Combined - Hot path with regex")]
    public int Phase2CCombined_HotPathWithRegex()
    {
        int totalMatches = 0;
        
        // Hot path execution
        for (int i = 0; i < 100; i++)
        {
            // Database hot path (optimized by Dynamic PGO)
            var result = db.Database.ExecuteQuery("SELECT * FROM users WHERE age > 30");
            
            // Regex pattern matching (optimized by [GeneratedRegex])
            foreach (var row in result)
            {
                var email = row["email"].ToString();
                if (CombinedEmailRegex().IsMatch(email))
                    totalMatches++;
            }
        }
        
        return totalMatches;
    }

    [GeneratedRegex(@"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}",
        RegexOptions.Compiled)]
    private static partial Regex CombinedEmailRegex();
}
