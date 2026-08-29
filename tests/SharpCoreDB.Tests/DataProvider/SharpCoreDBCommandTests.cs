#nullable enable

using System.Data;
using System.Collections.Generic;
using System.IO;
using SharpCoreDB.Data.Provider;

namespace SharpCoreDB.Tests.DataProvider;

/// <summary>
/// Integration-style tests for SharpCoreDBCommand exercising the ADO.NET provider surface.
/// These run under the main SharpCoreDB.Tests coverage collection (unlike the minimal
/// tests in Provider.Sync.Tests) and specifically cover the Guid normalization path
/// added for EF Core GUID foreign key round-tripping.
/// </summary>
public sealed class SharpCoreDBCommandTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SharpCoreDBConnection _connection;

    public SharpCoreDBCommandTests()
    {
        _dbPath = $"./test_cmd_{Guid.NewGuid():N}.scdb";
        _connection = new SharpCoreDBConnection($"Data Source={_dbPath};Password=TestPassword123;Cache=Shared");
        _connection.Open();

        // Ensure base table for GUID + DML tests (exercises ExecuteNonQuery + flush path)
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "CREATE TABLE IF NOT EXISTS GuidTest (Id TEXT PRIMARY KEY, Name TEXT)";
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        _connection?.Dispose();
        try
        {
            if (File.Exists(_dbPath))
                File.Delete(_dbPath);
        }
        catch (IOException)
        {
            // Acceptable in test cleanup - file may be locked briefly
        }
    }

    [Fact]
    public void Constructor_Default_ShouldInitializeDefaults()
    {
        // Arrange & Act
        var cmd = new SharpCoreDBCommand();

        // Assert
        Assert.Equal(CommandType.Text, cmd.CommandType);
        Assert.Equal(30, cmd.CommandTimeout);
        Assert.NotNull(cmd.Parameters);
        Assert.False(cmd.DesignTimeVisible); // default
    }

    [Fact]
    public void Constructor_WithCommandText_ShouldSetCommandText()
    {
        // Arrange & Act
        var cmd = new SharpCoreDBCommand("SELECT 1");

        // Assert
        Assert.Equal("SELECT 1", cmd.CommandText);
    }

    [Fact]
    public void Constructor_WithConnection_ShouldSetConnection()
    {
        // Arrange & Act
        var cmd = new SharpCoreDBCommand("SELECT 1", _connection);

        // Assert
        Assert.Same(_connection, cmd.Connection);
    }

    [Fact]
    public void ExecuteNonQuery_InsertWithGuidParam_ShouldNormalizeGuidAndFlush()
    {
        // Arrange
        var testGuid = Guid.NewGuid();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "INSERT INTO GuidTest (Id, Name) VALUES (@id, @name)";
        cmd.Parameters.Add(new SharpCoreDBParameter("@id", testGuid));
        cmd.Parameters.Add(new SharpCoreDBParameter("@name", "GuidRow"));

        // Act
        var affected = cmd.ExecuteNonQuery();

        // Assert
        Assert.Equal(1, affected); // rows affected by the single INSERT (issue #340)

        // Verify round-trip via reader (also exercises BuildParameterDictionary on SELECT)
        using var verify = _connection.CreateCommand();
        verify.CommandText = "SELECT Id, Name FROM GuidTest WHERE Id = @id";
        verify.Parameters.Add(new SharpCoreDBParameter("@id", testGuid));
        using var reader = verify.ExecuteReader();
        Assert.True(reader.Read());
        // The stored value is the normalized "D" string form
        var storedId = reader.GetString(0);
        Assert.Equal(testGuid.ToString("D"), storedId);
        Assert.Equal("GuidRow", reader.GetString(1));
    }

    [Fact]
    public async Task ExecuteScalarAsync_SimpleConstantQuery_ShouldReturnValue()
    {
        // Arrange - simple constant query (no table/params) to reliably exercise
        // the full async scalar path (Task.Run + BuildParameterDictionary + result extraction)
        // while the Guid/string param paths are already covered by the reader-based tests.
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT 'ScalarAsyncOK' AS Result";

        // Act
        var result = await cmd.ExecuteScalarAsync();

        // Assert
        Assert.Equal("ScalarAsyncOK", result);
    }

    [Fact]
    public void ExecuteReader_SystemTableQuery_ShouldReturnTablesViaMetadata()
    {
        // Arrange - this hits the SQLITE_MASTER special path + ExecuteSystemTableQuery
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT name, type FROM sqlite_master WHERE type = 'table'";

        // Act
        using var reader = cmd.ExecuteReader();

        // Assert - at minimum the GuidTest table we created should appear
        var tableNames = new List<string>();
        while (reader.Read())
        {
            tableNames.Add(reader.GetString(0));
        }
        Assert.Contains("GuidTest", tableNames);
    }

    [Fact]
    public async Task ExecuteReaderAsync_Typed_ShouldReturnSharpCoreDBDataReader()
    {
        // Arrange
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT 42 AS Answer";

        // Act
        var reader = await cmd.ExecuteReaderAsync();

        // Assert
        Assert.IsType<SharpCoreDBDataReader>(reader);
        Assert.True(await reader.ReadAsync());
        Assert.Equal(42, reader.GetInt32(0));
        await reader.DisposeAsync();
    }

    [Fact]
    public void Cancel_And_Prepare_ShouldNotThrow()
    {
        // Arrange
        var cmd = new SharpCoreDBCommand("SELECT 1");

        // Act & Assert - no-op methods
        cmd.Cancel();
        cmd.Prepare();
        Assert.True(true);
    }
}
