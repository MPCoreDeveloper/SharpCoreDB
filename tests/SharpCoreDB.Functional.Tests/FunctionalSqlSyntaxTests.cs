namespace SharpCoreDB.Functional.Tests;

using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.Interfaces;
using static SharpCoreDB.Functional.Prelude;

/// <summary>
/// Tests for the Functional SQL syntax extensions: <c>OPTIONALLY FROM</c>, <c>IS SOME</c>,
/// <c>IS NONE</c>, <c>UNWRAP</c>, and <c>MATCH SOME/NONE</c>.
/// Run these tests to verify the functional SQL layer works end-to-end.
/// </summary>
public sealed class FunctionalSqlSyntaxTests : IDisposable
{
    private readonly Database _db;
    private readonly FunctionalDb _fdb;
    private readonly string _dbPath;
    private readonly ServiceProvider _serviceProvider;

    public FunctionalSqlSyntaxTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"functional_sql_test_{Guid.NewGuid():N}");

        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        _serviceProvider = services.BuildServiceProvider();
        var factory = _serviceProvider.GetRequiredService<DatabaseFactory>();

        _db = (Database)factory.Create(_dbPath, "testPassword");
        _db.ExecuteSQL("CREATE TABLE Users (Id INTEGER, Name TEXT, Email TEXT, ManagerId INTEGER)");
        _db.ExecuteBatchSQL([
            "INSERT INTO Users VALUES (1, 'Alice', 'alice@example.com', NULL)",
            "INSERT INTO Users VALUES (2, 'Bob', '', 1)",
            "INSERT INTO Users VALUES (3, 'Charlie', 'charlie@example.com', 99)",
            "INSERT INTO Users VALUES (4, 'Diana', '', NULL)",
            "INSERT INTO Users VALUES (5, 'Eve', 'eve@example.com', 1)"
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
    //  Translator unit tests
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void IsFunctionalSql_WithStandardSql_ReturnsFalse()
    {
        Assert.False(FunctionalSqlTranslator.IsFunctionalSql(
            "SELECT Id, Name FROM Users WHERE Id = 1"));
    }

    [Fact]
    public void IsFunctionalSql_WithOptionallyFrom_ReturnsTrue()
    {
        Assert.True(FunctionalSqlTranslator.IsFunctionalSql(
            "SELECT Id, Name OPTIONALLY FROM Users"));
    }

    [Fact]
    public void IsFunctionalSql_WithIsSome_ReturnsTrue()
    {
        Assert.True(FunctionalSqlTranslator.IsFunctionalSql(
            "SELECT Id FROM Users WHERE Email IS SOME"));
    }

    [Fact]
    public void IsFunctionalSql_WithIsNone_ReturnsTrue()
    {
        Assert.True(FunctionalSqlTranslator.IsFunctionalSql(
            "SELECT Id FROM Users WHERE Email IS NONE"));
    }

    [Fact]
    public void Translate_OptionallyFrom_ProducesStandardFrom()
    {
        // Arrange
        var translator = new FunctionalSqlTranslator();

        // Act
        var result = translator.Translate("SELECT Id, Name OPTIONALLY FROM Users");

        // Assert
        Assert.True(result.IsOptional);
        Assert.Equal("SELECT Id, Name FROM Users", result.StandardSql);
    }

    [Fact]
    public void Translate_IsSome_ProducesNotNullAndNotEmpty()
    {
        // Arrange
        var translator = new FunctionalSqlTranslator();

        // Act
        var result = translator.Translate(
            "SELECT Id, Name FROM Users WHERE Email IS SOME");

        // Assert
        Assert.DoesNotContain("IS SOME", result.StandardSql);
        Assert.DoesNotContain("MATCH SOME", result.StandardSql);
        Assert.Single(result.SomeColumns);
        Assert.Equal("Email", result.SomeColumns[0]);
    }

    [Fact]
    public void Translate_IsNone_ProducesIsNullOrEmpty()
    {
        // Arrange
        var translator = new FunctionalSqlTranslator();

        // Act
        var result = translator.Translate(
            "SELECT Id, Name FROM Users WHERE Email IS NONE");

        // Assert
        Assert.DoesNotContain("IS NONE", result.StandardSql);
        Assert.DoesNotContain("MATCH NONE", result.StandardSql);
        Assert.Single(result.NoneColumns);
        Assert.Equal("Email", result.NoneColumns[0]);
    }

    [Fact]
    public void Translate_Unwrap_ProducesColumnAlias()
    {
        // Arrange
        var translator = new FunctionalSqlTranslator();

        // Act
        var result = translator.Translate(
            "SELECT Id, UNWRAP Email AS SafeEmail DEFAULT 'none' OPTIONALLY FROM Users");

        // Assert
        Assert.True(result.IsOptional);
        Assert.Contains("Email AS SafeEmail", result.StandardSql);
        Assert.DoesNotContain("UNWRAP", result.StandardSql);
        Assert.Single(result.UnwrapMappings);
        Assert.Equal("Email", result.UnwrapMappings[0].Column);
        Assert.Equal("SafeEmail", result.UnwrapMappings[0].Alias);
        Assert.Equal("none", result.UnwrapMappings[0].DefaultValue);
    }

    [Fact]
    public void Translate_CombinedSyntax_ParsesAllKeywords()
    {
        // Arrange
        var translator = new FunctionalSqlTranslator();
        var sql = "SELECT Id, Name, UNWRAP Email AS SafeEmail DEFAULT 'no-email' " +
                  "OPTIONALLY FROM Users WHERE Email IS SOME";

        // Act
        var result = translator.Translate(sql);

        // Assert
        Assert.True(result.IsOptional);
        Assert.Single(result.SomeColumns);
        Assert.Single(result.UnwrapMappings);
        Assert.DoesNotContain("OPTIONALLY", result.StandardSql);
        Assert.DoesNotContain("UNWRAP", result.StandardSql);
        Assert.DoesNotContain("IS SOME", result.StandardSql);
    }

    [Fact]
    public void Translate_MatchSome_TranslatesLikeIsSome()
    {
        // Arrange
        var translator = new FunctionalSqlTranslator();

        // Act
        var result = translator.Translate(
            "SELECT Id FROM Users WHERE MATCH SOME Email");

        // Assert
        Assert.DoesNotContain("MATCH SOME", result.StandardSql);
        Assert.Single(result.SomeColumns);
    }

    // ─────────────────────────────────────────────────────────────────────
    //  End-to-end integration tests
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task OptionallyFrom_WrapsEveryRowInOption()
    {
        // Act
        var results = await _fdb.ExecuteFunctionalSqlAsync<UserDto>(
            "SELECT Id, Name, Email OPTIONALLY FROM Users");

        // Assert — all 5 users returned, each wrapped in Option
        Assert.True(results.Count >= 5);
        foreach (var opt in results)
        {
            Assert.True(opt.IsSome, "Every mapped row should be Some");
        }
    }

    [Fact]
    public async Task IsSome_FiltersOutNullAndEmptyEmails()
    {
        // Act — only users with a real email
        var results = await _fdb.ExecuteFunctionalSqlAsync<Dictionary<string, object>>(
            "SELECT Id, Name, Email OPTIONALLY FROM Users WHERE Email IS SOME");

        // Assert — Alice, Charlie, Eve have emails; Bob and Diana do not
        var names = new List<string>();
        foreach (var opt in results)
        {
            opt.IfSome(row => names.Add(Convert.ToString(row["Name"], System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty));
        }

        Assert.Contains("Alice", names);
        Assert.Contains("Charlie", names);
        Assert.Contains("Eve", names);
        Assert.DoesNotContain("Bob", names);
        Assert.DoesNotContain("Diana", names);
    }

    [Fact]
    public async Task IsNone_ReturnsOnlyRowsWithMissingEmail()
    {
        // Act — only users WITHOUT email
        var results = await _fdb.ExecuteFunctionalSqlAsync<Dictionary<string, object>>(
            "SELECT Id, Name, Email OPTIONALLY FROM Users WHERE Email IS NONE");

        // Assert — Bob and Diana have no email
        var names = new List<string>();
        foreach (var opt in results)
        {
            opt.IfSome(row => names.Add(Convert.ToString(row["Name"], System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty));
        }

        Assert.Contains("Bob", names);
        Assert.Contains("Diana", names);
        Assert.DoesNotContain("Alice", names);
        Assert.DoesNotContain("Eve", names);
    }

    [Fact]
    public async Task IsSome_WithManagerId_FiltersNullForeignKeys()
    {
        // Act — only users who have a manager assigned
        var results = await _fdb.ExecuteFunctionalSqlAsync<Dictionary<string, object>>(
            "SELECT Id, Name, ManagerId OPTIONALLY FROM Users WHERE ManagerId IS SOME");

        // Assert — Bob (1), Charlie (99), Eve (1) have ManagerId; Alice, Diana do not
        var names = new List<string>();
        foreach (var opt in results)
        {
            opt.IfSome(row => names.Add(Convert.ToString(row["Name"], System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty));
        }

        Assert.Contains("Bob", names);
        Assert.Contains("Charlie", names);
        Assert.Contains("Eve", names);
        Assert.DoesNotContain("Alice", names);
        Assert.DoesNotContain("Diana", names);
    }

    [Fact]
    public async Task ExecuteFunctionalSqlUnwrapped_ReturnsFlatSequence()
    {
        // Act — convenience method that unwraps Option
        var users = await _fdb.ExecuteFunctionalSqlUnwrappedAsync<Dictionary<string, object>>(
            "SELECT Id, Name, Email OPTIONALLY FROM Users WHERE Email IS SOME");

        // Assert — direct access, no Option wrapping
        Assert.True(users.Count >= 3);
        Assert.All(users, u =>
        {
            var email = Convert.ToString(u["Email"], System.Globalization.CultureInfo.InvariantCulture);
            Assert.False(string.IsNullOrEmpty(email));
        });
    }

    [Fact]
    public async Task StandardSql_StillWorksViaFunctionalApi()
    {
        // Act — standard SQL without functional keywords still works
        var results = await _fdb.ExecuteFunctionalSqlAsync<UserDto>(
            "SELECT Id, Name FROM Users WHERE Id = 1");

        // Assert — single result, not flagged as optional
        Assert.Single(results);
        var user = results.First();
        Assert.True(user.IsSome);
        Assert.Equal("Alice", user.Match(Some: u => u.Name, None: () => "fail"));
    }

    [Fact]
    public async Task CombinedQuery_IsSomeAndIsNone_WorksOnDifferentColumns()
    {
        // Act — users who HAVE email but DO NOT have a manager
        var results = await _fdb.ExecuteFunctionalSqlUnwrappedAsync<Dictionary<string, object>>(
            "SELECT Id, Name, Email OPTIONALLY FROM Users WHERE Email IS SOME AND ManagerId IS NONE");

        // Assert — only Alice (has email, no manager)
        Assert.Single(results);
        var first = results.First();
        Assert.Equal("Alice", Convert.ToString(first["Name"], System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task EmptyResult_ReturnsEmptySequence()
    {
        // Act — impossible filter
        var results = await _fdb.ExecuteFunctionalSqlAsync<UserDto>(
            "SELECT Id, Name OPTIONALLY FROM Users WHERE Id = 999");

        // Assert
        Assert.True(results.Count == 0);
    }

    [Fact]
    public async Task OptionChaining_WorksWithFunctionalSqlResults()
    {
        // Act — get results and chain Option operations
        var results = await _fdb.ExecuteFunctionalSqlAsync<UserDto>(
            "SELECT Id, Name, Email OPTIONALLY FROM Users WHERE Email IS SOME");

        // Chain: extract emails, provide defaults
        var emails = new List<string>();
        foreach (var opt in results)
        {
            var email = opt
                .Map(u => u.Email)
                .Bind(e => string.IsNullOrEmpty(e) ? Option<string>.None : Option<string>.Some(e))
                .IfNone("no-email");
            emails.Add(email);
        }

        // All should be real emails (IS SOME filtered out empties)
        Assert.All(emails, e => Assert.NotEqual("no-email", e));
        Assert.All(emails, e => Assert.Contains("@", e));
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
}
