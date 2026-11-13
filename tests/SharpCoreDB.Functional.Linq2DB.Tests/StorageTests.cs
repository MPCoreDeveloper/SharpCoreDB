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

    /// <summary>
    /// The no-password half of the report in issue #497: a connection string without a password still has to
    /// store rows in a SharpCoreDB file. Up to 2.1.2 this wrote a plain <c>SQLite format 3</c> file that
    /// SharpCoreDB cannot open at all.
    /// <para>
    /// The path deliberately has no <c>.scdb</c> extension, exactly like the report's <c>probe.db</c>: that selects
    /// SharpCoreDB's directory layout, so the "no SQLite header" check runs over whichever storage shape the path
    /// produced (the single file, or every file of the directory).
    /// </para>
    /// </summary>
    [Fact]
    public async Task DataConnection_WithoutPassword_StillWritesASharpCoreDbFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"linq2db_probe_{Guid.NewGuid():N}.db");

        try
        {
            await using (var db = new SharpCoreDBDataConnection($"Data Source={path}"))
            {
                await db.CreateTableAsync<Note>();
                await db.InsertAsync(new Note { Id = 1, Body = "synthetic" });

                (await db.GetTable<Note>().CountAsync()).Should().Be(1);
                db.GetUnderlyingConnection().Should().BeOfType<SharpCoreDBConnection>();
            }

            var files = File.Exists(path)
                ? [path]
                : Directory.Exists(path)
                    ? Directory.GetFiles(path)
                    : [];
            files.Should().NotBeEmpty("the adapter has to have created storage for the rows");

            foreach (var file in files)
            {
                var header = new byte[15];
                int read;
                using (var stream = File.OpenRead(file))
                {
                    read = stream.Read(header);
                }

                System.Text.Encoding.ASCII.GetString(header, 0, read)
                    .Should().NotBe("SQLite format 3", $"{Path.GetFileName(file)} must not be a SQLite database");
            }

            // The core provider reads the rows back: no SQLite engine was involved in writing them.
            using var reopened = new SharpCoreDBConnection($"Data Source={path}");
            reopened.Open();
            using var command = reopened.CreateCommand();
            command.CommandText = "SELECT body FROM notes WHERE id = 1";
            command.ExecuteScalar().Should().Be("synthetic");
        }
        finally
        {
            // ".db" is directory mode, so the storage is a directory tree rather than a single file.
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
                else if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // the test's own temp path
            }
        }
    }
}
