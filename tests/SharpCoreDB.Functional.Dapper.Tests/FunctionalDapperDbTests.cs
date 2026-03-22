using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.Functional.Dapper;
using SharpCoreDB.Interfaces;

namespace SharpCoreDB.Functional.Dapper.Tests;

public sealed class FunctionalDapperDbTests : IAsyncDisposable
{
    private readonly DatabaseFactory _factory;
    private readonly List<IDatabase> _dbs = [];
    private readonly List<string> _paths = [];

    public FunctionalDapperDbTests()
    {
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        _factory = services.BuildServiceProvider().GetRequiredService<DatabaseFactory>();
    }

    [Fact]
    public async Task GetByIdAsync_WhenRowExists_ReturnsSome()
    {
        var db = CreateDatabase();
        db.ExecuteSQL("CREATE TABLE users (Id INTEGER, Name TEXT)");
        db.ExecuteSQL("INSERT INTO users VALUES (1, 'Alice')");

        var sut = db.FunctionalDapper();
        var result = await sut.GetByIdAsync<UserRow, int>("users", 1);

        Assert.True(result.IsSome);
    }

    [Fact]
    public async Task DeleteAsync_WhenRowDeleted_ReturnsSuccess()
    {
        var db = CreateDatabase();
        db.ExecuteSQL("CREATE TABLE users (Id INTEGER, Name TEXT)");
        db.ExecuteSQL("INSERT INTO users VALUES (2, 'DeleteMe')");

        var sut = db.FunctionalDapper();
        var deleteResult = await sut.DeleteAsync("DELETE FROM users WHERE Id = @Id", new { Id = 2 });
        var readResult = await sut.GetByIdAsync<UserRow, int>("users", 2);

        Assert.True(deleteResult.IsSucc);
        Assert.True(readResult.IsNone);
    }

    [Fact]
    public async Task CountAsync_WithPredicate_ReturnsExpectedCount()
    {
        var db = CreateDatabase();
        db.ExecuteSQL("CREATE TABLE users (Id INTEGER, Name TEXT)");
        db.ExecuteSQL("INSERT INTO users VALUES (1, 'A')");
        db.ExecuteSQL("INSERT INTO users VALUES (2, 'B')");
        db.ExecuteSQL("INSERT INTO users VALUES (3, 'C')");

        var sut = db.FunctionalDapper();
        var count = await sut.CountAsync("SELECT COUNT(*) FROM users WHERE Id >= @FromId", new { FromId = 2 });

        Assert.Equal(2L, count);
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
        var path = Path.Combine(Path.GetTempPath(), $"functional_dapper_{Guid.NewGuid():N}");
        _paths.Add(path);
        var db = _factory.Create(path, "testpass");
        _dbs.Add(db);
        return db;
    }

    private sealed class UserRow
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }
}
