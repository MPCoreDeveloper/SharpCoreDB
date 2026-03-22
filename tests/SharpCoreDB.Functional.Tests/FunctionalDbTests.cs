using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.Functional;
using SharpCoreDB.Interfaces;

namespace SharpCoreDB.Functional.Tests;

public sealed class FunctionalDbTests : IAsyncDisposable
{
    private readonly DatabaseFactory _factory;
    private readonly List<IDatabase> _dbs = [];
    private readonly List<string> _paths = [];

    public FunctionalDbTests()
    {
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        _factory = services.BuildServiceProvider().GetRequiredService<DatabaseFactory>();
    }

    [Fact]
    public async Task GetByIdAsync_WhenRowExists_ReturnsSome()
    {
        // Arrange
        var db = CreateDatabase();
        db.ExecuteSQL("CREATE TABLE users (Id INTEGER, Name TEXT)");
        db.ExecuteSQL("INSERT INTO users VALUES (1, 'Alice')");
        var sut = db.Functional();

        // Act
        var result = await sut.GetByIdAsync<UserRow>("users", 1);

        // Assert
        Assert.True(result.IsSome);
    }

    [Fact]
    public async Task GetByIdAsync_WhenRowMissing_ReturnsNone()
    {
        // Arrange
        var db = CreateDatabase();
        db.ExecuteSQL("CREATE TABLE users (Id INTEGER, Name TEXT)");
        var sut = db.Functional();

        // Act
        var result = await sut.GetByIdAsync<UserRow>("users", 42);

        // Assert
        Assert.True(result.IsNone);
    }

    [Fact]
    public async Task InsertAsync_WhenEntityValid_ReturnsSuccess()
    {
        // Arrange
        var db = CreateDatabase();
        db.ExecuteSQL("CREATE TABLE users (Id INTEGER, Name TEXT)");
        var sut = db.Functional();

        // Act
        var result = await sut.InsertAsync("users", new UserRow { Id = 5, Name = "Delta" });

        // Assert
        Assert.True(result.IsSucc);
    }

    [Fact]
    public async Task DeleteAsync_WhenRowExists_RemovesRow()
    {
        // Arrange
        var db = CreateDatabase();
        db.ExecuteSQL("CREATE TABLE users (Id INTEGER, Name TEXT)");
        db.ExecuteSQL("INSERT INTO users VALUES (8, 'ToDelete')");
        var sut = db.Functional();

        // Act
        var deleteResult = await sut.DeleteAsync("users", 8);
        var afterDelete = await sut.GetByIdAsync<UserRow>("users", 8);

        // Assert
        Assert.True(deleteResult.IsSucc);
        Assert.True(afterDelete.IsNone);
    }

    [Fact]
    public async Task CountAsync_WithWhereClause_ReturnsExpectedCount()
    {
        // Arrange
        var db = CreateDatabase();
        db.ExecuteSQL("CREATE TABLE users (Id INTEGER, Name TEXT)");
        db.ExecuteSQL("INSERT INTO users VALUES (1, 'A')");
        db.ExecuteSQL("INSERT INTO users VALUES (2, 'B')");
        db.ExecuteSQL("INSERT INTO users VALUES (3, 'C')");
        var sut = db.Functional();

        // Act
        var count = await sut.CountAsync("users", "Id >= 2");

        // Assert
        Assert.Equal(2, count);
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
        var testPath = Path.Combine(Path.GetTempPath(), $"functional_test_{Guid.NewGuid():N}");
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
}
