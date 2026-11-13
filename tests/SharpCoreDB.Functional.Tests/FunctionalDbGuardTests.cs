using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.Functional;
using SharpCoreDB.Interfaces;

namespace SharpCoreDB.Functional.Tests;

/// <summary>
/// Argument, cancellation and default-id-column contract of <see cref="FunctionalDb"/>.
/// Added with the mutation baseline: 51 mutants in <c>FunctionalDb.cs</c> survived the green suite,
/// almost all of them statement mutations on the <c>ThrowIfNullOrWhiteSpace</c> /
/// <c>ThrowIfNull</c> / <c>ThrowIfCancellationRequested</c> guards and the
/// <c>string.IsNullOrWhiteSpace(idColumn) ? DefaultIdColumn : idColumn</c> choice. Nothing asserted
/// either, so a guard could be deleted without a red test.
/// </summary>
public sealed class FunctionalDbGuardTests : IAsyncDisposable
{
    private readonly DatabaseFactory _factory;
    private readonly List<IDatabase> _dbs = [];
    private readonly List<string> _paths = [];

    public FunctionalDbGuardTests()
    {
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        _factory = services.BuildServiceProvider().GetRequiredService<DatabaseFactory>();
    }

    // ---- GetByIdAsync ----

    [Fact]
    public void GetByIdAsync_WithBlankTableName_ThrowsArgumentException()
    {
        var sut = CreateDatabase().Functional();

        Assert.Throws<ArgumentException>(() => { _ = sut.GetByIdAsync<UserRow>("  ", 1); });
    }

    [Fact]
    public void GetByIdAsync_WithNullId_ThrowsArgumentNullException()
    {
        var sut = CreateDatabase().Functional();

        Assert.Throws<ArgumentNullException>(() => { _ = sut.GetByIdAsync<UserRow>("users", null!); });
    }

    [Fact]
    public void GetByIdAsync_WhenCancelled_ThrowsOperationCanceled()
    {
        var sut = CreateDatabase().Functional();

        Assert.Throws<OperationCanceledException>(() =>
        {
            _ = sut.GetByIdAsync<UserRow>("users", 1, cancellationToken: new CancellationToken(canceled: true));
        });
    }

    // ---- FindOneAsync ----

    [Fact]
    public void FindOneAsync_WithBlankSql_ThrowsArgumentException()
    {
        var sut = CreateDatabase().Functional();

        Assert.Throws<ArgumentException>(() => { _ = sut.FindOneAsync<UserRow>(" "); });
    }

    [Fact]
    public void FindOneAsync_WhenCancelled_ThrowsOperationCanceled()
    {
        var sut = CreateDatabase().Functional();

        Assert.Throws<OperationCanceledException>(() =>
        {
            _ = sut.FindOneAsync<UserRow>("SELECT 1", cancellationToken: new CancellationToken(canceled: true));
        });
    }

    // ---- QueryAsync ----

    [Fact]
    public void QueryAsync_WithBlankSql_ThrowsArgumentException()
    {
        var sut = CreateDatabase().Functional();

        Assert.Throws<ArgumentException>(() => { _ = sut.QueryAsync<UserRow>(""); });
    }

    [Fact]
    public void QueryAsync_WhenCancelled_ThrowsOperationCanceled()
    {
        var sut = CreateDatabase().Functional();

        Assert.Throws<OperationCanceledException>(() =>
        {
            _ = sut.QueryAsync<UserRow>("SELECT 1", cancellationToken: new CancellationToken(canceled: true));
        });
    }

    // ---- ExecuteFunctionalSqlAsync ----

    [Fact]
    public void ExecuteFunctionalSqlAsync_WithBlankSql_ThrowsArgumentException()
    {
        var sut = CreateDatabase().Functional();

        Assert.Throws<ArgumentException>(() => { _ = sut.ExecuteFunctionalSqlAsync<UserRow>("   "); });
    }

