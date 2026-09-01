// <copyright file="VacuumStressTests.cs" company="MPCoreDeveloper">
// Copyright (c) 2025-2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

using Xunit;
using SharpCoreDB;
using System.IO;
using Microsoft.Extensions.DependencyInjection;

namespace SharpCoreDB.Tests;

/// <summary>
/// Stress regression tests for the sporadic CI-only corruption of single-file tables
/// (JsonException '0x02' / "Expected: 100, Actual: 0" after reopen).
///
/// Root cause (fixed): the WAL manager wrote to the shared <c>FileStream</c> with a bare
/// <c>Position</c> + <c>WriteAsync</c>, while the background write-behind worker wrote data
/// pages under a lock. A concurrent <c>Position</c> mutation could land WAL bytes on a data
/// page (or vice versa). The WAL now writes through <c>SingleFileStorageProvider.WriteAt</c>,
/// which serializes all <c>FileStream.Position</c> use with the worker.
///
/// Before the fix these cycles failed ~50% of the time; after the fix they are stable.
/// </summary>
public class VacuumStressTests
{
    private static readonly Lazy<DatabaseFactory> Factory = new(() =>
    {
        var services = new ServiceCollection();
        services.AddSharpCoreDB();
        return services.BuildServiceProvider().GetRequiredService<DatabaseFactory>();
    });

    public static IEnumerable<object[]> StressRuns()
    {
        for (var i = 0; i < 10; i++)
        {
            yield return [i];
        }
    }

    private static (SharpCoreDB.SingleFileDatabase Db, string Path) CreatePopulated(string tag, int run)
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"{tag}_{run}_{Guid.NewGuid():N}.scdb");
        var options = DatabaseOptions.CreateSingleFileDefault();
        var db = (SharpCoreDB.SingleFileDatabase)Factory.Value.CreateWithOptions(path, "test_password", options);

        db.ExecuteSQL("CREATE TABLE docs (name TEXT NOT NULL, age INTEGER)");
        var statements = new List<string>(100);
        for (var i = 0; i < 100; i++)
        {
            statements.Add($"INSERT INTO docs (name, age) VALUES ('User{i}', {20 + i})");
        }

        db.ExecuteBatchSQL(statements);
        db.Flush();
        return (db, path);
    }

    private static void AssertRowsReadable(string path)
    {
        var options = DatabaseOptions.CreateSingleFileDefault();
        var reopened = (SharpCoreDB.SingleFileDatabase)Factory.Value.CreateWithOptions(path, "test_password", options);
        try
        {
            var all = reopened.ExecuteQuery("SELECT * FROM docs");
            Assert.Equal(100, all.Count);
            Assert.Equal("User42", reopened.ExecuteQuery("SELECT * FROM docs WHERE name = 'User42'").Single()["name"]);
        }
        finally
        {
            (reopened as IDisposable)?.Dispose();
        }
    }

    private static void Cleanup(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path + ".backup")) File.Delete(path + ".backup");
        }
        catch
        {
            // ignore cleanup errors
        }
    }

    [Theory]
    [MemberData(nameof(StressRuns))]
    public void InsertFlushReopen_Roundtrip(int run)
    {
        // Regression for the WAL-vs-worker FileStream.Position race that corrupted the
        // data block already on the plain create → insert → flush → reopen path.
        var (db, path) = CreatePopulated("stress_no_vac", run);
        (db as IDisposable)?.Dispose();
        try
        {
            AssertRowsReadable(path);
        }
        finally
        {
            Cleanup(path);
        }
    }

    [Theory]
    [MemberData(nameof(StressRuns))]
    public void VacuumFull_Roundtrip_SurvivesReopen(int run)
    {
        // Issue #343 regression: VacuumMode.Full must swap the stream and remain readable.
        var (db, path) = CreatePopulated("stress_vac", run);
        try
        {
            var result = db.VacuumAsync(VacuumMode.Full, System.Threading.CancellationToken.None)
                .GetAwaiter().GetResult();
            Assert.True(result.Success, $"run {run}: VacuumFull failed: {result.ErrorMessage}");
            Assert.True(result.BlocksMoved >= 1, $"run {run}: BlocksMoved was {result.BlocksMoved}");
        }
        finally
        {
            (db as IDisposable)?.Dispose();
        }

        try
        {
            AssertRowsReadable(path);
        }
        finally
        {
            Cleanup(path);
        }
    }
}
