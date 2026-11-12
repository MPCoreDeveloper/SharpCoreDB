using System;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using LinqToDB;
using LinqToDB.Async;
using LinqToDB.Mapping;
using SharpCoreDB.Data.Provider;
using Xunit;

namespace SharpCoreDB.Functional.Linq2DB.Tests;

/// <summary>
/// The adapter is the SQLite <em>dialect</em>, not the SQLite <em>engine</em>: linq2db generates the SQL, and
/// SharpCoreDB — encrypted, with its own file format — stores it.
/// <para>
/// Up to 2.1.2 the connection string went to <c>ProviderName.SQLite</c>, which opened Microsoft.Data.Sqlite and
/// the native <c>e_sqlite3</c> library: a password was refused, and without one the rows landed in an
/// unencrypted <c>SQLite format 3</c> file that SharpCoreDB cannot read.
/// </para>
/// </summary>
public sealed class StorageTests
{
    [Table("notes")]
    public sealed class Note
    {
        [Column("id"), PrimaryKey] public long Id { get; set; }
        [Column("body")] public string Body { get; set; } = "";
    }

    [Fact]
    public async Task DataConnection_WithPassword_StoresDataInASharpCoreDbFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"linq2db_storage_{Guid.NewGuid():N}.scdb");

        await using (var db = new SharpCoreDBDataConnection($"Data Source={path};Password=unit-test-password"))
        {
            // A password is accepted: the encryption is SharpCoreDB's, not the native SQLite library's.
            await db.CreateTableAsync<Note>();
            await db.InsertAsync(new Note { Id = 1, Body = "it's a probe" });

            var rows = await db.GetTable<Note>().ToListAsync();
            rows.Should().ContainSingle();
            rows[0].Body.Should().Be("it's a probe");

            // The connection linq2db executes on is SharpCoreDB's own ADO.NET connection.
            db.GetUnderlyingConnection().Should().BeOfType<SharpCoreDBConnection>();
        }

        try
        {
            var header = new byte[15];
            using (var stream = File.OpenRead(path))
            {
                stream.ReadExactly(header);
            }

            System.Text.Encoding.ASCII.GetString(header)
                .Should().NotBe("SQLite format 3", "the rows must live in a SharpCoreDB file, not a SQLite one");

            // The file is a SharpCoreDB database: the core provider reopens it with the same password.
            using var reopened = new SharpCoreDBConnection($"Data Source={path};Password=unit-test-password");
            reopened.Open();
            using var command = reopened.CreateCommand();
            command.CommandText = "SELECT body FROM notes WHERE id = 1";
            command.ExecuteScalar().Should().Be("it's a probe");
        }
        finally
        {
            try { File.Delete(path); } catch { /* the test's own temp file */ }
        }
    }
}
