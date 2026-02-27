#nullable enable

using FluentAssertions;
using SharpCoreDB.Provider.Sync.Builders;
using Xunit;

namespace SharpCoreDB.Provider.Sync.Tests;

/// <summary>
/// Tests for Scope metadata tables and CRUD operations.
/// Phase 2 verification: ScopeInfoBuilder command generation.
/// </summary>
public sealed class ScopeInfoBuilderTests
{
    [Fact]
    public void GetParsedScopeInfoTableNames_ShouldReturnCorrectNames()
    {
        // Arrange
        var builder = new SharpCoreDBScopeInfoBuilder();

        // Act
        var tableNames = builder.GetParsedScopeInfoTableNames();

        // Assert
        tableNames.Should().NotBeNull();
        tableNames.Name.Should().Be("scope_info");
        tableNames.QuotedName.Should().Be("[scope_info]");
    }

    [Fact]
    public void GetParsedScopeInfoClientTableNames_ShouldReturnCorrectNames()
    {
        // Arrange
        var builder = new SharpCoreDBScopeInfoBuilder();

        // Act
        var tableNames = builder.GetParsedScopeInfoClientTableNames();

        // Assert
        tableNames.Should().NotBeNull();
        tableNames.Name.Should().Be("scope_info_client");
        tableNames.QuotedName.Should().Be("[scope_info_client]");
    }

    [Fact]
    public void GetCreateScopeInfoTableCommand_ShouldGenerateValidDDL()
    {
        // Arrange
        var builder = new SharpCoreDBScopeInfoBuilder();
        using var connection = new SharpCoreDB.Data.Provider.SharpCoreDBConnection("Path=test.db");
        connection.Open();

        // Act
        var command = builder.GetCreateScopeInfoTableCommand(connection, null!);

        // Assert
        command.Should().NotBeNull();
        command.CommandText.Should().Contain("CREATE TABLE [scope_info]");
        command.CommandText.Should().Contain("sync_scope_id");
        command.CommandText.Should().Contain("sync_scope_name");
        command.CommandText.Should().Contain("PRIMARY KEY");
    }

    [Fact]
    public void GetCreateScopeInfoClientTableCommand_ShouldGenerateValidDDL()
    {
        // Arrange
        var builder = new SharpCoreDBScopeInfoBuilder();
        using var connection = new SharpCoreDB.Data.Provider.SharpCoreDBConnection("Path=test.db");
        connection.Open();

        // Act
        var command = builder.GetCreateScopeInfoClientTableCommand(connection, null!);

        // Assert
        command.Should().NotBeNull();
        command.CommandText.Should().Contain("CREATE TABLE [scope_info_client]");
        command.CommandText.Should().Contain("sync_scope_id");
        command.CommandText.Should().Contain("sync_scope_hash");
        command.CommandText.Should().Contain("PRIMARY KEY");
    }

    [Fact]
    public async Task GetInsertScopeInfoCommand_ShouldHaveAllParameters()
    {
        // Arrange
        var builder = new SharpCoreDBScopeInfoBuilder();
        using var connection = new SharpCoreDB.Data.Provider.SharpCoreDBConnection("Path=test.db");
        connection.Open();

        // Act
        var command = builder.GetInsertScopeInfoCommand(connection, null!);

        // Assert
        command.Should().NotBeNull();
        command.CommandText.Should().Contain("INSERT OR REPLACE INTO [scope_info]");
        command.Parameters.Count.Should().BeGreaterThanOrEqualTo(9);
        command.Parameters.Cast<System.Data.Common.DbParameter>().Should().Contain(p => p.ParameterName == "@sync_scope_id");
        command.Parameters.Cast<System.Data.Common.DbParameter>().Should().Contain(p => p.ParameterName == "@sync_scope_name");
    }

    [Fact]
    public void GetScopeInfoCommand_ShouldHaveWhereClause()
    {
        // Arrange
        var builder = new SharpCoreDBScopeInfoBuilder();
        using var connection = new SharpCoreDB.Data.Provider.SharpCoreDBConnection("Path=test.db");
        connection.Open();

        // Act
        var command = builder.GetScopeInfoCommand(connection, null!);

        // Assert
        command.Should().NotBeNull();
        command.CommandText.Should().Contain("WHERE [sync_scope_name] = @sync_scope_name");
        command.Parameters.Count.Should().Be(1);
    }

    [Fact]
    public void GetLocalTimestampCommand_ShouldCallSyncTimestamp()
    {
        // Arrange
        var builder = new SharpCoreDBScopeInfoBuilder();
        using var connection = new SharpCoreDB.Data.Provider.SharpCoreDBConnection("Path=test.db");
        connection.Open();

        // Act
        var command = builder.GetLocalTimestampCommand(connection, null!);

        // Assert
        command.Should().NotBeNull();
        command.CommandText.Should().Be("SELECT SYNC_TIMESTAMP()");
    }

    [Fact]
    public void GetDeleteScopeInfoCommand_ShouldHaveWhereClause()
    {
        // Arrange
        var builder = new SharpCoreDBScopeInfoBuilder();
        using var connection = new SharpCoreDB.Data.Provider.SharpCoreDBConnection("Path=test.db");
        connection.Open();

        // Act
        var command = builder.GetDeleteScopeInfoCommand(connection, null!);

        // Assert
        command.Should().NotBeNull();
        command.CommandText.Should().Contain("DELETE FROM [scope_info]");
        command.CommandText.Should().Contain("WHERE [sync_scope_name] = @sync_scope_name");
    }

    [Fact]
    public void GetUpdateScopeInfoCommand_ShouldUpdateAllFields()
    {
        // Arrange
        var builder = new SharpCoreDBScopeInfoBuilder();
        using var connection = new SharpCoreDB.Data.Provider.SharpCoreDBConnection("Path=test.db");
        connection.Open();

        // Act
        var command = builder.GetUpdateScopeInfoCommand(connection, null!);

        // Assert
        command.Should().NotBeNull();
        command.CommandText.Should().Contain("UPDATE [scope_info] SET");
        command.CommandText.Should().Contain("sync_scope_schema");
        command.CommandText.Should().Contain("scope_last_sync");
        command.CommandText.Should().Contain("WHERE [sync_scope_id]");
    }
}
