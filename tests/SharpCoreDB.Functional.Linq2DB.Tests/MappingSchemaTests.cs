using System.Globalization;
using FluentAssertions;
using LinqToDB;
using LinqToDB.Common;
using LinqToDB.Data;
using LinqToDB.Mapping;
using Xunit;

namespace SharpCoreDB.Functional.Linq2DB.Tests;

/// <summary>
/// How <see cref="SharpCoreDBMappingSchema"/> combines with a caller's own mapping schema, and the text formats
/// its converters read and write.
/// <para>
/// Up to 2.1.4 both <c>new SharpCoreDBDataConnection(DataOptions)</c> and <c>UseSharpCoreDB</c> applied the
/// schema with <c>UseMappingSchema</c>, which linq2db documents as "replaces all previous registrations": a fluent
/// mapping of <c>Entity</c> to <c>custom_table</c> silently queried <c>[Entity]</c> instead. The Guid converters
/// disagreed with each other (<c>"D"</c> for parameters, <c>"N"</c> for <c>Guid → string</c>), and stored dates
/// were parsed with the current culture — under a Buddhist-calendar culture (th-TH) the year 2026 read back as 1483.
/// </para>
/// </summary>
public sealed class MappingSchemaTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"linq2db_schema_{Guid.NewGuid():N}.scdb");

    private string ConnectionString => $"Data Source={_path}";

    [Fact]
    public void Constructor_WithUserMappingSchema_KeepsTheUserMappings()
    {
        var options = SharpCoreDBDataConnection.CreateOptions(ConnectionString)
            .UseAdditionalMappingSchema(UserSchema());

        using var connection = new SharpCoreDBDataConnection(options);
        Execute(connection, "CREATE TABLE custom_table (custom_id INTEGER PRIMARY KEY, custom_code TEXT)");
        connection.Insert(new Unattributed { Id = 5, Code = Ulid.NewUlid() });

        connection.GetTable<Unattributed>().Single().Id.Should().Be(5);
    }

    [Fact]
    public void UseSharpCoreDB_OnOptionsWithAMappingSchema_KeepsTheUserMappings()
    {
        var options = new DataOptions()
            .UseMappingSchema(UserSchema())
            .UseSharpCoreDB(ConnectionString);

        using var connection = new SharpCoreDBDataConnection(options);
        Execute(connection, "CREATE TABLE custom_table (custom_id INTEGER PRIMARY KEY, custom_code TEXT)");
        connection.Insert(new Unattributed { Id = 6, Code = Ulid.NewUlid() });

        connection.GetTable<Unattributed>().Single().Id.Should().Be(6);
    }

    [Fact]
    public void Constructor_WithUserMappingSchema_StillAppliesTheSharpCoreDbConverters()
    {
        var options = SharpCoreDBDataConnection.CreateOptions(ConnectionString)
            .UseAdditionalMappingSchema(UserSchema());
        var code = Ulid.NewUlid();

        using var connection = new SharpCoreDBDataConnection(options);
        Execute(connection, "CREATE TABLE custom_table (custom_id INTEGER PRIMARY KEY, custom_code TEXT)");
        connection.Insert(new Unattributed { Id = 8, Code = code });

        connection.GetTable<Unattributed>().Single(e => e.Code == code).Id.Should().Be(8);
    }

    [Fact]
    public void Constructor_WithUserConverter_LetsTheUserConverterWin()
    {
        // The caller's schema has priority over the SharpCoreDB defaults: here it stores Guids in "N" form.
        var user = UserSchema();
        user.SetConverter<Guid, DataParameter>(g =>
            new DataParameter(null, g.ToString("N"), LinqToDB.DataType.NVarChar));
        var options = SharpCoreDBDataConnection.CreateOptions(ConnectionString).UseAdditionalMappingSchema(user);
        var guid = Guid.NewGuid();

        using var connection = new SharpCoreDBDataConnection(options);
        Execute(connection, "CREATE TABLE guid_rows (id INTEGER PRIMARY KEY, value TEXT)");
        connection.Insert(new GuidRow { Id = 1, Value = guid });

        Scalar(connection, "SELECT value FROM guid_rows WHERE id = 1").Should().Be(guid.ToString("N"));
    }

    [Fact]
    public void GuidToString_UsesTheFormatStoredByParameters()
    {
        var guid = Guid.NewGuid();
        using var connection = new SharpCoreDBDataConnection(ConnectionString);
        Execute(connection, "CREATE TABLE guid_rows (id INTEGER PRIMARY KEY, value TEXT)");
        connection.Insert(new GuidRow { Id = 1, Value = guid });

        var stored = Scalar(connection, "SELECT value FROM guid_rows WHERE id = 1");

        Converter.ChangeTypeTo<string>(guid, SharpCoreDBMappingSchema.Instance).Should().Be(stored);
    }

    [Fact]
    public void DateTime_UnderANonGregorianCulture_RoundTrips()
    {
        var value = new DateTime(2026, 1, 1, 12, 30, 15, 250);
        using var connection = new SharpCoreDBDataConnection(ConnectionString);
        Execute(connection, "CREATE TABLE date_rows (id INTEGER PRIMARY KEY, at TEXT, at_offset TEXT)");

        var read = WithCulture("th-TH", () =>
        {
            connection.Insert(new DateRow
            {
                Id = 1, At = value, AtOffset = new DateTimeOffset(value, TimeSpan.FromHours(10))
            });
            return connection.GetTable<DateRow>().Single(r => r.Id == 1);
        });

        read.At.Should().Be(value);
        read.AtOffset.Should().Be(new DateTimeOffset(value, TimeSpan.FromHours(10)));
    }

    [Fact]
    public void DateTime_StoredWithUtcMarker_ReadsBackAsUtc()
    {
        // A round-trip ("O") string written by another client — plain ADO.NET, a migration — carries its kind.
        // Up to 2.1.4 the reader parsed it without RoundtripKind, converting it to local time (Kind = Local).
        using var connection = new SharpCoreDBDataConnection(ConnectionString);
        Execute(connection, "CREATE TABLE date_rows (id INTEGER PRIMARY KEY, at TEXT, at_offset TEXT)");
        Execute(connection,
            "INSERT INTO date_rows (id, at, at_offset) VALUES (1, '2026-01-01T12:00:00.0000000Z', '2026-01-01 12:00:00.000+00:00')");

        var read = connection.GetTable<DateRow>().Single(r => r.Id == 1).At;

        read.Kind.Should().Be(DateTimeKind.Utc);
        read.Should().Be(new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc));
    }

    public void Dispose()
    {
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

    private static MappingSchema UserSchema()
    {
        var schema = new MappingSchema();
        new FluentMappingBuilder(schema)
            .Entity<Unattributed>().HasTableName("custom_table")
            .Property(e => e.Id).HasColumnName("custom_id").IsPrimaryKey()
            .Property(e => e.Code).HasColumnName("custom_code")
            .Build();
        return schema;
    }

    private static T WithCulture<T>(string name, Func<T> action)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
        try
        {
            return action();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    private static void Execute(SharpCoreDBDataConnection connection, string sql)
    {
        using var command = connection.GetUnderlyingConnection().CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static string? Scalar(SharpCoreDBDataConnection connection, string sql)
    {
        using var command = connection.GetUnderlyingConnection().CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    public sealed class Unattributed
    {
        public long Id { get; set; }
        public Ulid Code { get; set; } = null!;
    }

    [Table("guid_rows")]
    public sealed class GuidRow
    {
        [PrimaryKey, Column("id")] public long Id { get; set; }
        [Column("value")] public Guid Value { get; set; }
    }

    [Table("date_rows")]
    public sealed class DateRow
    {
        [PrimaryKey, Column("id")] public long Id { get; set; }
        [Column("at")] public DateTime At { get; set; }
        [Column("at_offset")] public DateTimeOffset AtOffset { get; set; }
    }
}
