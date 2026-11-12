// <copyright file="UnionOutcomeTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

#if NET11_0_OR_GREATER
namespace SharpCoreDB.Tests.Net11;

using System.Runtime.CompilerServices;
using SharpCoreDB;
using SharpCoreDB.Net11;
using SharpCoreDB.Unions;
using Xunit;

/// <summary>
/// Tests for the opt-in C# 15 union surface (<see cref="QueryOutcome"/>, <see cref="ExecuteOutcome"/>,
/// <see cref="TableLookup"/>). The switches below deliberately carry **no discard arm**: that is what
/// makes them the compile-time exhaustiveness proof the design asks for — adding a case type to a union
/// breaks this file at build time, not at run time.
/// </summary>
public sealed class UnionOutcomeTests : IDisposable
{
    private readonly string testDbPath;
    private readonly Database db;

    public UnionOutcomeTests()
    {
        testDbPath = Path.Combine(Path.GetTempPath(), $"net11_unions_{Guid.NewGuid()}");
        Directory.CreateDirectory(testDbPath);

        db = new Database(
            Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions
                .BuildServiceProvider(new Microsoft.Extensions.DependencyInjection.ServiceCollection().AddSharpCoreDB()),
            testDbPath,
            "test_password",
            isReadOnly: false,
            config: DatabaseConfig.Benchmark);
    }

    public void Dispose()
    {
        try { db.Dispose(); } catch { }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        try
        {
            if (Directory.Exists(testDbPath))
            {
                Directory.Delete(testDbPath, recursive: true);
            }
        }
        catch { }
    }

    [Fact]
    public void QueryOutcome_EveryCase_SwitchesExhaustively()
    {
        QueryOutcome[] outcomes =
        [
            new QuerySuccess([new Dictionary<string, object?> { ["id"] = 1 }]),
            new QueryNotFound(),
            new QueryFailure("syntax error"),
        ];

        string[] described = [.. outcomes.Select(Describe)];

        Assert.Equal(["rows: 1", "not found", "error: syntax error"], described);
    }

    [Fact]
    public void ExecuteOutcome_ImplicitConversion_BoxesTheCaseInUnionValue()
    {
        ExecuteOutcome outcome = new ExecuteSuccess(42);

        Assert.True(typeof(ExecuteOutcome).IsValueType, "C# 15 unions are structs.");

        var boxed = Assert.IsType<ExecuteSuccess>(((IUnion)outcome).Value);
        Assert.Equal(42, boxed.RowsAffected);
    }

    [Fact]
    public void TableLookup_FromExtensionIndexer_DistinguishesFoundFromMissing()
    {
        db.ExecuteSQL("CREATE TABLE docs (id INTEGER PRIMARY KEY, name TEXT)");

        TableLookup found = db["docs"] is { } info ? new TableFound(info) : new TableMissing("docs");
        TableLookup missing = db["does_not_exist"] is { } other ? new TableFound(other) : new TableMissing("does_not_exist");

        Assert.Equal("docs", DescribeTable(found));
        Assert.Equal("does_not_exist", DescribeTable(missing));
    }

    // No discard arm on purpose: the compiler proves every QueryOutcome case is handled here.
    private static string Describe(QueryOutcome outcome) => outcome switch
    {
        QuerySuccess success => $"rows: {success.Rows.Count}",
        QueryNotFound => "not found",
        QueryFailure failure => $"error: {failure.Message}",
    };

    // No discard arm on purpose: the compiler proves every TableLookup case is handled here.
    private static string DescribeTable(TableLookup lookup) => lookup switch
    {
        TableFound found => found.Table.Name,
        TableMissing missing => missing.Name,
    };
}
#endif
