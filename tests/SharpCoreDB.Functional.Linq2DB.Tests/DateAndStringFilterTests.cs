using FluentAssertions;
using LinqToDB;
using LinqToDB.Mapping;
using Xunit;

namespace SharpCoreDB.Functional.Linq2DB.Tests;

/// <summary>
/// LINQ filters that linq2db's SQLite dialect translates into scalar functions over a column: every
/// <see cref="DateTime"/> comparison (<c>strftime('%Y-%m-%d %H:%M:%f', [x].[at]) = strftime(…, @p)</c>), date parts
/// (<c>CAST(strftime('%Y', …) AS int)</c>), <c>.Date</c>, <c>ToUpper()</c> and <c>Length</c>.
/// <para>
/// Up to 2.1.4 SharpCoreDB evaluated none of these over a table: each filter matched no row, so
/// <c>Where(x =&gt; x.At == d)</c>, <c>x.At &gt; d</c> and a date-filtered <c>DeleteWhereAsync</c> silently did
/// nothing.
/// </para>
/// </summary>
public sealed class DateAndStringFilterTests : IDisposable
{
    private static readonly DateTime Jan1Noon = new(2026, 1, 1, 12, 0, 0);
    private static readonly DateTime Mid2025 = new(2025, 6, 15, 8, 30, 0, 250);
    private static readonly DateTime Mar10Late = new(2026, 3, 10, 23, 59, 59, 999);

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"linq2db_filters_{Guid.NewGuid():N}.scdb");
    private readonly SharpCoreDBDataConnection _connection;

    public DateAndStringFilterTests()
    {
        _connection = new SharpCoreDBDataConnection($"Data Source={_path}");
        using (var command = _connection.GetUnderlyingConnection().CreateCommand())
        {
            command.CommandText = "CREATE TABLE events (id INTEGER PRIMARY KEY, name TEXT, at TEXT)";
            command.ExecuteNonQuery();
        }

        _connection.Insert(new Event { Id = 1, Name = "Alpha", At = Jan1Noon });
        _connection.Insert(new Event { Id = 2, Name = "beta", At = Mid2025 });
        _connection.Insert(new Event { Id = 3, Name = "Gamma", At = Mar10Late });
    }

    [Fact]
    public void Where_DateTimeEquals_FindsTheRow()
    {
        Ids(_connection.GetTable<Event>().Where(e => e.At == Jan1Noon)).Should().Equal(1);
    }

    [Fact]
    public void Where_DateTimeGreaterThan_FindsLaterRows()
    {
        var cutoff = new DateTime(2026, 1, 1);

        Ids(_connection.GetTable<Event>().Where(e => e.At > cutoff)).Should().Equal(1, 3);
    }

    [Fact]
    public void Where_DateTimeRange_FindsRowsInside()
    {
        var from = new DateTime(2025, 1, 1);
        var to = new DateTime(2026, 2, 1);

        Ids(_connection.GetTable<Event>().Where(e => e.At >= from && e.At < to)).Should().Equal(1, 2);
    }

    [Fact]
    public void Where_DateTimeYear_FindsRowsInThatYear()
    {
        Ids(_connection.GetTable<Event>().Where(e => e.At.Year == 2026)).Should().Equal(1, 3);
    }

    [Fact]
    public void Where_DateTimeDate_FindsRowsOnThatDay()
    {
        var day = new DateTime(2026, 3, 10);

        Ids(_connection.GetTable<Event>().Where(e => e.At.Date == day)).Should().Equal(3);
    }

    [Fact]
    public void Where_ToUpper_ComparesCaseInsensitively()
    {
        Ids(_connection.GetTable<Event>().Where(e => e.Name.ToUpper() == "BETA")).Should().Equal(2);
    }

    [Fact]
    public void Where_StringLength_FindsMatchingRows()
    {
        Ids(_connection.GetTable<Event>().Where(e => e.Name.Length == 5)).Should().Equal(1, 3);
    }

    [Fact]
    public void Select_DateTimeYear_ProjectsTheYear()
    {
        _connection.GetTable<Event>().Where(e => e.Id == 2).Select(e => e.At.Year).Single().Should().Be(2025);
    }

    [Fact]
    public async Task DeleteWhereAsync_ByDateTime_DeletesOnlyMatchingRows()
    {
        var db = new FunctionalLinq2DbContext(_connection);
        var cutoff = new DateTime(2026, 1, 1);

        var deleted = await db.DeleteWhereAsync<Event>(e => e.At < cutoff);

        deleted.Match(n => n, _ => -1).Should().Be(1);
        Ids(_connection.GetTable<Event>()).Should().Equal(1, 3);
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

    private static int[] Ids(IQueryable<Event> query) => [.. query.Select(e => e.Id).AsEnumerable().Order()];

    [Table("events")]
    public sealed class Event
    {
        [PrimaryKey, Column("id")] public int Id { get; set; }
        [Column("name")] public string Name { get; set; } = "";
        [Column("at")] public DateTime At { get; set; }
    }
}
