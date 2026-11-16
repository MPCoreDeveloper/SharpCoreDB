using FluentAssertions;
using LinqToDB;
using LinqToDB.Data;
using LinqToDB.Mapping;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace SharpCoreDB.Functional.Linq2DB.Tests;

/// <summary>
/// The error, cancellation and transaction contract of <see cref="FunctionalLinq2DbContext"/>, its batch insert, and
/// its DI registration.
/// <para>
/// The rule: methods returning <c>Option</c>/<c>Seq</c> throw on a failure, methods returning <c>Fin</c> capture it
/// as <c>Fail</c>, and cancellation always surfaces as <see cref="OperationCanceledException"/>. Up to 2.1.4 a token
/// cancelled before the call threw while one cancelled during the call came back as <c>Fail</c>, and
/// <c>TransactionAsync</c> rolled back with the caller's (cancelled) token — the rollback then threw and the
/// action's own error was replaced by "The operation was canceled".
/// </para>
/// </summary>
public sealed class FunctionalContextContractTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"linq2db_contract_{Guid.NewGuid():N}.scdb");
    private readonly SharpCoreDBDataConnection _connection;
    private readonly FunctionalLinq2DbContext _sut;

    public FunctionalContextContractTests()
    {
        _connection = new SharpCoreDBDataConnection(ConnectionString);
        _sut = new FunctionalLinq2DbContext(_connection);
        CreateTable(_connection);
    }

    private string ConnectionString => $"Data Source={_path}";

    // ── #7 TransactionAsync ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TransactionAsync_WhenActionThrows_RollsBackItsWrites()
    {
        var result = await _sut.TransactionAsync<int>(async () =>
        {
            await _connection.InsertAsync(new ContractRow { Id = 1, Name = "inside" });
            throw new InvalidOperationException("boom");
        });

        result.IsFail.Should().BeTrue();
        _connection.GetTable<ContractRow>().Count().Should().Be(0);
    }

    [Fact]
    public async Task TransactionAsync_WhenActionThrowsAfterTokenIsCancelled_ReportsTheActionsError()
    {
        using var cts = new CancellationTokenSource();

        var result = await _sut.TransactionAsync<int>(() =>
        {
            cts.Cancel();
            throw new InvalidOperationException("the action's own error");
        }, cts.Token);

        result.Match(_ => "<succ>", e => e.Message).Should().Be("the action's own error");
    }

    [Fact]
    public async Task TransactionAsync_WithContextOverload_PassesContextAndTokenAndCommits()
    {
        using var cts = new CancellationTokenSource();
        CancellationToken seen = default;

        var result = await _sut.TransactionAsync(async (db, ct) =>
        {
            seen = ct;
            return await db.InsertAsync(new ContractRow { Id = 2, Name = "committed" }, ct);
        }, cts.Token);

        result.IsSucc.Should().BeTrue();
        seen.Should().Be(cts.Token);
        _connection.GetTable<ContractRow>().Single().Name.Should().Be("committed");
    }

    // ── #8 cancellation is never a Fail ─────────────────────────────────────────────────────────

    [Fact]
    public async Task TransactionAsync_WhenCancelledDuringTheAction_ThrowsOperationCanceled()
    {
        using var cts = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _sut.TransactionAsync<int>(() =>
        {
            cts.Cancel();
            cts.Token.ThrowIfCancellationRequested();
            return Task.FromResult(0);
        }, cts.Token));
    }

    // ── #9 InsertBatchAsync ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task InsertBatchAsync_WithCallerOptions_UsesMultiRowSqlAndInsertsEveryRow()
    {
        // ProviderSpecific bulk copy needs a native Microsoft.Data.Sqlite connection, which the adapter never has.
        var options = new BulkCopyOptions { BulkCopyType = BulkCopyType.ProviderSpecific, MaxBatchSize = 2 };
        var rows = Enumerable.Range(10, 5).Select(i => new ContractRow { Id = i, Name = $"r{i}" }).ToList();

        var result = await _sut.InsertBatchAsync(rows, options);

        result.Match(n => n, _ => -1).Should().Be(5);
        _connection.GetTable<ContractRow>().Count().Should().Be(5);
    }

    [Fact]
    public void ToRowCount_AboveInt32_IsAFailNotAWrappedNumber()
    {
        var result = FunctionalLinq2DbContext.ToRowCount(3_000_000_000L);

        result.IsFail.Should().BeTrue();
    }

    [Fact]
    public void ToRowCount_WithinInt32_IsTheCount()
    {
        FunctionalLinq2DbContext.ToRowCount(42L).Match(n => n, _ => -1).Should().Be(42);
    }

    // ── #10 DI registers the functional context ─────────────────────────────────────────────────

    [Fact]
    public void AddSharpCoreDBLinq2Db_WithConnectionString_RegistersTheContextOnTheScopesConnection()
    {
        using var provider = new ServiceCollection().AddSharpCoreDBLinq2Db(ConnectionString).BuildServiceProvider();
        using var scope = provider.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<FunctionalLinq2DbContext>();

        context.Connection.Should().BeSameAs(scope.ServiceProvider.GetRequiredService<SharpCoreDBDataConnection>());
    }

    [Fact]
    public void AddSharpCoreDBLinq2Db_WithConfigureCallback_RegistersTheContext()
    {
        using var provider = new ServiceCollection()
            .AddSharpCoreDBLinq2Db(o => o.UseSharpCoreDB(ConnectionString))
            .BuildServiceProvider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<FunctionalLinq2DbContext>().Should().NotBeNull();
    }

    [Fact]
    public void AddSharpCoreDBLinq2Db_KeepsACallersOwnContextRegistration()
    {
        var mine = new FunctionalLinq2DbContext(_connection);
        using var provider = new ServiceCollection()
            .AddSingleton(mine)
            .AddSharpCoreDBLinq2Db(ConnectionString)
            .BuildServiceProvider();

        provider.GetRequiredService<FunctionalLinq2DbContext>().Should().BeSameAs(mine);
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

    private static void CreateTable(SharpCoreDBDataConnection connection)
    {
        using var command = connection.GetUnderlyingConnection().CreateCommand();
        command.CommandText = "CREATE TABLE IF NOT EXISTS contract_rows (id INTEGER PRIMARY KEY, name TEXT)";
        command.ExecuteNonQuery();
    }

    [Table("contract_rows")]
    public sealed class ContractRow
    {
        [PrimaryKey, Column("id")] public int Id { get; set; }
        [Column("name")] public string Name { get; set; } = "";
    }
}
