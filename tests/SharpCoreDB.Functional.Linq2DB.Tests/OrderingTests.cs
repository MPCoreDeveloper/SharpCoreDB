using FluentAssertions;
using LinqToDB;
using LinqToDB.Mapping;
using Xunit;

namespace SharpCoreDB.Functional.Linq2DB.Tests;

/// <summary>
/// LINQ ordering through the adapter: linq2db emits <c>ORDER BY [x].[a], [x].[b] DESC</c> for
/// <c>OrderBy(…).ThenByDescending(…)</c> and wraps computed keys in SQL functions.
/// <para>
/// Up to 2.1.4 the engine read only the first ORDER BY token — here <c>[x].[grp],</c>, which matched no column — so
/// every multi-key, qualified or computed ordering came back in storage order.
/// </para>
/// </summary>
public sealed class OrderingTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"linq2db_ordering_{Guid.NewGuid():N}.scdb");
    private readonly SharpCoreDBDataConnection _connection;

    public OrderingTests()
    {
        _connection = new SharpCoreDBDataConnection($"Data Source={_path}");
        using (var command = _connection.GetUnderlyingConnection().CreateCommand())
        {
            command.CommandText = "CREATE TABLE ranked (id INTEGER PRIMARY KEY, grp INTEGER, name TEXT, at TEXT)";
            command.ExecuteNonQuery();
        }

        _connection.Insert(new Ranked { Id = 1, Grp = 2, Name = "bb", At = new DateTime(2026, 3, 1) });
        _connection.Insert(new Ranked { Id = 2, Grp = 1, Name = "a", At = new DateTime(2024, 1, 1) });
        _connection.Insert(new Ranked { Id = 3, Grp = 2, Name = "ccc", At = new DateTime(2025, 6, 1) });
        _connection.Insert(new Ranked { Id = 4, Grp = 1, Name = "dddd", At = new DateTime(2023, 1, 1) });
    }

    [Fact]
    public void OrderBy_ThenBy_SortsByBothKeys()
    {
        Ids(_connection.GetTable<Ranked>().OrderBy(r => r.Grp).ThenBy(r => r.Name)).Should().Equal(2, 4, 1, 3);
    }

    [Fact]
    public void OrderByDescending_ThenBy_SortsEachKeyInItsOwnDirection()
    {
        Ids(_connection.GetTable<Ranked>().OrderByDescending(r => r.Grp).ThenBy(r => r.Name)).Should().Equal(1, 3, 2, 4);
    }

    [Fact]
    public void OrderByDescending_DateTime_SortsNewestFirst()
    {
        Ids(_connection.GetTable<Ranked>().OrderByDescending(r => r.At)).Should().Equal(1, 3, 2, 4);
    }

    [Fact]
    public void OrderBy_StringLength_SortsByTheComputedKey()
    {
        Ids(_connection.GetTable<Ranked>().OrderByDescending(r => r.Name.Length)).Should().Equal(4, 3, 1, 2);
    }

    [Fact]
    public void OrderBy_ThenTake_TakesFromTheSortedRows()
    {
        Ids(_connection.GetTable<Ranked>().OrderBy(r => r.Grp).ThenByDescending(r => r.Id).Take(2)).Should().Equal(4, 2);
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

    private static int[] Ids(IQueryable<Ranked> query) => [.. query.Select(r => r.Id)];

    [Table("ranked")]
    public sealed class Ranked
    {
        [PrimaryKey, Column("id")] public int Id { get; set; }
        [Column("grp")] public int Grp { get; set; }
        [Column("name")] public string Name { get; set; } = "";
        [Column("at")] public DateTime At { get; set; }
    }
}
