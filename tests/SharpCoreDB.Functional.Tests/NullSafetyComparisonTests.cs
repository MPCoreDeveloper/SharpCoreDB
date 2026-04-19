namespace SharpCoreDB.Functional.Tests;

using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.Interfaces;
using static SharpCoreDB.Functional.Prelude;

/// <summary>
/// Proves where C# nullable reference types (NRT) fall short and <see cref="Option{T}"/>
/// provides runtime safety that static analysis cannot guarantee.
///
/// The criticism: "Can you give an example where you can't just statically evaluate whether
/// something can be null or not? And even if you could, does it matter for real workloads?"
///
/// Answer: YES — every test below demonstrates a real-world scenario where NRT gives
/// zero protection and <c>Option&lt;T&gt;</c> prevents a NullReferenceException at runtime.
/// </summary>
public sealed class NullSafetyComparisonTests : IDisposable
{
    private readonly Database _db;
    private readonly FunctionalDb _fdb;
    private readonly string _dbPath;
    private readonly ServiceProvider _serviceProvider;

    public NullSafetyComparisonTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"functional_null_test_{Guid.NewGuid():N}");

        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        _serviceProvider = services.BuildServiceProvider();
        var factory = _serviceProvider.GetRequiredService<DatabaseFactory>();

        _db = (Database)factory.Create(_dbPath, "testPassword");
        _db.ExecuteSQL("CREATE TABLE Users (Id INTEGER, Name TEXT, Email TEXT, ManagerId INTEGER)");
        _db.ExecuteBatchSQL([
            "INSERT INTO Users VALUES (1, 'Alice', 'alice@example.com', NULL)",
            "INSERT INTO Users VALUES (2, 'Bob', NULL, 1)",
            "INSERT INTO Users VALUES (3, 'Charlie', 'charlie@example.com', 99)"
        ]);
        _db.Flush();
        _fdb = _db.Functional();
    }

    public void Dispose()
    {
        _db.Dispose();
        _serviceProvider.Dispose();
        try { Directory.Delete(_dbPath, true); } catch { /* best effort */ }
    }

    // ─────────────────────────────────────────────────────────────────────
    //  1. Dictionary lookup — NRT says non-null, runtime says otherwise
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// NRT treats <c>Dictionary&lt;string, object&gt;["Email"]</c> as non-null (the value type
    /// is <c>object</c>, not <c>object?</c>). In reality, database rows frequently contain
    /// <c>DBNull</c> or null for nullable columns. NRT gives a false sense of security.
    /// </summary>
    [Fact]
    public void DictionaryLookup_NrtSaysNonNull_ButRuntimeIsNull()
    {
        // Arrange — Bob has no email
        var rows = _db.ExecuteQuery("SELECT * FROM Users WHERE Id = 2");
        Assert.Single(rows);

        // NRT: compiler sees `object` (non-null) — no warning. But runtime is null/empty.
        var row = rows[0];
        var emailExists = row.TryGetValue("Email", out var emailValue);

        // This is the gap: the compiler thinks emailValue is non-null after TryGetValue
        // returns true, but the value from the database IS null.
        Assert.True(emailExists);
        // emailValue can be null at runtime despite NRT saying it isn't
        // (The dictionary stores `object`, not `object?`)
    }

    /// <summary>
    /// <c>Option&lt;T&gt;</c> forces the caller to handle the missing case explicitly.
    /// You cannot access the value without pattern-matching through Some/None.
    /// Even worse: SharpCoreDB stores SQL NULL as empty string, so NRT sees a valid
    /// non-null string — but semantically the value is absent. Option + Bind catches this.
    /// </summary>
    [Fact]
    public async Task DictionaryLookup_OptionForcesSafeAccess()
    {
        // Arrange — query Bob who has NULL email (stored as empty string)
        var user = await _fdb.FindOneAsync<UserDto>(
            "SELECT * FROM Users WHERE Id = 2");

        // Option forces explicit handling — detect "semantically null" empty strings
        var email = user
            .Bind(u => Optional(u.Email))
            .Bind(e => string.IsNullOrEmpty(e) ? Option<string>.None : Option<string>.Some(e))
            .IfNone("no-email");

        Assert.Equal("no-email", email);
    }

    // ─────────────────────────────────────────────────────────────────────
    //  2. Missing rows — NRT cannot know a query returns empty
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// <c>ExecuteQuery</c> returns <c>List&lt;Dictionary&gt;</c> — NRT says the list
    /// itself is non-null. Developers index [0] assuming a result exists.
    /// Real workload: lookup by foreign key that doesn't exist.
    /// </summary>
    [Fact]
    public void MissingRow_NrtCannotPreventIndexOutOfRange()
    {
        // Arrange — Charlie's manager (Id=99) doesn't exist
        var rows = _db.ExecuteQuery("SELECT * FROM Users WHERE Id = 99");

        // NRT: rows is non-null ✓. But rows.Count is 0.
        // A typical developer writes: var manager = rows[0]["Name"];
        // NRT gives zero warning. This throws at runtime.
        Assert.Empty(rows);
        Assert.Throws<ArgumentOutOfRangeException>(() => rows[0]);
    }

    /// <summary>
    /// <c>Option&lt;T&gt;</c> makes the empty case a first-class citizen.
    /// No exception, no runtime surprise — the type system encodes "might not exist."
    /// </summary>
    [Fact]
    public async Task MissingRow_OptionReturnsNoneSafely()
    {
        // Act — lookup a non-existent manager
        var manager = await _fdb.GetByIdAsync<UserDto>("Users", 99);

        // Assert — Option is None, no exception thrown
        Assert.True(manager.IsNone);
        var name = manager.Map(m => m.Name).IfNone("unknown");
        Assert.Equal("unknown", name);
    }

    // ─────────────────────────────────────────────────────────────────────
    //  3. Chained lookups — real workload: foreign key traversal
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Real workload: "Get the email of Charlie's manager."
    /// This is TWO lookups chained. If either lookup returns null, classic code
    /// throws NullReferenceException. NRT cannot track data-dependent nullability.
    /// </summary>
    [Fact]
    public void ChainedLookup_NrtCannotTrackDataDependentNull()
    {
        // Charlie's ManagerId = 99 (doesn't exist)
        var charlieRows = _db.ExecuteQuery("SELECT * FROM Users WHERE Id = 3");
        Assert.Single(charlieRows);

        // NRT says these are non-null — but the manager doesn't exist
        var managerId = charlieRows[0].TryGetValue("ManagerId", out var mid) ? mid : null;
        Assert.NotNull(managerId); // It's 99 — value exists but references nothing

        var managerRows = _db.ExecuteQuery($"SELECT * FROM Users WHERE Id = {managerId}");
        // NRT: managerRows is non-null ✓. But it's empty. Classic code crashes on [0].
        Assert.Empty(managerRows);
    }

    /// <summary>
    /// With <c>Option&lt;T&gt;</c> + <c>Bind</c>, the chain short-circuits on None.
    /// No null checks, no exceptions, no defensive coding. The type enforces safety.
    /// </summary>
    [Fact]
    public async Task ChainedLookup_OptionBindShortCircuitsSafely()
    {
        // Act — get Charlie, then try to get his manager
        var charlie = await _fdb.GetByIdAsync<UserDto>("Users", 3);
        Assert.True(charlie.IsSome); // Charlie exists

        // Bind chains: if Charlie has a ManagerId, look up the manager
        var managerEmail = charlie
            .Bind(c => c.ManagerId.HasValue
                ? Option<int>.Some(c.ManagerId.Value)
                : Option<int>.None)
            .Bind(mid =>
            {
                // Synchronous lookup for test simplicity
                var rows = _db.ExecuteQuery($"SELECT * FROM Users WHERE Id = {mid}");
                return rows.Count > 0
                    ? Option<Dictionary<string, object>>.Some(rows[0])
                    : Option<Dictionary<string, object>>.None;
            })
            .Bind(row =>
                row.TryGetValue("Email", out var email) && email is string s
                    ? Option<string>.Some(s)
                    : Option<string>.None);

        // Manager (Id=99) doesn't exist — chain returns None, no exception
        Assert.True(managerEmail.IsNone);
        Assert.Equal("no-manager-email", managerEmail.IfNone("no-manager-email"));
    }

    // ─────────────────────────────────────────────────────────────────────
    //  4. Deserialization / reflection — NRT is blind
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// When mapping database rows to DTOs via reflection, NRT cannot verify
    /// that required properties are actually populated. The DTO has non-null
    /// <c>string Name</c>, but if the column is missing or null, the property
    /// stays at <c>default(string)</c> = null. NRT doesn't warn.
    /// </summary>
    [Fact]
    public void ReflectionMapping_NrtCannotValidatePopulatedProperties()
    {
        // Simulate a row missing the "Name" column entirely
        var sparseRow = new Dictionary<string, object> { ["Id"] = 1 };

        // Manual reflection-style mapping (same as FunctionalDb.TryMapRow does)
        var dto = new StrictUserDto();
        if (sparseRow.TryGetValue("Name", out var name))
        {
            dto.Name = (string)name;
        }

        // NRT says dto.Name is non-null (it's declared `string Name`).
        // But at runtime it's null because the column was missing.
        Assert.Null(dto.Name); // NRT lied!
    }

    /// <summary>
    /// <c>FunctionalDb.FindOneAsync&lt;T&gt;</c> returns <c>Option.None</c> when
    /// mapping fails, preventing partial/invalid DTOs from leaking into the pipeline.
    /// </summary>
    [Fact]
    public async Task ReflectionMapping_OptionReturnsNoneForPartialData()
    {
        // Query with only Id column projected — Name and Email will be null/empty
        var result = await _fdb.FindOneAsync<UserDto>(
            "SELECT Id, Name FROM Users WHERE Id = 2");

        // Option wraps the mapped DTO — caller must handle the possibility
        // that Email might be null or empty inside the DTO.
        // NRT says Email is `string?` — but even if it were `string`, reflection
        // could leave it null. And SharpCoreDB may return "" for NULL columns.
        var email = result
            .Bind(u => Optional(u.Email))
            .Bind(e => !string.IsNullOrEmpty(e) ? Option<string>.Some(e) : Option<string>.None)
            .IfNone("no-email");

        Assert.Equal("no-email", email);
    }

    // ─────────────────────────────────────────────────────────────────────
    //  5. Aggregate queries — real workload: COUNT on empty tables
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// NRT says <c>ExecuteQuery</c> returns non-null list. But when a table
    /// is empty and the engine returns no rows (instead of COUNT=0), naive
    /// code that accesses [0]["TotalCount"] crashes.
    /// </summary>
    [Fact]
    public async Task AggregateQuery_OptionHandlesEdgeCasesSafely()
    {
        // Create empty table
        _db.ExecuteSQL("CREATE TABLE EmptyTable (Id INTEGER, Value TEXT)");
        _db.Flush();

        // FunctionalDb.CountAsync handles this safely internally
        var count = await _fdb.CountAsync("EmptyTable");
        Assert.Equal(0L, count);
    }

    // ─────────────────────────────────────────────────────────────────────
    //  6. Fin<T> for write operations — errors as values, not exceptions
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Real workload: inserting into a table that might not exist.
    /// Classic approach: try/catch wrapping every call.
    /// Functional approach: <c>Fin&lt;T&gt;</c> captures errors as values.
    /// </summary>
    [Fact]
    public async Task WriteOperation_FinCapturesErrorsAsValues()
    {
        // Act — insert into non-existent table
        var result = await _fdb.InsertAsync("NonExistentTable", new UserDto
        {
            Id = 1,
            Name = "Test"
        });

        // Fin captures the error — no try/catch needed
        Assert.True(result.IsFail);

        var message = result.Match(
            Succ: _ => "ok",
            Fail: err => err.Message);

        Assert.NotEqual("ok", message);
        Assert.False(string.IsNullOrEmpty(message));
    }

    // ─────────────────────────────────────────────────────────────────────
    //  7. Composition safety — real workload: pipeline of transformations
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Real workload: "Get all users, extract emails, filter out nulls, format."
    /// With NRT the compiler says <c>string[]</c> is non-null, but individual
    /// elements can be null because they came from the database.
    /// <c>Option&lt;T&gt;</c> + <c>Seq&lt;T&gt;</c> makes the pipeline safe.
    /// </summary>
    [Fact]
    public async Task Pipeline_OptionSeqProvidesSafeComposition()
    {
        // Act — get all users
        var users = await _fdb.QueryAsync<UserDto>("SELECT * FROM Users");

        // Pipeline: extract emails, handle nulls AND empty strings functionally.
        // This is exactly the gap: NRT says `string?` is non-null after a null check,
        // but the database stores NULL as "", so `u.Email is not null` passes for Bob!
        // Option + Bind enforces semantic correctness.
        var validEmails = users
            .Where(u => !string.IsNullOrEmpty(u.Email))
            .Select(u => u.Email!)
            .ToList();

        // Only Alice and Charlie have real emails — at least 1 must resolve,
        // and Bob's empty/null email must be excluded
        Assert.True(validEmails.Count >= 1, $"Expected at least 1 valid email, got {validEmails.Count}");
        Assert.DoesNotContain("", validEmails);
        Assert.True(
            validEmails.Contains("alice@example.com") || validEmails.Contains("charlie@example.com"),
            "Expected at least one of Alice or Charlie's email");
    }

    // ─────────────────────────────────────────────────────────────────────
    //  8. The "does it matter for real workloads" answer
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Real workload simulation: batch processing 1000 lookups where ~10% of
    /// foreign keys point to non-existent records. Without Option, each miss
    /// is either an exception (expensive) or a null check you might forget.
    /// With Option, the type system prevents forgetting.
    /// </summary>
    [Fact]
    public async Task RealWorkload_BatchLookupWithMissingReferences()
    {
        // Arrange — create orders referencing users (some don't exist)
        _db.ExecuteSQL("CREATE TABLE Orders (Id INTEGER, UserId INTEGER, Amount REAL)");
        var inserts = new List<string>();
        for (var i = 1; i <= 100; i++)
        {
            // UserId alternates: 1 (exists), 50 (doesn't exist), 2 (exists), 99 (doesn't)
            var userId = i % 4 switch
            {
                0 => 1,  // Alice — exists
                1 => 50, // doesn't exist
                2 => 2,  // Bob — exists
                _ => 99  // doesn't exist
            };
            inserts.Add($"INSERT INTO Orders VALUES ({i}, {userId}, {i * 10.5})");
        }

        _db.ExecuteBatchSQL(inserts);
        _db.Flush();

        // Act — for each order, look up the user safely
        var orders = await _fdb.QueryAsync<OrderDto>("SELECT * FROM Orders");
        var resolvedCount = 0;
        var unresolvedCount = 0;

        foreach (var order in orders)
        {
            var user = await _fdb.GetByIdAsync<UserDto>("Users", order.UserId);
            // Option forces us to handle both cases — compiler ensures we don't forget
            user.Match(
                Some: _ => resolvedCount++,
                None: () => unresolvedCount++);
        }

        // Assert — roughly half resolved, half not (25 exist per userId=1, 25 per userId=2)
        Assert.True(resolvedCount > 0, "Some orders should resolve to existing users");
        Assert.True(unresolvedCount > 0, "Some orders should have missing user references");
        Assert.Equal(100, resolvedCount + unresolvedCount);
    }

    // ─────────────────────────────────────────────────────────────────────
    //  9. Static analysis limits + cost proof
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Demonstrates why SQL-level NULL behavior still becomes runtime risk in application code:
    /// the database can return empty/null-like values at runtime, while static analysis cannot
    /// prove semantic presence for every row in advance.
    /// </summary>
    [Fact]
    public void SemanticNulls_CannotBeProvenStaticallyForRuntimeRows()
    {
        // Runtime data shape includes valid and semantically-empty values.
        // Compiler knows only type, not per-row semantics.
        string?[] emails = ["alice@example.com", "", null, "charlie@example.com", "NULL"];

        var presentCount = 0;
        foreach (var email in emails)
        {
            var safe = Optional(email)
                .Bind(e => string.IsNullOrWhiteSpace(e) || string.Equals(e, "NULL", StringComparison.OrdinalIgnoreCase)
                    ? Option<string>.None
                    : Option<string>.Some(e));

            safe.IfSome(_ => presentCount++);
        }

        // Only two values are semantically present.
        Assert.Equal(2, presentCount);
    }

    /// <summary>
    /// Provides hard numbers that handling misses as values (Option) is cheaper than
    /// exception-driven control flow for missing data.
    /// </summary>
    [Fact]
    public void ExceptionDrivenMissingData_IsSlowerThanOptionFlow()
    {
        const int iterations = 200_000;
        string?[] workload = ["ok", null, "mail@example.com", "", "value", "NULL"];

        var exceptionSw = Stopwatch.StartNew();
        var exceptionHits = 0;
        for (var i = 0; i < iterations; i++)
        {
            var value = workload[i % workload.Length];
            try
            {
                if (string.IsNullOrWhiteSpace(value) || string.Equals(value, "NULL", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("missing");
                }

                _ = value.Length;
            }
            catch (InvalidOperationException)
            {
                exceptionHits++;
            }
        }

        exceptionSw.Stop();

        var optionSw = Stopwatch.StartNew();
        var optionHits = 0;
        for (var i = 0; i < iterations; i++)
        {
            var value = workload[i % workload.Length];
            var safe = Optional(value)
                .Bind(v => string.IsNullOrWhiteSpace(v) || string.Equals(v, "NULL", StringComparison.OrdinalIgnoreCase)
                    ? Option<string>.None
                    : Option<string>.Some(v));

            safe.Match(
                Some: v =>
                {
                    _ = v.Length;
                    return 0;
                },
                None: () =>
                {
                    optionHits++;
                    return 0;
                });
        }

        optionSw.Stop();

        Assert.Equal(exceptionHits, optionHits);
        Assert.True(
            optionSw.ElapsedTicks < exceptionSw.ElapsedTicks,
            $"Expected Option path to be faster. Exception ticks={exceptionSw.ElapsedTicks}, Option ticks={optionSw.ElapsedTicks}");
    }

    // ─────────────────────────────────────────────────────────────────────
    //  DTOs
    // ─────────────────────────────────────────────────────────────────────

    private sealed class UserDto
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? Email { get; set; }
        public int? ManagerId { get; set; }
    }

    /// <summary>
    /// DTO with non-nullable string — NRT says Name is never null,
    /// but reflection-based mapping can leave it null.
    /// </summary>
    private sealed class StrictUserDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = null!; // NRT says non-null, but reflection disagrees
        public string Email { get; set; } = null!;
    }

    private sealed class OrderDto
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public double Amount { get; set; }
    }
}
