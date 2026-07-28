using System;
using System.IO;
using FluentAssertions;
using LinqToDB;
using LinqToDB.Async;
using LinqToDB.Mapping;
using SharpCoreDB.Functional.Linq2DB;
using Xunit;

namespace SharpCoreDB.Functional.Linq2DB.Tests;

/// <summary>
/// Shared fixture for LINQ2DB tests to avoid Windows SQLite file locking by using a single DB instance.
/// </summary>
public sealed class Linq2DbTestFixture : IDisposable
{
    public readonly string DbPath = Path.Combine(Path.GetTempPath(), $"linq2db_shared_test_{Guid.NewGuid():N}.scdb");
    public readonly SharpCoreDBDataConnection Connection;
    public readonly FunctionalLinq2DbContext FunctionalDb;

    public Linq2DbTestFixture()
    {
        Connection = new SharpCoreDBDataConnection($"Data Source={DbPath}");
        FunctionalDb = new FunctionalLinq2DbContext(Connection);

        // Ensure connection is open and create tables once
        var underlyingConn = Connection.GetUnderlyingConnection();
        if (underlyingConn.State != System.Data.ConnectionState.Open)
            underlyingConn.Open();

        using (var cmd = underlyingConn.CreateCommand())
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
                );
                CREATE TABLE IF NOT EXISTS TestEntities (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    name TEXT NOT NULL,
                    age INTEGER
                );
                CREATE TABLE IF NOT EXISTS Users (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    email TEXT NOT NULL,
                    is_active INTEGER
                );";
            cmd.ExecuteNonQuery();
        }
    }

    public void Dispose()
    {
        Connection?.Dispose();

        // Robust cleanup for Windows SQLite locking and sidecar files
        var cleanupPaths = new[] { DbPath, DbPath + "-wal", DbPath + "-shm" };

        for (int i = 0; i < 20; i++)
        {
            try
            {
                foreach (var path in cleanupPaths)
                {
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }
                }

                return;
            }
            catch (IOException)
            {
                if (i == 19)
                {
                    return;
                }

                System.Threading.Thread.Sleep(200);
            }
            catch (UnauthorizedAccessException)
            {
                if (i == 19)
                {
                    return;
                }

                System.Threading.Thread.Sleep(200);
            }
        }
    }
}

/// <summary>
/// Tests for SharpCoreDB-specific type mappings (ULID, GUID, DateTime).
/// Now uses IClassFixture to share one DB instance.
/// </summary>
public sealed class TypeMappingTests : IClassFixture<Linq2DbTestFixture>
{
    private readonly Linq2DbTestFixture _fixture;

    public TypeMappingTests(Linq2DbTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Ulid_ShouldRoundTrip()
    {
        // Arrange
        var ulid = Ulid.NewUlid();
        var entity = new TypedEntity { UlidValue = ulid, Name = "ULID Test" };

        // Act
        await _fixture.Connection.InsertAsync(entity);
        var retrieved = await _fixture.Connection.GetTable<TypedEntity>()
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
        await _fixture.Connection.InsertAsync(entity);
        var retrieved = await _fixture.Connection.GetTable<TypedEntity>()
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
        await _fixture.Connection.InsertAsync(entity);
        var retrieved = await _fixture.Connection.GetTable<TypedEntity>()
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
        await _fixture.Connection.InsertAsync(entity);
        var retrieved = await _fixture.Connection.GetTable<TypedEntity>()
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
        await _fixture.Connection.InsertAsync(entity);
        var retrieved = await _fixture.Connection.GetTable<TypedEntity>()
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
        await _fixture.Connection.InsertAsync(entity);
        var retrieved = await _fixture.Connection.GetTable<TypedEntity>()
            .Where(e => e.Name == "Nullable Test")
            .FirstOrDefaultAsync();

        // Assert
        retrieved.Should().NotBeNull();
        retrieved!.OptionalUlid.Should().BeNull();
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
