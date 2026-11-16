using FluentAssertions;
using LinqToDB;
using LinqToDB.Mapping;
using Xunit;

namespace SharpCoreDB.Functional.Linq2DB.Tests;

/// <summary>
/// <see cref="FunctionalLinq2DbContext.GetByIdAsync{T}"/> looks the row up by its mapped primary key.
/// <para>
/// Up to 2.1.4 it ignored <c>keyValues</c> and returned the first row of the table — <c>GetByIdAsync([2])</c> and
/// <c>GetByIdAsync([999])</c> both answered with row 1 — and swallowed every exception into <c>None</c>, so a
/// failing query read as "not found".
/// </para>
/// </summary>
public sealed class GetByIdAsyncTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"linq2db_getbyid_{Guid.NewGuid():N}.scdb");
    private readonly SharpCoreDBDataConnection _connection;
    private readonly FunctionalLinq2DbContext _sut;

    public GetByIdAsyncTests()
    {
        _connection = new SharpCoreDBDataConnection($"Data Source={_path}");
        _sut = new FunctionalLinq2DbContext(_connection);

        Execute("CREATE TABLE gbi_rows (id INTEGER PRIMARY KEY, name TEXT)");
        Execute("CREATE TABLE gbi_pairs (tenant INTEGER, code TEXT, name TEXT)");

        _connection.Insert(new KeyedRow { Id = 1, Name = "one" });
        _connection.Insert(new KeyedRow { Id = 2, Name = "two" });
        _connection.Insert(new KeyedRow { Id = 3, Name = "three" });

        _connection.Insert(new PairRow { Tenant = 1, Code = "a", Name = "1a" });
        _connection.Insert(new PairRow { Tenant = 1, Code = "b", Name = "1b" });
        _connection.Insert(new PairRow { Tenant = 2, Code = "a", Name = "2a" });
    }

    [Fact]
    public async Task GetByIdAsync_WithExistingKey_ReturnsThatRow()
    {
        var result = await _sut.GetByIdAsync<KeyedRow>([2L]);

        result.Match(r => r.Name, () => "<none>").Should().Be("two");
    }

    [Fact]
    public async Task GetByIdAsync_WithMissingKey_ReturnsNone()
    {
        var result = await _sut.GetByIdAsync<KeyedRow>([999L]);

        result.IsNone.Should().BeTrue();
    }

    [Fact]
    public async Task GetByIdAsync_WithKeyOfConvertibleType_ConvertsToPropertyType()
    {
        // An int literal for a long key is the common call shape: GetByIdAsync<T>([3]).
        var result = await _sut.GetByIdAsync<KeyedRow>([3]);

        result.Match(r => r.Name, () => "<none>").Should().Be("three");
    }

    [Fact]
    public async Task GetByIdAsync_WithCompositeKey_MatchesEveryKeyColumnInOrder()
    {
        var result = await _sut.GetByIdAsync<PairRow>([2, "a"]);

        result.Match(r => r.Name, () => "<none>").Should().Be("2a");
    }

    [Fact]
    public async Task GetByIdAsync_WithWrongKeyCount_ThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _sut.GetByIdAsync<PairRow>([2]));
    }

    [Fact]
    public async Task GetByIdAsync_WithEmptyKeyValues_ThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _sut.GetByIdAsync<KeyedRow>([]));
    }

    [Fact]
    public async Task GetByIdAsync_WithUnconvertibleKey_ThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _sut.GetByIdAsync<KeyedRow>(["not a number"]));
    }

    [Fact]
    public async Task GetByIdAsync_OnEntityWithoutPrimaryKey_ThrowsInvalidOperationException()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.GetByIdAsync<NoKeyRow>([1]));
    }

    [Fact]
    public async Task GetByIdAsync_WhenQueryFails_PropagatesTheException()
    {
        // The table does not exist: that is an error, not "not found".
        await Assert.ThrowsAnyAsync<Exception>(() => _sut.GetByIdAsync<MissingTableRow>([1]));
    }

    public void Dispose()
    {
        _connection.Dispose();
        try
        {
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup of a temp file.
        }
    }

    private void Execute(string sql)
    {
        using var command = _connection.GetUnderlyingConnection().CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    [Table("gbi_rows")]
    public sealed class KeyedRow
    {
        [PrimaryKey, Column("id")] public long Id { get; set; }
        [Column("name")] public string Name { get; set; } = "";
    }

    [Table("gbi_pairs")]
    public sealed class PairRow
    {
        [PrimaryKey(0), Column("tenant")] public int Tenant { get; set; }
        [PrimaryKey(1), Column("code")] public string Code { get; set; } = "";
        [Column("name")] public string Name { get; set; } = "";
    }

    [Table("gbi_rows")]
    public sealed class NoKeyRow
    {
        [Column("id")] public long Id { get; set; }
    }

    [Table("gbi_table_that_does_not_exist")]
    public sealed class MissingTableRow
    {
        [PrimaryKey, Column("id")] public long Id { get; set; }
    }
}
