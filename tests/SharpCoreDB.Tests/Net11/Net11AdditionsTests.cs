// <copyright file="Net11AdditionsTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

#if NET11_0_OR_GREATER
namespace SharpCoreDB.Tests.Net11;

using SharpCoreDB;
using SharpCoreDB.Net11;
using Xunit;

/// <summary>
/// Tests for the additive net11.0-only C# 15 preview API surface
/// (<see cref="SharpCoreDB.Net11.Net11Additions"/>). These tests run only on the net11.0 target.
/// </summary>
public sealed class Net11AdditionsTests : IDisposable
{
    private readonly string testDbPath;
    private readonly Database db;

    public Net11AdditionsTests()
    {
        testDbPath = Path.Combine(Path.GetTempPath(), $"net11_additions_{Guid.NewGuid()}");
        Directory.CreateDirectory(testDbPath);

        var config = DatabaseConfig.Benchmark;
        db = new Database(
            Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions
                .BuildServiceProvider(new Microsoft.Extensions.DependencyInjection.ServiceCollection().AddSharpCoreDB()),
            testDbPath,
            "test_password",
            isReadOnly: false,
            config: config);
    }

    public void Dispose()
    {
        try { db.Dispose(); } catch { }

        // Force garbage collection to release file handles
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
    public void ExtensionIndexer_ReturnsTableInfo_ForExistingTable()
    {
        db.ExecuteSQL("CREATE TABLE docs (id INTEGER PRIMARY KEY, name TEXT)");

        var info = db["docs"];

        Assert.NotNull(info);
        Assert.Equal("docs", info!.Name);
    }

    [Fact]
    public void ExtensionIndexer_ReturnsNull_ForUnknownTable()
    {
        var info = db["does_not_exist"];

        Assert.Null(info);
    }

    [Fact]
    public void NewList_PresizesCapacity()
    {
        var list = Net11Additions.NewList<int>(64);

        Assert.Empty(list);
        Assert.Equal(64, list.Capacity);
    }
}
#endif