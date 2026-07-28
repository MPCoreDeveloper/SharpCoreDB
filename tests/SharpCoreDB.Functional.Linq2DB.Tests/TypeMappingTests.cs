using System;
using System.IO;
using FluentAssertions;
using LinqToDB;
using LinqToDB.Async;
using LinqToDB.Mapping;
using Xunit;

namespace SharpCoreDB.Functional.Linq2DB.Tests;

/// <summary>
/// Tests for SharpCoreDB-specific type mappings (ULID, GUID, DateTime).
/// C# 14: Validates custom mapping schema behavior.
/// </summary>
public sealed class TypeMappingTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly SharpCoreDBDataConnection _connection;

    public TypeMappingTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"test_types_{Guid.NewGuid():N}.scdb");
        // Use Data Source= format compatible with Microsoft.Data.Sqlite / linq2db SQLite provider
        _connection = new SharpCoreDBDataConnection($"Data Source={_testDbPath}");

        // Use underlying ADO.NET command for table creation (bypasses linq2db SQLite provider adapter "Path=" incompatibility)
        using (var cmd = _connection.Connection.CreateCommand())
        {
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS TypedEntities (
                    ulid_value TEXT PRIMARY KEY,
                    name TEXT NOT NULL,
                    guid_value TEXT,
                    created_at TEXT,
                    is_active INTEGER,
                    binary_data BLOB,
                    optional_ulid TEXT
                );";
            cmd.ExecuteNonQuery();
        }
    }

    [Fact]
    public async Task Ulid_ShouldRoundTrip()
    {
        // Arrange
        var ulid = Ulid.NewUlid();
        var entity = new TypedEntity { UlidValue = ulid, Name = "ULID Test" };

        // Act
        await _connection.InsertAsync(entity);
        var retrieved = await _connection.GetTable<TypedEntity>()
            .Where(e => e.UlidValue == ulid)
            .FirstOrDefaultAsync();

        // Assert
        retrieved.Should().NotBeNull();
        retrieved!.UlidValue.Should().Be(ulid);
    }

    [Fact]
    public async Task Guid_ShouldRoundTrip()
    {
        // Arrange
        var guid = Guid.NewGuid();
        var entity = new TypedEntity { UlidValue = Ulid.NewUlid(), GuidValue = guid, Name = "GUID Test" };

        // Act
        await _connection.InsertAsync(entity);
        var retrieved = await _connection.GetTable<TypedEntity>()
            .Where(e => e.GuidValue == guid)
            .FirstOrDefaultAsync();

        // Assert
        retrieved.Should().NotBeNull();
        retrieved!.GuidValue.Should().Be(guid);
    }

    [Fact]
    public async Task DateTime_ShouldRoundTrip()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var entity = new TypedEntity { UlidValue = Ulid.NewUlid(), CreatedAt = now, Name = "DateTime Test" };

        // Act
        await _connection.InsertAsync(entity);
        var retrieved = await _connection.GetTable<TypedEntity>()
            .Where(e => e.Name == "DateTime Test")
            .FirstOrDefaultAsync();

        // Assert
        retrieved.Should().NotBeNull();
        retrieved!.CreatedAt.Should().BeCloseTo(now, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task Boolean_ShouldMapToInteger()
    {
        // Arrange
        var entity = new TypedEntity { UlidValue = Ulid.NewUlid(), IsActive = true, Name = "Bool Test" };

        // Act
        await _connection.InsertAsync(entity);
        var retrieved = await _connection.GetTable<TypedEntity>()
            .Where(e => e.IsActive)
            .FirstOrDefaultAsync();

        // Assert
        retrieved.Should().NotBeNull();
        retrieved!.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task ByteArray_ShouldRoundTrip()
    {
        // Arrange
        var data = new byte[] { 1, 2, 3, 4, 5, 255 };
        var entity = new TypedEntity { UlidValue = Ulid.NewUlid(), BinaryData = data, Name = "Binary Test" };

        // Act
        await _connection.InsertAsync(entity);
        var retrieved = await _connection.GetTable<TypedEntity>()
            .Where(e => e.Name == "Binary Test")
            .FirstOrDefaultAsync();

        // Assert
        retrieved.Should().NotBeNull();
        retrieved!.BinaryData.Should().BeEquivalentTo(data);
    }

    [Fact]
    public async Task NullableUlid_ShouldHandleNulls()
    {
        // Arrange
        var entity = new TypedEntity { UlidValue = Ulid.NewUlid(), OptionalUlid = null, Name = "Nullable Test" };

        // Act
        await _connection.InsertAsync(entity);
        var retrieved = await _connection.GetTable<TypedEntity>()
            .Where(e => e.Name == "Nullable Test")
            .FirstOrDefaultAsync();

        // Assert
        retrieved.Should().NotBeNull();
        retrieved!.OptionalUlid.Should().BeNull();
    }

    public void Dispose()
    {
        _connection?.Dispose();
        if (File.Exists(_testDbPath))
        {
            File.Delete(_testDbPath);
        }
    }
}

[Table(Name = "TypedEntities")]
public sealed class TypedEntity
{
    [PrimaryKey]
    [Column(Name = "ulid_value")]
    public required Ulid UlidValue { get; set; }

    [Column(Name = "name"), NotNull]
    public required string Name { get; set; }

    [Column(Name = "guid_value")]
    public Guid GuidValue { get; set; }

    [Column(Name = "created_at")]
    public DateTime CreatedAt { get; set; }

    [Column(Name = "is_active")]
    public bool IsActive { get; set; }

    [Column(Name = "binary_data")]
    public byte[]? BinaryData { get; set; }

    [Column(Name = "optional_ulid")]
    public Ulid? OptionalUlid { get; set; }
}
