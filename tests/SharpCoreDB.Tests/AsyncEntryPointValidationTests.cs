// <copyright file="AsyncEntryPointValidationTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace SharpCoreDB.Tests;

using Microsoft.Extensions.DependencyInjection;
using SharpCoreDB.Interfaces;
using SharpCoreDB.Services;
using Xunit;

/// <summary>
/// <c>ExecuteSQL(string)</c> and <c>ExecuteSQL(sql, parameters)</c> have called
/// <see cref="SqlQueryValidator.ValidateQuery"/> since 2026-09-15; their async twins never did, so a caller
/// that reached the database asynchronously — ADO.NET async, EF Core, the Serilog sink — bypassed a security
/// check its own sync sibling enforced, and the two entry points disagreed about which SQL is acceptable.
/// Session 75 gave both async overloads the sync call, and these facts pin the agreement from both sides:
/// what the sync sibling rejects, the async one must reject too, and a clean statement must still pass on
/// both (no over-throwing).
/// </summary>
public sealed class AsyncEntryPointValidationTests : IDisposable
{
    private readonly DatabaseFactory _factory;
    private readonly string _dir;

    public AsyncEntryPointValidationTests()
    {
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        _factory = services.BuildServiceProvider().GetRequiredService<DatabaseFactory>();
        _dir = Path.Combine(Path.GetTempPath(), $"SCDB_AsyncValidation_{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir))
            {
                Directory.Delete(_dir, true);
            }
        }
        catch
        {
            // best effort
        }
    }

    /// <summary>Strict mode, where a warning is an exception — the mode the mismatch is observable in.</summary>
    private IDatabase Create(string name)
        => _factory.Create(
            Path.Combine(_dir, name),
            "pw",
            isReadOnly: false,
            config: new DatabaseConfig
            {
                NoEncryptMode = true,
                SqlValidationMode = SqlQueryValidator.ValidationMode.Strict,
                StrictParameterValidation = true
            });

    /// <summary>
    /// A statement with no parameters that carries a string literal: the validator's first check warns, so
    /// Strict mode throws <see cref="SecurityException"/> on the sync path.
    /// </summary>
    [Fact]
    public async Task ExecuteSQLAsync_StrictMode_RejectsTheUnparameterizedStatementItsSyncSiblingRejects()
    {
        await using IDatabase db = Create("unparameterized");
        db.ExecuteSQL("CREATE TABLE docs (id INTEGER, name TEXT)");
        const string sql = "SELECT * FROM docs WHERE name = 'literal'";

        Assert.Throws<SecurityException>(() => db.ExecuteSQL(sql));
        await Assert.ThrowsAsync<SecurityException>(() => db.ExecuteSQLAsync(sql));
    }

    /// <summary>
    /// The parameterized overload passes its parameter set to the validator, like the sync one: a named
    /// placeholder with no matching key warns, and Strict mode turns that into an exception.
    /// </summary>
    [Fact]
    public async Task ExecuteSQLAsync_Parameterized_StrictMode_RejectsTheParameterMismatchItsSyncSiblingRejects()
    {
        await using IDatabase db = Create("parameterized");
        db.ExecuteSQL("CREATE TABLE docs (id INTEGER, name TEXT)");
        const string sql = "INSERT INTO docs VALUES (@id, @name)";
        Dictionary<string, object?> parameters = new() { ["@id"] = 1 };

        var syncError = Assert.Throws<SecurityException>(() => db.ExecuteSQL(sql, parameters));
        Assert.Contains("name", syncError.Message, StringComparison.Ordinal);

        var asyncError = await Assert.ThrowsAsync<SecurityException>(() => db.ExecuteSQLAsync(sql, parameters));
        Assert.Contains("name", asyncError.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The other side of the same contract: a clean parameterized statement is still accepted on both paths,
    /// so the added check cannot pass by rejecting everything.
    /// </summary>
    [Fact]
    public async Task ExecuteSQLAsync_StrictMode_AcceptsTheCleanStatementItsSyncSiblingAccepts()
    {
        await using IDatabase db = Create("clean");
        db.ExecuteSQL("CREATE TABLE docs (id INTEGER, name TEXT)");

        db.ExecuteSQL(
            "INSERT INTO docs VALUES (@id, @name)",
            new Dictionary<string, object?> { ["@id"] = 1, ["@name"] = "a" });
        await db.ExecuteSQLAsync(
            "INSERT INTO docs VALUES (@id, @name)",
            new Dictionary<string, object?> { ["@id"] = 2, ["@name"] = "b" });

        var rows = db.ExecuteQuery("SELECT id, name FROM docs");
        Assert.Equal(2, rows.Count);
    }
}