    [Fact]
    public void ExecuteFunctionalSqlAsync_WhenCancelled_ThrowsOperationCanceled()
    {
        var sut = CreateDatabase().Functional();

        Assert.Throws<OperationCanceledException>(() =>
        {
            _ = sut.ExecuteFunctionalSqlAsync<UserRow>(
                "SELECT * FROM users",
                cancellationToken: new CancellationToken(canceled: true));
        });
    }

    // ---- InsertAsync ----

    [Fact]
    public async Task InsertAsync_WithBlankTableName_ThrowsArgumentException()
    {
        var sut = CreateDatabase().Functional();

        await Assert.ThrowsAsync<ArgumentException>(() => sut.InsertAsync(" ", new UserRow { Id = 1 }));
    }

    [Fact]
    public async Task InsertAsync_WithNullEntity_ThrowsArgumentNullException()
    {
        var sut = CreateDatabase().Functional();

        await Assert.ThrowsAsync<ArgumentNullException>(() => sut.InsertAsync<UserRow>("users", null!));
    }

    [Fact]
    public async Task InsertAsync_WhenCancelled_ThrowsOperationCanceled()
    {
        var sut = CreateDatabase().Functional();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            sut.InsertAsync("users", new UserRow { Id = 1 }, new CancellationToken(canceled: true)));
    }

    // ---- UpdateAsync ----

    [Fact]
    public async Task UpdateAsync_WithBlankTableName_ThrowsArgumentException()
    {
        var sut = CreateDatabase().Functional();

        await Assert.ThrowsAsync<ArgumentException>(() => sut.UpdateAsync(" ", 1, new UserRow { Id = 1 }));
    }

    [Fact]
    public async Task UpdateAsync_WithNullId_ThrowsArgumentNullException()
    {
        var sut = CreateDatabase().Functional();

        await Assert.ThrowsAsync<ArgumentNullException>(() => sut.UpdateAsync("users", null!, new UserRow { Id = 1 }));
    }

    [Fact]
    public async Task UpdateAsync_WithNullEntity_ThrowsArgumentNullException()
    {
        var sut = CreateDatabase().Functional();

        await Assert.ThrowsAsync<ArgumentNullException>(() => sut.UpdateAsync<UserRow>("users", 1, null!));
    }

    [Fact]
    public async Task UpdateAsync_WhenCancelled_ThrowsOperationCanceled()
    {
        var sut = CreateDatabase().Functional();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            sut.UpdateAsync("users", 1, new UserRow { Id = 1 }, cancellationToken: new CancellationToken(canceled: true)));
    }

    // ---- DeleteAsync ----

    [Fact]
    public async Task DeleteAsync_WithBlankTableName_ThrowsArgumentException()
    {
        var sut = CreateDatabase().Functional();

        await Assert.ThrowsAsync<ArgumentException>(() => sut.DeleteAsync("  ", 1));
    }

    [Fact]
    public async Task DeleteAsync_WithNullId_ThrowsArgumentNullException()
    {
        var sut = CreateDatabase().Functional();

        await Assert.ThrowsAsync<ArgumentNullException>(() => sut.DeleteAsync("users", null!));
    }

    [Fact]
    public async Task DeleteAsync_WhenCancelled_ThrowsOperationCanceled()
    {
        var sut = CreateDatabase().Functional();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            sut.DeleteAsync("users", 1, cancellationToken: new CancellationToken(canceled: true)));
    }

    // ---- CountAsync ----

    [Fact]
    public void CountAsync_WithBlankTableName_ThrowsArgumentException()
    {
        var sut = CreateDatabase().Functional();

        Assert.Throws<ArgumentException>(() => { _ = sut.CountAsync(" "); });
    }

    [Fact]
    public void CountAsync_WhenCancelled_ThrowsOperationCanceled()
    {
        var sut = CreateDatabase().Functional();

        Assert.Throws<OperationCanceledException>(() =>
        {
            _ = sut.CountAsync("users", cancellationToken: new CancellationToken(canceled: true));
        });
    }

    [Fact]
    public async Task CountAsync_WithoutWhereClause_CountsEveryRow()
    {
        // Arrange — the empty-where branch must produce no filter at all
        var db = CreateDatabase();
        db.ExecuteSQL("CREATE TABLE users (Id INTEGER, Name TEXT)");
        db.ExecuteSQL("INSERT INTO users VALUES (1, 'A')");
        db.ExecuteSQL("INSERT INTO users VALUES (2, 'B')");
        var sut = db.Functional();

        // Act
        var count = await sut.CountAsync("users");

        // Assert
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task CountAsync_OnEmptyTable_ReturnsZero()
    {
        // Arrange
        var db = CreateDatabase();
        db.ExecuteSQL("CREATE TABLE users (Id INTEGER, Name TEXT)");

        // Act
        var count = await db.Functional().CountAsync("users");

        // Assert
        Assert.Equal(0, count);
    }

    // ---- DefaultIdColumn ----

    [Fact]
    public void DefaultIdColumn_ByDefault_IsId()
    {
        var sut = CreateDatabase().Functional();

        Assert.Equal("Id", sut.DefaultIdColumn);
    }

    [Fact]
    public void DefaultIdColumn_WhenBlank_FallsBackToId()
    {
        // Arrange / Act — whitespace must not become the column name
        var sut = new FunctionalDb(CreateDatabase()) { DefaultIdColumn = "   " };

        // Assert
        Assert.Equal("Id", sut.DefaultIdColumn);
    }

    [Fact]
    public void DefaultIdColumn_WhenCustom_IsPreserved()
    {
        var sut = new FunctionalDb(CreateDatabase()) { DefaultIdColumn = "UserId" };

        Assert.Equal("UserId", sut.DefaultIdColumn);
    }

    [Fact]
    public async Task GetByIdAsync_WithoutIdColumn_UsesDefaultIdColumn()
    {
        // Arrange — the table has no "Id" column at all, so only the default can find the row
        var db = CreateDatabase();
        db.ExecuteSQL("CREATE TABLE accounts (UserId INTEGER, Name TEXT)");
        db.ExecuteSQL("INSERT INTO accounts VALUES (7, 'Grace')");
        var sut = new FunctionalDb(db) { DefaultIdColumn = "UserId" };

        // Act
        var result = await sut.GetByIdAsync<AccountRow>("accounts", 7);

        // Assert
        Assert.True(result.IsSome);
        Assert.Equal("Grace", result.Match(Some: a => a.Name, None: () => string.Empty));
    }

    [Fact]
    public async Task GetByIdAsync_WithExplicitIdColumn_OverridesDefault()
    {
        // Arrange — DefaultIdColumn names a column that does not exist here; the explicit one does
        var db = CreateDatabase();
        db.ExecuteSQL("CREATE TABLE accounts (AccountId INTEGER, Name TEXT)");
        db.ExecuteSQL("INSERT INTO accounts VALUES (9, 'Ada')");
        var sut = new FunctionalDb(db) { DefaultIdColumn = "UserId" };

        // Act
        var result = await sut.GetByIdAsync<AccountRow>("accounts", 9, idColumn: "AccountId");

        // Assert
        Assert.True(result.IsSome);
        Assert.Equal("Ada", result.Match(Some: a => a.Name, None: () => string.Empty));
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var db in _dbs)
        {
            await db.DisposeAsync();
        }

        foreach (var path in _paths)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }
            catch
            {
                // Ignore cleanup errors.
            }
        }
    }

    private IDatabase CreateDatabase()
    {
        var testPath = Path.Combine(Path.GetTempPath(), $"functional_guard_{Guid.NewGuid():N}");
        _paths.Add(testPath);
        var db = _factory.Create(testPath, "testpass");
        _dbs.Add(db);
        return db;
    }

    private sealed class UserRow
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    private sealed class AccountRow
    {
        public int UserId { get; set; }

        public int AccountId { get; set; }

        public string Name { get; set; } = string.Empty;
    }
}

