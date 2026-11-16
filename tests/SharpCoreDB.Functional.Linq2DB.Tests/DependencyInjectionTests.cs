using System.Data.Common;
using FluentAssertions;
using LinqToDB;
using LinqToDB.Mapping;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace SharpCoreDB.Functional.Linq2DB.Tests;

/// <summary>
/// Connection ownership of the DI registrations and of reused <see cref="DataOptions"/>.
/// <para>
/// Up to 2.1.4 the options captured one live <c>SharpCoreDBConnection</c>, so every DI scope — every web request
/// — shared that single ADO.NET connection (and its transaction state) for the lifetime of the application, and
/// nothing ever disposed it. The <c>Action&lt;DataOptions&gt;</c> overload could not work at all:
/// <see cref="DataOptions"/> is immutable, so the callback's result was discarded and resolving the connection threw
/// "Configuration string is not provided".
/// </para>
/// </summary>
public sealed class DependencyInjectionTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"linq2db_di_{Guid.NewGuid():N}.scdb");

    private string ConnectionString => $"Data Source={_path}";

    [Fact]
    public void AddSharpCoreDBLinq2Db_WithConfigureCallback_ResolvesAWorkingConnection()
    {
        using var provider = new ServiceCollection()
            .AddSharpCoreDBLinq2Db(options => options.UseSharpCoreDB(ConnectionString))
            .BuildServiceProvider();
        using var scope = provider.CreateScope();
        var connection = scope.ServiceProvider.GetRequiredService<SharpCoreDBDataConnection>();

        CreateTable(connection);
        connection.Insert(new DiRow { Id = 1, Name = "configured" });

        connection.GetTable<DiRow>().Single().Name.Should().Be("configured");
    }

    [Fact]
    public void AddSharpCoreDBLinq2Db_WithConnectionString_GivesEachScopeItsOwnConnection()
    {
        using var provider = new ServiceCollection().AddSharpCoreDBLinq2Db(ConnectionString).BuildServiceProvider();
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        var a = first.ServiceProvider.GetRequiredService<SharpCoreDBDataConnection>().Connection;
        var b = second.ServiceProvider.GetRequiredService<SharpCoreDBDataConnection>().Connection;

        a.Should().NotBeSameAs(b);
    }

    [Fact]
    public void AddSharpCoreDBLinq2Db_WhenScopeEnds_DisposesItsConnection()
    {
        using var provider = new ServiceCollection().AddSharpCoreDBLinq2Db(ConnectionString).BuildServiceProvider();
        DbConnection connection;
        var disposed = false;

        using (var scope = provider.CreateScope())
        {
            connection = scope.ServiceProvider.GetRequiredService<SharpCoreDBDataConnection>().Connection;
            connection.Disposed += (_, _) => disposed = true;
        }

        disposed.Should().BeTrue();
    }

    [Fact]
    public void AddSharpCoreDBLinq2Db_WithOverlappingScopes_SeeEachOthersCommittedRows()
    {
        // Separate connections to one database must still share its data (SharpCoreDBInstancePool).
        using var provider = new ServiceCollection().AddSharpCoreDBLinq2Db(ConnectionString).BuildServiceProvider();
        using var writerScope = provider.CreateScope();
        using var readerScope = provider.CreateScope();
        var writer = writerScope.ServiceProvider.GetRequiredService<SharpCoreDBDataConnection>();
        var reader = readerScope.ServiceProvider.GetRequiredService<SharpCoreDBDataConnection>();

        CreateTable(writer);
        writer.Insert(new DiRow { Id = 7, Name = "shared" });

        reader.GetTable<DiRow>().Single(r => r.Id == 7).Name.Should().Be("shared");
    }

    [Fact]
    public void Constructor_WithReusedOptions_OpensItsOwnConnection()
    {
        var options = SharpCoreDBDataConnection.CreateOptions(ConnectionString);

        using var first = new SharpCoreDBDataConnection(options);
        using var second = new SharpCoreDBDataConnection(options);

        first.Connection.Should().NotBeSameAs(second.Connection);
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

    private static void CreateTable(SharpCoreDBDataConnection connection)
    {
        using var command = connection.GetUnderlyingConnection().CreateCommand();
        command.CommandText = "CREATE TABLE IF NOT EXISTS di_rows (id INTEGER PRIMARY KEY, name TEXT)";
        command.ExecuteNonQuery();
    }

    [Table("di_rows")]
    public sealed class DiRow
    {
        [PrimaryKey, Column("id")] public long Id { get; set; }
        [Column("name")] public string Name { get; set; } = "";
    }
}
